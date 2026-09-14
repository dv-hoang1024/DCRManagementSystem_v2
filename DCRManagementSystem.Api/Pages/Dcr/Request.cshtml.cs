using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using DCRManagementSystem.Api.WebPortal.Models;
using DCRManagementSystem.Api.WebPortal.Services;

namespace DCRManagementSystem.Api.Pages.Dcr;

public sealed class RequestModel : PageModel
{
    private readonly DcrApiClient _dcr;

    public RequestModel(DcrApiClient dcr) => _dcr = dcr;

    public int Id { get; private set; }
    public DcrPortalSettings Portal { get; private set; } = new();
    public DcrEditModel? Dcr { get; private set; }
    public List<DcrDepartment> Departments { get; private set; } = new();
    public List<DcrApprovalHistoryItem> History { get; private set; } = new();
    public List<DcrAttachmentItem> Attachments { get; private set; } = new();
    public bool CanApprove { get; private set; }
    public bool CanEdit { get; private set; }
    public bool CanSubmit { get; private set; }
    public bool CanDelete { get; private set; }
    public IReadOnlyList<DcrAttachmentTypeOption> AttachmentTypeOptions => DcrAttachmentTypes.All;
    public string ErrorMessage { get; private set; } = string.Empty;
    public string Message => TempData["DcrMessage"]?.ToString() ?? string.Empty;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Id = id;
        var gate = await EnsureReadyAsync();
        if (gate is not null) return gate;

        try
        {
            await LoadAsync(id);
            return Page();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.GetBaseException().Message;
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDecisionAsync(
        int id,
        string decision,
        string comment,
        string rowVersionBase64)
    {
        Id = id;
        var gate = await EnsureReadyAsync();
        if (gate is not null) return gate;

        if (!Portal.AllowApproval)
        {
            TempData["DcrMessage"] = "Administrator đang tắt chức năng phê duyệt trên Web.";
            return RedirectToPage(new { id });
        }

        try
        {
            var auth = await _dcr.AuthenticateDecisionAsync(HttpContext.RequestAborted);
            var result = await _dcr.DecideAsync(
                id,
                decision ?? string.Empty,
                comment ?? string.Empty,
                auth,
                DecodeRowVersion(rowVersionBase64),
                HttpContext.RequestAborted);

            TempData["DcrMessage"] = decision switch
            {
                DcrDecisions.Approved => "Đã phê duyệt DCR.",
                DcrDecisions.Rejected => "Đã từ chối DCR.",
                DcrDecisions.Returned => "Đã trả DCR về để bổ sung thông tin.",
                _ => "Đã xử lý quyết định."
            };
        }
        catch (Exception ex)
        {
            TempData["DcrMessage"] = "Không xử lý được quyết định: " + ex.GetBaseException().Message;
        }
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostSubmitAsync(int id, string rowVersionBase64)
    {
        Id = id;
        var gate = await EnsureReadyAsync();
        if (gate is not null) return gate;

        if (!Portal.AllowCreate)
        {
            TempData["DcrMessage"] = "Administrator đang tắt chức năng tạo/chỉnh sửa DCR trên Web.";
            return RedirectToPage(new { id });
        }

        try
        {
            await _dcr.SubmitAsync(id, DecodeRowVersion(rowVersionBase64), HttpContext.RequestAborted);
            TempData["DcrMessage"] = "DCR đã được Submit vào luồng phê duyệt.";
        }
        catch (Exception ex)
        {
            TempData["DcrMessage"] = "Submit thất bại: " + ex.GetBaseException().Message;
        }
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, string confirmation)
    {
        Id = id;
        var gate = await EnsureReadyAsync();
        if (gate is not null) return gate;

        var isAdmin = string.Equals(_dcr.CurrentUser?.Role, "Administrator", StringComparison.OrdinalIgnoreCase);
        if (!Portal.AllowCreate && !isAdmin)
        {
            TempData["DcrMessage"] = "Administrator đang tắt chức năng tạo/chỉnh sửa DCR trên Web.";
            return RedirectToPage(new { id });
        }

        try
        {
            var current = await _dcr.GetDcrAsync(id, HttpContext.RequestAborted);
            if (!string.Equals(confirmation?.Trim(), current.DCRNumber, StringComparison.OrdinalIgnoreCase))
            {
                TempData["DcrMessage"] = $"Chưa xóa DCR: vui lòng nhập đúng mã {current.DCRNumber} để xác nhận.";
                return RedirectToPage(new { id });
            }

            await _dcr.DeleteDcrAsync(id, HttpContext.RequestAborted);
            TempData["DcrMessage"] = $"Đã xóa {current.DCRNumber}. Snapshot xóa được lưu trong nhật ký audit của hệ thống.";
            return RedirectToPage("/Dcr/Index", new { scope = DcrScopes.MyRequests });
        }
        catch (Exception ex)
        {
            TempData["DcrMessage"] = "Không thể xóa DCR: " + ex.GetBaseException().Message;
            return RedirectToPage(new { id });
        }
    }

    public async Task<IActionResult> OnPostUploadAsync(int id, IFormFile file, string attachmentType)
    {
        Id = id;
        var gate = await EnsureReadyAsync();
        if (gate is not null) return gate;

        if (!Portal.AllowCreate)
        {
            TempData["DcrMessage"] = "Administrator đang tắt chức năng tạo/chỉnh sửa DCR trên Web.";
            return RedirectToPage(new { id });
        }

        try
        {
            var normalizedType = DcrAttachmentTypes.Normalize(attachmentType);
            await _dcr.UploadAttachmentAsync(id, file, normalizedType, HttpContext.RequestAborted);
            TempData["DcrMessage"] = "Upload attachment thành công.";
        }
        catch (Exception ex)
        {
            TempData["DcrMessage"] = "Upload thất bại: " + ex.GetBaseException().Message;
        }
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnGetAttachmentAsync(int id, int attachmentId)
    {
        var gate = await EnsureReadyAsync();
        if (gate is not null) return gate;
        try
        {
            var file = await _dcr.DownloadAsync($"api/attachments/{attachmentId}/download", HttpContext.RequestAborted);
            return File(file.Data, file.ContentType, file.FileName);
        }
        catch (Exception ex)
        {
            TempData["DcrMessage"] = "Không tải được attachment: " + ex.GetBaseException().Message;
            return RedirectToPage(new { id });
        }
    }

    public async Task<IActionResult> OnGetPdfAsync(int id)
    {
        var gate = await EnsureReadyAsync();
        if (gate is not null) return gate;
        try
        {
            var file = await _dcr.DownloadAsync($"api/dcr/{id}/pdf/export", HttpContext.RequestAborted);
            return File(file.Data, file.ContentType, file.FileName);
        }
        catch (Exception ex)
        {
            TempData["DcrMessage"] = "Không tạo được PDF: " + ex.GetBaseException().Message;
            return RedirectToPage(new { id });
        }
    }

    public async Task<IActionResult> OnGetFinalPdfAsync(int id)
    {
        var gate = await EnsureReadyAsync();
        if (gate is not null) return gate;
        try
        {
            var file = await _dcr.DownloadAsync($"api/dcr/{id}/pdf/final", HttpContext.RequestAborted);
            return File(file.Data, file.ContentType, file.FileName);
        }
        catch (Exception ex)
        {
            TempData["DcrMessage"] = "Không tải được Final PDF: " + ex.GetBaseException().Message;
            return RedirectToPage(new { id });
        }
    }

    private async Task<IActionResult?> EnsureReadyAsync()
    {
        try
        {
            Portal = await _dcr.GetPortalStatusAsync(HttpContext.RequestAborted);
            if (!Portal.Enabled)
                return RedirectToPage("/Dcr/Login");

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

    private async Task LoadAsync(int id)
    {
        Dcr = await _dcr.GetDcrAsync(id, HttpContext.RequestAborted);
        Departments = await _dcr.GetDepartmentsAsync(HttpContext.RequestAborted);
        History = await _dcr.GetApprovalHistoryAsync(id, HttpContext.RequestAborted);
        Attachments = await _dcr.GetAttachmentsAsync(id, HttpContext.RequestAborted);
        CanApprove = Portal.AllowApproval && await _dcr.CanApproveAsync(id, HttpContext.RequestAborted);

        var user = _dcr.CurrentUser;
        var isAdmin = string.Equals(user?.Role, "Administrator", StringComparison.OrdinalIgnoreCase);
        CanEdit = Portal.AllowCreate &&
                  DcrStatusPolicy.IsEditable(Dcr.Status) &&
                  (Dcr.CreatedBy == user?.UserId || isAdmin);
        CanSubmit = CanEdit;
        CanDelete = isAdmin || (Portal.AllowCreate &&
                    DcrStatusPolicy.CanDelete(Dcr.Status, Dcr.CreatedBy, user?.UserId, isAdmin));
    }

    private static byte[] DecodeRowVersion(string value)
    {
        try { return Convert.FromBase64String(value ?? string.Empty); }
        catch { return Array.Empty<byte>(); }
    }

    public string DepartmentName(int id)
        => Departments.FirstOrDefault(x => x.Id == id)?.DepartmentName
           ?? Departments.FirstOrDefault(x => x.Id == id)?.DepartmentCode
           ?? $"#{id}";

    public string FormatDate(DateTime? value) => value?.ToString("dd/MM/yyyy") ?? "-";
    public string FormatDateTime(DateTime? value) => value?.ToString("dd/MM/yyyy HH:mm") ?? "-";
    public string AttachmentTypeNames(string? value) => DcrAttachmentTypes.FormatDisplayNames(value);

    public string FormatBytes(long value)
    {
        if (value < 1024) return $"{value} B";
        if (value < 1024 * 1024) return $"{value / 1024d:0.0} KB";
        if (value < 1024L * 1024 * 1024) return $"{value / 1024d / 1024d:0.0} MB";
        return $"{value / 1024d / 1024d / 1024d:0.0} GB";
    }

    public string StatusText(string value) => value switch
    {
        "Draft" => "Bản nháp",
        "Returned" => "Trả về",
        "InApproval" => "Đang phê duyệt",
        "Approved" => "Đã duyệt",
        "Rejected" => "Bị từ chối",
        _ => value
    };

    public string StatusCss(string value) => value switch
    {
        "Approved" => "approved",
        "Rejected" => "rejected",
        "Draft" => "draft",
        "Returned" => "returned",
        "InApproval" => "approval",
        _ => "neutral"
    };
}
