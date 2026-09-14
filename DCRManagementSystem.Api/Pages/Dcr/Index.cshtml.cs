using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using DCRManagementSystem.Api.WebPortal.Models;
using DCRManagementSystem.Api.WebPortal.Services;

namespace DCRManagementSystem.Api.Pages.Dcr;

public sealed class IndexModel : PageModel
{
    private readonly DcrApiClient _dcr;
    public IndexModel(DcrApiClient dcr) => _dcr = dcr;

    public DcrPortalSettings Portal { get; private set; } = new();
    public DcrUser? CurrentUser => _dcr.CurrentUser;
    public List<DcrListItem> Items { get; private set; } = new();
    public string Scope { get; private set; } = DcrScopes.PendingMyApproval;
    public string Search { get; private set; } = string.Empty;
    public string Message => TempData["DcrMessage"]?.ToString() ?? string.Empty;

    public IReadOnlyList<(string Label, string Value)> Tabs { get; } = new[]
    {
        ("Cần phê duyệt", DcrScopes.PendingMyApproval),
        ("DCR liên quan", DcrScopes.RelatedToMe),
        ("DCR của tôi", DcrScopes.MyRequests),
        ("Đã duyệt", DcrScopes.Approved),
        ("Bị từ chối", DcrScopes.Rejected),
        ("Tất cả", DcrScopes.All)
    };

    public string CurrentTabLabel => Tabs.FirstOrDefault(x => x.Value == Scope).Label ?? Scope;

    public async Task<IActionResult> OnGetAsync(string? scope, string? search)
    {
        try
        {
            Portal = await _dcr.GetPortalStatusAsync(HttpContext.RequestAborted);
            if (!Portal.Enabled)
                return RedirectToPage("/Dcr/Login");

            if (!_dcr.IsAuthenticated)
                return RedirectToPage("/Dcr/Login", new { returnUrl = Request.Path + (Request.QueryString.Value ?? string.Empty) });

            Scope = Tabs.Any(x => x.Value == scope) ? scope! : DcrScopes.PendingMyApproval;
            Search = search?.Trim() ?? string.Empty;
            Items = await _dcr.GetListAsync(Scope, Search, HttpContext.RequestAborted);
            return Page();
        }
        catch (UnauthorizedAccessException)
        {
            _dcr.Logout();
            return RedirectToPage("/Dcr/Login", new { returnUrl = Request.Path + (Request.QueryString.Value ?? string.Empty) });
        }
        catch (Exception ex)
        {
            TempData["DcrMessage"] = "Không tải được DCR: " + ex.GetBaseException().Message;
            Items = new();
            return Page();
        }
    }

    public string StatusText(string value) => value switch
    {
        "Draft" => "Bản nháp",
        "Returned" => "Trả về",
        "InApproval" => "Đang phê duyệt",
        "Approved" => "Đã duyệt",
        "Rejected" => "Bị từ chối",
        "Cancelled" => "Đã hủy",
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
