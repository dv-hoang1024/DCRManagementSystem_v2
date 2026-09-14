using DCRManagementSystem.Data;
using DCRManagementSystem.Models;
using DCRManagementSystem.Services;
using DCRManagementSystem.Services.Remote;
using Microsoft.EntityFrameworkCore;

namespace DCRManagementSystem.Helpers;

public static class AppServices
{
    private static AppSettings? _settings;
    private static string _effectiveConnectionString = string.Empty;

    public static AppSettings Settings =>
        _settings ?? throw new InvalidOperationException("AppServices chưa được khởi tạo.");

    public static bool UseRemoteApi => Settings.UseRemoteApi;
    public static string ApiConnectionLabel => Settings.UseRemoteApi ? Settings.Api.ActiveEndpointName : "SQL Server";
    public static string ApiBaseUrl => Settings.UseRemoteApi ? Settings.Api.BaseUrl : string.Empty;

    public static void Initialize(AppSettings settings)
    {
        _settings = settings;

        if (settings.UseRemoteApi)
        {
            Directory.CreateDirectory(ApplicationDataPaths.PersistentRoot);
            Directory.CreateDirectory(Path.Combine(ApplicationDataPaths.PersistentRoot, "Temp"));
            return;
        }

        Directory.CreateDirectory(settings.GetAbsoluteStorageRoot());
        Directory.CreateDirectory(settings.GetAbsoluteApprovedPdfRoot());
        settings.InitializeRuntimeSecrets();
        _effectiveConnectionString = settings.Database.ApplyPerformanceOptions(settings.ConnectionString);
    }

    public static AppDbContext CreateDbContext()
    {
        if (Settings.UseRemoteApi)
            throw new InvalidOperationException("Ứng dụng đang chạy ở RemoteApi mode. WinForms client không được kết nối SQL trực tiếp.");

        var connectionString = string.IsNullOrWhiteSpace(_effectiveConnectionString)
            ? Settings.Database.ApplyPerformanceOptions(Settings.ConnectionString)
            : _effectiveConnectionString;
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString, sql =>
            {
                sql.CommandTimeout(Math.Clamp(Settings.Database.CommandTimeoutSeconds, 10, 120));
            })
            .Options;
        return new AppDbContext(options);
    }

    public static IAuthService CreateAuthService() =>
        Settings.UseRemoteApi ? new RemoteAuthService(Settings) : new AuthService(CreateDbContext, Settings);

    public static NotificationService CreateNotificationService()
    {
        EnsureDirectSql(nameof(CreateNotificationService));
        return new NotificationService(CreateDbContext, Settings);
    }

    public static IEmailConfigurationService CreateEmailConfigurationService() =>
        Settings.UseRemoteApi
            ? new RemoteEmailConfigurationService(Settings)
            : new EmailConfigurationService(CreateDbContext, Settings);

    public static IPdfService CreatePdfService() =>
        Settings.UseRemoteApi ? new RemotePdfService(Settings) : new PdfService(CreateDbContext, Settings);

    public static IDcrService CreateDcrService()
    {
        if (Settings.UseRemoteApi)
            return new RemoteDcrService(Settings);

        var notifications = CreateNotificationService();
        return new DcrService(
            CreateDbContext,
            Settings,
            notifications,
            new ApprovalRoutingService(),
            new ApprovalSignatureService(Settings),
            new PdfService(CreateDbContext, Settings));
    }

    public static IAttachmentService CreateAttachmentService() =>
        Settings.UseRemoteApi
            ? new RemoteAttachmentService(Settings)
            : new AttachmentService(CreateDbContext, Settings);

    public static IWebPortalConfigurationService CreateWebPortalConfigurationService() =>
        Settings.UseRemoteApi
            ? new RemoteWebPortalConfigurationService(Settings)
            : new WebPortalConfigurationService(CreateDbContext);

    public static IStorageConfigurationService CreateStorageConfigurationService() =>
        Settings.UseRemoteApi
            ? new RemoteStorageConfigurationService(Settings)
            : new StorageConfigurationService(CreateDbContext, Settings);

    public static DatabaseConfigurationService CreateDatabaseConfigurationService() =>
        new(Settings);

    public static DraftRecoveryService CreateDraftRecoveryService() => new();

    public static ReminderService CreateReminderService()
    {
        EnsureDirectSql(nameof(CreateReminderService));
        return new ReminderService(CreateDbContext, Settings, CreateNotificationService());
    }

    public static IEmailOutboxService CreateEmailOutboxService() =>
        Settings.UseRemoteApi
            ? new RemoteEmailOutboxService(Settings)
            : new EmailOutboxService(CreateDbContext);

    public static IMailWorkerService CreateMailWorkerService() =>
        Settings.UseRemoteApi
            ? new RemoteMailWorkerService(Settings)
            : new MailWorkerService(CreateDbContext, Settings);

    public static IMailWorkerStateService CreateMailWorkerStateService() =>
        Settings.UseRemoteApi
            ? new RemoteMailWorkerStateService(Settings)
            : new MailWorkerStateService(CreateDbContext);

    public static IAdminService CreateAdminService() =>
        Settings.UseRemoteApi ? new RemoteAdminService(Settings) : new AdminService(CreateDbContext, Settings);

    public static async Task<ApiHealthResponse> TestRemoteApiAsync(CancellationToken cancellationToken = default)
    {
        if (!Settings.UseRemoteApi)
            throw new InvalidOperationException("Ứng dụng hiện không chạy ở RemoteApi mode.");
        return await new RemoteApiClient(Settings).HealthAsync(cancellationToken).ConfigureAwait(false);
    }

    public static void ClearRemoteSession()
    {
        if (Settings.UseRemoteApi)
            RemoteApiSession.ClearAll();
    }

    private static void EnsureDirectSql(string operation)
    {
        if (Settings.UseRemoteApi)
            throw new InvalidOperationException($"{operation} chỉ chạy trên DCR API server/DirectSql mode.");
    }
}
