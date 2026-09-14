using System.Text.Json.Serialization;

namespace DCRManagementSystem.Api.WebPortal.Models;

public sealed class DcrPortalSettings
{
    public bool Enabled { get; set; }
    public bool AllowCreate { get; set; } = true;
    public bool AllowApproval { get; set; } = true;
    public string BaseUrl { get; set; } = "https://dcr.ggpcontrol.cloud";
}

public sealed class DcrUser
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string WindowsAccount { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public int? DepartmentId { get; set; }
    public int? BusinessUnitId { get; set; }
    public int? DirectManagerUserId { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool CanUseProductionTracking { get; set; }
    public bool CanUseWarehouseManagement { get; set; }
    public DcrDepartment? Department { get; set; }
}

public sealed class DcrAuthenticationResult
{
    public DcrUser User { get; set; } = new();
    public string AuthMethod { get; set; } = string.Empty;
    public string WindowsIdentity { get; set; } = string.Empty;
}

public sealed class DcrApiLoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string WindowsIdentity { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
}

public sealed class DcrApiRefreshRequest
{
    public string RefreshToken { get; set; } = string.Empty;
    public string WindowsIdentity { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
}

public sealed class DcrApiChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public sealed class DcrApiLoginResponse
{
    public DcrAuthenticationResult Authentication { get; set; } = new();
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime AccessTokenExpiresUtc { get; set; }
    public DateTime RefreshTokenExpiresUtc { get; set; }
}

public sealed class DcrApiErrorResponse
{
    public string Error { get; set; } = string.Empty;
    public string ErrorType { get; set; } = string.Empty;
    public string TraceId { get; set; } = string.Empty;
}

public sealed class DcrListItem
{
    public int Id { get; set; }
    public string DCRNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Rank { get; set; } = "C";
    public string Program { get; set; } = string.Empty;
    public string BuildStage { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int CurrentStage { get; set; }
    public int RevisionNo { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? LastSavedAt { get; set; }
}

public sealed class DcrEditModel
{
    public int? Id { get; set; }
    public Guid CreationToken { get; set; } = Guid.NewGuid();
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    public string DCRNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Rank { get; set; } = "C";
    public int RequestingDepartmentId { get; set; }
    public string ModuleGroup { get; set; } = string.Empty;
    public int RequestOwnerId { get; set; }
    public string RequestOwnerName { get; set; } = string.Empty;
    public string RequestOwnerEmail { get; set; } = string.Empty;
    public string RequestOwnerPhone { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; }
    public string Program { get; set; } = string.Empty;
    public string BuildStage { get; set; } = string.Empty;
    public string RelatedECR { get; set; } = string.Empty;
    public string RelatedPPS { get; set; } = string.Empty;
    public string RelatedECN { get; set; } = string.Empty;
    public string RelatedMCN { get; set; } = string.Empty;
    public string ProblemDescription { get; set; } = string.Empty;
    public string Solution { get; set; } = string.Empty;
    public string MaterialChangeDescription { get; set; } = string.Empty;
    public string FormFitFunctionDetail { get; set; } = string.Empty;
    public string RetrofitVolume { get; set; } = string.Empty;
    public string RetrofitInstruction { get; set; } = string.Empty;
    public bool MaterialIdentificationRequired { get; set; }
    public string MaterialUsageStation { get; set; } = string.Empty;
    public bool SupplierSupportsMRD { get; set; }
    public DateTime? ExpectedArrivalDate { get; set; }
    public bool TemporaryProcessRequired { get; set; }
    public bool ReworkRequired { get; set; }
    public DateTime? PlannedStartDate { get; set; }
    public DateTime? PlannedEndDate { get; set; }
    public string ProductionOrderNumber { get; set; } = string.Empty;
    public string Status { get; set; } = "Draft";
    public int CurrentStage { get; set; }
    public int RevisionNo { get; set; } = 1;
    public int DraftStep { get; set; }
    public DateTime? LastSavedAt { get; set; }
    public DateTime? ReturnedDate { get; set; }
    public string LastReturnReason { get; set; } = string.Empty;
    public string FinalPdfPath { get; set; } = string.Empty;
    public string FinalPdfSha256 { get; set; } = string.Empty;
    public DateTime? FinalPdfGeneratedAt { get; set; }
    public int CreatedBy { get; set; }
    public List<DcrPartEditItem> Parts { get; set; } = new();
    public List<DcrImpactDepartmentEditItem> ImpactedDepartments { get; set; } = new();
    public List<DcrApprovalPlanEditItem> ApprovalPlan { get; set; } = new();
}

public sealed class DcrApprovalPlanEditItem
{
    public int Id { get; set; }
    public int LevelNumber { get; set; } = 1;
    public string LevelName { get; set; } = string.Empty;
    public int ApproverId { get; set; }
    public string ApproverName { get; set; } = string.Empty;
    public string ApproverEmail { get; set; } = string.Empty;
    public string ApproverRole { get; set; } = string.Empty;
    public int? BusinessUnitId { get; set; }
    public string BusinessUnitCode { get; set; } = string.Empty;
    public string BusinessUnitName { get; set; } = string.Empty;
    public int? DepartmentId { get; set; }
    public string DepartmentCode { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public int Sequence { get; set; }
}

public sealed class DcrApprovalPlanSuggestionResult
{
    public string Rank { get; set; } = "C";
    public List<DcrApprovalPlanEditItem> ApprovalPlan { get; set; } = new();
    public List<string> MissingApprovers { get; set; } = new();
    public bool IsComplete => ApprovalPlan.Count > 0 && MissingApprovers.Count == 0;
}

public sealed class DcrApprovalPlanTemplate
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Rank { get; set; } = "C";
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<DcrApprovalPlanEditItem> ApprovalPlan { get; set; } = new();
    public string DisplayName => $"Rank {Rank} - {Name}" + (IsDefault ? " (Mặc định)" : string.Empty);
}

public sealed class DcrApproverSearchItem
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int? BusinessUnitId { get; set; }
    public string BusinessUnitCode { get; set; } = string.Empty;
    public string BusinessUnit { get; set; } = string.Empty;
    public int? DepartmentId { get; set; }
    public string DepartmentCode { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

public sealed class DcrPartEditItem
{
    public int Id { get; set; }
    public string ChangeType { get; set; } = string.Empty;
    public string PartNumber { get; set; } = string.Empty;
    public string PartName { get; set; } = string.Empty;
    public string KPC { get; set; } = string.Empty;
    public string Quantity { get; set; } = string.Empty;
    public string ReplacedBy { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public sealed class DcrImpactDepartmentEditItem
{
    public int Id { get; set; }
    public int DepartmentId { get; set; }
    public decimal? EstimatedCost { get; set; }
}

public sealed class DcrDepartment
{
    public int Id { get; set; }
    public string DepartmentCode { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public sealed class DcrProductLine
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
}

public sealed class DcrPartChangeType
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
}

public sealed class DcrApprovalHistoryItem
{
    public int Id { get; set; }
    public int RevisionNo { get; set; }
    public int StageNumber { get; set; }
    public string StageName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Approver { get; set; } = string.Empty;
    public string Decision { get; set; } = string.Empty;
    public DateTime? DecisionDate { get; set; }
    public string Comments { get; set; } = string.Empty;
    public string AuthMethod { get; set; } = string.Empty;
    public string SignatureStatus { get; set; } = string.Empty;
}

public sealed class DcrAttachmentItem
{
    public int Id { get; set; }
    public string AttachmentType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public long StoredFileSize { get; set; }
    public bool IsCompressed { get; set; }
    public string Sha256Hash { get; set; } = string.Empty;
    public string UploadedBy { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }
}

public sealed class DcrAuditItem
{
    public long Id { get; set; }
    public string User { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string ComputerName { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
}

public sealed class DcrApiSaveDraftRequest
{
    public DcrEditModel Model { get; set; } = new();
    public int DraftStep { get; set; }
    public bool IsAutoSave { get; set; }
}

public sealed class DcrApiSaveDraftResponse
{
    public int RequestId { get; set; }
    public bool AlreadyExisted { get; set; }
    public DcrEditModel Model { get; set; } = new();
}

public sealed class DcrApiSubmitRequest
{
    public byte[] ExpectedRowVersion { get; set; } = Array.Empty<byte>();
}

public sealed class DcrDecisionAuthentication
{
    public string AuthMethod { get; set; } = string.Empty;
    public DateTime AuthenticatedAt { get; set; }
    public string WindowsIdentity { get; set; } = string.Empty;
    public string ProofToken { get; set; } = string.Empty;
}

public sealed class DcrApiDecisionRequest
{
    public string Decision { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public DcrDecisionAuthentication Authentication { get; set; } = new();
    public byte[] ExpectedRowVersion { get; set; } = Array.Empty<byte>();
}

public sealed class DcrSubmitResult
{
    public bool ApprovedImmediately { get; set; }
}

public sealed class DcrDecisionResult
{
    public string Decision { get; set; } = string.Empty;
    public bool StageAdvanced { get; set; }
    public int PreviousStage { get; set; }
    public int CurrentStage { get; set; }
    public bool WorkflowCompleted { get; set; }
    public string RequestStatus { get; set; } = string.Empty;
}

public sealed class DcrUploadStartRequest
{
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string AttachmentType { get; set; } = string.Empty;
}

public sealed class DcrUploadStartResponse
{
    public string UploadId { get; set; } = string.Empty;
    public int ChunkSizeBytes { get; set; } = 8 * 1024 * 1024;
}

public sealed record DcrDownload(Stream Data, string FileName, string ContentType);

public static class DcrScopes
{
    public const string PendingMyApproval = "Chờ tôi duyệt";
    public const string RelatedToMe = "DCR liên quan đến tôi";
    public const string MyRequests = "Tôi đã tạo";
    public const string Approved = "Đã hoàn thành";
    public const string Rejected = "Đã từ chối";
    public const string All = "Tất cả";
}

public static class DcrDecisions
{
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Returned = "Returned";
}
