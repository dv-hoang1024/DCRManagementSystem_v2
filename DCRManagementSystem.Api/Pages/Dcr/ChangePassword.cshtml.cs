using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using DCRManagementSystem.Api.WebPortal.Models;
using DCRManagementSystem.Api.WebPortal.Services;

namespace DCRManagementSystem.Api.Pages.Dcr;

public sealed class ChangePasswordModel : PageModel
{
    private readonly DcrApiClient _dcr;

    public ChangePasswordModel(DcrApiClient dcr) => _dcr = dcr;

    [BindProperty] public string CurrentPassword { get; set; } = string.Empty;
    [BindProperty] public string NewPassword { get; set; } = string.Empty;
    [BindProperty] public string ConfirmPassword { get; set; } = string.Empty;

    public DcrUser? CurrentUser => _dcr.CurrentUser;
    public string AuthMethod => _dcr.CurrentAuthMethod;
    public string ErrorMessage { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync()
    {
        var gate = await EnsureReadyAsync();
        return gate ?? Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var gate = await EnsureReadyAsync();
        if (gate is not null) return gate;

        CurrentPassword ??= string.Empty;
        NewPassword ??= string.Empty;
        ConfirmPassword ??= string.Empty;

        if (string.IsNullOrWhiteSpace(CurrentPassword))
        {
            ErrorMessage = "Vui lòng nhập mật khẩu DCR hiện tại.";
            return Page();
        }
        if (NewPassword.Length < 8)
        {
            ErrorMessage = "Mật khẩu mới phải có ít nhất 8 ký tự.";
            return Page();
        }
        if (!string.Equals(NewPassword, ConfirmPassword, StringComparison.Ordinal))
        {
            ErrorMessage = "Mật khẩu nhập lại không khớp.";
            return Page();
        }
        if (string.Equals(CurrentPassword, NewPassword, StringComparison.Ordinal))
        {
            ErrorMessage = "Mật khẩu mới phải khác mật khẩu hiện tại.";
            return Page();
        }

        try
        {
            await _dcr.ChangePasswordAsync(CurrentPassword, NewPassword, HttpContext.RequestAborted);
            _dcr.Logout();
            TempData["DcrLoginMessage"] = "Đổi mật khẩu DCR thành công. Vui lòng đăng nhập lại bằng mật khẩu mới.";
            return RedirectToPage("/Dcr/Login");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.GetBaseException().Message;
            return Page();
        }
    }

    private async Task<IActionResult?> EnsureReadyAsync()
    {
        try
        {
            var portal = await _dcr.GetPortalStatusAsync(HttpContext.RequestAborted);
            if (!portal.Enabled)
                return RedirectToPage("/Dcr/Login");

            if (!_dcr.IsAuthenticated)
                return RedirectToPage("/Dcr/Login", new { returnUrl = Request.Path });
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            _dcr.Logout();
            return RedirectToPage("/Dcr/Login", new { returnUrl = Request.Path });
        }
        catch (Exception ex)
        {
            TempData["DcrLoginMessage"] = "Không kết nối được DCR API: " + ex.GetBaseException().Message;
            return RedirectToPage("/Dcr/Login");
        }
    }
}
