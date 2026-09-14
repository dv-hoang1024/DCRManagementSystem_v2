using System.Text.Json;
using DCRManagementSystem.Data;
using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DCRManagementSystem.Services;

public sealed class AdminService : IAdminService
{
    private readonly Func<AppDbContext> _dbFactory;
    private readonly AppSettings _settings;

    public AdminService(Func<AppDbContext> dbFactory, AppSettings settings)
    {
        _dbFactory = dbFactory;
        _settings = settings;
    }


    public async Task DeleteDcrAsync(int requestId, int actorUserId, bool isAdmin)
    {
        var storageService = new StorageConfigurationService(_dbFactory, _settings);
        var storageRoot = storageService.ResolveRoot(await storageService.GetAsync());

        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        await LockDcrForDeletionAsync(db, requestId);

        var request = await db.DCRRequests
            .AsNoTracking()
            .Include(x => x.RequestOwner)
            .Include(x => x.RequestingDepartment)
            .Include(x => x.Parts)
            .Include(x => x.ImpactedDepartments)
            .Include(x => x.ApprovalPlan)
            .Include(x => x.ApprovalFlow)
            .Include(x => x.Attachments)
            .SingleOrDefaultAsync(x => x.Id == requestId)
            ?? throw new InvalidOperationException("Không tìm thấy DCR cần xóa.");

        // This check runs after taking the database deletion lock, so a DCR
        // cannot be submitted into approval between validation and deletion.
        DcrDeletionPolicy.EnsureCanDelete(request.Status, request.CreatedBy, actorUserId, isAdmin);

        var snapshotJson = JsonSerializer.Serialize(new
        {
            request.Id,
            request.DCRNumber,
            request.Title,
            request.Status,
            request.CurrentStage,
            request.RevisionNo,
            request.RequestingDepartmentId,
            RequestingDepartment = request.RequestingDepartment?.DepartmentCode,
            request.RequestOwnerId,
            RequestOwner = request.RequestOwner?.FullName,
            request.Program,
            request.BuildStage,
            request.ProblemDescription,
            request.Solution,
            request.MaterialChangeDescription,
            request.CreatedBy,
            request.CreatedDate,
            request.SubmittedDate,
            request.CompletedDate,
            request.RejectedDate,
            RowVersion = Convert.ToHexString(request.RowVersion),
            Parts = request.Parts.OrderBy(x => x.SortOrder).Select(x => new
            {
                x.Id, x.ChangeType, x.PartNumber, x.PartName, x.KPC, x.Quantity, x.ReplacedBy, x.SortOrder
            }),
            ImpactedDepartments = request.ImpactedDepartments.Select(x => new
            {
                x.Id, x.DepartmentId, x.EstimatedCost
            }),
            ApprovalPlan = request.ApprovalPlan.OrderBy(x => x.LevelNumber).ThenBy(x => x.Sequence).Select(x => new
            {
                x.Id, x.LevelNumber, x.LevelName, x.ApproverId, x.Sequence, x.IsRequired
            }),
            ApprovalFlow = request.ApprovalFlow.OrderBy(x => x.RevisionNo).ThenBy(x => x.StageNumber).ThenBy(x => x.Sequence).Select(x => new
            {
                x.Id, x.RevisionNo, x.StageNumber, x.StageCode, x.StageName, x.ApproverId, x.DepartmentId,
                x.Decision, x.DecisionDate, x.Comments, x.AssignedDate, x.DueDate, x.ReminderCount, x.LastReminderAt,
                x.AuthMethod, x.AuthenticatedAt, x.WindowsIdentity, x.SignatureHash
            }),
            Attachments = request.Attachments.Select(x => new
            {
                x.Id, x.AttachmentType, x.OriginalFileName, x.StoredFileName, x.FilePath, x.FileSize,
                x.StoredFileSize, x.IsCompressed, x.Sha256Hash, x.UploadedBy, x.UploadedAt, x.IsDeleted
            }),
            request.FinalPdfPath,
            request.FinalPdfSha256,
            request.FinalPdfGeneratedAt
        });

        var physicalFiles = request.Attachments
            .Where(x => !string.IsNullOrWhiteSpace(x.FilePath))
            .Select(x => Path.IsPathRooted(x.FilePath)
                ? Path.GetFullPath(x.FilePath)
                : Path.GetFullPath(Path.Combine(storageRoot, x.FilePath)))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!string.IsNullOrWhiteSpace(request.FinalPdfPath))
        {
            var finalPdfPath = Path.IsPathRooted(request.FinalPdfPath)
                ? Path.GetFullPath(request.FinalPdfPath)
                : Path.GetFullPath(Path.Combine(_settings.GetAbsoluteApprovedPdfRoot(), request.FinalPdfPath));
            if (!physicalFiles.Contains(finalPdfPath, StringComparer.OrdinalIgnoreCase))
                physicalFiles.Add(finalPdfPath);
        }

        var generatedMailPdfs = await db.EmailOutbox.AsNoTracking()
            .Where(x => x.RequestId == requestId && x.AttachmentFilePath != string.Empty)
            .Select(x => x.AttachmentFilePath)
            .Distinct()
            .ToListAsync();
        physicalFiles.AddRange(generatedMailPdfs.Where(x => !physicalFiles.Contains(x, StringComparer.OrdinalIgnoreCase)));

        long deletionLogId;
        var cancelledOutboxStatus = EmailOutboxStatuses.Cancelled;
        var sentOutboxStatus = EmailOutboxStatuses.Sent;
        var outboxCancelReason = isAdmin
            ? "DCR deleted by Administrator before email delivery."
            : "DCR deleted by its creator before email delivery.";
        var cancelledMailCount = await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE EmailOutbox SET Status = {cancelledOutboxStatus}, LastError = {outboxCancelReason}, NextAttemptAt = NULL WHERE RequestId = {requestId} AND Status <> {sentOutboxStatus} AND Status <> {cancelledOutboxStatus};");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM DCRNotificationLogs WHERE RequestId = {requestId};");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM AuditLogs WHERE RequestId = {requestId};");
        var deleted = await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM DCRRequests WHERE Id = {requestId};");
        if (deleted != 1)
            throw new InvalidOperationException("DCR không được xóa do dữ liệu đã thay đổi. Hãy Refresh và thử lại.");

        var log = new DCRDeletionLog
        {
            OriginalRequestId = request.Id,
            DCRNumber = request.DCRNumber,
            DeletedBy = actorUserId,
            DeletedAt = DateTime.Now,
            SnapshotJson = snapshotJson,
            CleanupStatus = "Pending",
            CleanupDetails = string.Empty,
            ComputerName = AuditEnvironment.ComputerName,
            IpAddress = AuditEnvironment.LocalIpAddress,
            WindowsIdentity = AuditEnvironment.WindowsIdentityName,
            SessionId = RequestExecutionContext.SessionId
        };
        db.DCRDeletionLogs.Add(log);
        await db.SaveChangesAsync();
        deletionLogId = log.Id;
        await tx.CommitAsync();

        var cleanupErrors = new List<string>();
        var deletedFileCount = 0;
        foreach (var path in physicalFiles)
        {
            try
            {
                await NetworkResilienceService.ExecuteFileAsync(_ =>
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                        deletedFileCount++;
                    }
                    return Task.CompletedTask;
                });
            }
            catch (Exception ex)
            {
                cleanupErrors.Add($"{path}: {ex.Message}");
            }
        }

        foreach (var attachmentId in request.Attachments.Select(x => x.Id).Distinct())
        {
            var cacheFolder = Path.Combine(
                Path.GetTempPath(),
                "DCRManagementSystem",
                "AttachmentCache",
                attachmentId.ToString());
            try
            {
                if (Directory.Exists(cacheFolder))
                    Directory.Delete(cacheFolder, recursive: true);
            }
            catch (Exception ex)
            {
                cleanupErrors.Add($"{cacheFolder}: {ex.Message}");
            }
        }

        await using var logDb = _dbFactory();
        await logDb.OpenSqlConnectionWithRetryAsync();
        var deletionLog = await logDb.DCRDeletionLogs.SingleAsync(x => x.Id == deletionLogId);
        deletionLog.CleanupStatus = cleanupErrors.Count == 0 ? "Completed" : "CompletedWithErrors";
        var cleanupSummary =
            $"Đã hủy {cancelledMailCount:N0} tác vụ email; " +
            $"đã xóa {request.ApprovalFlow.Count:N0} tác vụ phê duyệt/thời hạn; " +
            $"đã xóa {deletedFileCount:N0}/{physicalFiles.Count:N0} file liên quan.";
        deletionLog.CleanupDetails = cleanupErrors.Count == 0
            ? cleanupSummary
            : cleanupSummary + Environment.NewLine + string.Join(Environment.NewLine, cleanupErrors);
        await logDb.SaveChangesAsync();
    }

    private static async Task LockDcrForDeletionAsync(AppDbContext db, int requestId)
    {
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT Id FROM DCRRequests WITH (XLOCK, HOLDLOCK) WHERE Id = @requestId;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@requestId";
        parameter.Value = requestId;
        command.Parameters.Add(parameter);
        await command.ExecuteScalarAsync();
    }

    public async Task<List<User>> GetUsersAsync()
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        return await db.Users
            .AsNoTracking()
            .Include(x => x.Department)
                .ThenInclude(x => x!.BusinessUnit)
            .Include(x => x.BusinessUnit)
            .Include(x => x.DirectManager)
            .Include(x => x.BusinessUnitAssignments)
                .ThenInclude(x => x.BusinessUnit)
            .Include(x => x.BusinessUnitAssignments)
                .ThenInclude(x => x.ReportsToUser)
            .Include(x => x.DepartmentAssignments)
                .ThenInclude(x => x.Department)
            .Include(x => x.DepartmentAssignments)
                .ThenInclude(x => x.ReportsToUser)
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.Username)
            .ToListAsync();
    }

    public async Task<List<ProductLineDefinition>> GetProductLinesAsync(bool activeOnly = false)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        var query = db.ProductLines.AsNoTracking().AsQueryable();
        if (activeOnly) query = query.Where(x => x.IsActive);
        return await query.OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync();
    }

    public async Task<ProductLineDefinition> SaveProductLineAsync(int? id, string name, int sortOrder, bool isActive)
    {
        name = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Tên Dòng sản phẩm không được để trống.");
        if (name.Length > 100)
            throw new InvalidOperationException("Tên Dòng sản phẩm tối đa 100 ký tự.");

        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        if (await db.ProductLines.AnyAsync(x => x.Name == name && x.Id != id))
            throw new InvalidOperationException("Dòng sản phẩm đã tồn tại.");

        ProductLineDefinition entity;
        if (id.HasValue)
            entity = await db.ProductLines.SingleAsync(x => x.Id == id.Value);
        else
        {
            entity = new ProductLineDefinition { CreatedAt = DateTime.Now };
            db.ProductLines.Add(entity);
        }

        entity.Name = name;
        entity.SortOrder = Math.Max(0, sortOrder);
        entity.IsActive = isActive;
        await db.SaveChangesAsync();
        return entity;
    }

    public async Task DeleteProductLineAsync(int id)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        var entity = await db.ProductLines.SingleAsync(x => x.Id == id);
        db.ProductLines.Remove(entity);
        await db.SaveChangesAsync();
    }

    public async Task<List<PartChangeTypeDefinition>> GetPartChangeTypesAsync(bool activeOnly = false)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        var query = db.PartChangeTypes.AsNoTracking().AsQueryable();
        if (activeOnly) query = query.Where(x => x.IsActive);
        return await query.OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync();
    }

    public async Task<PartChangeTypeDefinition> SavePartChangeTypeAsync(int? id, string name, int sortOrder, bool isActive)
    {
        name = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Tên Loại thay đổi không được để trống.");
        if (name.Length > 80)
            throw new InvalidOperationException("Tên Loại thay đổi tối đa 80 ký tự.");

        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        if (await db.PartChangeTypes.AnyAsync(x => x.Name == name && x.Id != id))
            throw new InvalidOperationException("Loại thay đổi đã tồn tại.");

        PartChangeTypeDefinition entity;
        if (id.HasValue)
            entity = await db.PartChangeTypes.SingleAsync(x => x.Id == id.Value);
        else
        {
            entity = new PartChangeTypeDefinition { CreatedAt = DateTime.Now };
            db.PartChangeTypes.Add(entity);
        }

        entity.Name = name;
        entity.SortOrder = Math.Max(0, sortOrder);
        entity.IsActive = isActive;
        await db.SaveChangesAsync();
        return entity;
    }

    public async Task DeletePartChangeTypeAsync(int id)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        var entity = await db.PartChangeTypes.SingleAsync(x => x.Id == id);
        db.PartChangeTypes.Remove(entity);
        await db.SaveChangesAsync();
    }

    public async Task<List<RoleDefinition>> GetRolesAsync(bool activeOnly = false)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        var query = db.Roles.AsNoTracking().AsQueryable();
        if (activeOnly)
            query = query.Where(x => x.IsActive);
        return await query
            .OrderByDescending(x => x.HierarchyLevel)
            .ThenBy(x => x.RoleName)
            .ToListAsync();
    }

    public async Task<RoleDefinition> SaveRoleAsync(
        int? roleId,
        string roleName,
        string description,
        int hierarchyLevel,
        bool isActive)
    {
        roleName = (roleName ?? string.Empty).Trim();
        description = (description ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(roleName))
            throw new InvalidOperationException("Tên Role không được để trống.");
        if (roleName.Length > 80)
            throw new InvalidOperationException("Tên Role tối đa 80 ký tự.");
        if (hierarchyLevel < 0 || hierarchyLevel > 1000)
            throw new InvalidOperationException("Hierarchy Level phải nằm trong khoảng 0 đến 1000.");

        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        if (await db.Roles.AnyAsync(x => x.RoleName == roleName && x.Id != roleId))
            throw new InvalidOperationException("Role đã tồn tại.");

        RoleDefinition entity;
        string oldName;
        if (roleId.HasValue)
        {
            entity = await db.Roles.SingleAsync(x => x.Id == roleId.Value);
            oldName = entity.RoleName;
            if (entity.IsSystemProtected && !string.Equals(oldName, roleName, StringComparison.Ordinal))
                throw new InvalidOperationException("Role Administrator là role hệ thống và không được đổi tên.");
            if (entity.IsSystemProtected && !isActive)
                throw new InvalidOperationException("Role Administrator là role hệ thống và không được vô hiệu hóa.");
        }
        else
        {
            entity = new RoleDefinition { CreatedAt = DateTime.Now };
            oldName = string.Empty;
            db.Roles.Add(entity);
        }

        await using var tx = await db.Database.BeginTransactionAsync();
        if (!string.IsNullOrWhiteSpace(oldName) && !string.Equals(oldName, roleName, StringComparison.Ordinal))
        {
            await db.Users.Where(x => x.Role == oldName).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Role, roleName));
            await db.WorkflowStageTemplates.Where(x => x.ApproverRole == oldName).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ApproverRole, roleName));
            await db.ApprovalMatrixRules.Where(x => x.ApproverRole == oldName).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ApproverRole, roleName));
        }

        entity.RoleName = roleName;
        entity.Description = description;
        entity.HierarchyLevel = hierarchyLevel;
        entity.IsActive = isActive;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return entity;
    }

    public async Task DeleteRoleAsync(int roleId)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        var role = await db.Roles.SingleOrDefaultAsync(x => x.Id == roleId)
                   ?? throw new InvalidOperationException("Không tìm thấy Role.");
        if (role.IsSystemProtected || string.Equals(role.RoleName, RoleNames.Administrator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Role Administrator là role hệ thống và không được xóa.");

        var userCount = await db.Users.CountAsync(x => !x.IsDeleted && x.Role == role.RoleName);
        var workflowCount = await db.WorkflowStageTemplates.CountAsync(x => x.ApproverRole == role.RoleName);
        var matrixCount = await db.ApprovalMatrixRules.CountAsync(x => x.ApproverRole == role.RoleName);
        if (userCount + workflowCount + matrixCount > 0)
        {
            throw new InvalidOperationException(
                $"Không thể xóa Role '{role.RoleName}' vì đang được sử dụng: {userCount} user, {workflowCount} workflow stage, {matrixCount} approval matrix rule. Hãy chuyển các tham chiếu sang Role khác trước.");
        }

        db.Roles.Remove(role);
        await db.SaveChangesAsync();
    }

    public async Task DeleteUserAsync(int userId, int actorUserId)
    {
        if (userId == actorUserId)
            throw new InvalidOperationException("Bạn không thể xóa chính tài khoản đang đăng nhập.");

        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        var user = await db.Users.SingleOrDefaultAsync(x => x.UserId == userId && !x.IsDeleted)
                   ?? throw new InvalidOperationException("Không tìm thấy người dùng.");

        if (string.Equals(user.Role, RoleNames.Administrator, StringComparison.OrdinalIgnoreCase))
        {
            var otherAdmins = await db.Users.CountAsync(x => x.UserId != userId && !x.IsDeleted && x.IsActive && x.Role == RoleNames.Administrator);
            if (otherAdmins == 0)
                throw new InvalidOperationException("Không thể xóa Administrator cuối cùng của hệ thống.");
        }

        var activeOwnedDcr = await db.DCRRequests.CountAsync(x =>
            (x.CreatedBy == userId || x.RequestOwnerId == userId) &&
            (x.Status == RequestStatuses.Draft || x.Status == RequestStatuses.Returned || x.Status == RequestStatuses.InApproval));
        var activeApprovalLines = await db.DCRApprovalFlows.CountAsync(x =>
            x.ApproverId == userId &&
            (x.Decision == ApprovalDecisions.Pending || x.Decision == ApprovalDecisions.Waiting) &&
            db.DCRRequests.Any(r => r.Id == x.RequestId && r.RevisionNo == x.RevisionNo && r.Status == RequestStatuses.InApproval));
        if (activeOwnedDcr > 0 || activeApprovalLines > 0)
        {
            throw new InvalidOperationException(
                $"Không thể xóa user khi còn công việc đang mở: {activeOwnedDcr} DCR Bản nháp/Trả về/Đang phê duyệt do user sở hữu và {activeApprovalLines} approval line Pending/Waiting. Hãy hoàn tất/chuyển giao các DCR này trước.");
        }

        var activeDirectReportCount = await db.Users.CountAsync(x =>
            x.DirectManagerUserId == userId && x.IsActive && !x.IsDeleted);
        if (activeDirectReportCount > 0)
            throw new InvalidOperationException(
                $"Không thể xóa user vì còn {activeDirectReportCount} người đang báo cáo trực tiếp. Hãy gán quản lý khác cho các nhân sự này trước.");

        // Preserve historical DCR/audit foreign keys while removing the account from all active use.
        var departments = await db.Departments
            .Where(x => x.ManagerUserId == userId || x.DirectorUserId == userId)
            .ToListAsync();
        foreach (var department in departments)
        {
            if (department.ManagerUserId == userId) department.ManagerUserId = null;
            if (department.DirectorUserId == userId) department.DirectorUserId = null;
        }

        var businessUnits = await db.BusinessUnits.Where(x => x.DirectorUserId == userId).ToListAsync();
        foreach (var unit in businessUnits) unit.DirectorUserId = null;

        var directReports = await db.Users.Where(x => x.DirectManagerUserId == userId && !x.IsDeleted).ToListAsync();
        foreach (var report in directReports)
            report.DirectManagerUserId = null;

        var draftPlanEntries = await db.DCRApprovalPlanEntries
            .Where(x => x.ApproverId == userId && db.DCRRequests.Any(r => r.Id == x.RequestId && (r.Status == RequestStatuses.Draft || r.Status == RequestStatuses.Returned)))
            .ToListAsync();
        if (draftPlanEntries.Count > 0)
            db.DCRApprovalPlanEntries.RemoveRange(draftPlanEntries);

        var rules = await db.ApprovalMatrixRules.Where(x => x.ApproverUserId == userId).ToListAsync();
        foreach (var rule in rules)
        {
            rule.IsActive = false;
            rule.Description = string.IsNullOrWhiteSpace(rule.Description)
                ? $"Disabled because user {user.Username} was deleted."
                : rule.Description + $" | Disabled because user {user.Username} was deleted.";
        }

        db.UserBusinessUnitAssignments.RemoveRange(
            await db.UserBusinessUnitAssignments.Where(x => x.UserId == userId).ToListAsync());
        db.UserDepartmentAssignments.RemoveRange(
            await db.UserDepartmentAssignments.Where(x => x.UserId == userId).ToListAsync());

        var stamp = DateTime.Now;
        user.IsActive = false;
        user.IsDeleted = true;
        user.DeletedAt = stamp;
        user.DeletedBy = actorUserId;
        user.WindowsAccount = string.Empty;
        user.Username = $"deleted_{user.UserId}_{stamp:yyyyMMddHHmmss}_{user.Username}";
        if (user.Username.Length > 80)
            user.Username = user.Username[..80];

        await db.SaveChangesAsync();
    }

    public async Task<List<BusinessUnit>> GetBusinessUnitsAsync(bool activeOnly = false)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        var query = db.BusinessUnits
            .AsNoTracking()
            .Include(x => x.DirectorUser)
            .Include(x => x.ParentBusinessUnit)
            .AsQueryable();
        if (activeOnly) query = query.Where(x => x.IsActive);
        return await query.OrderBy(x => x.UnitCode).ToListAsync();
    }

    public async Task<BusinessUnit> SaveBusinessUnitAsync(
        int? id,
        string code,
        string name,
        int? directorUserId,
        bool isActive,
        int? parentBusinessUnitId = null,
        string? unitType = null,
        int sortOrder = 0)
    {
        code = (code ?? string.Empty).Trim().ToUpperInvariant();
        name = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(code)) throw new InvalidOperationException("Mã Khối không được để trống.");
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Tên Khối không được để trống.");

        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        if (await db.BusinessUnits.AnyAsync(x => x.UnitCode == code && x.Id != id))
            throw new InvalidOperationException("Mã Khối đã tồn tại.");

        unitType = (unitType ?? OrganizationUnitTypes.Division).Trim();
        if (!OrganizationUnitTypes.All.Contains(unitType, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Loại đơn vị tổ chức không hợp lệ.");
        unitType = OrganizationUnitTypes.All.First(x => string.Equals(x, unitType, StringComparison.OrdinalIgnoreCase));
        if (parentBusinessUnitId.HasValue)
        {
            if (id.HasValue && parentBusinessUnitId.Value == id.Value)
                throw new InvalidOperationException("Đơn vị không thể là cấp trên của chính nó.");
            var parentExists = await db.BusinessUnits.AnyAsync(x => x.Id == parentBusinessUnitId.Value && x.IsActive);
            if (!parentExists)
                throw new InvalidOperationException("Đơn vị cấp trên không tồn tại hoặc đã ngừng hoạt động.");

            var ancestorId = (int?)parentBusinessUnitId.Value;
            var visited = new HashSet<int>();
            while (ancestorId.HasValue && visited.Add(ancestorId.Value))
            {
                if (id.HasValue && ancestorId.Value == id.Value)
                    throw new InvalidOperationException("Không thể tạo vòng lặp trong cây cơ cấu tổ chức.");
                ancestorId = await db.BusinessUnits.AsNoTracking()
                    .Where(x => x.Id == ancestorId.Value)
                    .Select(x => x.ParentBusinessUnitId)
                    .SingleOrDefaultAsync();
            }
        }

        User? director = null;
        if (directorUserId.HasValue)
        {
            director = await db.Users.SingleOrDefaultAsync(x => x.UserId == directorUserId.Value && x.IsActive && !x.IsDeleted)
                ?? throw new InvalidOperationException("Director/Block Head không tồn tại hoặc không hoạt động.");
            var directorLevel = await db.Roles.AsNoTracking()
                .Where(x => x.RoleName == RoleNames.Director)
                .Select(x => (int?)x.HierarchyLevel)
                .SingleOrDefaultAsync() ?? 30;
            var headLevel = await db.Roles.AsNoTracking()
                .Where(x => x.RoleName == director.Role && x.IsActive)
                .Select(x => (int?)x.HierarchyLevel)
                .SingleOrDefaultAsync() ?? 0;
            if (string.Equals(director.Role, RoleNames.Administrator, StringComparison.OrdinalIgnoreCase) || headLevel < directorLevel)
                throw new InvalidOperationException("Người đứng đầu Khối phải có Role Director, CTO, COO, DCEO, CEO hoặc role nghiệp vụ cấp cao hơn Director.");
        }

        await using var tx = await db.Database.BeginTransactionAsync();
        BusinessUnit unit;
        if (id.HasValue) unit = await db.BusinessUnits.SingleAsync(x => x.Id == id.Value);
        else { unit = new BusinessUnit(); db.BusinessUnits.Add(unit); }

        unit.UnitCode = code;
        unit.UnitName = name;
        unit.ParentBusinessUnitId = parentBusinessUnitId;
        unit.UnitType = unitType;
        unit.SortOrder = Math.Max(0, sortOrder);
        unit.IsActive = isActive;
        await db.SaveChangesAsync();

        unit.DirectorUserId = director?.UserId;
        if (director is not null)
        {
            var assignment = await db.UserBusinessUnitAssignments
                .SingleOrDefaultAsync(x => x.UserId == director.UserId && x.BusinessUnitId == unit.Id);
            if (assignment is null)
                db.UserBusinessUnitAssignments.Add(new UserBusinessUnitAssignment
                {
                    UserId = director.UserId,
                    BusinessUnitId = unit.Id,
                    IsPrimary = director.BusinessUnitId == unit.Id
                });
        }

        // Manager của mọi phòng ban trong Khối tự động báo cáo Director/Head của Khối.
        var departmentIds = await db.Departments.Where(x => x.BusinessUnitId == unit.Id && x.ManagerUserId.HasValue).Select(x => x.ManagerUserId!.Value).ToListAsync();
        var managers = await db.Users.Where(x => departmentIds.Contains(x.UserId) && !x.IsDeleted).ToListAsync();
        foreach (var manager in managers)
        {
            if (director is not null && manager.UserId != director.UserId)
                manager.DirectManagerUserId = director.UserId;
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return unit;
    }

    public async Task DeleteBusinessUnitAsync(int id)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        var unit = await db.BusinessUnits.SingleOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("Không tìm thấy Khối.");
        var deptCount = await db.Departments.CountAsync(x => x.BusinessUnitId == id);
        var childUnitCount = await db.BusinessUnits.CountAsync(x => x.ParentBusinessUnitId == id);
        var userCount = await db.Users.CountAsync(x => !x.IsDeleted &&
            (x.BusinessUnitId == id || x.BusinessUnitAssignments.Any(a => a.BusinessUnitId == id)));
        if (deptCount + childUnitCount + userCount > 0)
            throw new InvalidOperationException($"Không thể xóa đơn vị '{unit.UnitName}' vì còn {childUnitCount} đơn vị con, {deptCount} phòng ban và {userCount} user. Hãy chuyển các đối tượng này trước.");
        db.BusinessUnits.Remove(unit);
        await db.SaveChangesAsync();
    }

    public async Task<List<Department>> GetDepartmentsAsync(bool activeOnly = false)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        var query = db.Departments
            .AsNoTracking()
            .Include(x => x.BusinessUnit)
                .ThenInclude(x => x!.DirectorUser)
            .Include(x => x.ManagerUser)
            .AsQueryable();

        if (activeOnly) query = query.Where(x => x.IsActive);
        return await query.OrderBy(x => x.DepartmentCode).ToListAsync();
    }

    public async Task<User> SaveUserModulePermissionsAsync(
        int userId,
        bool canUseProductionTracking,
        bool canUseWarehouseManagement)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        var user = await db.Users.SingleOrDefaultAsync(x => x.UserId == userId && !x.IsDeleted)
            ?? throw new InvalidOperationException("Người dùng không tồn tại.");

        user.CanUseProductionTracking = canUseProductionTracking;
        user.CanUseWarehouseManagement = canUseWarehouseManagement;
        await db.SaveChangesAsync();
        return user;
    }

    public async Task<User> SaveUserAsync(
        int? userId,
        string username,
        string fullName,
        string email,
        string phone,
        int? businessUnitId,
        int? departmentId,
        IReadOnlyCollection<int>? businessUnitIds,
        IReadOnlyCollection<int>? departmentIds,
        int? directManagerUserId,
        string role,
        bool isActive,
        string? newPassword)
    {
        username = (username ?? string.Empty).Trim();
        fullName = (fullName ?? string.Empty).Trim();
        role = (role ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(username)) throw new InvalidOperationException("Username không được để trống.");
        if (string.IsNullOrWhiteSpace(fullName)) throw new InvalidOperationException("Họ tên không được để trống.");
        if (!string.IsNullOrWhiteSpace(newPassword) && newPassword.Length < 8)
            throw new InvalidOperationException("Mật khẩu phải có ít nhất 8 ký tự.");

        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        var roleDef = await db.Roles.AsNoTracking().SingleOrDefaultAsync(x => x.RoleName == role && x.IsActive)
            ?? throw new InvalidOperationException("Role không tồn tại hoặc đã bị vô hiệu hóa.");
        var managerLevel = await db.Roles.AsNoTracking().Where(x => x.RoleName == RoleNames.Manager).Select(x => (int?)x.HierarchyLevel).SingleOrDefaultAsync() ?? 20;
        var directorLevel = await db.Roles.AsNoTracking().Where(x => x.RoleName == RoleNames.Director).Select(x => (int?)x.HierarchyLevel).SingleOrDefaultAsync() ?? 30;
        var isAdministrator = string.Equals(role, RoleNames.Administrator, StringComparison.OrdinalIgnoreCase);
        var isStaff = string.Equals(role, RoleNames.Staff, StringComparison.OrdinalIgnoreCase);
        var isDepartmentLeadership = !isAdministrator && roleDef.HierarchyLevel >= managerLevel && roleDef.HierarchyLevel < directorLevel;
        var isBlockLeadership = !isAdministrator && roleDef.HierarchyLevel >= directorLevel;

        var selectedDepartmentIds = (departmentIds ?? Array.Empty<int>()).Where(x => x > 0).ToHashSet();
        if (departmentId.HasValue) selectedDepartmentIds.Add(departmentId.Value);
        if (userId.HasValue && roleDef.HierarchyLevel >= managerLevel && !isAdministrator)
        {
            var headedDepartmentIds = await db.Departments.AsNoTracking()
                .Where(x => x.ManagerUserId == userId.Value && x.IsActive)
                .Select(x => x.Id)
                .ToListAsync();
            selectedDepartmentIds.UnionWith(headedDepartmentIds);
        }
        var selectedDepartments = await db.Departments.AsNoTracking()
            .Where(x => selectedDepartmentIds.Contains(x.Id) && x.IsActive)
            .ToListAsync();
        if (selectedDepartments.Count != selectedDepartmentIds.Count)
            throw new InvalidOperationException("Một hoặc nhiều Phòng ban kiêm nhiệm không tồn tại hoặc đã ngừng hoạt động.");

        var selectedBusinessUnitIds = (businessUnitIds ?? Array.Empty<int>()).Where(x => x > 0).ToHashSet();
        if (businessUnitId.HasValue) selectedBusinessUnitIds.Add(businessUnitId.Value);
        if (userId.HasValue && roleDef.HierarchyLevel >= directorLevel && !isAdministrator)
        {
            var headedUnitIds = await db.BusinessUnits.AsNoTracking()
                .Where(x => x.DirectorUserId == userId.Value && x.IsActive)
                .Select(x => x.Id)
                .ToListAsync();
            selectedBusinessUnitIds.UnionWith(headedUnitIds);
        }
        foreach (var departmentItem in selectedDepartments.Where(x => x.BusinessUnitId.HasValue))
            selectedBusinessUnitIds.Add(departmentItem.BusinessUnitId!.Value);
        var selectedUnits = await db.BusinessUnits.AsNoTracking()
            .Where(x => selectedBusinessUnitIds.Contains(x.Id) && x.IsActive)
            .ToListAsync();
        if (selectedUnits.Count != selectedBusinessUnitIds.Count)
            throw new InvalidOperationException("Một hoặc nhiều Khối kiêm nhiệm không tồn tại hoặc đã ngừng hoạt động.");

        Department? department = departmentId.HasValue
            ? selectedDepartments.FirstOrDefault(x => x.Id == departmentId.Value)
            : null;
        if ((isStaff || isDepartmentLeadership) && department is null)
        {
            department = selectedDepartments.OrderBy(x => x.Id).FirstOrDefault();
            departmentId = department?.Id;
        }
        if (department?.BusinessUnitId is int departmentUnitId)
            businessUnitId = departmentUnitId;
        if (!businessUnitId.HasValue)
            businessUnitId = selectedUnits.OrderBy(x => x.Id).Select(x => (int?)x.Id).FirstOrDefault();
        var unit = businessUnitId.HasValue ? selectedUnits.FirstOrDefault(x => x.Id == businessUnitId.Value) : null;

        if (isStaff && selectedDepartments.Count == 0)
            throw new InvalidOperationException("User Role Staff phải được phân công ít nhất một Phòng ban.");
        if (isDepartmentLeadership && selectedDepartments.Count == 0)
            throw new InvalidOperationException("Người dùng cấp Manager trở lên nhưng dưới Director phải được phân công ít nhất một Phòng ban.");
        if (isBlockLeadership && selectedUnits.Count == 0)
            throw new InvalidOperationException("Director/CTO/COO/DCEO/CEO hoặc cấp tương đương phải được phân công ít nhất một Khối hoặc Phòng ban.");

        // Manager/Director của đơn vị có thể để trống. Riêng Staff luôn phải có ít nhất
        // một cấp quản lý trực tiếp; ưu tiên Head của Phòng ban chính nếu đã cấu hình.
        if (isStaff && !directManagerUserId.HasValue)
            directManagerUserId = department?.ManagerUserId;
        if (isStaff && !directManagerUserId.HasValue)
            throw new InvalidOperationException("Staff phải có ít nhất một cấp quản lý trực tiếp. Hãy chọn Direct Manager hoặc cấu hình Head cho Phòng ban.");
        if (isDepartmentLeadership && !directManagerUserId.HasValue)
            directManagerUserId = await FindNearestUnitHeadIdAsync(db, unit?.Id);

        // Director and higher keep their primary legacy DepartmentId empty, while the
        // many-to-many assignment table retains every department they concurrently lead.
        if (isBlockLeadership)
            departmentId = null;

        if (directManagerUserId.HasValue)
        {
            if (userId.HasValue && directManagerUserId.Value == userId.Value)
                throw new InvalidOperationException("Người dùng không thể là cấp quản lý trực tiếp của chính mình.");
            var manager = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == directManagerUserId.Value && x.IsActive && !x.IsDeleted)
                ?? throw new InvalidOperationException("Cấp quản lý trực tiếp không tồn tại hoặc không hoạt động.");
            var directManagerLevel = await db.Roles.AsNoTracking().Where(x => x.RoleName == manager.Role && x.IsActive).Select(x => (int?)x.HierarchyLevel).SingleOrDefaultAsync() ?? 0;
            if (directManagerLevel <= roleDef.HierarchyLevel)
                throw new InvalidOperationException("Cấp quản lý trực tiếp phải có Role cao hơn người dùng.");

            if (isStaff)
            {
                var managerDepartmentIds = await db.UserDepartmentAssignments.AsNoTracking()
                    .Where(x => x.UserId == manager.UserId)
                    .Select(x => x.DepartmentId)
                    .ToListAsync();
                if (manager.DepartmentId.HasValue) managerDepartmentIds.Add(manager.DepartmentId.Value);
                var explicitlyHeadsSelectedDepartment = selectedDepartments.Any(x => x.ManagerUserId == manager.UserId);
                if (!explicitlyHeadsSelectedDepartment && !managerDepartmentIds.Any(selectedDepartmentIds.Contains))
                    throw new InvalidOperationException("Direct Manager của Staff phải cùng ít nhất một Phòng ban kiêm nhiệm hoặc là Head được cấu hình cho Phòng ban đó.");
            }
        }

        if (await db.Users.AnyAsync(x => !x.IsDeleted && x.Username == username && x.UserId != userId))
            throw new InvalidOperationException("Username đã tồn tại.");
        if (userId.HasValue)
        {
            var highestDirectReportLevel = await (
                from report in db.Users.AsNoTracking()
                join reportRole in db.Roles.AsNoTracking() on report.Role equals reportRole.RoleName
                where report.DirectManagerUserId == userId.Value && report.IsActive && !report.IsDeleted
                select (int?)reportRole.HierarchyLevel).MaxAsync() ?? -1;
            if (highestDirectReportLevel >= 0 && (!isActive || roleDef.HierarchyLevel <= highestDirectReportLevel))
                throw new InvalidOperationException("Không thể vô hiệu hóa hoặc hạ Role vì vẫn còn nhân sự đang báo cáo trực tiếp. Hãy chuyển Direct Manager của họ trước.");
        }
        var isNewUser = !userId.HasValue;
        await using var transaction = await db.Database.BeginTransactionAsync();
        User user;
        if (userId.HasValue)
        {
            user = await db.Users.SingleAsync(x => x.UserId == userId.Value && !x.IsDeleted);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(newPassword))
                throw new InvalidOperationException("Người dùng mới phải có mật khẩu để đăng nhập lần đầu. Sau lần đăng nhập thành công, user có thể chọn ghi nhớ Windows/AD trên máy đang sử dụng.");
            user = new User { CreatedAt = DateTime.Now };
            db.Users.Add(user);
        }

        user.Username = username;
        user.FullName = fullName;
        user.Email = (email ?? string.Empty).Trim();
        user.Phone = (phone ?? string.Empty).Trim();
        user.WindowsAccount = string.Empty; // legacy field retained in DB; no longer used as an authentication authority.
        user.BusinessUnitId = businessUnitId;
        user.DepartmentId = departmentId;
        user.DirectManagerUserId = directManagerUserId;
        user.Role = role;
        user.IsActive = isActive;
        if (!string.IsNullOrWhiteSpace(newPassword)) user.PasswordHash = PasswordHasher.Hash(newPassword);
        await db.SaveChangesAsync();

        await ReplaceUserOrganizationAssignmentsAsync(
            db,
            user.UserId,
            selectedBusinessUnitIds,
            selectedDepartmentIds,
            businessUnitId,
            departmentId,
            role,
            directManagerUserId);

        // Creating a leadership account also fills vacant organizational Head positions.
        // Conditional updates prevent concurrent requests from overwriting an existing Head.
        if (isNewUser && isActive && !isAdministrator)
        {
            if (roleDef.HierarchyLevel >= managerLevel)
            {
                foreach (var selectedDepartmentId in selectedDepartmentIds)
                {
                    await db.Departments
                        .Where(x => x.Id == selectedDepartmentId && x.IsActive && x.ManagerUserId == null)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ManagerUserId, user.UserId));
                }
            }

            if (roleDef.HierarchyLevel >= directorLevel)
            {
                foreach (var selectedUnitId in selectedBusinessUnitIds)
                {
                    await db.BusinessUnits
                        .Where(x => x.Id == selectedUnitId && x.IsActive && x.DirectorUserId == null)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.DirectorUserId, user.UserId));
                }
            }
        }

        if (roleDef.HierarchyLevel < managerLevel)
        {
            var headedDepartments = await db.Departments.Where(x => x.ManagerUserId == user.UserId).ToListAsync();
            foreach (var headedDepartment in headedDepartments)
                headedDepartment.ManagerUserId = null;
        }
        if (roleDef.HierarchyLevel < directorLevel)
        {
            var headedUnits = await db.BusinessUnits.Where(x => x.DirectorUserId == user.UserId).ToListAsync();
            foreach (var headedUnit in headedUnits)
                headedUnit.DirectorUserId = null;
        }
        await db.SaveChangesAsync();

        await transaction.CommitAsync();
        await db.Entry(user).Collection(x => x.BusinessUnitAssignments).Query().Include(x => x.BusinessUnit).LoadAsync();
        await db.Entry(user).Collection(x => x.DepartmentAssignments).Query().Include(x => x.Department).LoadAsync();
        return user;
    }

    private static async Task ReplaceUserOrganizationAssignmentsAsync(
        AppDbContext db,
        int userId,
        IReadOnlySet<int> businessUnitIds,
        IReadOnlySet<int> departmentIds,
        int? primaryBusinessUnitId,
        int? primaryDepartmentId,
        string fallbackJobTitle,
        int? fallbackReportsToUserId)
    {
        var currentUnits = await db.UserBusinessUnitAssignments.Where(x => x.UserId == userId).ToListAsync();
        db.UserBusinessUnitAssignments.RemoveRange(currentUnits.Where(x => !businessUnitIds.Contains(x.BusinessUnitId)));
        foreach (var unitId in businessUnitIds)
        {
            var assignment = currentUnits.FirstOrDefault(x => x.BusinessUnitId == unitId);
            if (assignment is null)
            {
                assignment = new UserBusinessUnitAssignment
                {
                    UserId = userId,
                    BusinessUnitId = unitId,
                    JobTitle = fallbackJobTitle,
                    ReportsToUserId = fallbackReportsToUserId
                };
                db.UserBusinessUnitAssignments.Add(assignment);
            }
            assignment.IsPrimary = primaryBusinessUnitId == unitId;
        }

        var currentDepartments = await db.UserDepartmentAssignments.Where(x => x.UserId == userId).ToListAsync();
        db.UserDepartmentAssignments.RemoveRange(currentDepartments.Where(x => !departmentIds.Contains(x.DepartmentId)));
        foreach (var departmentId in departmentIds)
        {
            var assignment = currentDepartments.FirstOrDefault(x => x.DepartmentId == departmentId);
            if (assignment is null)
            {
                assignment = new UserDepartmentAssignment
                {
                    UserId = userId,
                    DepartmentId = departmentId,
                    JobTitle = fallbackJobTitle,
                    ReportsToUserId = fallbackReportsToUserId
                };
                db.UserDepartmentAssignments.Add(assignment);
            }
            assignment.IsPrimary = primaryDepartmentId == departmentId;
        }
        await db.SaveChangesAsync();
    }

    public async Task SaveOrganizationAssignmentDetailsAsync(
        int userId,
        int? businessUnitId,
        int? departmentId,
        string jobTitle,
        int? reportsToUserId,
        bool isActing,
        int sortOrder)
    {
        if (businessUnitId.HasValue == departmentId.HasValue)
            throw new InvalidOperationException("Phải chọn đúng một phạm vi: Đơn vị tổ chức hoặc Phòng ban.");

        jobTitle = (jobTitle ?? string.Empty).Trim();
        if (jobTitle.Length > 200)
            throw new InvalidOperationException("Chức danh tối đa 200 ký tự.");

        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        var user = await db.Users.SingleOrDefaultAsync(x => x.UserId == userId && x.IsActive && !x.IsDeleted)
            ?? throw new InvalidOperationException("Người dùng không tồn tại hoặc không hoạt động.");

        if (reportsToUserId.HasValue)
        {
            if (reportsToUserId.Value == userId)
                throw new InvalidOperationException("Người dùng không thể báo cáo cho chính mình.");
            if (!await db.Users.AnyAsync(x => x.UserId == reportsToUserId.Value && x.IsActive && !x.IsDeleted))
                throw new InvalidOperationException("Người quản lý theo nhiệm vụ không tồn tại hoặc không hoạt động.");
        }

        if (businessUnitId.HasValue)
        {
            if (!await db.BusinessUnits.AnyAsync(x => x.Id == businessUnitId.Value))
                throw new InvalidOperationException("Đơn vị tổ chức không tồn tại.");
            var assignment = await db.UserBusinessUnitAssignments
                .SingleOrDefaultAsync(x => x.UserId == userId && x.BusinessUnitId == businessUnitId.Value);
            if (assignment is null)
            {
                assignment = new UserBusinessUnitAssignment { UserId = userId, BusinessUnitId = businessUnitId.Value };
                db.UserBusinessUnitAssignments.Add(assignment);
            }
            assignment.JobTitle = jobTitle;
            assignment.ReportsToUserId = reportsToUserId;
            assignment.IsActing = isActing;
            assignment.SortOrder = Math.Max(0, sortOrder);
            if (assignment.IsPrimary)
                user.DirectManagerUserId = reportsToUserId;
        }
        else
        {
            if (!await db.Departments.AnyAsync(x => x.Id == departmentId!.Value))
                throw new InvalidOperationException("Phòng ban không tồn tại.");
            var assignment = await db.UserDepartmentAssignments
                .SingleOrDefaultAsync(x => x.UserId == userId && x.DepartmentId == departmentId.Value);
            if (assignment is null)
            {
                assignment = new UserDepartmentAssignment { UserId = userId, DepartmentId = departmentId.Value };
                db.UserDepartmentAssignments.Add(assignment);
            }
            assignment.JobTitle = jobTitle;
            assignment.ReportsToUserId = reportsToUserId;
            assignment.IsActing = isActing;
            assignment.SortOrder = Math.Max(0, sortOrder);
            if (assignment.IsPrimary)
                user.DirectManagerUserId = reportsToUserId;
        }

        await db.SaveChangesAsync();
    }

    public async Task<string> NormalizeOrganizationLinksAsync()
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var users = await db.Users
            .Include(x => x.BusinessUnitAssignments)
            .Include(x => x.DepartmentAssignments)
            .Where(x => !x.IsDeleted)
            .ToListAsync();
        var departments = await db.Departments.AsNoTracking().ToDictionaryAsync(x => x.Id);
        var units = await db.BusinessUnits.AsNoTracking().ToDictionaryAsync(x => x.Id);
        var unitAssignmentKeys = users.SelectMany(x => x.BusinessUnitAssignments.Select(a => (x.UserId, a.BusinessUnitId))).ToHashSet();
        var departmentAssignmentKeys = users.SelectMany(x => x.DepartmentAssignments.Select(a => (x.UserId, a.DepartmentId))).ToHashSet();
        var fixes = 0;
        var managerSuggestions = 0;

        foreach (var user in users)
        {
            if (user.DepartmentId.HasValue && departments.TryGetValue(user.DepartmentId.Value, out var primaryDepartment))
            {
                if (departmentAssignmentKeys.Add((user.UserId, primaryDepartment.Id)))
                {
                    db.UserDepartmentAssignments.Add(new UserDepartmentAssignment
                    {
                        UserId = user.UserId,
                        DepartmentId = primaryDepartment.Id,
                        IsPrimary = true,
                        JobTitle = user.Role,
                        ReportsToUserId = user.DirectManagerUserId
                    });
                    fixes++;
                }

                if (primaryDepartment.BusinessUnitId.HasValue && user.BusinessUnitId != primaryDepartment.BusinessUnitId)
                {
                    user.BusinessUnitId = primaryDepartment.BusinessUnitId;
                    fixes++;
                }
            }

            var assignedDepartmentIds = user.DepartmentAssignments.Select(x => x.DepartmentId).ToList();
            if (user.DepartmentId.HasValue) assignedDepartmentIds.Add(user.DepartmentId.Value);
            foreach (var assignedDepartmentId in assignedDepartmentIds.Distinct())
            {
                if (!departments.TryGetValue(assignedDepartmentId, out var assignedDepartment) || !assignedDepartment.BusinessUnitId.HasValue)
                    continue;
                var assignedUnitId = assignedDepartment.BusinessUnitId.Value;
                if (!units.ContainsKey(assignedUnitId) || !unitAssignmentKeys.Add((user.UserId, assignedUnitId)))
                    continue;
                db.UserBusinessUnitAssignments.Add(new UserBusinessUnitAssignment
                {
                    UserId = user.UserId,
                    BusinessUnitId = assignedUnitId,
                    IsPrimary = user.BusinessUnitId == assignedUnitId,
                    JobTitle = user.Role,
                    ReportsToUserId = user.DirectManagerUserId
                });
                fixes++;
            }

            if (string.Equals(user.Role, RoleNames.Staff, StringComparison.OrdinalIgnoreCase) && !user.DirectManagerUserId.HasValue)
            {
                var suggestedManager = assignedDepartmentIds
                    .Distinct()
                    .Select(id => departments.TryGetValue(id, out var item) ? item.ManagerUserId : null)
                    .FirstOrDefault(id => id.HasValue && id.Value != user.UserId);
                if (suggestedManager.HasValue)
                {
                    user.DirectManagerUserId = suggestedManager;
                    managerSuggestions++;
                }
            }
        }

        foreach (var unit in units.Values.Where(x => x.DirectorUserId.HasValue))
        {
            var head = users.FirstOrDefault(x => x.UserId == unit.DirectorUserId.Value);
            if (head is null || !unitAssignmentKeys.Add((head.UserId, unit.Id))) continue;
            db.UserBusinessUnitAssignments.Add(new UserBusinessUnitAssignment
            {
                UserId = head.UserId,
                BusinessUnitId = unit.Id,
                JobTitle = head.Role,
                ReportsToUserId = head.DirectManagerUserId
            });
            fixes++;
        }
        foreach (var department in departments.Values.Where(x => x.ManagerUserId.HasValue))
        {
            var head = users.FirstOrDefault(x => x.UserId == department.ManagerUserId.Value);
            if (head is null || !departmentAssignmentKeys.Add((head.UserId, department.Id))) continue;
            db.UserDepartmentAssignments.Add(new UserDepartmentAssignment
            {
                UserId = head.UserId,
                DepartmentId = department.Id,
                JobTitle = head.Role,
                ReportsToUserId = head.DirectManagerUserId
            });
            fixes++;
        }

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return $"Đã chuẩn hóa {fixes} liên kết đơn vị/phòng ban và gợi ý quản lý trực tiếp cho {managerSuggestions} Staff. Không có dữ liệu nào bị xóa hoặc đổi tên.";
    }

    public async Task<Department> SaveDepartmentAsync(
        int? id,
        string code,
        string name,
        int? businessUnitId,
        int? managerUserId,
        bool isActive)
    {
        code = (code ?? string.Empty).Trim().ToUpperInvariant();
        name = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(code)) throw new InvalidOperationException("Mã phòng ban không được để trống.");
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Tên phòng ban không được để trống.");
        if (!businessUnitId.HasValue) throw new InvalidOperationException("Phòng ban phải thuộc một Khối.");

        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        var unit = await db.BusinessUnits.SingleOrDefaultAsync(x => x.Id == businessUnitId.Value && x.IsActive)
            ?? throw new InvalidOperationException("Khối không tồn tại hoặc không hoạt động.");
        if (await db.Departments.AnyAsync(x => x.DepartmentCode == code && x.Id != id))
            throw new InvalidOperationException("Mã phòng ban đã tồn tại.");

        Department department;
        if (id.HasValue) department = await db.Departments.SingleAsync(x => x.Id == id.Value);
        else { department = new Department(); db.Departments.Add(department); }

        User? manager = null;
        if (managerUserId.HasValue)
        {
            manager = await db.Users.SingleOrDefaultAsync(x => x.UserId == managerUserId.Value && x.IsActive && !x.IsDeleted)
                ?? throw new InvalidOperationException("Manager không tồn tại hoặc không hoạt động.");
            var managerLevel = await db.Roles.AsNoTracking()
                .Where(x => x.RoleName == RoleNames.Manager)
                .Select(x => (int?)x.HierarchyLevel)
                .SingleOrDefaultAsync() ?? 20;
            var headLevel = await db.Roles.AsNoTracking()
                .Where(x => x.RoleName == manager.Role && x.IsActive)
                .Select(x => (int?)x.HierarchyLevel)
                .SingleOrDefaultAsync() ?? 0;
            if (string.Equals(manager.Role, RoleNames.Administrator, StringComparison.OrdinalIgnoreCase) || headLevel < managerLevel)
                throw new InvalidOperationException("Người đứng đầu Phòng ban phải có Role Manager, Director, CTO, COO, DCEO, CEO hoặc role nghiệp vụ cấp cao hơn Manager.");
        }

        department.DepartmentCode = code;
        department.DepartmentName = name;
        department.BusinessUnitId = unit.Id;
        department.ManagerUserId = managerUserId;
        department.DirectorUserId = null; // legacy column is no longer used
        department.IsActive = isActive;
        await db.SaveChangesAsync();

        if (manager is not null)
        {
            if (!await db.UserDepartmentAssignments.AnyAsync(x => x.UserId == manager.UserId && x.DepartmentId == department.Id))
                db.UserDepartmentAssignments.Add(new UserDepartmentAssignment
                {
                    UserId = manager.UserId,
                    DepartmentId = department.Id,
                    IsPrimary = manager.DepartmentId == department.Id
                });
            if (!await db.UserBusinessUnitAssignments.AnyAsync(x => x.UserId == manager.UserId && x.BusinessUnitId == unit.Id))
                db.UserBusinessUnitAssignments.Add(new UserBusinessUnitAssignment
                {
                    UserId = manager.UserId,
                    BusinessUnitId = unit.Id,
                    IsPrimary = manager.BusinessUnitId == unit.Id
                });
            var nearestUnitHeadId = await FindNearestUnitHeadIdAsync(db, unit.Id);
            if (nearestUnitHeadId.HasValue && nearestUnitHeadId.Value != manager.UserId)
                manager.DirectManagerUserId = nearestUnitHeadId;
            await db.SaveChangesAsync();
        }
        return department;
    }

    private static async Task<int?> FindNearestUnitHeadIdAsync(AppDbContext db, int? businessUnitId)
    {
        var visited = new HashSet<int>();
        var currentId = businessUnitId;
        while (currentId.HasValue && visited.Add(currentId.Value))
        {
            var unit = await db.BusinessUnits.AsNoTracking()
                .Where(x => x.Id == currentId.Value && x.IsActive)
                .Select(x => new { x.DirectorUserId, x.ParentBusinessUnitId })
                .SingleOrDefaultAsync();
            if (unit is null) return null;
            if (unit.DirectorUserId.HasValue) return unit.DirectorUserId;
            currentId = unit.ParentBusinessUnitId;
        }

        return null;
    }

    public async Task DeleteDepartmentAsync(int id)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

        var department = await db.Departments.SingleOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("Không tìm thấy phòng ban.");

        var activeUserCount = await db.Users.CountAsync(x => !x.IsDeleted &&
            (x.DepartmentId == id || x.DepartmentAssignments.Any(a => a.DepartmentId == id)));
        var requestingDcrCount = await db.DCRRequests.CountAsync(x => x.RequestingDepartmentId == id);
        var impactedDcrCount = await db.DCRImpactedDepartments.CountAsync(x => x.DepartmentId == id);
        var approvalHistoryCount = await db.DCRApprovalFlows.CountAsync(x => x.DepartmentId == id);
        var matrixRuleCount = await db.ApprovalMatrixRules.CountAsync(x =>
            x.RequestingDepartmentId == id || x.TargetDepartmentId == id);

        if (activeUserCount + requestingDcrCount + impactedDcrCount + approvalHistoryCount + matrixRuleCount > 0)
        {
            throw new InvalidOperationException(
                $"Không thể xóa phòng ban '{department.DepartmentName}' vì đang được sử dụng: " +
                $"{activeUserCount} người dùng, {requestingDcrCount} DCR yêu cầu, " +
                $"{impactedDcrCount} liên kết phòng ban ảnh hưởng, {approvalHistoryCount} dòng lịch sử phê duyệt và " +
                $"{matrixRuleCount} quy tắc ma trận phê duyệt. Hãy chuyển các tham chiếu sang phòng ban khác; " +
                "nếu cần giữ lịch sử, hãy sửa phòng ban và bỏ chọn 'Đang hoạt động' thay vì xóa.");
        }

        // Soft-deleted accounts are no longer editable but still retain a restricted FK.
        // Detach only that obsolete organizational reference so an otherwise-unused
        // department can be removed without altering DCR or approval history.
        var archivedUsers = await db.Users.Where(x => x.DepartmentId == id && x.IsDeleted).ToListAsync();
        foreach (var user in archivedUsers)
            user.DepartmentId = null;

        db.Departments.Remove(department);
        try
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException ex)
        {
            await transaction.RollbackAsync();
            throw new InvalidOperationException(
                $"Không thể xóa phòng ban '{department.DepartmentName}' vì vẫn còn dữ liệu liên quan. " +
                "Hãy làm mới dữ liệu, chuyển các tham chiếu hoặc vô hiệu hóa phòng ban.", ex);
        }
    }

    public async Task<List<WorkflowStageTemplate>> GetWorkflowTemplatesAsync()
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();

        return await db.WorkflowStageTemplates
            .AsNoTracking()
            .OrderBy(x => x.StageNumber)
            .ToListAsync();
    }

    public async Task SaveWorkflowTemplatesAsync(IEnumerable<WorkflowStageTemplate> templates)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        var validRoles = await db.Roles.AsNoTracking().Where(x => x.IsActive).Select(x => x.RoleName).ToListAsync();

        foreach (var input in templates)
        {
            if (!string.IsNullOrWhiteSpace(input.ApproverRole) && !validRoles.Contains(input.ApproverRole))
                throw new InvalidOperationException($"Role '{input.ApproverRole}' không tồn tại hoặc không hoạt động.");
            var entity = await db.WorkflowStageTemplates.SingleAsync(x => x.Id == input.Id);
            entity.StageName = input.StageName.Trim();
            entity.ApproverRole = input.ApproverRole.Trim();
            entity.IsActive = input.IsActive;
        }

        await db.SaveChangesAsync();
    }

    public async Task<List<ApprovalPlanTemplateEditModel>> GetApprovalPlanTemplatesAsync(bool activeOnly = false, string? rank = null)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();

        var query = db.ApprovalPlanTemplates
            .AsNoTracking()
            .Include(x => x.Entries)
                .ThenInclude(x => x.Approver)
                    .ThenInclude(x => x!.Department)
                        .ThenInclude(x => x!.BusinessUnit)
            .Include(x => x.Entries)
                .ThenInclude(x => x.Approver)
                    .ThenInclude(x => x!.BusinessUnit)
            .AsQueryable();
        if (activeOnly) query = query.Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(rank))
        {
            var normalizedRank = DcrRanks.Normalize(rank);
            query = query.Where(x => x.Rank == normalizedRank);
        }

        var rows = await query
            .OrderBy(x => x.Rank)
            .ThenByDescending(x => x.IsDefault)
            .ThenBy(x => x.Name)
            .ToListAsync();
        return rows.Select(ToApprovalPlanTemplateModel).ToList();
    }

    public async Task<ApprovalPlanTemplateEditModel> SaveApprovalPlanTemplateAsync(ApprovalPlanTemplateEditModel input)
    {
        var name = (input.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Tên cấu hình luồng phê duyệt không được để trống.");
        if (name.Length > 160)
            throw new InvalidOperationException("Tên cấu hình luồng phê duyệt tối đa 160 ký tự.");

        var rank = DcrRanks.Normalize(input.Rank);
        var plan = NormalizeApprovalPlanTemplate(input.ApprovalPlan);

        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        var approverIds = plan.Select(x => x.ApproverId).ToList();
        var approvers = await db.Users
            .AsNoTracking()
            .Where(x => approverIds.Contains(x.UserId))
            .ToListAsync();
        var unavailable = approverIds
            .Where(id => approvers.All(x => x.UserId != id || !x.IsActive || x.IsDeleted || string.IsNullOrWhiteSpace(x.Email)))
            .Distinct()
            .ToList();
        if (unavailable.Count > 0)
            throw new InvalidOperationException("Cấu hình có người duyệt không hoạt động, đã bị xóa hoặc chưa có email: " + string.Join(", ", unavailable.Select(x => $"UserId={x}")) + ".");

        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var entity = input.Id > 0
            ? await db.ApprovalPlanTemplates.Include(x => x.Entries).SingleOrDefaultAsync(x => x.Id == input.Id)
            : null;
        if (input.Id > 0 && entity is null)
            throw new InvalidOperationException("Cấu hình luồng phê duyệt không còn tồn tại. Hãy làm mới dữ liệu.");
        if (await db.ApprovalPlanTemplates.AnyAsync(x => x.Id != input.Id && x.Rank == rank && x.Name == name))
            throw new InvalidOperationException($"Rank {rank} đã có cấu hình tên '{name}'.");

        var oldRank = entity?.Rank;
        var wasOldDefault = entity?.IsDefault == true;
        if (entity is null)
        {
            entity = new ApprovalPlanTemplate { CreatedAt = DateTime.Now };
            db.ApprovalPlanTemplates.Add(entity);
        }
        else if (entity.Entries.Count > 0)
        {
            db.ApprovalPlanTemplateEntries.RemoveRange(entity.Entries);
        }

        var hasOtherInRank = await db.ApprovalPlanTemplates.AnyAsync(x => x.Id != input.Id && x.Rank == rank && x.IsActive);
        var makeDefault = input.IsActive && (input.IsDefault || !hasOtherInRank);
        if (makeDefault)
        {
            await db.ApprovalPlanTemplates
                .Where(x => x.Id != input.Id && x.Rank == rank && x.IsDefault)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsDefault, false));
        }

        entity.Name = name;
        entity.Rank = rank;
        entity.IsDefault = makeDefault;
        entity.IsActive = input.IsActive;
        entity.UpdatedAt = DateTime.Now;
        entity.Entries = plan.Select(x => new ApprovalPlanTemplateEntry
        {
            LevelNumber = x.LevelNumber,
            LevelName = x.LevelName,
            ApproverId = x.ApproverId,
            Sequence = x.Sequence
        }).ToList();

        await db.SaveChangesAsync();

        if (wasOldDefault && (!string.Equals(oldRank, rank, StringComparison.Ordinal) || !entity.IsDefault))
        {
            var replacement = await db.ApprovalPlanTemplates
                .Where(x => x.Id != entity.Id && x.Rank == oldRank && x.IsActive)
                .OrderBy(x => x.Name)
                .FirstOrDefaultAsync();
            if (replacement is not null)
            {
                replacement.IsDefault = true;
                replacement.UpdatedAt = DateTime.Now;
                await db.SaveChangesAsync();
            }
        }

        await transaction.CommitAsync();
        return (await GetApprovalPlanTemplatesAsync(false, rank)).Single(x => x.Id == entity.Id);
    }

    public async Task DeleteApprovalPlanTemplateAsync(int templateId)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var entity = await db.ApprovalPlanTemplates.SingleOrDefaultAsync(x => x.Id == templateId)
            ?? throw new InvalidOperationException("Cấu hình luồng phê duyệt không còn tồn tại.");
        var rank = entity.Rank;
        var wasDefault = entity.IsDefault;
        db.ApprovalPlanTemplates.Remove(entity);
        await db.SaveChangesAsync();
        if (wasDefault)
        {
            var replacement = await db.ApprovalPlanTemplates
                .Where(x => x.Rank == rank && x.IsActive)
                .OrderBy(x => x.Name)
                .FirstOrDefaultAsync();
            if (replacement is not null)
            {
                replacement.IsDefault = true;
                replacement.UpdatedAt = DateTime.Now;
                await db.SaveChangesAsync();
            }
        }
        await transaction.CommitAsync();
    }

    private static List<ApprovalPlanEditItem> NormalizeApprovalPlanTemplate(IEnumerable<ApprovalPlanEditItem>? source)
    {
        var rows = (source ?? []).Where(x => x.ApproverId > 0).ToList();
        if (rows.Count == 0)
            throw new InvalidOperationException("Cấu hình phải có ít nhất một người phê duyệt.");
        if (rows.Count > 100)
            throw new InvalidOperationException("Cấu hình không được vượt quá 100 người phê duyệt.");
        if (rows.Any(x => x.LevelNumber <= 0))
            throw new InvalidOperationException("Cấp phê duyệt phải lớn hơn 0.");
        if (rows.GroupBy(x => x.ApproverId).Any(x => x.Count() > 1))
            throw new InvalidOperationException("Mỗi người chỉ được xuất hiện một lần trong một cấu hình.");

        var levels = rows.Select(x => x.LevelNumber).Distinct().OrderBy(x => x).ToList();
        if (!levels.SequenceEqual(Enumerable.Range(1, levels.Count)))
            throw new InvalidOperationException("Các cấp phê duyệt phải liên tục, bắt đầu từ cấp 1.");

        var result = new List<ApprovalPlanEditItem>();
        foreach (var group in rows.GroupBy(x => x.LevelNumber).OrderBy(x => x.Key))
        {
            var levelName = group.Select(x => x.LevelName?.Trim()).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                ?? $"Cấp phê duyệt {group.Key}";
            var sequence = 1;
            foreach (var row in group.OrderBy(x => x.Sequence).ThenBy(x => x.ApproverId))
            {
                result.Add(new ApprovalPlanEditItem
                {
                    LevelNumber = group.Key,
                    LevelName = levelName,
                    ApproverId = row.ApproverId,
                    Sequence = sequence++
                });
            }
        }
        return result;
    }

    private static ApprovalPlanTemplateEditModel ToApprovalPlanTemplateModel(ApprovalPlanTemplate template)
    {
        return new ApprovalPlanTemplateEditModel
        {
            Id = template.Id,
            Name = template.Name,
            Rank = DcrRanks.Normalize(template.Rank),
            IsDefault = template.IsDefault,
            IsActive = template.IsActive,
            UpdatedAt = template.UpdatedAt,
            ApprovalPlan = template.Entries.OrderBy(x => x.LevelNumber).ThenBy(x => x.Sequence).Select(x =>
            {
                var department = x.Approver?.Department;
                var unit = x.Approver?.BusinessUnit ?? department?.BusinessUnit;
                return new ApprovalPlanEditItem
                {
                    LevelNumber = x.LevelNumber,
                    LevelName = x.LevelName,
                    ApproverId = x.ApproverId,
                    ApproverName = x.Approver?.FullName ?? $"User #{x.ApproverId}",
                    ApproverEmail = x.Approver?.Email ?? string.Empty,
                    ApproverRole = x.Approver?.Role ?? string.Empty,
                    BusinessUnitId = unit?.Id,
                    BusinessUnitCode = unit?.UnitCode ?? string.Empty,
                    BusinessUnitName = unit?.UnitName ?? string.Empty,
                    DepartmentId = department?.Id,
                    DepartmentCode = department?.DepartmentCode ?? string.Empty,
                    DepartmentName = department?.DepartmentName ?? unit?.UnitName ?? string.Empty,
                    Sequence = x.Sequence
                };
            }).ToList()
        };
    }

    public async Task<List<ApprovalMatrixRule>> GetApprovalMatrixRulesAsync()
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        return await db.ApprovalMatrixRules
            .AsNoTracking()
            .Include(x => x.RequestingDepartment)
            .Include(x => x.TargetDepartment)
            .Include(x => x.ApproverUser)
            .OrderBy(x => x.StageCode)
            .ThenBy(x => x.Priority)
            .ThenBy(x => x.Id)
            .ToListAsync();
    }

    public async Task ReplaceApprovalMatrixRulesAsync(IEnumerable<ApprovalMatrixRule> inputRules)
    {
        var rules = inputRules.ToList();
        if (rules.Count == 0)
        {
            throw new InvalidOperationException("Approval Matrix phải có ít nhất một rule.");
        }

        foreach (var rule in rules)
        {
            rule.StageCode = rule.StageCode.Trim();
            rule.ApproverSource = rule.ApproverSource.Trim();
            rule.ApproverRole = rule.ApproverRole.Trim();
            rule.Description = rule.Description.Trim();

            if (string.IsNullOrWhiteSpace(rule.StageCode) || rule.StageCode == WorkflowStageCodes.Submission)
                throw new InvalidOperationException("Stage Code của Approval Matrix không hợp lệ.");
            if (!ApproverSources.All.Contains(rule.ApproverSource))
                throw new InvalidOperationException($"Approver Source '{rule.ApproverSource}' không hợp lệ.");
            if (rule.ApproverSource == ApproverSources.TargetDepartmentManager && !rule.TargetDepartmentId.HasValue)
                throw new InvalidOperationException("TargetDepartmentManager bắt buộc phải chọn Target Department.");
            if (rule.ApproverSource == ApproverSources.SpecificUser && !rule.ApproverUserId.HasValue)
                throw new InvalidOperationException("SpecificUser bắt buộc phải chọn Approver User.");
            if (rule.ApproverSource == ApproverSources.Role && string.IsNullOrWhiteSpace(rule.ApproverRole))
                throw new InvalidOperationException("Role source bắt buộc phải chọn Approver Role.");
        }

        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();

        var activeRoles = await db.Roles.AsNoTracking().Where(x => x.IsActive).Select(x => x.RoleName).ToListAsync();
        foreach (var rule in rules.Where(x => x.ApproverSource == ApproverSources.Role))
        {
            if (!activeRoles.Contains(rule.ApproverRole))
                throw new InvalidOperationException($"Approval Matrix tham chiếu Role '{rule.ApproverRole}' không tồn tại hoặc không hoạt động.");
        }

        var departmentIds = await db.Departments
            .AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => x.Id)
            .ToListAsync();
        var activeUserIds = await db.Users
            .AsNoTracking()
            .Where(x => x.IsActive && !x.IsDeleted)
            .Select(x => x.UserId)
            .ToListAsync();

        foreach (var rule in rules)
        {
            if (rule.RequestingDepartmentId.HasValue && !departmentIds.Contains(rule.RequestingDepartmentId.Value))
                throw new InvalidOperationException("Approval Matrix tham chiếu Requesting Department không hoạt động hoặc không tồn tại.");
            if (rule.TargetDepartmentId.HasValue && !departmentIds.Contains(rule.TargetDepartmentId.Value))
                throw new InvalidOperationException("Approval Matrix tham chiếu Target Department không hoạt động hoặc không tồn tại.");
            if (rule.ApproverUserId.HasValue && !activeUserIds.Contains(rule.ApproverUserId.Value))
                throw new InvalidOperationException("Approval Matrix tham chiếu Specific User không hoạt động hoặc không tồn tại.");
        }

        var activeStages = await db.WorkflowStageTemplates
            .AsNoTracking()
            .Where(x => x.IsActive && x.StageCode != WorkflowStageCodes.Submission)
            .Select(x => new { x.StageCode, x.StageName })
            .ToListAsync();

        var missingStages = activeStages
            .Where(stage => !rules.Any(rule => rule.IsActive && rule.StageCode == stage.StageCode))
            .Select(stage => stage.StageName)
            .ToList();
        if (missingStages.Count > 0)
        {
            throw new InvalidOperationException(
                "Approval Matrix thiếu rule hoạt động cho stage: " + string.Join(", ", missingStages));
        }

        await using var tx = await db.Database.BeginTransactionAsync();
        db.ApprovalMatrixRules.RemoveRange(await db.ApprovalMatrixRules.ToListAsync());
        await db.SaveChangesAsync();

        foreach (var source in rules)
        {
            db.ApprovalMatrixRules.Add(new ApprovalMatrixRule
            {
                StageCode = source.StageCode,
                RequestingDepartmentId = source.RequestingDepartmentId,
                TargetDepartmentId = source.TargetDepartmentId,
                ApproverSource = source.ApproverSource,
                ApproverRole = source.ApproverRole,
                ApproverUserId = source.ApproverUserId,
                Priority = source.Priority,
                IsActive = source.IsActive,
                Description = source.Description
            });
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

}
