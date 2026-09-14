using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;

namespace DCRManagementSystem.Services.Remote;

internal sealed class RemoteStorageConfigurationService : IStorageConfigurationService
{
    private readonly RemoteApiClient _api;
    public RemoteStorageConfigurationService(AppSettings settings) => _api = new RemoteApiClient(settings);

    public Task<FileStorageSettings> GetAsync() => _api.GetAsync<FileStorageSettings>("api/admin/storage");

    public Task SaveAsync(FileStorageSettings value) =>
        _api.PostAsync("api/admin/storage", new ApiStorageSettingsRequest { Settings = value });

    public Task<string> TestConnectionAsync(FileStorageSettings value) =>
        _api.PostAsync<ApiStorageSettingsRequest, string>("api/admin/storage/test", new ApiStorageSettingsRequest { Settings = value });

    public string ResolveRoot(FileStorageSettings value)
    {
        if (value.UseNetworkShare)
        {
            var server = value.ServerAddress.Trim().Trim('\\');
            var share = value.ShareName.Trim().Trim('\\');
            var sub = value.RootSubfolder.Trim().Trim('\\');
            var root = $@"\\{server}\{share}";
            return string.IsNullOrWhiteSpace(sub) ? root : Path.Combine(root, sub);
        }
        return value.RootSubfolder;
    }
}
