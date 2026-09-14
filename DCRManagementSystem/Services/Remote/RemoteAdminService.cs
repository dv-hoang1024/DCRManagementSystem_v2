using DCRManagementSystem.Models;

namespace DCRManagementSystem.Services.Remote;

internal sealed class RemoteAdminService : IAdminService
{
    private readonly RemoteApiClient _api;
    public RemoteAdminService(Helpers.AppSettings settings) => _api = new RemoteApiClient(settings);

    public Task DeleteDcrAsync(int requestId, int actorUserId, bool isAdmin) => _api.DeleteAsync($"api/admin/dcr/{requestId}");
    public Task<List<User>> GetUsersAsync() => _api.GetAsync<List<User>>("api/admin/users");
    public Task<List<ProductLineDefinition>> GetProductLinesAsync(bool activeOnly = false) => _api.GetAsync<List<ProductLineDefinition>>($"api/admin/product-lines?activeOnly={activeOnly}");
    public Task<ProductLineDefinition> SaveProductLineAsync(int? id, string name, int sortOrder, bool isActive) =>
        _api.PostAsync<ApiSaveProductLineRequest, ProductLineDefinition>("api/admin/product-lines", new ApiSaveProductLineRequest { Id = id, Name = name, SortOrder = sortOrder, IsActive = isActive });
    public Task DeleteProductLineAsync(int id) => _api.DeleteAsync($"api/admin/product-lines/{id}");
    public Task<List<PartChangeTypeDefinition>> GetPartChangeTypesAsync(bool activeOnly = false) => _api.GetAsync<List<PartChangeTypeDefinition>>($"api/admin/change-types?activeOnly={activeOnly}");
    public Task<PartChangeTypeDefinition> SavePartChangeTypeAsync(int? id, string name, int sortOrder, bool isActive) =>
        _api.PostAsync<ApiSavePartChangeTypeRequest, PartChangeTypeDefinition>("api/admin/change-types", new ApiSavePartChangeTypeRequest { Id = id, Name = name, SortOrder = sortOrder, IsActive = isActive });
    public Task DeletePartChangeTypeAsync(int id) => _api.DeleteAsync($"api/admin/change-types/{id}");
    public Task<List<RoleDefinition>> GetRolesAsync(bool activeOnly = false) => _api.GetAsync<List<RoleDefinition>>($"api/admin/roles?activeOnly={activeOnly}");
    public Task<RoleDefinition> SaveRoleAsync(int? roleId, string roleName, string description, int hierarchyLevel, bool isActive) =>
        _api.PostAsync<ApiSaveRoleRequest, RoleDefinition>("api/admin/roles", new ApiSaveRoleRequest { RoleId = roleId, RoleName = roleName, Description = description, HierarchyLevel = hierarchyLevel, IsActive = isActive });
    public Task DeleteRoleAsync(int roleId) => _api.DeleteAsync($"api/admin/roles/{roleId}");
    public Task DeleteUserAsync(int userId, int actorUserId) => _api.DeleteAsync($"api/admin/users/{userId}");
    public Task<User> SaveUserModulePermissionsAsync(int userId, bool canUseProductionTracking, bool canUseWarehouseManagement) =>
        _api.PostAsync<ApiSaveUserModulePermissionsRequest, User>($"api/admin/users/{userId}/module-permissions",
            new ApiSaveUserModulePermissionsRequest
            {
                CanUseProductionTracking = canUseProductionTracking,
                CanUseWarehouseManagement = canUseWarehouseManagement
            });
    public Task<List<BusinessUnit>> GetBusinessUnitsAsync(bool activeOnly = false) => _api.GetAsync<List<BusinessUnit>>($"api/admin/business-units?activeOnly={activeOnly}");
    public Task<BusinessUnit> SaveBusinessUnitAsync(int? id, string code, string name, int? directorUserId, bool isActive, int? parentBusinessUnitId = null, string? unitType = null, int sortOrder = 0) =>
        _api.PostAsync<ApiSaveBusinessUnitRequest, BusinessUnit>("api/admin/business-units", new ApiSaveBusinessUnitRequest { Id = id, Code = code, Name = name, DirectorUserId = directorUserId, IsActive = isActive, ParentBusinessUnitId = parentBusinessUnitId, UnitType = unitType ?? OrganizationUnitTypes.Division, SortOrder = sortOrder });
    public Task DeleteBusinessUnitAsync(int id) => _api.DeleteAsync($"api/admin/business-units/{id}");
    public Task<List<Department>> GetDepartmentsAsync(bool activeOnly = false) => _api.GetAsync<List<Department>>($"api/admin/departments?activeOnly={activeOnly}");
    public Task DeleteDepartmentAsync(int id) => _api.DeleteAsync($"api/admin/departments/{id}");
    public Task<User> SaveUserAsync(int? userId, string username, string fullName, string email, string phone, int? businessUnitId, int? departmentId, IReadOnlyCollection<int>? businessUnitIds, IReadOnlyCollection<int>? departmentIds, int? directManagerUserId, string role, bool isActive, string? newPassword) =>
        _api.PostAsync<ApiSaveUserRequest, User>("api/admin/users", new ApiSaveUserRequest
        {
            UserId = userId,
            Username = username,
            FullName = fullName,
            Email = email,
            Phone = phone,
            BusinessUnitId = businessUnitId,
            DepartmentId = departmentId,
            BusinessUnitIds = businessUnitIds?.Distinct().ToList() ?? new List<int>(),
            DepartmentIds = departmentIds?.Distinct().ToList() ?? new List<int>(),
            DirectManagerUserId = directManagerUserId,
            Role = role,
            IsActive = isActive,
            NewPassword = newPassword
        });
    public Task<Department> SaveDepartmentAsync(int? id, string code, string name, int? businessUnitId, int? managerUserId, bool isActive) =>
        _api.PostAsync<ApiSaveDepartmentRequest, Department>("api/admin/departments", new ApiSaveDepartmentRequest { Id = id, Code = code, Name = name, BusinessUnitId = businessUnitId, ManagerUserId = managerUserId, IsActive = isActive });
    public Task SaveOrganizationAssignmentDetailsAsync(int userId, int? businessUnitId, int? departmentId, string jobTitle, int? reportsToUserId, bool isActing, int sortOrder) =>
        _api.PostAsync("api/admin/organization/assignment", new ApiSaveOrganizationAssignmentRequest { UserId = userId, BusinessUnitId = businessUnitId, DepartmentId = departmentId, JobTitle = jobTitle, ReportsToUserId = reportsToUserId, IsActing = isActing, SortOrder = sortOrder });
    public Task<string> NormalizeOrganizationLinksAsync() => _api.PostAsync<object, string>("api/admin/organization/normalize", new { });
    public Task<List<WorkflowStageTemplate>> GetWorkflowTemplatesAsync() => _api.GetAsync<List<WorkflowStageTemplate>>("api/admin/workflow-templates");
    public Task SaveWorkflowTemplatesAsync(IEnumerable<WorkflowStageTemplate> templates) => _api.PostAsync("api/admin/workflow-templates", new ApiWorkflowTemplatesRequest { Templates = templates.ToList() });
    public Task<List<ApprovalPlanTemplateEditModel>> GetApprovalPlanTemplatesAsync(bool activeOnly = false, string? rank = null) =>
        _api.GetAsync<List<ApprovalPlanTemplateEditModel>>($"api/admin/approval-plan-templates?activeOnly={activeOnly}&rank={Uri.EscapeDataString(rank ?? string.Empty)}");
    public Task<ApprovalPlanTemplateEditModel> SaveApprovalPlanTemplateAsync(ApprovalPlanTemplateEditModel template) =>
        _api.PostAsync<ApiSaveApprovalPlanTemplateRequest, ApprovalPlanTemplateEditModel>("api/admin/approval-plan-templates", new ApiSaveApprovalPlanTemplateRequest { Template = template });
    public Task DeleteApprovalPlanTemplateAsync(int templateId) => _api.DeleteAsync($"api/admin/approval-plan-templates/{templateId}");
    public Task<List<ApprovalMatrixRule>> GetApprovalMatrixRulesAsync() => _api.GetAsync<List<ApprovalMatrixRule>>("api/admin/approval-matrix");
    public Task ReplaceApprovalMatrixRulesAsync(IEnumerable<ApprovalMatrixRule> inputRules) => _api.PostAsync("api/admin/approval-matrix", new ApiApprovalMatrixRequest { Rules = inputRules.ToList() });
}
