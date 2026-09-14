using DCRManagementSystem.Data;
using DCRManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace DCRManagementSystem.Services;

public sealed class ApprovalRouteAssignment
{
    public int ApproverId { get; set; }
    public int? DepartmentId { get; set; }
    public int Sequence { get; set; }
}

public sealed class ApprovalRoutingService
{
    public async Task<List<ApprovalRouteAssignment>> ResolveAsync(
        AppDbContext db,
        DCRRequest request,
        WorkflowStageTemplate template)
    {
        var rules = await db.ApprovalMatrixRules
            .AsNoTracking()
            .Where(x => x.IsActive && x.StageCode == template.StageCode)
            .Where(x => !x.RequestingDepartmentId.HasValue ||
                        x.RequestingDepartmentId == request.RequestingDepartmentId)
            .OrderByDescending(x => x.RequestingDepartmentId.HasValue)
            .ThenBy(x => x.Priority)
            .ThenBy(x => x.Id)
            .ToListAsync();

        if (rules.Count == 0)
        {
            throw new InvalidOperationException(
                $"Chưa cấu hình Approval Matrix cho stage {template.StageCode}.");
        }

        var selectedRules = rules
            .Where(x => x.RequestingDepartmentId == request.RequestingDepartmentId)
            .ToList();

        if (selectedRules.Count == 0)
        {
            selectedRules = rules.Where(x => !x.RequestingDepartmentId.HasValue).ToList();
        }

        var assignments = new List<ApprovalRouteAssignment>();
        var sequence = 1;

        foreach (var rule in selectedRules)
        {
            switch (rule.ApproverSource)
            {
                case ApproverSources.RequestingDepartmentManager:
                {
                    var department = await db.Departments
                        .Include(x => x.ManagerUser)
                        .SingleOrDefaultAsync(x => x.Id == request.RequestingDepartmentId && x.IsActive)
                        ?? throw new InvalidOperationException("Requesting Department không còn hoạt động.");

                    var manager = department.ManagerUser;
                    ValidateManager(manager, department.DepartmentCode);
                    assignments.Add(new ApprovalRouteAssignment
                    {
                        ApproverId = manager!.UserId,
                        DepartmentId = department.Id,
                        Sequence = sequence++
                    });
                    break;
                }

                case ApproverSources.ImpactedDepartmentManager:
                {
                    var impacts = request.ImpactedDepartments
                        .Where(x => x.DepartmentId > 0)
                        .OrderBy(x => x.DepartmentId)
                        .ToList();

                    foreach (var impact in impacts)
                    {
                        var department = await db.Departments
                            .Include(x => x.ManagerUser)
                            .SingleOrDefaultAsync(x => x.Id == impact.DepartmentId && x.IsActive)
                            ?? throw new InvalidOperationException(
                                $"Impacted Department ID {impact.DepartmentId} không còn hoạt động.");

                        var manager = department.ManagerUser;
                        ValidateManager(manager, department.DepartmentCode);
                        assignments.Add(new ApprovalRouteAssignment
                        {
                            ApproverId = manager!.UserId,
                            DepartmentId = department.Id,
                            Sequence = sequence++
                        });
                    }
                    break;
                }

                case ApproverSources.TargetDepartmentManager:
                {
                    if (!rule.TargetDepartmentId.HasValue)
                    {
                        throw new InvalidOperationException(
                            $"Approval Matrix {rule.Id} thiếu Target Department.");
                    }

                    var department = await db.Departments
                        .Include(x => x.ManagerUser)
                        .SingleOrDefaultAsync(x => x.Id == rule.TargetDepartmentId.Value && x.IsActive)
                        ?? throw new InvalidOperationException("Target Department không còn hoạt động.");

                    var manager = department.ManagerUser;
                    ValidateManager(manager, department.DepartmentCode);
                    assignments.Add(new ApprovalRouteAssignment
                    {
                        ApproverId = manager!.UserId,
                        DepartmentId = department.Id,
                        Sequence = sequence++
                    });
                    break;
                }

                case ApproverSources.SpecificUser:
                {
                    if (!rule.ApproverUserId.HasValue)
                    {
                        throw new InvalidOperationException(
                            $"Approval Matrix {rule.Id} thiếu Specific User.");
                    }

                    var user = await db.Users
                        .AsNoTracking()
                        .SingleOrDefaultAsync(x => x.UserId == rule.ApproverUserId.Value && x.IsActive && !x.IsDeleted)
                        ?? throw new InvalidOperationException("Specific approver không còn hoạt động.");

                    assignments.Add(new ApprovalRouteAssignment
                    {
                        ApproverId = user.UserId,
                        DepartmentId = user.DepartmentId,
                        Sequence = sequence++
                    });
                    break;
                }

                case ApproverSources.Role:
                {
                    if (string.IsNullOrWhiteSpace(rule.ApproverRole))
                    {
                        throw new InvalidOperationException(
                            $"Approval Matrix {rule.Id} thiếu Approver Role.");
                    }

                    var userQuery = db.Users
                        .AsNoTracking()
                        .Where(x => x.IsActive && !x.IsDeleted && x.Role == rule.ApproverRole);

                    if (rule.TargetDepartmentId.HasValue)
                    {
                        var targetDepartment = await db.Departments.AsNoTracking()
                            .SingleOrDefaultAsync(x => x.Id == rule.TargetDepartmentId.Value && x.IsActive)
                            ?? throw new InvalidOperationException("Target Department không còn hoạt động.");
                        var roleLevel = await db.Roles.AsNoTracking()
                            .Where(x => x.RoleName == rule.ApproverRole && x.IsActive)
                            .Select(x => (int?)x.HierarchyLevel)
                            .SingleOrDefaultAsync() ?? 0;
                        var directorLevel = await db.Roles.AsNoTracking()
                            .Where(x => x.RoleName == RoleNames.Director)
                            .Select(x => x.HierarchyLevel)
                            .SingleAsync();

                        // Manager/Staff are department scoped. Director and higher roles are Business Unit scoped.
                        if (roleLevel >= directorLevel && targetDepartment.BusinessUnitId.HasValue)
                        {
                            var scopeUnitIds = await GetUnitAndAncestorIdsAsync(db, targetDepartment.BusinessUnitId.Value);
                            userQuery = userQuery.Where(x =>
                                (x.BusinessUnitId.HasValue && scopeUnitIds.Contains(x.BusinessUnitId.Value)) ||
                                x.BusinessUnitAssignments.Any(a => scopeUnitIds.Contains(a.BusinessUnitId)));
                        }
                        else
                            userQuery = userQuery.Where(x =>
                                x.DepartmentId == rule.TargetDepartmentId.Value ||
                                x.DepartmentAssignments.Any(a => a.DepartmentId == rule.TargetDepartmentId.Value));
                    }

                    var user = await userQuery
                        .OrderBy(x => x.UserId)
                        .FirstOrDefaultAsync()
                        ?? throw new InvalidOperationException(
                            rule.TargetDepartmentId.HasValue
                                ? $"Không có user hoạt động với role {rule.ApproverRole} trong phạm vi tổ chức của Target Department ID {rule.TargetDepartmentId.Value}."
                                : $"Không có user hoạt động với role {rule.ApproverRole}.");

                    assignments.Add(new ApprovalRouteAssignment
                    {
                        ApproverId = user.UserId,
                        DepartmentId = user.DepartmentId,
                        Sequence = sequence++
                    });
                    break;
                }

                default:
                    throw new InvalidOperationException(
                        $"Approver Source '{rule.ApproverSource}' không được hỗ trợ.");
            }
        }

        return assignments
            .GroupBy(x => new { x.ApproverId, x.DepartmentId })
            .Select((x, index) => new ApprovalRouteAssignment
            {
                ApproverId = x.Key.ApproverId,
                DepartmentId = x.Key.DepartmentId,
                Sequence = index + 1
            })
            .ToList();
    }

    private static async Task<List<int>> GetUnitAndAncestorIdsAsync(AppDbContext db, int businessUnitId)
    {
        var result = new List<int>();
        var visited = new HashSet<int>();
        int? currentId = businessUnitId;
        while (currentId.HasValue && visited.Add(currentId.Value))
        {
            result.Add(currentId.Value);
            currentId = await db.BusinessUnits.AsNoTracking()
                .Where(x => x.Id == currentId.Value && x.IsActive)
                .Select(x => x.ParentBusinessUnitId)
                .SingleOrDefaultAsync();
        }

        return result;
    }

    private static void ValidateManager(User? manager, string departmentCode)
    {
        if (manager is null || !manager.IsActive || manager.IsDeleted)
        {
            throw new InvalidOperationException(
                $"Phòng ban {departmentCode} chưa có Manager đang hoạt động.");
        }
    }
}
