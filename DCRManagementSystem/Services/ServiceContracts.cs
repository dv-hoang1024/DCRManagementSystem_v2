using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;

namespace DCRManagementSystem.Services;

public interface IAuthService
{
    Task<AuthenticationResult?> LoginWithRememberedWindowsAsync();
    Task<AuthenticationResult?> LoginWithWindowsAsync();
    Task<AuthenticationResult?> LoginAsync(string username, string password);
    Task ChangePasswordAsync(int userId, string currentPassword, string newPassword);
    void RememberCurrentWindowsLogin(User user);
    void ForgetRememberedWindowsLogin();
    Task<DecisionAuthentication> AuthenticateDecisionAsync(int userId);
}

public interface IDcrService
{
    Task<List<Department>> GetActiveDepartmentsAsync();
    Task<List<ProductLineDefinition>> GetActiveProductLinesAsync();
    Task<List<PartChangeTypeDefinition>> GetActivePartChangeTypesAsync();
    Task<List<ApproverSearchItem>> SearchApproversAsync(string searchText, int maxResults = 40);
    Task<ApprovalPlanSuggestionResult> GetSuggestedApprovalPlanAsync(int userId, string rank);
    Task<List<ApprovalPlanTemplateEditModel>> GetApprovalPlanTemplatesAsync(string rank);
    Task<List<ApprovalPlanEditItem>> GetSavedApprovalPlanAsync(int userId, string rank);
    Task<List<ApprovalPlanEditItem>> SaveUserApprovalPlanAsync(int userId, string rank, IEnumerable<ApprovalPlanEditItem> approvalPlan);
    Task<List<DcrListItem>> GetListAsync(string scope, int userId, string searchText, bool isAdmin = false);
    Task<int> GetPendingApprovalCountAsync(int userId);
    Task<bool> CanViewAsync(int requestId, int userId, bool isAdmin);
    Task<DcrEditModel> CreateNewModelAsync(int userId);
    Task<DcrEditModel> LoadEditDataAsync(int requestId);
    Task<int> SaveDraftAsync(DcrEditModel model, int userId, bool isAdmin, int draftStep = 0, bool isAutoSave = false);
    Task<SubmitDcrResult> SubmitAsync(int requestId, int userId, bool isAdmin, byte[] expectedRowVersion);
    Task<bool> CanApproveAsync(int requestId, int userId);
    Task<DecisionProcessResult> ProcessDecisionAsync(int requestId, int userId, string decision, string comment, DecisionAuthentication authentication, byte[] expectedRowVersion);
    Task<List<ApprovalHistoryItem>> GetApprovalHistoryAsync(int requestId);
    Task<List<AttachmentListItem>> GetAttachmentsAsync(int requestId);
    Task<List<AuditListItem>> GetAuditLogsAsync(int requestId);
    Task<string> GetDcrNumberAsync(int requestId);
}

public interface IAdminService
{
    Task DeleteDcrAsync(int requestId, int actorUserId, bool isAdmin);
    Task<List<User>> GetUsersAsync();
    Task<List<ProductLineDefinition>> GetProductLinesAsync(bool activeOnly = false);
    Task<ProductLineDefinition> SaveProductLineAsync(int? id, string name, int sortOrder, bool isActive);
    Task DeleteProductLineAsync(int id);
    Task<List<PartChangeTypeDefinition>> GetPartChangeTypesAsync(bool activeOnly = false);
    Task<PartChangeTypeDefinition> SavePartChangeTypeAsync(int? id, string name, int sortOrder, bool isActive);
    Task DeletePartChangeTypeAsync(int id);
    Task<List<RoleDefinition>> GetRolesAsync(bool activeOnly = false);
    Task<RoleDefinition> SaveRoleAsync(int? roleId, string roleName, string description, int hierarchyLevel, bool isActive);
    Task DeleteRoleAsync(int roleId);
    Task DeleteUserAsync(int userId, int actorUserId);
    Task<User> SaveUserModulePermissionsAsync(int userId, bool canUseProductionTracking, bool canUseWarehouseManagement);
    Task<List<BusinessUnit>> GetBusinessUnitsAsync(bool activeOnly = false);
    Task<BusinessUnit> SaveBusinessUnitAsync(int? id, string code, string name, int? directorUserId, bool isActive, int? parentBusinessUnitId = null, string? unitType = null, int sortOrder = 0);
    Task DeleteBusinessUnitAsync(int id);
    Task<List<Department>> GetDepartmentsAsync(bool activeOnly = false);
    Task DeleteDepartmentAsync(int id);
    Task<User> SaveUserAsync(int? userId, string username, string fullName, string email, string phone, int? businessUnitId, int? departmentId, IReadOnlyCollection<int>? businessUnitIds, IReadOnlyCollection<int>? departmentIds, int? directManagerUserId, string role, bool isActive, string? newPassword);
    Task SaveOrganizationAssignmentDetailsAsync(int userId, int? businessUnitId, int? departmentId, string jobTitle, int? reportsToUserId, bool isActing, int sortOrder);
    Task<string> NormalizeOrganizationLinksAsync();
    Task<Department> SaveDepartmentAsync(int? id, string code, string name, int? businessUnitId, int? managerUserId, bool isActive);
    Task<List<WorkflowStageTemplate>> GetWorkflowTemplatesAsync();
    Task SaveWorkflowTemplatesAsync(IEnumerable<WorkflowStageTemplate> templates);
    Task<List<ApprovalPlanTemplateEditModel>> GetApprovalPlanTemplatesAsync(bool activeOnly = false, string? rank = null);
    Task<ApprovalPlanTemplateEditModel> SaveApprovalPlanTemplateAsync(ApprovalPlanTemplateEditModel template);
    Task DeleteApprovalPlanTemplateAsync(int templateId);
    Task<List<ApprovalMatrixRule>> GetApprovalMatrixRulesAsync();
    Task ReplaceApprovalMatrixRulesAsync(IEnumerable<ApprovalMatrixRule> inputRules);
}

public interface IAttachmentService
{
    Task UploadAsync(int requestId, string sourceFile, string attachmentType, int userId, bool isAdmin);
    Task DeleteAsync(int attachmentId, int userId, bool isAdmin);
    Task<string> GetAbsolutePathAsync(int attachmentId, int userId, bool isAdmin);
    Task OpenAsync(int attachmentId, int userId, bool isAdmin);
}

public interface IPdfService
{
    Task ExportAsync(int requestId, string outputPath);
    Task<string> GeneratePreviewPdfAsync(int requestId, CancellationToken cancellationToken = default);
    Task<string> GenerateFinalApprovedPdfAsync(int requestId, int? actorUserId = null);
    Task<string> GetVerifiedFinalApprovedPdfPathAsync(int requestId);
}


public interface IWebPortalConfigurationService
{
    Task<WebPortalSettings> GetAsync();
    Task SaveAsync(WebPortalSettings value);
}

public interface IStorageConfigurationService
{
    Task<FileStorageSettings> GetAsync();
    Task SaveAsync(FileStorageSettings value);
    Task<string> TestConnectionAsync(FileStorageSettings value);
    string ResolveRoot(FileStorageSettings value);
}

public interface IEmailConfigurationService
{
    Task<EmailSettings> GetAsync();
    Task SaveAsync(EmailSettings value);
    Task<string> TestAsync(EmailSettings value, string recipient);
    Task<string> SignInGraphAsync(EmailSettings value);
    Task<string> SignInGraphWithAccountSelectionAsync(EmailSettings value);
    Task SignOutGraphAsync(EmailSettings value);
    Task<string> GetSignedInGraphAccountAsync(EmailSettings value);
}

public interface IEmailOutboxService
{
    Task<long> EnqueueTestAsync(string recipient, CancellationToken cancellationToken = default);
    Task<int> RequeueAuthenticationBlockedAsync(CancellationToken cancellationToken = default);
    Task<EmailOutboxSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}

public interface IMailWorkerStateService
{
    Task<MailWorkerState> GetAsync(CancellationToken cancellationToken = default);
    Task RecordSignInAsync(string signedInAccount, CancellationToken cancellationToken = default);
    Task RecordSignOutAsync(CancellationToken cancellationToken = default);
}

public interface IMailWorkerService
{
    Task<MailWorkerRunResult> RunOnceAsync(int batchSize = 25, CancellationToken cancellationToken = default);
    Task<MailWorkerRunResult> RunPendingNowAsync(int batchSize = 25, CancellationToken cancellationToken = default);
}
