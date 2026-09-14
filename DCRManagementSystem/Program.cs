using DCRManagementSystem.Data;
using DCRManagementSystem.Forms;
using DCRManagementSystem.Helpers;
using DCRManagementSystem.Services;
using DCRManagementSystem.Services.Remote;
using QuestPDF.Infrastructure;

namespace DCRManagementSystem;

internal static class UiSingleInstance
{
    private static Mutex? _mutex;

    public static bool TryAcquire(string name)
    {
        _mutex = new Mutex(initiallyOwned: true, name: $"Local\\DCRManagementSystem.{name}", createdNew: out var createdNew);
        return createdNew;
    }

    public static void Release()
    {
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        _mutex = null;
    }
}

internal static class Program
{
    internal enum NetworkModes { SERVER, CLIENT }
    // Thông tin phiên bản được MainScreen truyền sang; DataAccessMode vẫn do AppSettings quyết định độc lập.
    internal static NetworkModes NetMode { get; private set; } = NetworkModes.CLIENT;

    [STAThread]
    private static void Main(string[] args)
    {
        var isUserManagementMode = args.Any(x => x.Equals("--user-management", StringComparison.OrdinalIgnoreCase));
        var isMaintenanceMode = args.Any(x => x.Equals("--migrate-legacy-ggp-users", StringComparison.OrdinalIgnoreCase) ||
                                              x.Equals("--mail-server-config", StringComparison.OrdinalIgnoreCase) ||
                                              x.Equals("--run-mail-worker", StringComparison.OrdinalIgnoreCase) ||
                                              x.Equals("--run-server-jobs", StringComparison.OrdinalIgnoreCase) ||
                                              x.Equals("--run-reminders", StringComparison.OrdinalIgnoreCase));

        // Chỉ một DCR Main/Login và một launcher Quản lý người dùng tại một thời điểm.
        // Các job/maintenance CLI không bị chặn bởi mutex UI.
        var singleInstanceName = isUserManagementMode ? "UserManagement" : (!isMaintenanceMode ? "MainUi" : string.Empty);
        if (!string.IsNullOrEmpty(singleInstanceName) && !UiSingleInstance.TryAcquire(singleInstanceName))
            return;

        ApplicationConfiguration.Initialize();
        QuestPDF.Settings.License = LicenseType.Community;

        try
        {
            ReadNetworkMode(args);
            var settings = AppSettings.Load();
            string? startupConnectionError = null;

            if (settings.UseRemoteApi)
            {
                try
                {
                    ApiEndpointResolver.ResolveAsync(settings).GetAwaiter().GetResult();
                }
                catch (Exception connectionEx)
                {
                    settings.Api.MarkDisconnected();
                    startupConnectionError = connectionEx.Message;
                }
            }

            AppServices.Initialize(settings);

            if (!settings.UseRemoteApi)
            {
                NetworkResilienceService.ExecuteSql(() =>
                {
                    using var db = AppServices.CreateDbContext();
                    db.Database.EnsureCreated();
                    DatabaseUpgradeService.Apply(db);
                    SecurityKeyGuard.EnsureSigningKeyMatches(db, settings);
                    // DbSeeder.Seed(db); // Disabled: never create default business data on startup/build.
                });
            }

            if (args.Any(x => x.Equals("--user-management", StringComparison.OrdinalIgnoreCase)))
            {
                Application.Run(new UserManagementLauncherForm());
                return;
            }

            if (args.Any(x => x.Equals("--migrate-legacy-ggp-users", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    if (settings.UseRemoteApi)
                        throw new InvalidOperationException("Migration người dùng cũ phải chạy ở DirectSql mode trên máy server.");
                    var legacyConnection = Environment.GetEnvironmentVariable("DCR_LEGACY_GGP_CONNECTION_STRING") ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(legacyConnection))
                        throw new InvalidOperationException("Thiếu DCR_LEGACY_GGP_CONNECTION_STRING. Hãy chạy Scripts\\Migrate-Legacy-GGP-Users-To-DCR.cmd.");
                    LegacyGgpUserMigrationService.RunAsync(AppServices.CreateDbContext, legacyConnection).GetAwaiter().GetResult();
                    Environment.ExitCode = 0;
                }
                catch (Exception migrationEx)
                {
                    Environment.ExitCode = 2;
                    UiMessageBox.Show(
                        $"Migration GGP -> DCR thất bại.\r\n\r\n{migrationEx.Message}",
                        "User Migration", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return;
            }

            if (args.Any(x => x.Equals("--mail-server-config", StringComparison.OrdinalIgnoreCase)))
            {
                if (settings.UseRemoteApi)
                    throw new InvalidOperationException("--mail-server-config phải chạy trên máy DCR API ở DirectSql mode.");
                Application.Run(new MailServerConsoleForm());
                return;
            }

            if (args.Any(x => x.Equals("--run-mail-worker", StringComparison.OrdinalIgnoreCase)))
            {
                if (settings.UseRemoteApi)
                    throw new InvalidOperationException("--run-mail-worker phải chạy trên máy DCR API ở DirectSql mode.");
                AppServices.CreateMailWorkerService().RunOnceAsync().GetAwaiter().GetResult();
                return;
            }

            if (args.Any(x => x.Equals("--run-server-jobs", StringComparison.OrdinalIgnoreCase)))
            {
                if (settings.UseRemoteApi)
                    throw new InvalidOperationException("--run-server-jobs phải chạy trên máy DCR API ở DirectSql mode.");
                AppServices.CreateReminderService().RunOnceAsync().GetAwaiter().GetResult();
                AppServices.CreateMailWorkerService().RunOnceAsync().GetAwaiter().GetResult();
                return;
            }

            if (args.Any(x => x.Equals("--run-reminders", StringComparison.OrdinalIgnoreCase)))
            {
                if (settings.UseRemoteApi)
                    throw new InvalidOperationException("--run-reminders phải chạy trên máy DCR API ở DirectSql mode.");
                AppServices.CreateReminderService().RunOnceAsync().GetAwaiter().GetResult();
                return;
            }

            var startupRequestId = ParseStartupRequestId(args);

            if (settings.UseRemoteApi)
            {
                Application.Run(new LoginForm(startupRequestId, startupConnectionError));
                return;
            }

            // On the designated Mail Server, keep EmailOutbox moving while the normal
            // application is open, including while it is sitting on the Login screen.
            // Client PCs only perform the lightweight state check and never send mail.
            using var mailPump = new ServerMailBackgroundPump(AppServices.CreateDbContext, settings);
            mailPump.Start();

            Application.Run(new LoginForm(startupRequestId));
        }
        catch (Exception ex)
        {
            UiMessageBox.Show(
                $"Không thể khởi động DCR Management System.\r\n\r\n{ex.Message}",
                "Lỗi khởi động",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            if (!string.IsNullOrEmpty(singleInstanceName)) UiSingleInstance.Release();
        }
    }

    private static void ReadNetworkMode(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (!args[i].Equals("--network-mode", StringComparison.OrdinalIgnoreCase))
                continue;

            var mode = args[i + 1].Trim();
            if (mode.Equals("SERVER", StringComparison.OrdinalIgnoreCase))
            {
                NetMode = NetworkModes.SERVER;
                return;
            }

            if (mode.Equals("CLIENT", StringComparison.OrdinalIgnoreCase))
            {
                NetMode = NetworkModes.CLIENT;
                return;
            }

            throw new ArgumentException($"Network mode '{mode}' không hợp lệ. Dùng SERVER hoặc CLIENT.");
        }
    }

    private static int? ParseStartupRequestId(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--open-dcr", StringComparison.OrdinalIgnoreCase) &&
                i + 1 < args.Length &&
                int.TryParse(args[i + 1], out var id))
            {
                return id;
            }

            if (Uri.TryCreate(args[i], UriKind.Absolute, out var uri) &&
                uri.Scheme.Equals("dcr", StringComparison.OrdinalIgnoreCase))
            {
                var segment = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                if (int.TryParse(segment, out id))
                {
                    return id;
                }

                if (uri.Host.Equals("request", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(uri.AbsolutePath.Trim('/'), out id))
                {
                    return id;
                }
            }
        }

        return null;
    }
}
