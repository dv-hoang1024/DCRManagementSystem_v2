namespace DCRManagementSystem.Services.Remote;

internal sealed class RemotePdfService : IPdfService
{
    private readonly RemoteApiClient _api;
    public RemotePdfService(Helpers.AppSettings settings) => _api = new RemoteApiClient(settings);

    public Task ExportAsync(int requestId, string outputPath) =>
        _api.DownloadToFileAsync($"api/dcr/{requestId}/pdf/export", outputPath);

    public Task<string> GeneratePreviewPdfAsync(int requestId, CancellationToken cancellationToken = default) =>
        _api.DownloadToTempAsync($"api/dcr/{requestId}/pdf/preview", ".pdf", cancellationToken);

    public async Task<string> GenerateFinalApprovedPdfAsync(int requestId, int? actorUserId = null)
    {
        await _api.PostAsync($"api/dcr/{requestId}/pdf/final/generate", new { }).ConfigureAwait(false);
        return await GetVerifiedFinalApprovedPdfPathAsync(requestId).ConfigureAwait(false);
    }

    public Task<string> GetVerifiedFinalApprovedPdfPathAsync(int requestId) =>
        _api.DownloadToTempAsync($"api/dcr/{requestId}/pdf/final", ".pdf");
}
