using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using DCRManagementSystem.Api.WebPortal.Models;
using DCRManagementSystem.Api.WebPortal.Services;

namespace DCRManagementSystem.Api.Pages.Dcr;

public sealed class EditModel : PageModel
{
    private readonly DcrApiClient _dcr;

    public EditModel(DcrApiClient dcr) => _dcr = dcr;

    [BindProperty] public DcrEditInput Input { get; set; } = new();
    [BindProperty] public List<int> SelectedImpactDepartmentIds { get; set; } = new();
    [BindProperty] public Dictionary<int, decimal?> ImpactCosts { get; set; } = new();
    [BindProperty] public List<DcrAttachmentUploadInput> AttachmentRows { get; set; } = [new()];
    [BindProperty] public string Action { get; set; } = "save";

    public DcrPortalSettings Portal { get; private set; } = new();
    public List<DcrDepartment> Departments { get; private set; } = new();
    public List<DcrProductLine> ProductLines { get; private set; } = new();
    public List<DcrPartChangeType> ChangeTypes { get; private set; } = new();
    public List<DcrApprovalPlanEditItem> ApprovalPlan { get; private set; } = new();
    public List<DcrApprovalPlanTemplate> ApprovalTemplates { get; private set; } = new();
    public IReadOnlyList<DcrAttachmentTypeOption> AttachmentTypeOptions => DcrAttachmentTypes.All;
    public string ErrorMessage { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        var gate = await EnsureReadyAsync();
        if (gate is not null) return gate;

        try
        {
            await LoadReferencesAsync();
            DcrEditModel model;
            if (id.HasValue)
            {
                model = await _dcr.GetDcrAsync(id.Value, HttpContext.RequestAborted);
                if (!DcrStatusPolicy.IsEditable(model.Status))
                    return RedirectToPage("/Dcr/Request", new { id = id.Value });

                var user = _dcr.CurrentUser;
                var isAdmin = string.Equals(user?.Role, "Administrator", StringComparison.OrdinalIgnoreCase);
                if (model.CreatedBy != user?.UserId && !isAdmin)
                    throw new UnauthorizedAccessException("Bạn không có quyền chỉnh sửa DCR này.");
            }
            else
            {
                model = await _dcr.GetNewDcrAsync(HttpContext.RequestAborted);
                model.Rank = NormalizeRank(model.Rank);
                model.ApprovalPlan = await LoadPreferredApprovalPlanAsync(model.Rank);
            }

            Input = DcrEditInput.FromModel(model);
            SelectedImpactDepartmentIds = model.ImpactedDepartments.Select(x => x.DepartmentId).Distinct().ToList();
            ImpactCosts = model.ImpactedDepartments.ToDictionary(x => x.DepartmentId, x => x.EstimatedCost);
            ApprovalPlan = model.ApprovalPlan;
            ApprovalTemplates = await LoadApprovalTemplatesSafeAsync(Input.Rank);
            if (Input.Parts.Count == 0) Input.Parts.Add(new DcrPartEditItem());
            return Page();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.GetBaseException().Message;
            if (Input.Parts.Count == 0) Input.Parts.Add(new DcrPartEditItem());
            return Page();
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var gate = await EnsureReadyAsync();
        if (gate is not null) return gate;

        try
        {
            await LoadReferencesAsync();

            DcrEditModel model = Input.Id.HasValue
                ? await _dcr.GetDcrAsync(Input.Id.Value, HttpContext.RequestAborted)
                : await _dcr.GetNewDcrAsync(HttpContext.RequestAborted);

            if (Input.Id.HasValue)
            {
                var user = _dcr.CurrentUser;
                var isAdmin = string.Equals(user?.Role, "Administrator", StringComparison.OrdinalIgnoreCase);
                if (model.CreatedBy != user?.UserId && !isAdmin)
                    throw new UnauthorizedAccessException("Bạn không có quyền chỉnh sửa DCR này.");
                model.RowVersion = DecodeRowVersion(Input.RowVersionBase64);
            }

            if (!Input.Id.HasValue) model.CreationToken = Input.CreationToken;
            ApplyInput(model);

            if (!Input.Id.HasValue && model.ApprovalPlan.Count == 0)
                model.ApprovalPlan = await LoadPreferredApprovalPlanAsync(model.Rank);

            ApprovalPlan = model.ApprovalPlan;
            ApprovalTemplates = await LoadApprovalTemplatesSafeAsync(model.Rank);

            // Read files from the cached multipart form itself. Binding IFormFile inside a
            // dynamically indexed complex collection is not reliable on every deployment;
            // a binding miss previously resulted in an empty list and the DCR was submitted
            // without uploading anything.
            var filesToUpload = await ReadAttachmentUploadsAsync(HttpContext.RequestAborted);
            var saved = await _dcr.SaveDraftAsync(model, 5, HttpContext.RequestAborted);
            if (saved.AlreadyExisted)
            {
                TempData["DcrMessage"] = "DCR đã được lưu từ lần gửi trước. Vui lòng kiểm tra nội dung và tệp đính kèm trước khi tiếp tục.";
                return RedirectToPage("/Dcr/Request", new { id = saved.RequestId });
            }
            var savedRowVersion = saved.Model.RowVersion ?? Array.Empty<byte>();

            // If an upload fails after a new draft was created, keep its identity in the
            // rendered form so retrying cannot accidentally create a second DCR.
            Input.Id = saved.RequestId;
            Input.DCRNumber = saved.Model.DCRNumber;
            Input.RowVersionBase64 = Convert.ToBase64String(savedRowVersion);
            // Razor tag helpers otherwise render stale posted values after an upload/submit failure.
            ModelState.Remove("Input.Id");
            ModelState.Remove("Input.DCRNumber");
            ModelState.Remove("Input.RowVersionBase64");

            HashSet<int>? attachmentIdsBefore = null;
            if (filesToUpload.Count > 0)
            {
                attachmentIdsBefore = (await _dcr.GetAttachmentsAsync(saved.RequestId, HttpContext.RequestAborted))
                    .Select(x => x.Id)
                    .ToHashSet();
            }

            foreach (var upload in filesToUpload)
                await _dcr.UploadAttachmentAsync(saved.RequestId, upload.File, upload.AttachmentType, HttpContext.RequestAborted);

            if (attachmentIdsBefore is not null)
            {
                var attachmentsAfter = await _dcr.GetAttachmentsAsync(saved.RequestId, HttpContext.RequestAborted);
                var uploadedCount = attachmentsAfter.Count(x => !attachmentIdsBefore.Contains(x.Id));
                if (uploadedCount != filesToUpload.Count)
                {
                    throw new InvalidOperationException(
                        $"Chưa lưu đủ tệp đính kèm ({uploadedCount}/{filesToUpload.Count}). DCR chưa được gửi duyệt; vui lòng chọn lại tệp và thử lại.");
                }
            }

            if (string.Equals(Action, "submit", StringComparison.OrdinalIgnoreCase))
            {
                await _dcr.SubmitAsync(saved.RequestId, savedRowVersion, HttpContext.RequestAborted);
                TempData["DcrMessage"] = "DCR đã được tạo và Submit vào luồng phê duyệt.";
            }
            else
            {
                TempData["DcrMessage"] = "Đã lưu DCR trên server.";
            }

            return RedirectToPage("/Dcr/Request", new { id = saved.RequestId });
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.GetBaseException().Message;
            try
            {
                await LoadReferencesAsync();
                if (ApprovalPlan.Count == 0)
                    ApprovalPlan = Input.Id.HasValue
                        ? (await _dcr.GetDcrAsync(Input.Id.Value, HttpContext.RequestAborted)).ApprovalPlan
                        : Input.ApprovalPlan;
                ApprovalTemplates = await LoadApprovalTemplatesSafeAsync(Input.Rank);
            }
            catch { }

            Input.Parts = Input.Parts?.Where(x => x is not null).ToList() ?? new();
            if (Input.Parts.Count == 0) Input.Parts.Add(new DcrPartEditItem());
            AttachmentRows = AttachmentRows?.Where(x => x is not null).ToList() ?? new();
            if (AttachmentRows.Count == 0) AttachmentRows.Add(new DcrAttachmentUploadInput());
            return Page();
        }
    }

    private void ApplyInput(DcrEditModel model)
    {
        model.Title = Input.Title?.Trim() ?? string.Empty;
        model.Rank = NormalizeRank(Input.Rank);
        model.RequestingDepartmentId = Input.RequestingDepartmentId;
        model.ModuleGroup = Input.ModuleGroup?.Trim() ?? string.Empty;
        model.Program = Input.Program?.Trim() ?? string.Empty;
        model.BuildStage = Input.BuildStage?.Trim() ?? string.Empty;
        model.RelatedECR = Input.RelatedECR?.Trim() ?? string.Empty;
        model.RelatedPPS = Input.RelatedPPS?.Trim() ?? string.Empty;
        model.RelatedECN = Input.RelatedECN?.Trim() ?? string.Empty;
        model.RelatedMCN = Input.RelatedMCN?.Trim() ?? string.Empty;
        model.ProblemDescription = Input.ProblemDescription?.Trim() ?? string.Empty;
        model.Solution = Input.Solution?.Trim() ?? string.Empty;
        model.MaterialChangeDescription = Input.MaterialChangeDescription?.Trim() ?? string.Empty;
        model.FormFitFunctionDetail = Input.FormFitFunctionDetail?.Trim() ?? string.Empty;
        model.RetrofitVolume = Input.RetrofitVolume?.Trim() ?? string.Empty;
        model.RetrofitInstruction = Input.RetrofitInstruction?.Trim() ?? string.Empty;
        model.MaterialIdentificationRequired = Input.MaterialIdentificationRequired;
        model.MaterialUsageStation = Input.MaterialUsageStation?.Trim() ?? string.Empty;
        model.SupplierSupportsMRD = Input.SupplierSupportsMRD;
        model.ExpectedArrivalDate = Input.ExpectedArrivalDate;
        model.TemporaryProcessRequired = Input.TemporaryProcessRequired;
        model.ReworkRequired = Input.ReworkRequired;
        model.PlannedStartDate = Input.PlannedStartDate;
        model.PlannedEndDate = Input.PlannedEndDate;
        model.ProductionOrderNumber = Input.ProductionOrderNumber?.Trim() ?? string.Empty;
        model.ApprovalPlan = (Input.ApprovalPlan ?? new List<DcrApprovalPlanEditItem>())
            .Where(x => x.ApproverId > 0 && x.LevelNumber > 0)
            .OrderBy(x => x.LevelNumber)
            .ThenBy(x => x.Sequence)
            .Select(x => new DcrApprovalPlanEditItem
            {
                Id = x.Id,
                LevelNumber = x.LevelNumber,
                LevelName = x.LevelName?.Trim() ?? string.Empty,
                ApproverId = x.ApproverId,
                ApproverName = x.ApproverName?.Trim() ?? string.Empty,
                ApproverEmail = x.ApproverEmail?.Trim() ?? string.Empty,
                ApproverRole = x.ApproverRole?.Trim() ?? string.Empty,
                BusinessUnitId = x.BusinessUnitId,
                BusinessUnitCode = x.BusinessUnitCode?.Trim() ?? string.Empty,
                BusinessUnitName = x.BusinessUnitName?.Trim() ?? string.Empty,
                DepartmentId = x.DepartmentId,
                DepartmentCode = x.DepartmentCode?.Trim() ?? string.Empty,
                DepartmentName = x.DepartmentName?.Trim() ?? string.Empty,
                Sequence = x.Sequence
            }).ToList();

        model.Parts = (Input.Parts ?? new List<DcrPartEditItem>())
            .Where(x => !string.IsNullOrWhiteSpace(x.ChangeType) ||
                        !string.IsNullOrWhiteSpace(x.PartNumber) ||
                        !string.IsNullOrWhiteSpace(x.PartName) ||
                        !string.IsNullOrWhiteSpace(x.Quantity))
            .Select((x, i) => new DcrPartEditItem
            {
                Id = x.Id,
                ChangeType = x.ChangeType?.Trim() ?? string.Empty,
                PartNumber = x.PartNumber?.Trim() ?? string.Empty,
                PartName = x.PartName?.Trim() ?? string.Empty,
                KPC = x.KPC?.Trim() ?? string.Empty,
                Quantity = x.Quantity?.Trim() ?? string.Empty,
                ReplacedBy = x.ReplacedBy?.Trim() ?? string.Empty,
                SortOrder = i
            })
            .ToList();

        var existingCosts = model.ImpactedDepartments
            .GroupBy(x => x.DepartmentId)
            .ToDictionary(x => x.Key, x => x.First().EstimatedCost);

        model.ImpactedDepartments = SelectedImpactDepartmentIds
            .Distinct()
            .Select(departmentId => new DcrImpactDepartmentEditItem
            {
                DepartmentId = departmentId,
                EstimatedCost = ImpactCosts.TryGetValue(departmentId, out var postedCost)
                    ? postedCost
                    : existingCosts.TryGetValue(departmentId, out var cost) ? cost : null
            })
            .ToList();
    }

    public async Task<IActionResult> OnGetApproversAsync(string? q)
    {
        var gate = await EnsureReadyAsync();
        if (gate is not null) return new UnauthorizedResult();
        try { return new JsonResult(await _dcr.SearchApproversAsync(q ?? string.Empty, HttpContext.RequestAborted)); }
        catch (Exception ex) { return new JsonResult(new { error = ex.GetBaseException().Message }) { StatusCode = 400 }; }
    }

    public async Task<IActionResult> OnGetTemplatesAsync(string? rank)
    {
        var gate = await EnsureReadyAsync();
        if (gate is not null) return new UnauthorizedResult();
        try { return new JsonResult(await _dcr.GetApprovalPlanTemplatesAsync(NormalizeRank(rank), HttpContext.RequestAborted)); }
        catch (Exception ex) { return new JsonResult(new { error = ex.GetBaseException().Message }) { StatusCode = 400 }; }
    }

    private async Task<List<DcrApprovalPlanEditItem>> LoadPreferredApprovalPlanAsync(string rank)
    {
        var templates = await LoadApprovalTemplatesSafeAsync(rank);
        var preferred = templates.FirstOrDefault(x => x.IsDefault) ?? templates.FirstOrDefault();
        if (preferred?.ApprovalPlan.Count > 0)
            return preferred.ApprovalPlan;

        try
        {
            var saved = await _dcr.GetSavedApprovalPlanAsync(rank, HttpContext.RequestAborted);
            if (saved.Count > 0) return saved;
        }
        catch
        {
            // A stale personal template must not prevent creating a DCR on Web.
        }

        var suggestion = await _dcr.GetSuggestedApprovalPlanAsync(rank, HttpContext.RequestAborted);
        return suggestion.ApprovalPlan;
    }

    private async Task<List<DcrApprovalPlanTemplate>> LoadApprovalTemplatesSafeAsync(string rank)
    {
        try { return await _dcr.GetApprovalPlanTemplatesAsync(rank, HttpContext.RequestAborted); }
        catch { return []; }
    }

    private static string NormalizeRank(string? rank)
    {
        var value = (rank ?? string.Empty).Trim().ToUpperInvariant();
        return value is "A" or "B" or "C" or "S" ? value : "C";
    }

    private async Task<IActionResult?> EnsureReadyAsync()
    {
        try
        {
            Portal = await _dcr.GetPortalStatusAsync(HttpContext.RequestAborted);
            if (!Portal.Enabled)
                return RedirectToPage("/Dcr/Login");
            if (!Portal.AllowCreate)
            {
                TempData["DcrMessage"] = "Administrator đang tắt chức năng tạo/chỉnh sửa DCR trên Web.";
                return RedirectToPage("/Dcr/Index");
            }
            if (!_dcr.IsAuthenticated)
                return RedirectToPage("/Dcr/Login", new { returnUrl = Request.Path + (Request.QueryString.Value ?? string.Empty) });
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            _dcr.Logout();
            return RedirectToPage("/Dcr/Login", new { returnUrl = Request.Path + (Request.QueryString.Value ?? string.Empty) });
        }
        catch (Exception ex)
        {
            TempData["DcrMessage"] = "Không kết nối được DCR API: " + ex.GetBaseException().Message;
            return RedirectToPage("/Dcr/Login");
        }
    }

    private async Task LoadReferencesAsync()
    {
        Departments = await _dcr.GetDepartmentsAsync(HttpContext.RequestAborted);
        ProductLines = await _dcr.GetProductLinesAsync(HttpContext.RequestAborted);
        ChangeTypes = await _dcr.GetChangeTypesAsync(HttpContext.RequestAborted);
    }

    private async Task<List<PendingAttachmentUpload>> ReadAttachmentUploadsAsync(
        CancellationToken cancellationToken)
    {
        if (!Request.HasFormContentType)
            return [];

        var form = await Request.ReadFormAsync(cancellationToken);
        var postedFiles = form.Files.Where(x => x.Length > 0).ToList();
        var result = new List<PendingAttachmentUpload>(postedFiles.Count);

        foreach (var file in postedFiles)
        {
            if (!TryGetAttachmentRowIndex(file.Name, out var rowIndex))
            {
                throw new InvalidOperationException(
                    $"Không nhận dạng được trường tệp đính kèm '{file.Name}'. DCR chưa được Submit.");
            }

            var typeField = $"AttachmentRows[{rowIndex}].AttachmentType";
            var attachmentType = DcrAttachmentTypes.Normalize(form[typeField].FirstOrDefault());
            result.Add(new PendingAttachmentUpload(file, attachmentType));
        }

        return result;
    }

    private static bool TryGetAttachmentRowIndex(string? fieldName, out int rowIndex)
    {
        const string prefix = "AttachmentRows[";
        const string suffix = "].File";
        rowIndex = -1;
        if (string.IsNullOrWhiteSpace(fieldName) ||
            !fieldName.StartsWith(prefix, StringComparison.Ordinal) ||
            !fieldName.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        var indexText = fieldName[prefix.Length..^suffix.Length];
        return int.TryParse(indexText, out rowIndex) && rowIndex >= 0;
    }

    private static byte[] DecodeRowVersion(string value)
    {
        try { return Convert.FromBase64String(value ?? string.Empty); }
        catch { return Array.Empty<byte>(); }
    }

    public sealed class DcrEditInput
    {
        public int? Id { get; set; }
        public Guid CreationToken { get; set; } = Guid.NewGuid();
        public string DCRNumber { get; set; } = string.Empty;
        public string RowVersionBase64 { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Rank { get; set; } = "C";
        public int RequestingDepartmentId { get; set; }
        public string ModuleGroup { get; set; } = string.Empty;
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
        public List<DcrPartEditItem> Parts { get; set; } = new();
        public List<DcrApprovalPlanEditItem> ApprovalPlan { get; set; } = new();

        public static DcrEditInput FromModel(DcrEditModel model) => new()
        {
            Id = model.Id,
            CreationToken = model.CreationToken,
            DCRNumber = model.DCRNumber,
            RowVersionBase64 = Convert.ToBase64String(model.RowVersion ?? Array.Empty<byte>()),
            Title = model.Title,
            Rank = NormalizeRank(model.Rank),
            RequestingDepartmentId = model.RequestingDepartmentId,
            ModuleGroup = model.ModuleGroup,
            Program = model.Program,
            BuildStage = model.BuildStage,
            RelatedECR = model.RelatedECR,
            RelatedPPS = model.RelatedPPS,
            RelatedECN = model.RelatedECN,
            RelatedMCN = model.RelatedMCN,
            ProblemDescription = model.ProblemDescription,
            Solution = model.Solution,
            MaterialChangeDescription = model.MaterialChangeDescription,
            FormFitFunctionDetail = model.FormFitFunctionDetail,
            RetrofitVolume = model.RetrofitVolume,
            RetrofitInstruction = model.RetrofitInstruction,
            MaterialIdentificationRequired = model.MaterialIdentificationRequired,
            MaterialUsageStation = model.MaterialUsageStation,
            SupplierSupportsMRD = model.SupplierSupportsMRD,
            ExpectedArrivalDate = model.ExpectedArrivalDate,
            TemporaryProcessRequired = model.TemporaryProcessRequired,
            ReworkRequired = model.ReworkRequired,
            PlannedStartDate = model.PlannedStartDate,
            PlannedEndDate = model.PlannedEndDate,
            ProductionOrderNumber = model.ProductionOrderNumber,
            Parts = model.Parts.Select(x => new DcrPartEditItem
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
            ApprovalPlan = model.ApprovalPlan.Select(x => new DcrApprovalPlanEditItem
            {
                Id = x.Id,
                LevelNumber = x.LevelNumber,
                LevelName = x.LevelName,
                ApproverId = x.ApproverId,
                ApproverName = x.ApproverName,
                ApproverEmail = x.ApproverEmail,
                ApproverRole = x.ApproverRole,
                BusinessUnitId = x.BusinessUnitId,
                BusinessUnitCode = x.BusinessUnitCode,
                BusinessUnitName = x.BusinessUnitName,
                DepartmentId = x.DepartmentId,
                DepartmentCode = x.DepartmentCode,
                DepartmentName = x.DepartmentName,
                Sequence = x.Sequence
            }).ToList()
        };
    }

    public sealed class DcrAttachmentUploadInput
    {
        public string AttachmentType { get; set; } = DcrAttachmentTypes.SupportingDocument;
        public IFormFile? File { get; set; }
    }

    private sealed record PendingAttachmentUpload(IFormFile File, string AttachmentType);
}
