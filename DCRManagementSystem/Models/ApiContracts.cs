using DCRManagementSystem.Helpers;

namespace DCRManagementSystem.Models;

public sealed class ApiLoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string WindowsIdentity { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
}

public sealed class ApiRefreshRequest
{
    public string RefreshToken { get; set; } = string.Empty;
    public string WindowsIdentity { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
}

public sealed class ApiLoginResponse
{
    public AuthenticationResult Authentication { get; set; } = new() { User = new User() };
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime AccessTokenExpiresUtc { get; set; }
    public DateTime RefreshTokenExpiresUtc { get; set; }
}

public sealed class ApiReauthenticateRequest
{
    public string Password { get; set; } = string.Empty;
}

public sealed class ApiChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public sealed class ApiDecisionProofResponse
{
    public DecisionAuthentication Authentication { get; set; } = new();
}

public sealed class ApiSaveDraftRequest
{
    public DcrEditModel Model { get; set; } = new();
    public int DraftStep { get; set; }
    public bool IsAutoSave { get; set; }
}

public sealed class ApiSaveDraftResponse
{
    public int RequestId { get; set; }
    public bool AlreadyExisted { get; set; }
    public DcrEditModel Model { get; set; } = new();
}

public sealed class ApiSubmitRequest
{
    public byte[] ExpectedRowVersion { get; set; } = Array.Empty<byte>();
}

public sealed class ApiSaveUserApprovalPlanRequest
{
    public string Rank { get; set; } = DcrRanks.C;
    public List<ApprovalPlanEditItem> ApprovalPlan { get; set; } = new();
}

public sealed class ApiDecisionRequest
{
    public string Decision { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public DecisionAuthentication Authentication { get; set; } = new();
    public byte[] ExpectedRowVersion { get; set; } = Array.Empty<byte>();
}

public sealed class ApiSaveProductLineRequest
{
    public int? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
}

public sealed class ApiSavePartChangeTypeRequest
{
    public int? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
}

public sealed class ApiSaveRoleRequest
{
    public int? RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int HierarchyLevel { get; set; }
    public bool IsActive { get; set; }
}

public sealed class ApiSaveBusinessUnitRequest
{
    public int? Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int? DirectorUserId { get; set; }
    public bool IsActive { get; set; }
    public int? ParentBusinessUnitId { get; set; }
    public string UnitType { get; set; } = OrganizationUnitTypes.Division;
    public int SortOrder { get; set; }
}

public sealed class ApiSaveOrganizationAssignmentRequest
{
    public int UserId { get; set; }
    public int? BusinessUnitId { get; set; }
    public int? DepartmentId { get; set; }
    public string JobTitle { get; set; } = string.Empty;
    public int? ReportsToUserId { get; set; }
    public bool IsActing { get; set; }
    public int SortOrder { get; set; }
}

public sealed class ApiSaveUserRequest
{
    public int? UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public int? BusinessUnitId { get; set; }
    public int? DepartmentId { get; set; }
    public List<int> BusinessUnitIds { get; set; } = new();
    public List<int> DepartmentIds { get; set; } = new();
    public int? DirectManagerUserId { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string? NewPassword { get; set; }
}

public sealed class ApiSaveUserModulePermissionsRequest
{
    public bool CanUseProductionTracking { get; set; }
    public bool CanUseWarehouseManagement { get; set; }
}

public sealed class ApiSaveDepartmentRequest
{
    public int? Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int? BusinessUnitId { get; set; }
    public int? ManagerUserId { get; set; }
    public bool IsActive { get; set; }
}

public sealed class ApiWorkflowTemplatesRequest
{
    public List<WorkflowStageTemplate> Templates { get; set; } = new();
}

public sealed class ApiSaveApprovalPlanTemplateRequest
{
    public ApprovalPlanTemplateEditModel Template { get; set; } = new();
}

public sealed class ApiApprovalMatrixRequest
{
    public List<ApprovalMatrixRule> Rules { get; set; } = new();
}


public sealed class ApiWebPortalSettingsRequest
{
    public WebPortalSettings Settings { get; set; } = new();
}

public sealed class ApiStorageSettingsRequest
{
    public FileStorageSettings Settings { get; set; } = new();
}

public sealed class ApiEmailSettingsRequest
{
    public EmailSettings Settings { get; set; } = new();
    public string Recipient { get; set; } = string.Empty;
}

public sealed class ApiUploadStartRequest
{
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string AttachmentType { get; set; } = string.Empty;
}

public sealed class ApiUploadStartResponse
{
    public string UploadId { get; set; } = string.Empty;
    public int ChunkSizeBytes { get; set; } = 8 * 1024 * 1024;
}

public sealed class ApiErrorResponse
{
    public string Error { get; set; } = string.Empty;
    public string ErrorType { get; set; } = string.Empty;
    public string TraceId { get; set; } = string.Empty;
}

public sealed class ApiHealthResponse
{
    public string Status { get; set; } = "ok";
    public string Server { get; set; } = string.Empty;
    public DateTime ServerTimeUtc { get; set; }
    public string Database { get; set; } = string.Empty;
}
