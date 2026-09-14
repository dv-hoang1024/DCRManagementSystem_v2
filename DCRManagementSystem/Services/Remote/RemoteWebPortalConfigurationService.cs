using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;

namespace DCRManagementSystem.Services.Remote;

internal sealed class RemoteWebPortalConfigurationService : IWebPortalConfigurationService
{
    private readonly RemoteApiClient _api;

    public RemoteWebPortalConfigurationService(AppSettings settings)
        => _api = new RemoteApiClient(settings);

    public Task<WebPortalSettings> GetAsync()
        => _api.GetAsync<WebPortalSettings>("api/admin/web-portal");

    public Task SaveAsync(WebPortalSettings value)
        => _api.PostAsync("api/admin/web-portal", new ApiWebPortalSettingsRequest { Settings = value });
}
