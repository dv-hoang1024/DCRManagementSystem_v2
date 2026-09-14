using System.Diagnostics;

namespace DCRManagementSystem.Services.Remote;

internal sealed class RemoteAttachmentService : IAttachmentService
{
    private readonly RemoteApiClient _api;
    public RemoteAttachmentService(Helpers.AppSettings settings) => _api = new RemoteApiClient(settings);

    public Task UploadAsync(int requestId, string sourceFile, string attachmentType, int userId, bool isAdmin) =>
        _api.UploadFileAsync($"api/dcr/{requestId}/attachments/upload", sourceFile, attachmentType);

    public Task DeleteAsync(int attachmentId, int userId, bool isAdmin) =>
        _api.DeleteAsync($"api/attachments/{attachmentId}");

    public Task<string> GetAbsolutePathAsync(int attachmentId, int userId, bool isAdmin) =>
        _api.DownloadToTempAsync($"api/attachments/{attachmentId}/download");

    public async Task OpenAsync(int attachmentId, int userId, bool isAdmin)
    {
        var path = await GetAbsolutePathAsync(attachmentId, userId, isAdmin).ConfigureAwait(false);
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }
}
