using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using DCRManagementSystem.Data;
using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace DCRManagementSystem.Services;

public sealed class SubmitDcrResult
{
    public bool ApprovedImmediately { get; init; }
    public NotificationDispatchResult Notification { get; init; } = new();
}

public sealed class DecisionProcessResult
{
    public string Decision { get; init; } = string.Empty;
    public bool StageAdvanced { get; init; }
    public int PreviousStage { get; init; }
    public int CurrentStage { get; init; }
    public bool WorkflowCompleted { get; init; }
    public string RequestStatus { get; init; } = string.Empty;
    public NotificationDispatchResult Notification { get; init; } = new();
}

public sealed class DcrService : IDcrService
{
    private readonly Func<AppDbContext> _dbFactory;
    private readonly AppSettings _settings;
    private readonly NotificationService _notifications;
    private readonly ApprovalRoutingService _routing;
    private readonly ApprovalSignatureService _signature;
    private readonly PdfService _pdf;

    public DcrService(
        Func<AppDbContext> dbFactory,
        AppSettings settings,
        NotificationService notifications,
        ApprovalRoutingService routing,
        ApprovalSignatureService signature,
        PdfService pdf)
    {
        _dbFactory = dbFactory;
        _settings = settings;
        _notifications = notifications;
        _routing = routing;
        _signature = signature;
        _pdf = pdf;
    }

    public async Task<List<Department>> GetActiveDepartmentsAsync()
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        return await db.Departments
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.DepartmentCode)
            .ToListAsync();
    }

    public async Task<List<ProductLineDefinition>> GetActiveProductLinesAsync()
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        return await db.ProductLines
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync();
    }

    public async Task<List<PartChangeTypeDefinition>> GetActivePartChangeTypesAsync()
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        return await db.PartChangeTypes
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync();
    }

    public async Task<List<ApproverSearchItem>> SearchApproversAsync(string searchText, int maxResults = 40)
    {
        searchText = (searchText ?? string.Empty).Trim();
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();

        var query = db.Users
            .AsNoTracking()
            .Include(x => x.Department)
                .ThenInclude(x => x!.BusinessUnit)
            .Include(x => x.BusinessUnit)
            .Where(x => x.IsActive && !x.IsDeleted);

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            query = query.Where(x =>
                x.FullName.Contains(searchText) ||
                x.Username.Contains(searchText) ||
                x.Email.Contains(searchText) ||
                x.Role.Contains(searchText) ||
                (x.BusinessUnit != null && (x.BusinessUnit.UnitName.Contains(searchText) || x.BusinessUnit.UnitCode.Contains(searchText))) ||
                (x.Department != null && (x.Department.DepartmentName.Contains(searchText) || x.Department.DepartmentCode.Contains(searchText))));
        }

        return await query
            .OrderBy(x => x.FullName)
            .ThenBy(x => x.UserId)
            .Take(Math.Clamp(maxResults, 1, 100))
            .Select(x => new ApproverSearchItem
            {
                UserId = x.UserId,
                Username = x.Username,
                FullName = x.FullName,
                Email = x.Email,
                BusinessUnitId = x.BusinessUnitId ?? (x.Department != null ? x.Department.BusinessUnitId : null),
                BusinessUnitCode = x.BusinessUnit != null ? x.BusinessUnit.UnitCode : (x.Department != null && x.Department.BusinessUnit != null ? x.Department.BusinessUnit.UnitCode : string.Empty),
                BusinessUnit = x.BusinessUnit != null ? x.BusinessUnit.UnitName : (x.Department != null && x.Department.BusinessUnit != null ? x.Department.BusinessUnit.UnitName : string.Empty),
                DepartmentId = x.DepartmentId,
                DepartmentCode = x.Department != null ? x.Department.DepartmentCode : string.Empty,
                Department = x.Department != null ? x.Department.DepartmentName : string.Empty,
                Role = x.Role
            })
            .ToListAsync();
    }

    public async Task<ApprovalPlanSuggestionResult> GetSuggestedApprovalPlanAsync(int userId, string rank)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();

        rank = DcrRanks.Normalize(rank);
        var users = await db.Users
            .AsNoTracking()
            .Include(x => x.Department)
                .ThenInclude(x => x!.BusinessUnit)
            .Include(x => x.BusinessUnit)
            .Where(x => x.IsActive && !x.IsDeleted)
            .ToListAsync();
        if (users.All(x => x.UserId != userId))
            throw new InvalidOperationException("Không tìm thấy user để gợi ý line phê duyệt.");

        var units = await db.BusinessUnits.AsNoTracking().Where(x => x.IsActive).ToListAsync();
        var departments = await db.Departments.AsNoTracking().Where(x => x.IsActive).ToListAsync();
        var byId = users.ToDictionary(x => x.UserId);
        var used = new HashSet<int> { userId };
        var result = new ApprovalPlanSuggestionResult { Rank = rank };

        static bool Matches(string? value, IEnumerable<string> aliases)
        {
            var normalized = NormalizeRoutingText(value);
            var tokens = NormalizeRoutingTokens(value)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .ToHashSet(StringComparer.Ordinal);
            return aliases.Any(alias =>
            {
                var aliasNormalized = NormalizeRoutingText(alias);
                if (aliasNormalized.Length == 0) return false;

                // Short organization/role codes (CU, ME, QA, CTO, NMSX...) must
                // match a whole token. A substring match made "CU" incorrectly
                // match "NGHIEN CUU" and could suggest an approver from the wrong block.
                var aliasTokens = NormalizeRoutingTokens(alias)
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (aliasNormalized.Length <= 4 && aliasTokens.Length == 1)
                    return tokens.Contains(aliasNormalized);

                return normalized.Contains(aliasNormalized, StringComparison.Ordinal);
            });
        }

        string OrganizationText(User user)
        {
            var department = user.Department;
            var unit = user.BusinessUnit ?? department?.BusinessUnit;
            return string.Join(' ', new[]
            {
                unit?.UnitCode, unit?.UnitName, department?.DepartmentCode, department?.DepartmentName
            }.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        User? FindRole(string[] roleAliases, string[]? organizationAliases = null)
        {
            var candidates = users
                .Where(x => x.UserId != userId && !used.Contains(x.UserId) && Matches(x.Role, roleAliases))
                .Select(x => new
                {
                    User = x,
                    RoleExact = roleAliases.Any(a => NormalizeRoutingText(a) == NormalizeRoutingText(x.Role)),
                    OrganizationMatch = organizationAliases is null || Matches(OrganizationText(x), organizationAliases)
                })
                .Where(x => organizationAliases is null || x.OrganizationMatch)
                .OrderByDescending(x => x.RoleExact)
                .ThenBy(x => x.User.UserId)
                .Select(x => x.User)
                .ToList();
            return candidates.FirstOrDefault();
        }

        User? FindDepartmentManager(string[] departmentAliases, string[]? unitAliases = null)
        {
            var matchingDepartments = departments
                .Where(x => Matches($"{x.DepartmentCode} {x.DepartmentName}", departmentAliases))
                .OrderBy(x => x.Id)
                .ToList();
            if (matchingDepartments.Count == 0 && unitAliases is not null)
            {
                var unitIds = units.Where(x => Matches($"{x.UnitCode} {x.UnitName}", unitAliases)).Select(x => x.Id).ToHashSet();
                matchingDepartments = departments.Where(x => x.BusinessUnitId.HasValue && unitIds.Contains(x.BusinessUnitId.Value)).OrderBy(x => x.Id).ToList();
            }

            foreach (var department in matchingDepartments)
            {
                if (department.ManagerUserId.HasValue && byId.TryGetValue(department.ManagerUserId.Value, out var manager) &&
                    manager.UserId != userId && !used.Contains(manager.UserId))
                    return manager;
            }
            return null;
        }

        User? FindUnitHead(string[] unitAliases, string[]? roleAliases = null)
        {
            foreach (var unit in units.Where(x => Matches($"{x.UnitCode} {x.UnitName}", unitAliases)).OrderBy(x => x.Id))
            {
                if (unit.DirectorUserId.HasValue && byId.TryGetValue(unit.DirectorUserId.Value, out var director) &&
                    director.UserId != userId && !used.Contains(director.UserId))
                    return director;
            }
            return roleAliases is null ? null : FindRole(roleAliases, unitAliases);
        }

        void AddSlot(int levelNumber, string levelName, string positionName, User? approver)
        {
            if (approver is null)
            {
                result.MissingApprovers.Add(positionName);
                return;
            }
            if (!used.Add(approver.UserId))
            {
                result.MissingApprovers.Add($"{positionName} (trùng người duyệt ở cấp trước)");
                return;
            }

            var department = approver.Department;
            var unit = approver.BusinessUnit ?? department?.BusinessUnit;
            result.ApprovalPlan.Add(new ApprovalPlanEditItem
            {
                LevelNumber = levelNumber,
                LevelName = levelName,
                ApproverId = approver.UserId,
                ApproverName = approver.FullName,
                ApproverEmail = approver.Email,
                ApproverRole = approver.Role,
                BusinessUnitId = unit?.Id,
                BusinessUnitCode = unit?.UnitCode ?? string.Empty,
                BusinessUnitName = unit?.UnitName ?? string.Empty,
                DepartmentId = department?.Id,
                DepartmentCode = department?.DepartmentCode ?? string.Empty,
                DepartmentName = department?.DepartmentName ?? unit?.UnitName ?? string.Empty,
                Sequence = result.ApprovalPlan.Count(x => x.LevelNumber == levelNumber) + 1
            });
        }

        var rndAliases = new[] { "NCPT", "RND", "R&D", "Nghiên cứu và Phát triển", "Research Development" };
        var purchaseAliases = new[] { "PUR", "PURCHASE", "PROCUREMENT", "Mua hàng", "Cung ứng" };
        var supplyAliases = new[] { "CU", "Cung ứng", "SCM", "Supply Chain" };
        var productionAliases = new[] { "NMSX", "PROD", "Nhà máy", "Sản xuất", "Factory", "Production" };
        var productionEngineeringAliases = new[] { "KTSX", "Kỹ thuật sản xuất", "Production Engineering", "Manufacturing Engineering", "ME" };
        var qualityAliases = new[] { "Chất lượng", "Quality", "QA", "QC" };
        var afterSalesAliases = new[] { "DVHM", "Hậu mãi", "After Sales", "After-sales" };

        AddSlot(1, "RnD - CTO phê duyệt", "CTO phụ trách RnD (NCPT)",
            FindRole([RoleNames.CTO, "Giám đốc Công nghệ"], rndAliases) ?? FindRole([RoleNames.CTO, "Giám đốc Công nghệ"]));
        AddSlot(2, "Pur - Trưởng phòng phê duyệt", "Trưởng phòng Pur / Mua hàng thuộc Khối Cung Ứng",
            FindDepartmentManager(purchaseAliases, supplyAliases));

        // The approved Rank-S flow intentionally has two parallel branches at level 3:
        // PTGD RnD & SCM and the three Factory level-1 approvers. GDNM and COO are
        // therefore levels 4 and 5, matching the official flow supplied by the business.
        var factoryLevel = 3;
        if (rank == DcrRanks.S)
        {
            AddSlot(3, "PTGĐ RnD & SCM phê duyệt", "PTGĐ phụ trách RnD & SCM",
                FindRole(["PTGĐ RnD & SCM", "Phó Tổng Giám đốc RnD & SCM", "Deputy General Director RnD SCM"]));
        }

        AddSlot(factoryLevel, "Nhà máy level 1", "Trưởng phòng Kỹ thuật Sản xuất",
            FindDepartmentManager(productionEngineeringAliases, productionAliases));
        AddSlot(factoryLevel, "Nhà máy level 1", "Trưởng phòng Chất lượng",
            FindDepartmentManager(qualityAliases, productionAliases));
        AddSlot(factoryLevel, "Nhà máy level 1", "Phó Giám đốc Sản xuất",
            FindRole(["PGĐ SX", "Phó Giám đốc Sản xuất", "Deputy Production Director", RoleNames.DCEO], productionAliases));

        AddSlot(factoryLevel + 1, "Nhà máy level 2 - GĐNM phê duyệt", "Giám đốc Nhà máy",
            FindRole(["GĐNM", "Giám đốc Nhà máy", "Factory Director"], productionAliases) ??
            FindUnitHead(productionAliases, [RoleNames.Director]));

        if (rank == DcrRanks.S)
        {
            AddSlot(factoryLevel + 2, "Nhà máy level 3 - COO phê duyệt", "COO",
                FindRole([RoleNames.COO, "Giám đốc Vận hành"]));
        }
        else
        {
            AddSlot(factoryLevel + 2, "Hậu mãi phê duyệt", "Trưởng đơn vị Dịch vụ Hậu mãi",
                FindUnitHead(afterSalesAliases, [RoleNames.Director]) ?? FindDepartmentManager(afterSalesAliases));
        }

        result.ApprovalPlan = result.ApprovalPlan
            .OrderBy(x => x.LevelNumber)
            .ThenBy(x => x.Sequence)
            .ToList();
        return result;
    }

    public async Task<List<ApprovalPlanTemplateEditModel>> GetApprovalPlanTemplatesAsync(string rank)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        rank = DcrRanks.Normalize(rank);

        var templates = await db.ApprovalPlanTemplates
            .AsNoTracking()
            .Include(x => x.Entries)
                .ThenInclude(x => x.Approver)
                    .ThenInclude(x => x!.Department)
                        .ThenInclude(x => x!.BusinessUnit)
            .Include(x => x.Entries)
                .ThenInclude(x => x.Approver)
                    .ThenInclude(x => x!.BusinessUnit)
            .Where(x => x.IsActive && x.Rank == rank)
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Name)
            .ToListAsync();

        return templates
            .Where(x => x.Entries.Count > 0 && x.Entries.All(e =>
                e.Approver is { IsActive: true, IsDeleted: false } &&
                !string.IsNullOrWhiteSpace(e.Approver.Email)))
            .Select(ToApprovalPlanTemplateModel)
            .ToList();
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
            ApprovalPlan = template.Entries
                .OrderBy(x => x.LevelNumber)
                .ThenBy(x => x.Sequence)
                .Select(x =>
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
                })
                .ToList()
        };
    }

    public async Task<List<ApprovalPlanEditItem>> GetSavedApprovalPlanAsync(int userId, string rank)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        rank = DcrRanks.Normalize(rank);

        var ownerExists = await db.Users.AsNoTracking()
            .AnyAsync(x => x.UserId == userId && x.IsActive && !x.IsDeleted);
        if (!ownerExists)
            throw new InvalidOperationException("Không tìm thấy user đang hoạt động để nạp mẫu line phê duyệt.");

        var entries = await db.UserApprovalPlanEntries
            .AsNoTracking()
            .Include(x => x.Approver)
                .ThenInclude(x => x!.Department)
                    .ThenInclude(x => x!.BusinessUnit)
            .Include(x => x.Approver)
                .ThenInclude(x => x!.BusinessUnit)
            .Where(x => x.OwnerUserId == userId && x.Rank == rank)
            .OrderBy(x => x.LevelNumber)
            .ThenBy(x => x.Sequence)
            .ThenBy(x => x.Id)
            .ToListAsync();

        if (entries.Count == 0)
            return [];

        if (entries.Count > 100 || entries.Any(x => x.LevelNumber <= 0) ||
            entries.GroupBy(x => x.ApproverId).Any(x => x.Count() > 1))
        {
            throw new InvalidOperationException("Mẫu line phê duyệt cá nhân không hợp lệ. Hãy tạo lại mẫu từ line hiện tại.");
        }

        var levels = entries.Select(x => x.LevelNumber).Distinct().OrderBy(x => x).ToList();
        if (!levels.SequenceEqual(Enumerable.Range(1, levels.Count)))
            throw new InvalidOperationException("Mẫu line phê duyệt cá nhân bị thiếu cấp. Hãy tạo lại mẫu từ line hiện tại.");

        var unavailable = entries
            .Where(x => x.Approver is null || !x.Approver.IsActive || x.Approver.IsDeleted || string.IsNullOrWhiteSpace(x.Approver.Email))
            .Select(x => x.Approver?.FullName ?? $"UserId={x.ApproverId}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (unavailable.Count > 0)
        {
            throw new InvalidOperationException(
                "Không thể nạp mẫu line cá nhân vì approver không còn hoạt động hoặc chưa có email: " +
                string.Join(", ", unavailable) + ".");
        }

        return entries.Select(x =>
        {
            var department = x.Approver!.Department;
            var unit = x.Approver.BusinessUnit ?? department?.BusinessUnit;
            return new ApprovalPlanEditItem
            {
                // A template row ID must never be reused as a DCRApprovalPlanEntry ID.
                Id = 0,
                LevelNumber = x.LevelNumber,
                LevelName = x.LevelName,
                ApproverId = x.ApproverId,
                ApproverName = x.Approver.FullName,
                ApproverEmail = x.Approver.Email,
                ApproverRole = x.Approver.Role,
                BusinessUnitId = unit?.Id,
                BusinessUnitCode = unit?.UnitCode ?? string.Empty,
                BusinessUnitName = unit?.UnitName ?? string.Empty,
                DepartmentId = department?.Id,
                DepartmentCode = department?.DepartmentCode ?? string.Empty,
                DepartmentName = department?.DepartmentName ?? unit?.UnitName ?? string.Empty,
                Sequence = x.Sequence
            };
        }).ToList();
    }

    public async Task<List<ApprovalPlanEditItem>> SaveUserApprovalPlanAsync(
        int userId,
        string rank,
        IEnumerable<ApprovalPlanEditItem> approvalPlan)
    {
        rank = DcrRanks.Normalize(rank);
        var normalized = NormalizeUserApprovalPlan(approvalPlan);

        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();

        var ownerExists = await db.Users.AsNoTracking()
            .AnyAsync(x => x.UserId == userId && x.IsActive && !x.IsDeleted);
        if (!ownerExists)
            throw new InvalidOperationException("Không tìm thấy user đang hoạt động để lưu mẫu line phê duyệt.");

        var approverIds = normalized.Select(x => x.ApproverId).ToList();
        var approvers = await db.Users
            .AsNoTracking()
            .Include(x => x.Department)
                .ThenInclude(x => x!.BusinessUnit)
            .Include(x => x.BusinessUnit)
            .Where(x => approverIds.Contains(x.UserId))
            .ToListAsync();
        var byId = approvers.ToDictionary(x => x.UserId);
        var unavailable = approverIds
            .Where(id => !byId.TryGetValue(id, out var approver) ||
                         !approver.IsActive || approver.IsDeleted || string.IsNullOrWhiteSpace(approver.Email))
            .Select(id => byId.TryGetValue(id, out var approver) && !string.IsNullOrWhiteSpace(approver.FullName)
                ? approver.FullName
                : $"UserId={id}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (unavailable.Count > 0)
        {
            throw new InvalidOperationException(
                "Không thể lưu mẫu line vì approver không còn hoạt động hoặc chưa có email: " +
                string.Join(", ", unavailable) + ".");
        }

        var now = DateTime.Now;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var existing = await db.UserApprovalPlanEntries
                .Where(x => x.OwnerUserId == userId && x.Rank == rank)
                .ToListAsync();
            if (existing.Count > 0)
            {
                db.UserApprovalPlanEntries.RemoveRange(existing);
                await db.SaveChangesAsync();
            }

            db.UserApprovalPlanEntries.AddRange(normalized.Select(x =>
            {
                var approver = byId[x.ApproverId];
                var department = approver.Department;
                var unit = approver.BusinessUnit ?? department?.BusinessUnit;
                return new UserApprovalPlanEntry
                {
                    OwnerUserId = userId,
                    Rank = rank,
                    LevelNumber = x.LevelNumber,
                    LevelName = x.LevelName,
                    ApproverId = x.ApproverId,
                    BusinessUnitId = unit?.Id,
                    BusinessUnitCode = unit?.UnitCode ?? string.Empty,
                    BusinessUnitName = unit?.UnitName ?? string.Empty,
                    DepartmentId = department?.Id,
                    DepartmentCode = department?.DepartmentCode ?? string.Empty,
                    DepartmentName = department?.DepartmentName ?? string.Empty,
                    ApproverRole = approver.Role,
                    ApproverName = approver.FullName,
                    ApproverEmail = approver.Email,
                    Sequence = x.Sequence,
                    CreatedAt = now,
                    UpdatedAt = now
                };
            }));
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        return normalized.Select(x =>
        {
            var approver = byId[x.ApproverId];
            var department = approver.Department;
            var unit = approver.BusinessUnit ?? department?.BusinessUnit;
            return new ApprovalPlanEditItem
            {
                Id = 0,
                LevelNumber = x.LevelNumber,
                LevelName = x.LevelName,
                ApproverId = x.ApproverId,
                ApproverName = approver.FullName,
                ApproverEmail = approver.Email,
                ApproverRole = approver.Role,
                BusinessUnitId = unit?.Id,
                BusinessUnitCode = unit?.UnitCode ?? string.Empty,
                BusinessUnitName = unit?.UnitName ?? string.Empty,
                DepartmentId = department?.Id,
                DepartmentCode = department?.DepartmentCode ?? string.Empty,
                DepartmentName = department?.DepartmentName ?? unit?.UnitName ?? string.Empty,
                Sequence = x.Sequence
            };
        }).ToList();
    }

    public async Task<List<DcrListItem>> GetListAsync(string scope, int userId, string searchText, bool isAdmin = false)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();

        var query = db.DCRRequests
            .AsNoTracking()
            .Include(x => x.RequestOwner)
            .Include(x => x.RequestingDepartment)
            .AsQueryable();

        switch (scope)
        {
            case DcrListScopes.PendingMyApproval:
                query = query.Where(r =>
                    r.Status == RequestStatuses.InApproval &&
                    db.DCRApprovalFlows.Any(a =>
                        a.RequestId == r.Id &&
                        a.RevisionNo == r.RevisionNo &&
                        a.StageNumber == r.CurrentStage &&
                        a.ApproverId == userId &&
                        a.Decision == ApprovalDecisions.Pending));
                break;
            case DcrListScopes.RelatedToMe:
                query = query.Where(x =>
                    x.CreatedBy == userId ||
                    x.RequestOwnerId == userId ||
                    db.DCRApprovalFlows.Any(a => a.RequestId == x.Id && a.ApproverId == userId));
                break;
            case DcrListScopes.MyRequests:
                query = query.Where(x => x.CreatedBy == userId);
                break;
            case DcrListScopes.Approved:
                query = query.Where(x => x.Status == RequestStatuses.Approved);
                if (!isAdmin)
                {
                    query = query.Where(x =>
                        x.CreatedBy == userId ||
                        x.RequestOwnerId == userId ||
                        db.DCRApprovalFlows.Any(a => a.RequestId == x.Id && a.ApproverId == userId));
                }
                break;
            case DcrListScopes.Rejected:
                query = query.Where(x => x.Status == RequestStatuses.Rejected);
                if (!isAdmin)
                {
                    query = query.Where(x =>
                        x.CreatedBy == userId ||
                        x.RequestOwnerId == userId ||
                        db.DCRApprovalFlows.Any(a => a.RequestId == x.Id && a.ApproverId == userId));
                }
                break;
            case DcrListScopes.All:
                break;
            default:
                query = query.Where(x => x.CreatedBy == userId);
                break;
        }

        searchText = searchText.Trim();
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            query = query.Where(x =>
                x.DCRNumber.Contains(searchText) ||
                x.Title.Contains(searchText) ||
                x.Rank.Contains(searchText) ||
                x.Program.Contains(searchText) ||
                (x.RequestOwner != null && x.RequestOwner.FullName.Contains(searchText)));
        }

        return await query
            .OrderByDescending(x => x.CreatedDate)
            .Select(x => new DcrListItem
            {
                Id = x.Id,
                DCRNumber = x.DCRNumber,
                Title = x.Title,
                Rank = x.Rank,
                Program = x.Program,
                BuildStage = x.BuildStage,
                Owner = x.RequestOwner != null ? x.RequestOwner.FullName : string.Empty,
                Department = x.RequestingDepartment != null ? x.RequestingDepartment.DepartmentCode : string.Empty,
                Status = x.Status,
                CurrentStage = x.CurrentStage,
                RevisionNo = x.RevisionNo,
                CreatedDate = x.CreatedDate,
                LastSavedAt = x.LastSavedAt
            })
            .ToListAsync();
    }

    public async Task<int> GetPendingApprovalCountAsync(int userId)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        return await db.DCRRequests
            .AsNoTracking()
            .CountAsync(r =>
                r.Status == RequestStatuses.InApproval &&
                db.DCRApprovalFlows.Any(a =>
                    a.RequestId == r.Id &&
                    a.RevisionNo == r.RevisionNo &&
                    a.StageNumber == r.CurrentStage &&
                    a.ApproverId == userId &&
                    a.Decision == ApprovalDecisions.Pending));
    }

    public async Task<bool> CanViewAsync(int requestId, int userId, bool isAdmin)
    {
        if (isAdmin)
            return true;

        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        return await db.DCRRequests
            .AsNoTracking()
            .Where(x => x.Id == requestId)
            .AnyAsync(x => x.CreatedBy == userId ||
                           db.DCRApprovalFlows.Any(a => a.RequestId == x.Id && a.ApproverId == userId));
    }

    public async Task<DcrEditModel> CreateNewModelAsync(int userId)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        var user = await db.Users
            .AsNoTracking()
            .Include(x => x.Department)
            .SingleAsync(x => x.UserId == userId);

        var directorLevel = await db.Roles.AsNoTracking()
            .Where(x => x.RoleName == RoleNames.Director)
            .Select(x => (int?)x.HierarchyLevel)
            .SingleOrDefaultAsync() ?? 30;
        var userRoleLevel = await db.Roles.AsNoTracking()
            .Where(x => x.RoleName == user.Role)
            .Select(x => (int?)x.HierarchyLevel)
            .SingleOrDefaultAsync();
        DcrCreationPolicy.EnsureCanStartDraft(user.DepartmentId, user.Role, userRoleLevel, directorLevel);

        return new DcrEditModel
        {
            RequestOwnerId = user.UserId,
            RequestOwnerName = user.FullName,
            RequestOwnerEmail = user.Email,
            RequestOwnerPhone = user.Phone,
            // Director-level users may not have a primary DepartmentId. They
            // select the requesting department explicitly in the create form.
            RequestingDepartmentId = user.DepartmentId ?? 0,
            RequestingDepartmentCode = user.Department?.DepartmentCode ?? string.Empty,
            RequestingDepartmentName = user.Department?.DepartmentName ?? string.Empty,
            ModuleGroup = user.Department?.DepartmentCode ?? string.Empty,
            CreatedDate = DateTime.Now,
            Rank = DcrRanks.C,
            Status = RequestStatuses.Draft,
            CreatedBy = user.UserId,
            RevisionNo = 1,
            DraftStep = 0
        };
    }

    public async Task<DcrEditModel> LoadEditDataAsync(int requestId)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        var request = await db.DCRRequests
            .AsNoTracking()
            .Include(x => x.RequestOwner)
            .Include(x => x.RequestingDepartment)
            .Include(x => x.Parts)
            .Include(x => x.ImpactedDepartments)
                .ThenInclude(x => x.Department)
            .Include(x => x.ApprovalPlan)
                .ThenInclude(x => x.Approver)
                    .ThenInclude(x => x.Department)
                        .ThenInclude(x => x!.BusinessUnit)
            .Include(x => x.ApprovalPlan)
                .ThenInclude(x => x.Approver)
                    .ThenInclude(x => x.BusinessUnit)
            .SingleOrDefaultAsync(x => x.Id == requestId)
            ?? throw new InvalidOperationException("Không tìm thấy DCR.");

        return new DcrEditModel
        {
            Id = request.Id,
            CreationToken = request.CreationToken ?? Guid.Empty,
            RowVersion = request.RowVersion,
            DCRNumber = request.DCRNumber,
            Title = request.Title,
            Rank = DcrRanks.Normalize(request.Rank),
            RequestingDepartmentId = request.RequestingDepartmentId,
            RequestingDepartmentCode = request.RequestingDepartment?.DepartmentCode ?? string.Empty,
            RequestingDepartmentName = request.RequestingDepartment?.DepartmentName ?? string.Empty,
            ModuleGroup = request.ModuleGroup,
            RequestOwnerId = request.RequestOwnerId,
            RequestOwnerName = request.RequestOwner?.FullName ?? string.Empty,
            RequestOwnerEmail = request.RequestOwner?.Email ?? string.Empty,
            RequestOwnerPhone = request.RequestOwner?.Phone ?? string.Empty,
            CreatedDate = request.CreatedDate,
            Program = request.Program,
            BuildStage = request.BuildStage,
            RelatedECR = request.RelatedECR,
            RelatedPPS = request.RelatedPPS,
            RelatedECN = request.RelatedECN,
            RelatedMCN = request.RelatedMCN,
            ProblemDescription = request.ProblemDescription,
            Solution = request.Solution,
            MaterialChangeDescription = request.MaterialChangeDescription,
            FormFitFunctionDetail = request.FormFitFunctionDetail,
            RetrofitVolume = request.RetrofitVolume,
            RetrofitInstruction = request.RetrofitInstruction,
            MaterialIdentificationRequired = request.MaterialIdentificationRequired,
            MaterialUsageStation = request.MaterialUsageStation,
            SupplierSupportsMRD = request.SupplierSupportsMRD,
            ExpectedArrivalDate = request.ExpectedArrivalDate,
            TemporaryProcessRequired = request.TemporaryProcessRequired,
            ReworkRequired = request.ReworkRequired,
            PlannedStartDate = request.PlannedStartDate,
            PlannedEndDate = request.PlannedEndDate,
            ProductionOrderNumber = request.ProductionOrderNumber,
            Status = request.Status,
            CurrentStage = request.CurrentStage,
            RevisionNo = request.RevisionNo,
            DraftStep = request.DraftStep,
            LastSavedAt = request.LastSavedAt,
            ReturnedDate = request.ReturnedDate,
            LastReturnReason = request.LastReturnReason,
            FinalPdfPath = request.FinalPdfPath,
            FinalPdfSha256 = request.FinalPdfSha256,
            FinalPdfGeneratedAt = request.FinalPdfGeneratedAt,
            CreatedBy = request.CreatedBy,
            Parts = request.Parts
                .OrderBy(x => x.SortOrder)
                .Select(x => new PartEditItem
                {
                    Id = x.Id,
                    ChangeType = x.ChangeType,
                    PartNumber = x.PartNumber,
                    PartName = x.PartName,
                    KPC = x.KPC,
                    Quantity = x.Quantity,
                    ReplacedBy = x.ReplacedBy,
                    SortOrder = x.SortOrder
                }).ToList(),
            ImpactedDepartments = request.ImpactedDepartments
                .OrderBy(x => x.Id)
                .Select(x => new ImpactDepartmentEditItem
                {
                    Id = x.Id,
                    DepartmentId = x.DepartmentId,
                    DepartmentCode = x.Department?.DepartmentCode ?? string.Empty,
                    DepartmentName = x.Department?.DepartmentName ?? string.Empty,
                    EstimatedCost = x.EstimatedCost
                }).ToList(),
            ApprovalPlan = request.ApprovalPlan
                .OrderBy(x => x.LevelNumber)
                .ThenBy(x => x.Sequence)
                .Select(x => new ApprovalPlanEditItem
                {
                    Id = x.Id,
                    LevelNumber = x.LevelNumber,
                    LevelName = x.LevelName,
                    ApproverId = x.ApproverId,
                    ApproverName = x.Approver != null ? x.Approver.FullName : string.Empty,
                    ApproverEmail = x.Approver != null ? x.Approver.Email : string.Empty,
                    ApproverRole = x.Approver?.Role ?? string.Empty,
                    BusinessUnitId = x.Approver?.BusinessUnit?.Id ?? x.Approver?.Department?.BusinessUnit?.Id,
                    BusinessUnitCode = x.Approver?.BusinessUnit?.UnitCode ?? x.Approver?.Department?.BusinessUnit?.UnitCode ?? string.Empty,
                    BusinessUnitName = x.Approver?.BusinessUnit?.UnitName ?? x.Approver?.Department?.BusinessUnit?.UnitName ?? string.Empty,
                    DepartmentId = x.Approver?.Department?.Id,
                    DepartmentCode = x.Approver?.Department?.DepartmentCode ?? string.Empty,
                    DepartmentName = x.Approver?.Department?.DepartmentName ?? x.Approver?.BusinessUnit?.UnitName ?? string.Empty,
                    Sequence = x.Sequence
                }).ToList()
        };
    }

    public async Task<int> SaveDraftAsync(
        DcrEditModel model,
        int userId,
        bool isAdmin,
        int draftStep = 0,
        bool isAutoSave = false)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        DCRRequest request;
        var isNew = !model.Id.HasValue;
        string oldSnapshot;

        if (isNew)
        {
            if (model.CreationToken == Guid.Empty)
                throw new InvalidOperationException("Thiếu mã tạo DCR. Vui lòng mở lại biểu mẫu tạo mới.");

            // The sequence and creation token are checked under one SQL transaction lock.
            // Replaying a POST must never create or overwrite another request.
            await DcrWriteLock.AcquireAsync(db, "DCR.CreateDraft");
            var existingId = await db.DCRRequests.AsNoTracking()
                .Where(x => x.CreatedBy == userId && x.CreationToken == model.CreationToken)
                .Select(x => (int?)x.Id)
                .SingleOrDefaultAsync();
            if (existingId.HasValue)
                throw new DcrAlreadyCreatedException(existingId.Value);

            request = new DCRRequest
            {
                CreationToken = model.CreationToken,
                DCRNumber = await GenerateDcrNumberAsync(db),
                RequestOwnerId = userId,
                CreatedBy = userId,
                CreatedDate = DateTime.Now,
                Status = RequestStatuses.Draft,
                RevisionNo = Math.Max(1, model.RevisionNo)
            };
            db.DCRRequests.Add(request);
            oldSnapshot = "{}";
        }
        else
        {
            request = await db.DCRRequests
                .Include(x => x.Parts)
                .Include(x => x.ImpactedDepartments)
                .Include(x => x.ApprovalPlan)
                .SingleOrDefaultAsync(x => x.Id == model.Id!.Value)
                ?? throw new InvalidOperationException("Không tìm thấy DCR.");

            if (!RequestStatuses.IsEditable(request.Status))
                throw new InvalidOperationException("DCR đang trong luồng duyệt hoặc đã đóng nên không thể chỉnh sửa.");
            if (request.CreatedBy != userId && !isAdmin)
                throw new UnauthorizedAccessException("Bạn không có quyền sửa DCR này.");

            if (model.RowVersion.Length > 0)
                db.Entry(request).Property(x => x.RowVersion).OriginalValue = model.RowVersion;

            oldSnapshot = CaptureSnapshot(request);
        }

        MapModelToRequest(model, request);
        request.DraftStep = Math.Max(request.DraftStep, Math.Clamp(draftStep, 0, 5));
        request.LastSavedAt = DateTime.Now;

        SynchronizeParts(db, request, model.Parts);
        SynchronizeImpactedDepartments(db, request, model.ImpactedDepartments);
        SynchronizeApprovalPlan(db, request, model.ApprovalPlan);

        try
        {
            await db.SaveChangesAsync();

            AddAudit(
                db,
                request.Id,
                userId,
                isNew ? "DCR Created" : isAutoSave ? "Draft Auto-Saved" : "Draft Saved",
                "DCRRequest",
                request.Id.ToString(),
                oldSnapshot,
                CaptureSnapshot(request));

            await db.SaveChangesAsync();
            await tx.CommitAsync();
            // Expose generated identities only after commit, so a failed save can retry
            // with the same creation token. AutoSave still avoids rebinding the controls.
            SynchronizeGeneratedIdentities(request, model);
            model.Id = request.Id;
            model.RowVersion = request.RowVersion.ToArray();
            model.LastSavedAt = request.LastSavedAt;
            model.DCRNumber = request.DCRNumber;
            model.Status = request.Status;
            model.CurrentStage = request.CurrentStage;
            model.RevisionNo = request.RevisionNo;
            model.DraftStep = request.DraftStep;
            model.CreatedBy = request.CreatedBy;
            model.CreatedDate = request.CreatedDate;
            model.RequestOwnerId = request.RequestOwnerId;
            return request.Id;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await tx.RollbackAsync();
            throw new DcrConcurrencyException(request.Id, ex);
        }
    }

    public Task<SubmitDcrResult> SubmitAsync(int requestId, int userId, bool isAdmin, byte[] expectedRowVersion)
        => SubmitCoreAsync(requestId, userId, isAdmin, expectedRowVersion, CurrentUser.AuthMethod, CurrentUser.WindowsIdentity);

    public Task<SubmitDcrResult> SubmitForApiAsync(
        int requestId,
        int userId,
        bool isAdmin,
        byte[] expectedRowVersion,
        string sessionAuthMethod,
        string sessionWindowsIdentity)
        => SubmitCoreAsync(requestId, userId, isAdmin, expectedRowVersion, sessionAuthMethod, sessionWindowsIdentity);

    private async Task<SubmitDcrResult> SubmitCoreAsync(
        int requestId,
        int userId,
        bool isAdmin,
        byte[] expectedRowVersion,
        string sessionAuthMethod,
        string sessionWindowsIdentity)
    {
        var approvedImmediately = false;

        await using (var db = _dbFactory())
        {
            await db.OpenSqlConnectionWithRetryAsync();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            await DcrWriteLock.AcquireAsync(db, $"DCR.Submit.{requestId}");

            var request = await db.DCRRequests
                .Include(x => x.Parts)
                .Include(x => x.ImpactedDepartments)
                .Include(x => x.ApprovalPlan)
                    .ThenInclude(x => x.Approver)
                .SingleOrDefaultAsync(x => x.Id == requestId)
                ?? throw new InvalidOperationException("Không tìm thấy DCR.");

            if (expectedRowVersion.Length > 0)
                db.Entry(request).Property(x => x.RowVersion).OriginalValue = expectedRowVersion;

            if (!RequestStatuses.IsEditable(request.Status))
                throw new InvalidOperationException("DCR đã được gửi duyệt hoặc đã đóng. Vui lòng tải lại DCR.");
            if (request.CreatedBy != userId && !isAdmin)
                throw new UnauthorizedAccessException("Bạn không có quyền Submit DCR này.");

            var previousStatus = request.Status;

            var validation = DcrValidationService.ValidateRequestForSubmit(request);
            if (!validation.IsValid)
                throw new InvalidOperationException(string.Join(Environment.NewLine, validation.Errors));

            if (await db.DCRApprovalFlows.AnyAsync(x =>
                    x.RequestId == requestId && x.RevisionNo == request.RevisionNo))
            {
                throw new InvalidOperationException("Workflow cho revision hiện tại đã tồn tại.");
            }

            var templates = await db.WorkflowStageTemplates
                .Where(x => x.IsActive)
                .OrderBy(x => x.StageNumber)
                .ToListAsync();

            var submission = templates.FirstOrDefault(x => x.StageCode == WorkflowStageCodes.Submission)
                ?? throw new InvalidOperationException("Workflow thiếu Request Submission.");

            var now = DateTime.Now;
            db.DCRApprovalFlows.Add(new DCRApprovalFlow
            {
                RequestId = request.Id,
                RevisionNo = request.RevisionNo,
                StageNumber = submission.StageNumber,
                StageCode = submission.StageCode,
                StageName = submission.StageName,
                ApproverId = userId,
                Decision = ApprovalDecisions.Submitted,
                DecisionDate = now,
                Comments = $"Submit revision {request.RevisionNo}",
                AssignedDate = now,
                IsRequired = true,
                Sequence = 1,
                AuthMethod = string.IsNullOrWhiteSpace(sessionAuthMethod) ? "SessionAuthentication" : sessionAuthMethod,
                AuthenticatedAt = now,
                WindowsIdentity = string.IsNullOrWhiteSpace(sessionWindowsIdentity) ? AuditEnvironment.WindowsIdentityName : sessionWindowsIdentity
            });

            var rows = new List<DCRApprovalFlow>();

            if (request.ApprovalPlan.Count > 0)
            {
                var planValidation = ValidateApprovalPlan(request.ApprovalPlan);
                if (planValidation.Count > 0)
                    throw new InvalidOperationException(string.Join(Environment.NewLine, planValidation));

                foreach (var level in request.ApprovalPlan
                             .Where(x => x.IsRequired)
                             .GroupBy(x => x.LevelNumber)
                             .OrderBy(x => x.Key))
                {
                    var stageNumber = submission.StageNumber + level.Key;
                    var levelName = level.Select(x => x.LevelName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                                    ?? $"Cấp phê duyệt {level.Key}";
                    var sequence = 1;
                    foreach (var plan in level.OrderBy(x => x.Sequence).ThenBy(x => x.Id))
                    {
                        rows.Add(new DCRApprovalFlow
                        {
                            RequestId = request.Id,
                            RevisionNo = request.RevisionNo,
                            StageNumber = stageNumber,
                            StageCode = $"CUSTOM_LEVEL_{level.Key}",
                            StageName = levelName,
                            ApproverId = plan.ApproverId,
                            DepartmentId = plan.Approver?.DepartmentId,
                            Decision = ApprovalDecisions.Waiting,
                            IsRequired = true,
                            Sequence = sequence++
                        });
                    }
                }
            }
            else
            {
                foreach (var template in templates.Where(x => x.StageCode != WorkflowStageCodes.Submission))
                {
                    var assignments = await _routing.ResolveAsync(db, request, template);
                    if (assignments.Count == 0)
                    {
                        throw new InvalidOperationException(
                            $"Approval Matrix không tìm được approver cho stage {template.StageName}.");
                    }

                    foreach (var assignment in assignments)
                    {
                        rows.Add(new DCRApprovalFlow
                        {
                            RequestId = request.Id,
                            RevisionNo = request.RevisionNo,
                            StageNumber = template.StageNumber,
                            StageCode = template.StageCode,
                            StageName = template.StageName,
                            ApproverId = assignment.ApproverId,
                            DepartmentId = assignment.DepartmentId,
                            Decision = ApprovalDecisions.Waiting,
                            IsRequired = true,
                            Sequence = assignment.Sequence
                        });
                    }
                }
            }

            db.DCRApprovalFlows.AddRange(rows);
            var firstRequiredStage = rows.Select(x => (int?)x.StageNumber).OrderBy(x => x).FirstOrDefault();
            request.SubmittedDate = now;
            request.LastSavedAt = now;

            if (firstRequiredStage.HasValue)
            {
                request.Status = RequestStatuses.InApproval;
                request.CurrentStage = firstRequiredStage.Value;
                ActivateStage(rows, firstRequiredStage.Value, now);
            }
            else
            {
                request.Status = RequestStatuses.Approved;
                request.CompletedDate = now;
                request.CurrentStage = submission.StageNumber;
                approvedImmediately = true;
            }

            AddAudit(db, request.Id, userId, "DCR Submitted", "DCRRequest", request.Id.ToString(),
                previousStatus, $"{request.Status}; Revision={request.RevisionNo}");

            try
            {
                await db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                await tx.RollbackAsync();
                throw new DcrConcurrencyException(requestId, ex);
            }
        }

        NotificationDispatchResult notification;
        if (approvedImmediately)
        {
            await TryGenerateFinalPdfAsync(requestId, userId);
            notification = await SafeSendOutcomeAsync(requestId, NotificationTypes.Approved, string.Empty);
        }
        else
        {
            notification = await SafeSendCurrentApprovalAssignedAsync(requestId);
        }

        await TryRunLocalMailWorkerAsync();

        return new SubmitDcrResult
        {
            ApprovedImmediately = approvedImmediately,
            Notification = notification
        };
    }

    public async Task<bool> CanApproveAsync(int requestId, int userId)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        return await db.DCRRequests
            .AsNoTracking()
            .Where(x => x.Id == requestId && x.Status == RequestStatuses.InApproval)
            .AnyAsync(r => db.DCRApprovalFlows.Any(a =>
                a.RequestId == r.Id &&
                a.RevisionNo == r.RevisionNo &&
                a.StageNumber == r.CurrentStage &&
                a.ApproverId == userId &&
                a.Decision == ApprovalDecisions.Pending));
    }

    public async Task<DecisionProcessResult> ProcessDecisionAsync(
        int requestId,
        int userId,
        string decision,
        string comment,
        DecisionAuthentication authentication,
        byte[] expectedRowVersion)
    {
        if (decision != ApprovalDecisions.Approved &&
            decision != ApprovalDecisions.Rejected &&
            decision != ApprovalDecisions.Returned)
        {
            throw new ArgumentException("Decision không hợp lệ.", nameof(decision));
        }

        comment = comment.Trim();
        if ((decision == ApprovalDecisions.Rejected || decision == ApprovalDecisions.Returned) &&
            string.IsNullOrWhiteSpace(comment))
        {
            throw new InvalidOperationException("Reject/Return bắt buộc phải nhập lý do.");
        }

        ValidateDecisionAuthentication(authentication);

        var becameApproved = false;
        var becameReturned = false;
        var becameRejected = false;
        var stageAdvanced = false;
        var previousStage = 0;
        var currentStageAfterDecision = 0;
        var requestStatusAfterDecision = string.Empty;

        await using (var db = _dbFactory())
        {
            await db.OpenSqlConnectionWithRetryAsync();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var request = await db.DCRRequests
                .Include(x => x.ApprovalFlow)
                .SingleOrDefaultAsync(x => x.Id == requestId)
                ?? throw new InvalidOperationException("Không tìm thấy DCR.");

            if (expectedRowVersion.Length > 0)
                db.Entry(request).Property(x => x.RowVersion).OriginalValue = expectedRowVersion;

            if (request.Status != RequestStatuses.InApproval)
                throw new InvalidOperationException("DCR hiện không ở luồng phê duyệt.");

            var decisionRevision = request.RevisionNo;
            var decisionStage = request.CurrentStage;
            previousStage = decisionStage;
            var assignedRows = request.ApprovalFlow
                .Where(x => x.RevisionNo == decisionRevision &&
                            x.StageNumber == decisionStage &&
                            x.ApproverId == userId &&
                            x.Decision == ApprovalDecisions.Pending)
                .ToList();

            if (assignedRows.Count == 0)
                throw new UnauthorizedAccessException("Bạn không phải approver đang chờ ở stage hiện tại.");

            var now = DateTime.Now;
            var signature = _signature.Sign(
                request.Id,
                decisionRevision,
                decisionStage,
                userId,
                decision,
                comment,
                authentication.AuthenticatedAt,
                authentication.WindowsIdentity);

            foreach (var row in assignedRows)
            {
                row.Decision = decision;
                row.DecisionDate = now;
                row.Comments = comment;
                row.AuthMethod = authentication.AuthMethod;
                row.AuthenticatedAt = authentication.AuthenticatedAt;
                row.WindowsIdentity = authentication.WindowsIdentity;
                row.SignatureHash = signature;
            }

            AddAudit(db, request.Id, userId, $"{decision} Stage {decisionStage}",
                "DCRApprovalFlow", string.Join(",", assignedRows.Select(x => x.Id)),
                ApprovalDecisions.Pending,
                JsonSerializer.Serialize(new { decision, comment, signature, revision = decisionRevision }));

            if (decision == ApprovalDecisions.Rejected)
            {
                request.Status = RequestStatuses.Rejected;
                request.RejectedDate = now;
                CancelOpenRows(request, decisionRevision, assignedRows);
                AddAudit(db, request.Id, userId, "DCR Rejected", "DCRRequest", request.Id.ToString(),
                    $"Status={RequestStatuses.InApproval}; Stage={decisionStage}; Revision={decisionRevision}",
                    $"Status={RequestStatuses.Rejected}; Reason={comment}");
                becameRejected = true;
            }
            else if (decision == ApprovalDecisions.Returned)
            {
                CancelOpenRows(request, decisionRevision, assignedRows);
                request.Status = RequestStatuses.Returned;
                request.CurrentStage = 0;
                request.ReturnedDate = now;
                request.LastReturnReason = comment;
                request.RevisionNo = decisionRevision + 1;
                request.DraftStep = 0;
                request.LastSavedAt = now;
                AddAudit(db, request.Id, userId, "DCR Returned", "DCRRequest", request.Id.ToString(),
                    $"Status={RequestStatuses.InApproval}; Stage={decisionStage}; Revision={decisionRevision}",
                    $"Status={RequestStatuses.Returned}; Stage=0; Revision={request.RevisionNo}; Reason={comment}");
                becameReturned = true;
            }
            else
            {
                var currentRequired = request.ApprovalFlow
                    .Where(x => x.RevisionNo == decisionRevision &&
                                x.StageNumber == decisionStage &&
                                x.IsRequired)
                    .ToList();

                if (currentRequired.All(x => x.Decision == ApprovalDecisions.Approved))
                {
                    var nextStage = request.ApprovalFlow
                        .Where(x => x.RevisionNo == decisionRevision &&
                                    x.StageNumber > decisionStage &&
                                    x.IsRequired &&
                                    x.Decision == ApprovalDecisions.Waiting)
                        .Select(x => (int?)x.StageNumber)
                        .OrderBy(x => x)
                        .FirstOrDefault();

                    if (nextStage.HasValue)
                    {
                        request.CurrentStage = nextStage.Value;
                        stageAdvanced = true;
                        ActivateStage(
                            request.ApprovalFlow.Where(x => x.RevisionNo == decisionRevision).ToList(),
                            nextStage.Value,
                            now);
                        AddAudit(db, request.Id, userId, "Workflow Advanced", "DCRRequest", request.Id.ToString(),
                            $"Stage={decisionStage}; Revision={decisionRevision}",
                            $"Stage={nextStage.Value}; Revision={decisionRevision}");
                    }
                    else
                    {
                        request.Status = RequestStatuses.Approved;
                        request.CompletedDate = now;
                        AddAudit(db, request.Id, userId, "DCR Approved", "DCRRequest", request.Id.ToString(),
                            $"Status={RequestStatuses.InApproval}; Stage={decisionStage}; Revision={decisionRevision}",
                            $"Status={RequestStatuses.Approved}; Revision={decisionRevision}");
                        becameApproved = true;
                    }
                }
            }

            currentStageAfterDecision = request.CurrentStage;
            requestStatusAfterDecision = request.Status;

            // Force an update on the DCR header for every approval action so RowVersion
            // serializes parallel decisions. A second approver holding a stale copy must
            // reload before continuing, preventing a stage from being advanced from stale data.
            request.LastSavedAt = now;

            try
            {
                await db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                await tx.RollbackAsync();
                throw new DcrConcurrencyException(requestId, ex);
            }
        }

        var notification = new NotificationDispatchResult();
        if (becameApproved)
        {
            await TryGenerateFinalPdfAsync(requestId, userId);
            // Queue the completion email explicitly for the DCR creator immediately after
            // the final approval commits. Reconciliation remains as a durable repair path.
            notification.MergeFrom(await SafeSendOutcomeAsync(requestId, NotificationTypes.Approved, comment));
            notification.MergeFrom(await SafeReconcileWorkflowNotificationsAsync(requestId));
        }
        else if (decision == ApprovalDecisions.Approved && stageAdvanced)
        {
            // The current level has finished. Queue the next level directly instead of
            // depending only on the background reconciliation pass. This is especially
            // important for custom N-level workflows and parallel co-approval levels.
            notification.MergeFrom(await SafeSendCurrentApprovalAssignedAsync(requestId));
            notification.MergeFrom(await SafeReconcileWorkflowNotificationsAsync(requestId));
        }
        else if (decision == ApprovalDecisions.Approved)
        {
            // A co-approver has approved, but other required approvers are still Pending
            // in the same level. No next-level email should be created yet.
            notification.MergeFrom(await SafeReconcileWorkflowNotificationsAsync(requestId));
        }

        if (becameReturned)
            notification.MergeFrom(await SafeSendOutcomeAsync(requestId, NotificationTypes.Returned, comment));
        if (becameRejected)
            notification.MergeFrom(await SafeSendOutcomeAsync(requestId, NotificationTypes.Rejected, comment));

        await TryRunLocalMailWorkerAsync();

        return new DecisionProcessResult
        {
            Decision = decision,
            StageAdvanced = stageAdvanced,
            PreviousStage = previousStage,
            CurrentStage = currentStageAfterDecision,
            WorkflowCompleted = becameApproved,
            RequestStatus = requestStatusAfterDecision,
            Notification = notification
        };
    }

    private async Task<NotificationDispatchResult> SafeReconcileWorkflowNotificationsAsync(int requestId)
    {
        try
        {
            return await _notifications.ReconcileWorkflowNotificationsAsync(requestId);
        }
        catch (Exception ex)
        {
            var result = new NotificationDispatchResult();
            result.AddFailed("(notification reconciliation)", ex.GetBaseException().Message);
            return result;
        }
    }

    private async Task<NotificationDispatchResult> SafeSendCurrentApprovalAssignedAsync(int requestId)
    {
        try
        {
            return await _notifications.SendCurrentApprovalAssignedAsync(requestId);
        }
        catch (Exception ex)
        {
            var result = new NotificationDispatchResult();
            result.AddFailed("(notification service)", ex.GetBaseException().Message);
            return result;
        }
    }

    private async Task<NotificationDispatchResult> SafeSendOutcomeAsync(int requestId, string notificationType, string comment)
    {
        try
        {
            return await _notifications.SendOutcomeAsync(requestId, notificationType, comment);
        }
        catch (Exception ex)
        {
            var result = new NotificationDispatchResult();
            result.AddFailed("(notification service)", ex.GetBaseException().Message);
            return result;
        }
    }

    private async Task TryRunLocalMailWorkerAsync()
    {
        try
        {
            var stateService = new MailWorkerStateService(_dbFactory);
            var state = await stateService.GetAsync();
            if (string.IsNullOrWhiteSpace(state.MachineName) ||
                string.IsNullOrWhiteSpace(state.SignedInAccount) ||
                !string.Equals(state.MachineName, Environment.MachineName, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(state.WindowsIdentity, MailWorkerStateService.GetCurrentWindowsIdentity(), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // On the designated Mail Server only, process the outbox immediately so an
            // approval action does not have to wait for the next Scheduled Task interval.
            await new MailWorkerService(_dbFactory, _settings).RunOnceAsync(50);
        }
        catch
        {
            // EmailOutbox is durable. Failure to run the immediate local worker must never
            // roll back a DCR decision; the scheduled server worker will retry later.
        }
    }

    private void ValidateDecisionAuthentication(DecisionAuthentication authentication)
    {
        if (DateTime.Now - authentication.AuthenticatedAt > TimeSpan.FromMinutes(5) ||
            authentication.AuthenticatedAt > DateTime.Now.AddMinutes(1))
        {
            throw new UnauthorizedAccessException("Phiên xác thực quyết định không hợp lệ hoặc đã hết hạn.");
        }

        if (authentication.AuthMethod == AuthMethods.WindowsIntegrated)
        {
            if (!_settings.Authentication.UseWindowsSessionForApproval ||
                string.IsNullOrWhiteSpace(authentication.WindowsIdentity))
            {
                throw new UnauthorizedAccessException("Windows Authentication không được cấu hình cho quyết định phê duyệt.");
            }
            return;
        }

        if (authentication.AuthMethod == AuthMethods.Ldap)
        {
            if (!_settings.Authentication.Ldap.Enabled)
                throw new UnauthorizedAccessException("LDAP Authentication hiện không được bật.");
            return;
        }

        if (authentication.AuthMethod == AuthMethods.InternalPassword)
        {
            if (_settings.Authentication.Mode.Equals(AuthenticationModes.WindowsOnly, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Local Password Authentication không được phép trong WindowsOnly mode.");
            return;
        }

        if (authentication.AuthMethod == AuthMethods.ApplicationSession)
        {
            // Active-session authentication is valid for all supported login methods.
            return;
        }

        throw new UnauthorizedAccessException("Phương thức xác thực quyết định không được hỗ trợ.");
    }

    public async Task<List<ApprovalHistoryItem>> GetApprovalHistoryAsync(int requestId)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        var rows = await db.DCRApprovalFlows
            .AsNoTracking()
            .Include(x => x.Department)
            .Include(x => x.Approver)
            .Where(x => x.RequestId == requestId)
            .OrderByDescending(x => x.RevisionNo)
            .ThenBy(x => x.StageNumber)
            .ThenBy(x => x.Sequence)
            .ToListAsync();

        return rows.Select(x => new ApprovalHistoryItem
        {
            Id = x.Id,
            RevisionNo = x.RevisionNo,
            StageNumber = x.StageNumber,
            StageName = x.StageName,
            Department = x.Department?.DepartmentCode ?? string.Empty,
            Approver = x.Approver?.FullName ?? string.Empty,
            Decision = x.Decision,
            DecisionDate = x.DecisionDate,
            Comments = x.Comments,
            AuthMethod = x.AuthMethod,
            SignatureHash = x.SignatureHash,
            SignatureStatus = GetSignatureStatus(x)
        }).ToList();
    }

    private string GetSignatureStatus(DCRApprovalFlow row)
    {
        if (string.IsNullOrWhiteSpace(row.SignatureHash))
            return "-";
        if (!row.ApproverId.HasValue || !row.AuthenticatedAt.HasValue)
            return "INVALID";

        return _signature.Verify(
            row.RequestId,
            row.RevisionNo,
            row.StageNumber,
            row.ApproverId.Value,
            row.Decision,
            row.Comments,
            row.AuthenticatedAt.Value,
            row.WindowsIdentity,
            row.SignatureHash)
            ? "Verified"
            : "INVALID";
    }

    public async Task<List<AttachmentListItem>> GetAttachmentsAsync(int requestId)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        return await db.DCRAttachments
            .AsNoTracking()
            .Where(x => x.RequestId == requestId && !x.IsDeleted)
            .OrderByDescending(x => x.UploadedAt)
            .Select(x => new AttachmentListItem
            {
                Id = x.Id,
                AttachmentType = x.AttachmentType,
                FileName = x.FileName,
                FileSize = x.FileSize,
                StoredFileSize = x.StoredFileSize,
                IsCompressed = x.IsCompressed,
                Sha256Hash = x.Sha256Hash,
                UploadedBy = x.Uploader != null ? x.Uploader.FullName : string.Empty,
                UploadedAt = x.UploadedAt
            }).ToListAsync();
    }

    public async Task<List<AuditListItem>> GetAuditLogsAsync(int requestId)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        return await db.AuditLogs
            .AsNoTracking()
            .Where(x => x.RequestId == requestId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new AuditListItem
            {
                Id = x.Id,
                User = x.User != null ? x.User.FullName : string.Empty,
                Action = x.Action,
                EntityName = x.EntityName,
                EntityId = x.EntityId,
                OldValue = x.OldValue,
                NewValue = x.NewValue,
                CreatedAt = x.CreatedAt,
                ComputerName = x.ComputerName,
                IpAddress = x.IpAddress,
                WindowsIdentity = x.WindowsIdentity
            }).ToListAsync();
    }

    public async Task<string> GetDcrNumberAsync(int requestId)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        return await db.DCRRequests
            .Where(x => x.Id == requestId)
            .Select(x => x.DCRNumber)
            .SingleAsync();
    }

    private void ActivateStage(ICollection<DCRApprovalFlow> rows, int stageNumber, DateTime now)
    {
        foreach (var row in rows.Where(x => x.StageNumber == stageNumber && x.IsRequired))
        {
            row.Decision = ApprovalDecisions.Pending;
            row.AssignedDate = now;
            row.DueDate = now.AddHours(Math.Max(1, _settings.Reminder.ApprovalDueHours));
        }
    }

    private static void CancelOpenRows(
        DCRRequest request,
        int revisionNo,
        IReadOnlyCollection<DCRApprovalFlow> decidedRows)
    {
        foreach (var row in request.ApprovalFlow.Where(x =>
                     x.RevisionNo == revisionNo &&
                     (x.Decision == ApprovalDecisions.Pending || x.Decision == ApprovalDecisions.Waiting)))
        {
            if (!decidedRows.Contains(row))
            {
                row.Decision = ApprovalDecisions.Cancelled;
            }
        }
    }

    private async Task<string> GenerateDcrNumberAsync(AppDbContext db)
    {
        var year = DateTime.Now.Year;
        var sequence = await db.DcrNumberSequences.SingleOrDefaultAsync(x => x.Year == year);
        if (sequence is null)
        {
            sequence = new DcrNumberSequence { Year = year, LastNumber = 1 };
            db.DcrNumberSequences.Add(sequence);
        }
        else
        {
            sequence.LastNumber++;
        }

        var prefix = string.IsNullOrWhiteSpace(_settings.DcrNumberPrefix)
            ? "DCR"
            : _settings.DcrNumberPrefix.Trim().ToUpperInvariant();
        return $"{prefix}{year}-{sequence.LastNumber:00000}";
    }

    private static void SynchronizeGeneratedIdentities(DCRRequest request, DcrEditModel model)
    {
        var inputParts = model.Parts.Where(IsMeaningfulPart).ToList();
        var persistedParts = request.Parts.OrderBy(x => x.SortOrder).ToList();
        var pairCount = Math.Min(inputParts.Count, persistedParts.Count);
        for (var i = 0; i < pairCount; i++)
        {
            inputParts[i].Id = persistedParts[i].Id;
            inputParts[i].SortOrder = persistedParts[i].SortOrder;
        }

        var persistedImpacts = request.ImpactedDepartments
            .GroupBy(x => x.DepartmentId)
            .ToDictionary(x => x.Key, x => x.First());

        foreach (var input in model.ImpactedDepartments.Where(x => x.DepartmentId > 0))
        {
            if (persistedImpacts.TryGetValue(input.DepartmentId, out var entity))
                input.Id = entity.Id;
        }

        var persistedPlan = request.ApprovalPlan
            .GroupBy(x => new { x.LevelNumber, x.ApproverId })
            .ToDictionary(x => x.Key, x => x.OrderBy(y => y.Sequence).First());
        foreach (var input in model.ApprovalPlan.Where(x => x.LevelNumber > 0 && x.ApproverId > 0))
        {
            if (persistedPlan.TryGetValue(new { input.LevelNumber, input.ApproverId }, out var entity))
            {
                input.Id = entity.Id;
                input.Sequence = entity.Sequence;
            }
        }
    }

    private static void SynchronizeParts(
        AppDbContext db,
        DCRRequest request,
        IEnumerable<PartEditItem> inputParts)
    {
        var desired = inputParts.Where(IsMeaningfulPart).ToList();
        var existing = request.Parts.ToList();
        var existingById = existing.Where(x => x.Id > 0).ToDictionary(x => x.Id);
        var retained = new HashSet<DCRPart>();
        var sortOrder = 1;

        foreach (var input in desired)
        {
            DCRPart entity;
            if (input.Id > 0 && existingById.TryGetValue(input.Id, out var tracked))
            {
                entity = tracked;
            }
            else
            {
                entity = new DCRPart();
                request.Parts.Add(entity);
            }

            entity.ChangeType = (input.ChangeType ?? string.Empty).Trim();
            entity.PartNumber = (input.PartNumber ?? string.Empty).Trim();
            entity.PartName = (input.PartName ?? string.Empty).Trim();
            entity.KPC = (input.KPC ?? string.Empty).Trim();
            entity.Quantity = (input.Quantity ?? string.Empty).Trim();
            entity.ReplacedBy = (input.ReplacedBy ?? string.Empty).Trim();
            entity.SortOrder = sortOrder++;
            retained.Add(entity);
        }

        foreach (var entity in existing.Where(x => !retained.Contains(x)))
        {
            request.Parts.Remove(entity);
            db.DCRParts.Remove(entity);
        }
    }

    private static void SynchronizeImpactedDepartments(
        AppDbContext db,
        DCRRequest request,
        IEnumerable<ImpactDepartmentEditItem> inputDepartments)
    {
        var desired = inputDepartments
            .Where(x => x.DepartmentId > 0)
            .GroupBy(x => x.DepartmentId)
            .Select(x => x.First())
            .ToList();

        var existing = request.ImpactedDepartments.ToList();
        var existingById = existing.Where(x => x.Id > 0).ToDictionary(x => x.Id);
        var retained = new HashSet<DCRImpactedDepartment>();

        foreach (var input in desired)
        {
            DCRImpactedDepartment? entity = null;
            if (input.Id > 0 && existingById.TryGetValue(input.Id, out var tracked))
                entity = tracked;

            entity ??= existing.FirstOrDefault(x =>
                !retained.Contains(x) && x.DepartmentId == input.DepartmentId);

            if (entity is null)
            {
                entity = new DCRImpactedDepartment();
                request.ImpactedDepartments.Add(entity);
            }

            entity.DepartmentId = input.DepartmentId;
            entity.EstimatedCost = input.EstimatedCost;
            retained.Add(entity);
        }

        foreach (var entity in existing.Where(x => !retained.Contains(x)))
        {
            request.ImpactedDepartments.Remove(entity);
            db.DCRImpactedDepartments.Remove(entity);
        }
    }

    private static void MapModelToRequest(DcrEditModel model, DCRRequest request)
    {
        if (model.RequestingDepartmentId <= 0)
            throw new InvalidOperationException("Vui lòng chọn Requesting Department.");

        request.Title = (model.Title ?? string.Empty).Trim();
        request.Rank = DcrRanks.Normalize(model.Rank);
        request.RequestingDepartmentId = model.RequestingDepartmentId;
        request.ModuleGroup = (model.ModuleGroup ?? string.Empty).Trim();
        request.Program = (model.Program ?? string.Empty).Trim();
        request.BuildStage = (model.BuildStage ?? string.Empty).Trim();
        request.RelatedECR = (model.RelatedECR ?? string.Empty).Trim();
        request.RelatedPPS = (model.RelatedPPS ?? string.Empty).Trim();
        request.RelatedECN = (model.RelatedECN ?? string.Empty).Trim();
        request.RelatedMCN = (model.RelatedMCN ?? string.Empty).Trim();
        request.ProblemDescription = (model.ProblemDescription ?? string.Empty).Trim();
        request.Solution = (model.Solution ?? string.Empty).Trim();
        request.MaterialChangeDescription = (model.MaterialChangeDescription ?? string.Empty).Trim();
        request.FormFitFunctionDetail = (model.FormFitFunctionDetail ?? string.Empty).Trim();
        request.RetrofitVolume = (model.RetrofitVolume ?? string.Empty).Trim();
        request.RetrofitInstruction = (model.RetrofitInstruction ?? string.Empty).Trim();
        request.MaterialIdentificationRequired = model.MaterialIdentificationRequired;
        request.MaterialUsageStation = (model.MaterialUsageStation ?? string.Empty).Trim();
        request.SupplierSupportsMRD = model.SupplierSupportsMRD;
        request.ExpectedArrivalDate = model.ExpectedArrivalDate;
        request.TemporaryProcessRequired = model.TemporaryProcessRequired;
        request.ReworkRequired = model.ReworkRequired;
        request.PlannedStartDate = model.PlannedStartDate;
        request.PlannedEndDate = model.PlannedEndDate;
        request.ProductionOrderNumber = (model.ProductionOrderNumber ?? string.Empty).Trim();
    }

    private static List<string> ValidateApprovalPlan(IEnumerable<DCRApprovalPlanEntry> planEntries)
    {
        var errors = new List<string>();
        var plan = planEntries.Where(x => x.IsRequired).OrderBy(x => x.LevelNumber).ThenBy(x => x.Sequence).ToList();
        if (plan.Count == 0)
            return errors;

        if (plan.Any(x => x.LevelNumber <= 0))
            errors.Add("Cấp phê duyệt phải bắt đầu từ 1.");

        var levels = plan.Select(x => x.LevelNumber).Distinct().OrderBy(x => x).ToList();
        for (var i = 0; i < levels.Count; i++)
        {
            if (levels[i] != i + 1)
            {
                errors.Add("Các cấp phê duyệt phải liên tục 1, 2, 3... không được bỏ trống cấp.");
                break;
            }
        }

        foreach (var level in plan.GroupBy(x => x.LevelNumber))
        {
            if (level.Any(x => x.ApproverId <= 0))
                errors.Add($"Cấp {level.Key}: có người phê duyệt không hợp lệ.");
        }

        if (plan.GroupBy(x => x.ApproverId).Any(x => x.Count() > 1))
            errors.Add("Mỗi người chỉ được xuất hiện một lần trong toàn bộ luồng phê duyệt của DCR.");

        foreach (var item in plan.Where(x => x.Approver is null || !x.Approver.IsActive || x.Approver.IsDeleted))
            errors.Add($"Cấp {item.LevelNumber}: approver UserId={item.ApproverId} không còn hoạt động hoặc đã bị xóa.");

        foreach (var item in plan.Where(x => x.Approver is null || string.IsNullOrWhiteSpace(x.Approver.Email)))
            errors.Add($"Cấp {item.LevelNumber}: approver UserId={item.ApproverId} chưa có Email trong Quản lý người dùng.");

        return errors;
    }

    private static void SynchronizeApprovalPlan(AppDbContext db, DCRRequest request, IEnumerable<ApprovalPlanEditItem> source)
    {
        var normalized = source
            .Where(x => x.ApproverId > 0 && x.LevelNumber > 0)
            .GroupBy(x => new { x.LevelNumber, x.ApproverId })
            .Select(x => x.First())
            .OrderBy(x => x.LevelNumber)
            .ThenBy(x => x.Sequence)
            .ThenBy(x => x.ApproverId)
            .ToList();

        var keepIds = normalized.Where(x => x.Id > 0).Select(x => x.Id).ToHashSet();
        foreach (var existing in request.ApprovalPlan.Where(x => !keepIds.Contains(x.Id)).ToList())
            db.DCRApprovalPlanEntries.Remove(existing);

        foreach (var group in normalized.GroupBy(x => x.LevelNumber))
        {
            var sequence = 1;
            foreach (var item in group)
            {
                var entity = item.Id > 0 ? request.ApprovalPlan.FirstOrDefault(x => x.Id == item.Id) : null;
                if (entity is null)
                {
                    entity = new DCRApprovalPlanEntry { Request = request };
                    request.ApprovalPlan.Add(entity);
                }
                entity.LevelNumber = item.LevelNumber;
                entity.LevelName = string.IsNullOrWhiteSpace(item.LevelName) ? $"Cấp phê duyệt {item.LevelNumber}" : item.LevelName.Trim();
                entity.ApproverId = item.ApproverId;
                entity.Sequence = sequence++;
                entity.IsRequired = true;
                item.Sequence = entity.Sequence;
            }
        }
    }

    private static List<ApprovalPlanEditItem> NormalizeUserApprovalPlan(IEnumerable<ApprovalPlanEditItem> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var input = source.ToList();
        if (input.Count == 0)
            throw new InvalidOperationException("Line phê duyệt hiện tại đang trống nên không thể lưu làm mẫu cá nhân.");
        if (input.Count > 100)
            throw new InvalidOperationException("Mẫu line phê duyệt không được vượt quá 100 approver.");
        if (input.Any(x => x.LevelNumber <= 0 || x.ApproverId <= 0))
            throw new InvalidOperationException("Mẫu line có cấp hoặc approver không hợp lệ.");
        if (input.GroupBy(x => x.ApproverId).Any(x => x.Count() > 1))
            throw new InvalidOperationException("Mỗi approver chỉ được xuất hiện một lần trong mẫu line cá nhân.");

        var levels = input.Select(x => x.LevelNumber).Distinct().OrderBy(x => x).ToList();
        if (!levels.SequenceEqual(Enumerable.Range(1, levels.Count)))
            throw new InvalidOperationException("Các cấp trong mẫu line phải liên tục 1, 2, 3... và không được bỏ trống cấp.");

        var result = new List<ApprovalPlanEditItem>(input.Count);
        foreach (var group in input.GroupBy(x => x.LevelNumber).OrderBy(x => x.Key))
        {
            var levelName = group.Select(x => (x.LevelName ?? string.Empty).Trim())
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? $"Cấp phê duyệt {group.Key}";
            if (levelName.Length > 200)
                throw new InvalidOperationException($"Tên cấp {group.Key} không được vượt quá 200 ký tự.");

            var sequence = 1;
            foreach (var item in group.OrderBy(x => x.Sequence).ThenBy(x => x.ApproverId))
            {
                result.Add(new ApprovalPlanEditItem
                {
                    Id = 0,
                    LevelNumber = group.Key,
                    LevelName = levelName,
                    ApproverId = item.ApproverId,
                    Sequence = sequence++
                });
            }
        }

        return result;
    }

    private static bool IsMeaningfulPart(PartEditItem part) =>
        !string.IsNullOrWhiteSpace(part.PartNumber) ||
        !string.IsNullOrWhiteSpace(part.PartName) ||
        !string.IsNullOrWhiteSpace(part.Quantity) ||
        !string.IsNullOrWhiteSpace(part.ReplacedBy);

    private static string NormalizeRoutingText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return new string(value.Normalize(NormalizationForm.FormD)
            .Where(x => CharUnicodeInfo.GetUnicodeCategory(x) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(x))
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    private static string NormalizeRoutingTokens(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToUpperInvariant(character));
            }
            else if (builder.Length > 0 && builder[^1] != ' ')
            {
                builder.Append(' ');
            }
        }
        return builder.ToString().Trim();
    }

    private static string CaptureSnapshot(DCRRequest request)
    {
        return JsonSerializer.Serialize(new
        {
            request.DCRNumber,
            request.Title,
            request.Rank,
            request.RequestingDepartmentId,
            request.ModuleGroup,
            request.Program,
            request.BuildStage,
            request.RelatedECR,
            request.RelatedPPS,
            request.RelatedECN,
            request.RelatedMCN,
            request.ProblemDescription,
            request.Solution,
            request.MaterialChangeDescription,
            request.FormFitFunctionDetail,
            request.RetrofitVolume,
            request.RetrofitInstruction,
            request.MaterialIdentificationRequired,
            request.MaterialUsageStation,
            request.SupplierSupportsMRD,
            request.ExpectedArrivalDate,
            request.TemporaryProcessRequired,
            request.ReworkRequired,
            request.PlannedStartDate,
            request.PlannedEndDate,
            request.ProductionOrderNumber,
            request.RevisionNo,
            Parts = request.Parts.OrderBy(x => x.SortOrder).Select(x => new
            {
                x.ChangeType, x.PartNumber, x.PartName, x.KPC, x.Quantity, x.ReplacedBy
            }),
            Impacts = request.ImpactedDepartments.OrderBy(x => x.DepartmentId).Select(x => new
            {
                x.DepartmentId, x.EstimatedCost
            }),
            ApprovalPlan = request.ApprovalPlan.OrderBy(x => x.LevelNumber).ThenBy(x => x.Sequence).Select(x => new
            {
                x.LevelNumber, x.LevelName, x.ApproverId, x.Sequence
            })
        });
    }

    private static void AddAudit(
        AppDbContext db,
        int requestId,
        int userId,
        string action,
        string entityName,
        string entityId,
        string oldValue,
        string newValue)
    {
        db.AuditLogs.Add(new AuditLog
        {
            RequestId = requestId,
            UserId = userId,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            OldValue = oldValue,
            NewValue = newValue,
            ComputerName = AuditEnvironment.ComputerName,
            IpAddress = AuditEnvironment.LocalIpAddress,
            WindowsIdentity = AuditEnvironment.WindowsIdentityName,
            SessionId = RequestExecutionContext.SessionId,
            CreatedAt = DateTime.Now
        });
    }

    private async Task TryGenerateFinalPdfAsync(int requestId, int actorUserId)
    {
        try
        {
            await _pdf.GenerateFinalApprovedPdfAsync(requestId, actorUserId);
        }
        catch (Exception ex)
        {
            await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
            var request = await db.DCRRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == requestId);
            if (request is not null)
            {
                AddAudit(db, requestId, actorUserId, "Final PDF Generation Failed", "DCRRequest",
                    requestId.ToString(), string.Empty, ex.Message);
                await db.SaveChangesAsync();
            }
        }
    }
}
