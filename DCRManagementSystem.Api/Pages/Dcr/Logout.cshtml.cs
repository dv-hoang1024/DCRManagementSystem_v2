using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using DCRManagementSystem.Api.WebPortal.Services;

namespace DCRManagementSystem.Api.Pages.Dcr;

public sealed class LogoutModel : PageModel
{
    private readonly DcrApiClient _dcr;
    public LogoutModel(DcrApiClient dcr) => _dcr = dcr;

    public IActionResult OnGet()
    {
        _dcr.Logout();
        return RedirectToPage("/Dcr/Login");
    }
}
