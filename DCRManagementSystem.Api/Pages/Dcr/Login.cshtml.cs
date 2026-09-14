using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using DCRManagementSystem.Api.WebPortal.Services;

namespace DCRManagementSystem.Api.Pages.Dcr;

public sealed class LoginModel : PageModel
{
    private readonly DcrApiClient _dcr;

    public LoginModel(DcrApiClient dcr) => _dcr = dcr;

    [BindProperty] public string Username { get; set; } = string.Empty;
    [BindProperty] public string Password { get; set; } = string.Empty;
    [BindProperty] public string ReturnUrl { get; set; } = string.Empty;

    public bool PortalEnabled { get; private set; }
    public string PortalError { get; private set; } = string.Empty;
    public string ErrorMessage { get; private set; } = string.Empty;
    public string Message => TempData["DcrLoginMessage"]?.ToString() ?? string.Empty;

    public async Task<IActionResult> OnGetAsync(string? returnUrl)
    {
        ReturnUrl = returnUrl ?? string.Empty;
        try
        {
            var status = await _dcr.GetPortalStatusAsync(HttpContext.RequestAborted);
            PortalEnabled = status.Enabled;
            if (PortalEnabled && _dcr.IsAuthenticated)
                return RedirectSafe(ReturnUrl);
        }
        catch (Exception ex)
        {
            PortalError = "Không kết nối được DCR API: " + ex.GetBaseException().Message;
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ReturnUrl ??= string.Empty;
        try
        {
            var status = await _dcr.GetPortalStatusAsync(HttpContext.RequestAborted);
            PortalEnabled = status.Enabled;
            if (!PortalEnabled)
            {
                ErrorMessage = "DCR Web Portal đang tắt.";
                return Page();
            }

            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
            {
                ErrorMessage = "Vui lòng nhập Username và Password.";
                return Page();
            }

            await _dcr.LoginAsync(Username, Password, HttpContext.RequestAborted);
            return RedirectSafe(ReturnUrl);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.GetBaseException().Message;
            return Page();
        }
    }

    private IActionResult RedirectSafe(string? value)
        => !string.IsNullOrWhiteSpace(value) && Url.IsLocalUrl(value)
            ? LocalRedirect(value)
            : RedirectToPage("/Dcr/Index");
}
