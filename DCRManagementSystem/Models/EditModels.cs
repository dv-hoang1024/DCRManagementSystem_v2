namespace DCRManagementSystem.Models;

public sealed class DcrEditModel
{
    public int? Id { get; set; }
    public Guid CreationToken { get; set; } = Guid.NewGuid();
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    public string DCRNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Rank { get; set; } = DcrRanks.C;
    public int RequestingDepartmentId { get; set; }
    public string RequestingDepartmentCode { get; set; } = string.Empty;
    public string RequestingDepartmentName { get; set; } = string.Empty;
    public string ModuleGroup { get; set; } = string.Empty;
    public int RequestOwnerId { get; set; }
    public string RequestOwnerName { get; set; } = string.Empty;
    public string RequestOwnerEmail { get; set; } = string.Empty;
    public string RequestOwnerPhone { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; } = DateTime.Now;
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
    public string Status { get; set; } = RequestStatuses.Draft;
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
    public List<PartEditItem> Parts { get; set; } = new();
    public List<ImpactDepartmentEditItem> ImpactedDepartments { get; set; } = new();
    public List<ApprovalPlanEditItem> ApprovalPlan { get; set; } = new();
}


public sealed class ApprovalPlanEditItem
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

public sealed class ApprovalPlanSuggestionResult
{
    public string Rank { get; set; } = DcrRanks.C;
    public List<ApprovalPlanEditItem> ApprovalPlan { get; set; } = new();
    public List<string> MissingApprovers { get; set; } = new();
    public bool IsComplete => ApprovalPlan.Count > 0 && MissingApprovers.Count == 0;
}

public sealed class ApprovalPlanTemplateEditModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Rank { get; set; } = DcrRanks.C;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime UpdatedAt { get; set; }
    public List<ApprovalPlanEditItem> ApprovalPlan { get; set; } = new();

    public string DisplayName => $"Rank {DcrRanks.Normalize(Rank)} - {Name}" + (IsDefault ? " (Mặc định)" : string.Empty);
}

public sealed class ApproverSearchItem
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
    public string DisplayText => $"{FullName} | {Email} | {(string.IsNullOrWhiteSpace(Department) ? BusinessUnit : Department)} | {Role}";
}

public sealed class PartEditItem
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

public sealed class ImpactDepartmentEditItem
{
    public int Id { get; set; }
    public int DepartmentId { get; set; }
    public string DepartmentCode { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public decimal? EstimatedCost { get; set; }
}

public sealed class DcrListItem
{
    public int Id { get; set; }
    public string DCRNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Rank { get; set; } = DcrRanks.C;
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

public sealed class ApprovalHistoryItem
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
    public string SignatureHash { get; set; } = string.Empty;
    public string SignatureStatus { get; set; } = string.Empty;
}

public sealed class AttachmentListItem
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

public sealed class AuditListItem
{
    public long Id { get; set; }
    public string User { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string OldValue { get; set; } = string.Empty;
    public string NewValue { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string ComputerName { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string WindowsIdentity { get; set; } = string.Empty;
}

public sealed class ValidationResultModel
{
    public bool IsValid => Errors.Count == 0;
    public List<string> Errors { get; } = new();
}
