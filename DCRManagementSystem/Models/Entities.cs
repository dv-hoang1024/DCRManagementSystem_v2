using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace DCRManagementSystem.Models;

public sealed class User
{
    public int UserId { get; set; }

    [MaxLength(80)]
    public string Username { get; set; } = string.Empty;

    [MaxLength(200)]
    [JsonIgnore]
    public string PasswordHash { get; set; } = string.Empty;

    [MaxLength(256)]
    public string WindowsAccount { get; set; } = string.Empty;

    [MaxLength(160)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(40)]
    public string Phone { get; set; } = string.Empty;

    public int? DepartmentId { get; set; }
    public int? BusinessUnitId { get; set; }
    public int? DirectManagerUserId { get; set; }

    [MaxLength(80)]
    public string Role { get; set; } = RoleNames.Staff;

    public bool IsActive { get; set; } = true;

    // GGP Control module permissions are managed per user and are independent
    // from Department/Role. They are also returned by DCR API login so the
    // Production/Warehouse gateway can enforce the same permission centrally.
    public bool CanUseProductionTracking { get; set; }
    public bool CanUseWarehouseManagement { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int? DeletedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public Department? Department { get; set; }
    public BusinessUnit? BusinessUnit { get; set; }
    public User? DirectManager { get; set; }
    public List<UserBusinessUnitAssignment> BusinessUnitAssignments { get; set; } = new();
    public List<UserDepartmentAssignment> DepartmentAssignments { get; set; } = new();
}

public sealed class UserBusinessUnitAssignment
{
    public int UserId { get; set; }
    public int BusinessUnitId { get; set; }
    public bool IsPrimary { get; set; }

    [MaxLength(200)]
    public string JobTitle { get; set; } = string.Empty;
    public int? ReportsToUserId { get; set; }
    public bool IsActing { get; set; }
    public int SortOrder { get; set; }

    [JsonIgnore]
    public User? User { get; set; }
    public BusinessUnit? BusinessUnit { get; set; }
    [JsonIgnore]
    public User? ReportsToUser { get; set; }
}

public sealed class UserDepartmentAssignment
{
    public int UserId { get; set; }
    public int DepartmentId { get; set; }
    public bool IsPrimary { get; set; }

    [MaxLength(200)]
    public string JobTitle { get; set; } = string.Empty;
    public int? ReportsToUserId { get; set; }
    public bool IsActing { get; set; }
    public int SortOrder { get; set; }

    [JsonIgnore]
    public User? User { get; set; }
    public Department? Department { get; set; }
    [JsonIgnore]
    public User? ReportsToUser { get; set; }
}

public sealed class RoleDefinition
{
    public int Id { get; set; }

    [MaxLength(80)]
    public string RoleName { get; set; } = string.Empty;

    [MaxLength(300)]
    public string Description { get; set; } = string.Empty;

    // Higher number means higher organizational level. Used only for suggestions/UI ordering.
    public int HierarchyLevel { get; set; }

    public bool IsSystemProtected { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}


public sealed class ProductLineDefinition
{
    public int Id { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public sealed class PartChangeTypeDefinition
{
    public int Id { get; set; }

    [MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public sealed class BusinessUnit
{
    public int Id { get; set; }

    [MaxLength(40)]
    public string UnitCode { get; set; } = string.Empty;

    [MaxLength(160)]
    public string UnitName { get; set; } = string.Empty;

    // Organizational units in the official chart are nested (company/division/
    // center/factory...). ParentBusinessUnitId preserves that reporting tree while
    // keeping the legacy BusinessUnit table and all existing DCR references intact.
    public int? ParentBusinessUnitId { get; set; }

    [MaxLength(40)]
    public string UnitType { get; set; } = OrganizationUnitTypes.Division;

    public int SortOrder { get; set; }

    // Configured Head of this block. The legacy column name is retained, but the
    // selected user may be Director, CTO, COO, DCEO, CEO or an equivalent higher role.
    public int? DirectorUserId { get; set; }

    public bool IsActive { get; set; } = true;

    public User? DirectorUser { get; set; }
    public BusinessUnit? ParentBusinessUnit { get; set; }
    [JsonIgnore]
    public List<BusinessUnit> ChildBusinessUnits { get; set; } = new();
}

public sealed class Department
{
    public int Id { get; set; }

    [MaxLength(40)]
    public string DepartmentCode { get; set; } = string.Empty;

    [MaxLength(160)]
    public string DepartmentName { get; set; } = string.Empty;

    public int? BusinessUnitId { get; set; }
    public int? ManagerUserId { get; set; }

    // Kept only for backward database compatibility. New routing uses BusinessUnit.DirectorUserId.
    public int? DirectorUserId { get; set; }

    public bool IsActive { get; set; } = true;

    public BusinessUnit? BusinessUnit { get; set; }
    public User? ManagerUser { get; set; }
    public User? DirectorUser { get; set; }
}

public sealed class DCRRequest
{
    public int Id { get; set; }
    public Guid? CreationToken { get; set; }

    [MaxLength(40)]
    public string DCRNumber { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(10)]
    public string Rank { get; set; } = DcrRanks.C;

    public int RequestingDepartmentId { get; set; }

    [MaxLength(100)]
    public string ModuleGroup { get; set; } = string.Empty;

    public int RequestOwnerId { get; set; }

    [MaxLength(100)]
    public string Program { get; set; } = string.Empty;

    [MaxLength(100)]
    public string BuildStage { get; set; } = string.Empty;

    [MaxLength(100)]
    public string RelatedECR { get; set; } = string.Empty;

    [MaxLength(100)]
    public string RelatedPPS { get; set; } = string.Empty;

    [MaxLength(100)]
    public string RelatedECN { get; set; } = string.Empty;

    [MaxLength(100)]
    public string RelatedMCN { get; set; } = string.Empty;

    public string ProblemDescription { get; set; } = string.Empty;

    public string Solution { get; set; } = string.Empty;

    public string MaterialChangeDescription { get; set; } = string.Empty;

    public string FormFitFunctionDetail { get; set; } = string.Empty;

    [MaxLength(100)]
    public string RetrofitVolume { get; set; } = string.Empty;

    public string RetrofitInstruction { get; set; } = string.Empty;

    public bool MaterialIdentificationRequired { get; set; }

    [MaxLength(200)]
    public string MaterialUsageStation { get; set; } = string.Empty;

    public bool SupplierSupportsMRD { get; set; }

    public DateTime? ExpectedArrivalDate { get; set; }

    public bool TemporaryProcessRequired { get; set; }

    public bool ReworkRequired { get; set; }

    public DateTime? PlannedStartDate { get; set; }

    public DateTime? PlannedEndDate { get; set; }

    [MaxLength(100)]
    public string ProductionOrderNumber { get; set; } = string.Empty;

    [MaxLength(40)]
    public string Status { get; set; } = RequestStatuses.Draft;

    public int CurrentStage { get; set; }

    public int RevisionNo { get; set; } = 1;

    public int DraftStep { get; set; }

    public DateTime? LastSavedAt { get; set; }

    public DateTime? ReturnedDate { get; set; }

    public string LastReturnReason { get; set; } = string.Empty;

    public int CreatedBy { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.Now;

    public DateTime? SubmittedDate { get; set; }

    public DateTime? CompletedDate { get; set; }

    public DateTime? RejectedDate { get; set; }

    [MaxLength(1000)]
    public string FinalPdfPath { get; set; } = string.Empty;

    [MaxLength(64)]
    public string FinalPdfSha256 { get; set; } = string.Empty;

    public DateTime? FinalPdfGeneratedAt { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public Department? RequestingDepartment { get; set; }
    public User? RequestOwner { get; set; }
    public User? Creator { get; set; }
    public ICollection<DCRPart> Parts { get; set; } = new List<DCRPart>();
    public ICollection<DCRImpactedDepartment> ImpactedDepartments { get; set; } = new List<DCRImpactedDepartment>();
    public ICollection<DCRApprovalFlow> ApprovalFlow { get; set; } = new List<DCRApprovalFlow>();
    public ICollection<DCRApprovalPlanEntry> ApprovalPlan { get; set; } = new List<DCRApprovalPlanEntry>();
    public ICollection<DCRAttachment> Attachments { get; set; } = new List<DCRAttachment>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}

public sealed class DCRPart
{
    public int Id { get; set; }
    public int RequestId { get; set; }

    [MaxLength(50)]
    public string ChangeType { get; set; } = string.Empty;

    [MaxLength(100)]
    public string PartNumber { get; set; } = string.Empty;

    [MaxLength(300)]
    public string PartName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string KPC { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Quantity { get; set; } = string.Empty;

    [MaxLength(150)]
    public string ReplacedBy { get; set; } = string.Empty;

    public int SortOrder { get; set; }
    public DCRRequest? Request { get; set; }
}

public sealed class DCRImpactedDepartment
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public int DepartmentId { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? EstimatedCost { get; set; }

    public DCRRequest? Request { get; set; }
    public Department? Department { get; set; }
}


public sealed class DCRApprovalPlanEntry
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public int LevelNumber { get; set; }

    [MaxLength(200)]
    public string LevelName { get; set; } = string.Empty;

    public int ApproverId { get; set; }
    public int Sequence { get; set; }
    public bool IsRequired { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DCRRequest? Request { get; set; }
    public User? Approver { get; set; }
}

/// <summary>
/// One row in the reusable approval-plan template owned by a user.
/// This is deliberately separate from DCRApprovalPlanEntry so changing a
/// personal template never changes approval lines already saved on a DCR.
/// </summary>
public sealed class UserApprovalPlanEntry
{
    public int Id { get; set; }
    public int OwnerUserId { get; set; }

    [MaxLength(10)]
    public string Rank { get; set; } = DcrRanks.C;

    public int LevelNumber { get; set; }

    [MaxLength(200)]
    public string LevelName { get; set; } = string.Empty;

    public int ApproverId { get; set; }

    // Snapshot fields make a saved personal line self-describing. ApproverId remains
    // authoritative, so loading a template can refresh these values from the latest user data.
    public int? BusinessUnitId { get; set; }

    [MaxLength(40)]
    public string BusinessUnitCode { get; set; } = string.Empty;

    [MaxLength(160)]
    public string BusinessUnitName { get; set; } = string.Empty;

    public int? DepartmentId { get; set; }

    [MaxLength(40)]
    public string DepartmentCode { get; set; } = string.Empty;

    [MaxLength(160)]
    public string DepartmentName { get; set; } = string.Empty;

    [MaxLength(80)]
    public string ApproverRole { get; set; } = string.Empty;

    [MaxLength(160)]
    public string ApproverName { get; set; } = string.Empty;

    [MaxLength(200)]
    public string ApproverEmail { get; set; } = string.Empty;

    public int Sequence { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public User? OwnerUser { get; set; }
    public User? Approver { get; set; }
}

/// <summary>
/// A named, system-wide approval plan that can be reused when creating a DCR.
/// Entries reference users by id so organization, role and email details are
/// refreshed whenever the template is loaded.
/// </summary>
public sealed class ApprovalPlanTemplate
{
    public int Id { get; set; }

    [MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(10)]
    public string Rank { get; set; } = DcrRanks.C;

    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public ICollection<ApprovalPlanTemplateEntry> Entries { get; set; } = new List<ApprovalPlanTemplateEntry>();
}

public sealed class ApprovalPlanTemplateEntry
{
    public int Id { get; set; }
    public int TemplateId { get; set; }
    public int LevelNumber { get; set; }

    [MaxLength(200)]
    public string LevelName { get; set; } = string.Empty;

    public int ApproverId { get; set; }
    public int Sequence { get; set; } = 1;

    public ApprovalPlanTemplate? Template { get; set; }
    public User? Approver { get; set; }
}

public sealed class DCRApprovalFlow
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public int RevisionNo { get; set; } = 1;
    public int StageNumber { get; set; }

    [MaxLength(80)]
    public string StageCode { get; set; } = string.Empty;

    [MaxLength(300)]
    public string StageName { get; set; } = string.Empty;

    public int? ApproverId { get; set; }
    public int? DepartmentId { get; set; }

    [MaxLength(40)]
    public string Decision { get; set; } = ApprovalDecisions.Waiting;

    public DateTime? DecisionDate { get; set; }
    public string Comments { get; set; } = string.Empty;
    public DateTime? AssignedDate { get; set; }
    public DateTime? DueDate { get; set; }
    public bool IsRequired { get; set; } = true;
    public int Sequence { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [MaxLength(80)]
    public string AuthMethod { get; set; } = string.Empty;

    public DateTime? AuthenticatedAt { get; set; }

    [MaxLength(256)]
    public string WindowsIdentity { get; set; } = string.Empty;

    [MaxLength(64)]
    public string SignatureHash { get; set; } = string.Empty;

    public int ReminderCount { get; set; }
    public DateTime? LastReminderAt { get; set; }

    public DCRRequest? Request { get; set; }
    public User? Approver { get; set; }
    public Department? Department { get; set; }
}

public sealed class DCRAttachment
{
    public int Id { get; set; }
    public int RequestId { get; set; }

    [MaxLength(80)]
    public string AttachmentType { get; set; } = AttachmentTypes.General;

    [MaxLength(260)]
    public string FileName { get; set; } = string.Empty;

    [MaxLength(260)]
    public string OriginalFileName { get; set; } = string.Empty;

    [MaxLength(260)]
    public string StoredFileName { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string FilePath { get; set; } = string.Empty;

    [MaxLength(20)]
    public string FileExtension { get; set; } = string.Empty;

    public long FileSize { get; set; }
    public long StoredFileSize { get; set; }
    public bool IsCompressed { get; set; }

    [MaxLength(20)]
    public string CompressionType { get; set; } = string.Empty;

    [MaxLength(64)]
    public string OriginalSha256Hash { get; set; } = string.Empty;

    [MaxLength(64)]
    public string Sha256Hash { get; set; } = string.Empty;

    [MaxLength(40)]
    public string StorageProvider { get; set; } = "FileSystem";

    public int UploadedBy { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.Now;
    public bool IsDeleted { get; set; }
    public DCRRequest? Request { get; set; }
    public User? Uploader { get; set; }
}

public sealed class AuditLog
{
    public long Id { get; set; }
    public int? RequestId { get; set; }
    public int? UserId { get; set; }

    [MaxLength(100)]
    public string Action { get; set; } = string.Empty;

    [MaxLength(100)]
    public string EntityName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string EntityId { get; set; } = string.Empty;

    public string OldValue { get; set; } = string.Empty;
    public string NewValue { get; set; } = string.Empty;

    [MaxLength(200)]
    public string ComputerName { get; set; } = string.Empty;

    [MaxLength(64)]
    public string IpAddress { get; set; } = string.Empty;

    [MaxLength(256)]
    public string WindowsIdentity { get; set; } = string.Empty;

    [MaxLength(64)]
    public string SessionId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DCRRequest? Request { get; set; }
    public User? User { get; set; }
}

public sealed class WorkflowStageTemplate
{
    public int Id { get; set; }
    public int StageNumber { get; set; }

    [MaxLength(80)]
    public string StageCode { get; set; } = string.Empty;

    [MaxLength(300)]
    public string StageName { get; set; } = string.Empty;

    [MaxLength(80)]
    public string ApproverRole { get; set; } = string.Empty;

    public bool IsImpactedDepartmentStage { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class ApprovalMatrixRule
{
    public int Id { get; set; }

    [MaxLength(80)]
    public string StageCode { get; set; } = string.Empty;

    public int? RequestingDepartmentId { get; set; }
    public int? TargetDepartmentId { get; set; }

    [MaxLength(80)]
    public string ApproverSource { get; set; } = ApproverSources.Role;

    [MaxLength(80)]
    public string ApproverRole { get; set; } = string.Empty;

    public int? ApproverUserId { get; set; }
    public int Priority { get; set; } = 100;
    public bool IsActive { get; set; } = true;

    [MaxLength(300)]
    public string Description { get; set; } = string.Empty;

    public Department? RequestingDepartment { get; set; }
    public Department? TargetDepartment { get; set; }
    public User? ApproverUser { get; set; }
}

public sealed class DCRNotificationLog
{
    public long Id { get; set; }
    public int RequestId { get; set; }
    public int? ApprovalFlowId { get; set; }

    [MaxLength(60)]
    public string NotificationType { get; set; } = string.Empty;

    [MaxLength(300)]
    public string Recipient { get; set; } = string.Empty;

    public DateTime SentAt { get; set; } = DateTime.Now;
    public bool Success { get; set; }

    [MaxLength(1000)]
    public string Details { get; set; } = string.Empty;

    public DCRRequest? Request { get; set; }
    public DCRApprovalFlow? ApprovalFlow { get; set; }
}



public sealed class EmailOutboxItem
{
    public long Id { get; set; }
    public int? RequestId { get; set; }
    public int? ApprovalFlowId { get; set; }
    public long? NotificationLogId { get; set; }

    [MaxLength(60)]
    public string NotificationType { get; set; } = string.Empty;

    [MaxLength(300)]
    public string Recipient { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Subject { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    [MaxLength(1600)]
    public string AttachmentFilePath { get; set; } = string.Empty;

    [MaxLength(260)]
    public string AttachmentFileName { get; set; } = string.Empty;

    [MaxLength(120)]
    public string AttachmentContentType { get; set; } = string.Empty;

    [MaxLength(40)]
    public string Status { get; set; } = EmailOutboxStatuses.Pending;

    public int RetryCount { get; set; }
    public int MaxRetryCount { get; set; } = 3;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public DateTime? SentAt { get; set; }

    [MaxLength(300)]
    public string SenderAccount { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string LastError { get; set; } = string.Empty;

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

}

public sealed class DCRDeletionLog
{
    public long Id { get; set; }

    public int OriginalRequestId { get; set; }

    [MaxLength(40)]
    public string DCRNumber { get; set; } = string.Empty;

    public int DeletedBy { get; set; }
    public DateTime DeletedAt { get; set; } = DateTime.Now;

    public string SnapshotJson { get; set; } = string.Empty;

    [MaxLength(40)]
    public string CleanupStatus { get; set; } = "Pending";

    public string CleanupDetails { get; set; } = string.Empty;

    [MaxLength(200)]
    public string ComputerName { get; set; } = string.Empty;

    [MaxLength(64)]
    public string IpAddress { get; set; } = string.Empty;

    [MaxLength(256)]
    public string WindowsIdentity { get; set; } = string.Empty;

    [MaxLength(64)]
    public string SessionId { get; set; } = string.Empty;

    public User? Deleter { get; set; }
}

public sealed class DcrNumberSequence
{
    [Key]
    public int Year { get; set; }
    public int LastNumber { get; set; }
}


public sealed class SystemSetting
{
    [MaxLength(120)]
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
