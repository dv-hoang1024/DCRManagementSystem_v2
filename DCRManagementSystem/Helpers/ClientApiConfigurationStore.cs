using System.Text.Json;

namespace DCRManagementSystem.Helpers;

public static class ClientApiConfigurationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static void ApplyLocalOverride(AppSettings settings)
    {
        if (settings is null || !settings.UseRemoteApi)
            return;

        var path = ApplicationDataPaths.ApiClientConfigPath;
        if (!File.Exists(path))
            return;

        try
        {
            var json = File.ReadAllText(path);
            var saved = JsonSerializer.Deserialize<ClientApiConnectionSettings>(json, JsonOptions);
            if (saved is null)
                return;

            if (!string.IsNullOrWhiteSpace(saved.LocalBaseUrl))
                settings.Api.LocalBaseUrl = saved.LocalBaseUrl.Trim();

            if (!string.IsNullOrWhiteSpace(saved.RemoteBaseUrl))
                settings.Api.RemoteBaseUrl = MigrateLegacyDcrHostname(saved.RemoteBaseUrl.Trim());

            settings.Api.AutoSelectEndpoint = true;

            if (saved.LocalProbeTimeoutMilliseconds > 0)
                settings.Api.LocalProbeTimeoutMilliseconds = saved.LocalProbeTimeoutMilliseconds;

            if (saved.RemoteProbeTimeoutMilliseconds > 0)
                settings.Api.RemoteProbeTimeoutMilliseconds = saved.RemoteProbeTimeoutMilliseconds;

            if (saved.MaxConnectionsPerServer > 0)
                settings.Api.MaxConnectionsPerServer = saved.MaxConnectionsPerServer;

            settings.Api.BaseUrl = settings.Api.RemoteBaseUrl;
        }
        catch
        {
            // A damaged local client override must never block startup.
            // LoginForm exposes the connection configuration so the user can repair it.
        }
    }

    public static void Save(RemoteApiSettings api)
    {
        ArgumentNullException.ThrowIfNull(api);
        api.Validate();

        var path = ApplicationDataPaths.ApiClientConfigPath;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var saved = new ClientApiConnectionSettings
        {
            LocalBaseUrl = api.LocalBaseUrl,
            RemoteBaseUrl = api.RemoteBaseUrl,
            LocalProbeTimeoutMilliseconds = api.LocalProbeTimeoutMilliseconds,
            RemoteProbeTimeoutMilliseconds = api.RemoteProbeTimeoutMilliseconds,
            MaxConnectionsPerServer = api.MaxConnectionsPerServer
        };

        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(saved, JsonOptions));
        File.Move(temp, path, true);
    }

    public static string GetConfigPath() => ApplicationDataPaths.ApiClientConfigPath;

    private static string MigrateLegacyDcrHostname(string remoteBaseUrl)
    {
        return remoteBaseUrl.TrimEnd('/').Equals(
            "https://api.ggpcontrol.cloud",
            StringComparison.OrdinalIgnoreCase)
            ? "https://dcr.ggpcontrol.cloud"
            : remoteBaseUrl;
    }

    private sealed class ClientApiConnectionSettings
    {
        public string LocalBaseUrl { get; set; } = string.Empty;
        public string RemoteBaseUrl { get; set; } = string.Empty;
        public int LocalProbeTimeoutMilliseconds { get; set; } = 1500;
        public int RemoteProbeTimeoutMilliseconds { get; set; } = 6000;
        public int MaxConnectionsPerServer { get; set; } = 16;
    }
}
