using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;

namespace DCRManagementSystem.Services.Remote;

internal sealed class RemoteEmailConfigurationService : IEmailConfigurationService
{
    private readonly RemoteApiClient _api;
    public RemoteEmailConfigurationService(AppSettings settings) => _api = new RemoteApiClient(settings);

    public Task<EmailSettings> GetAsync() => _api.GetAsync<EmailSettings>("api/admin/email/settings");
    public Task SaveAsync(EmailSettings value) => _api.PostAsync("api/admin/email/settings", new ApiEmailSettingsRequest { Settings = value });
    public Task<string> TestAsync(EmailSettings value, string recipient) => _api.PostAsync<ApiEmailSettingsRequest, string>("api/admin/email/test", new ApiEmailSettingsRequest { Settings = value, Recipient = recipient });

    public Task<string> SignInGraphAsync(EmailSettings value) =>
        throw new InvalidOperationException("Đăng nhập Microsoft Graph phải thực hiện trực tiếp trên máy chạy DCR API/Mail Worker để token được lưu đúng trên server.");
    public Task<string> SignInGraphWithAccountSelectionAsync(EmailSettings value) => SignInGraphAsync(value);
    public Task SignOutGraphAsync(EmailSettings value) =>
        throw new InvalidOperationException("Đăng xuất Microsoft Graph phải thực hiện trực tiếp trên máy chạy DCR API/Mail Worker.");
    public Task<string> GetSignedInGraphAccountAsync(EmailSettings value) => _api.GetAsync<string>("api/admin/email/signed-account");
}

internal sealed class RemoteEmailOutboxService : IEmailOutboxService
{
    private readonly RemoteApiClient _api;
    public RemoteEmailOutboxService(AppSettings settings) => _api = new RemoteApiClient(settings);
    public Task<long> EnqueueTestAsync(string recipient, CancellationToken cancellationToken = default) =>
        _api.PostAsync<object, long>("api/admin/email/outbox/test", new { Recipient = recipient }, cancellationToken: cancellationToken);
    public Task<int> RequeueAuthenticationBlockedAsync(CancellationToken cancellationToken = default) =>
        _api.PostAsync<object, int>("api/admin/email/outbox/requeue-auth", new { }, cancellationToken: cancellationToken);
    public Task<EmailOutboxSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
        _api.GetAsync<EmailOutboxSnapshot>("api/admin/email/outbox/snapshot", cancellationToken: cancellationToken);
}

internal sealed class RemoteMailWorkerStateService : IMailWorkerStateService
{
    private readonly RemoteApiClient _api;
    public RemoteMailWorkerStateService(AppSettings settings) => _api = new RemoteApiClient(settings);
    public Task<MailWorkerState> GetAsync(CancellationToken cancellationToken = default) => _api.GetAsync<MailWorkerState>("api/admin/email/worker/state", cancellationToken: cancellationToken);
    public Task RecordSignInAsync(string signedInAccount, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Mail Worker sign-in state được quản lý trên DCR API server.");
    public Task RecordSignOutAsync(CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Mail Worker sign-out state được quản lý trên DCR API server.");
}

internal sealed class RemoteMailWorkerService : IMailWorkerService
{
    private readonly RemoteApiClient _api;
    public RemoteMailWorkerService(AppSettings settings) => _api = new RemoteApiClient(settings);
    public Task<MailWorkerRunResult> RunOnceAsync(int batchSize = 25, CancellationToken cancellationToken = default) =>
        _api.PostAsync<object, MailWorkerRunResult>($"api/admin/email/worker/run?batchSize={Math.Clamp(batchSize, 1, 200)}", new { }, cancellationToken: cancellationToken);
    public Task<MailWorkerRunResult> RunPendingNowAsync(int batchSize = 25, CancellationToken cancellationToken = default) =>
        _api.PostAsync<object, MailWorkerRunResult>($"api/admin/email/worker/run-pending?batchSize={Math.Clamp(batchSize, 1, 200)}", new { }, cancellationToken: cancellationToken);
}
