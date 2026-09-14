using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DCRManagementSystem.Helpers;

public sealed class AppSettings
{
    public string DataAccessMode { get; set; } = DataAccessModes.RemoteApi;
    public RemoteApiSettings Api { get; set; } = new();

    public bool UseRemoteApi => DataAccessMode.Equals(DataAccessModes.RemoteApi, StringComparison.OrdinalIgnoreCase);

    public string ConnectionString { get; set; } = string.Empty;

    public DatabaseConnectionSettings Database { get; set; } = new();

    public string StorageRoot { get; set; } = @"Data\Attachments";
    public string ApprovedPdfRoot { get; set; } = @"Data\ApprovedPdf";
    public string DcrNumberPrefix { get; set; } = "DCR";
    public string ApplicationLinkTemplate { get; set; } = "dcr://request/{0}";
    public FileStorageSettings Storage { get; set; } = new();
    public AuthenticationSettings Authentication { get; set; } = new();
    public EmailSettings Email { get; set; } = new();
    public TeamsSettings Teams { get; set; } = new();
    public ReminderSettings Reminder { get; set; } = new();
    public SecuritySettings Security { get; set; } = new();

    internal byte[] SigningKeyBytes { get; private set; } = Array.Empty<byte>();

    public static AppSettings Load()
        => LoadFromFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));

    public static AppSettings LoadFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Đường dẫn file cấu hình không được để trống.", nameof(path));

        path = Path.GetFullPath(path);
        if (!File.Exists(path))
        {
            var defaults = new AppSettings();
            ClientApiConfigurationStore.ApplyLocalOverride(defaults);
            defaults.ApplyEnvironmentOverrides();
            defaults.FinalizeDataAccessConfiguration();
            return defaults;
        }

        var json = File.ReadAllText(path);
        var settings = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new AppSettings();

        settings.Database ??= new DatabaseConnectionSettings();
        settings.Api ??= new RemoteApiSettings();
        ClientApiConfigurationStore.ApplyLocalOverride(settings);
        settings.ApplyEnvironmentOverrides();
        settings.FinalizeDataAccessConfiguration();
        return settings;
    }

    private void ApplyEnvironmentOverrides()
    {
        var dataMode = Environment.GetEnvironmentVariable("DCR_DATA_ACCESS_MODE");
        if (!string.IsNullOrWhiteSpace(dataMode)) DataAccessMode = dataMode.Trim();

        var apiBaseUrl = Environment.GetEnvironmentVariable("DCR_API_BASE_URL");
        if (!string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            Api.BaseUrl = apiBaseUrl.Trim();
            Api.AutoSelectEndpoint = false;
        }

        var localApiBaseUrl = Environment.GetEnvironmentVariable("DCR_API_LOCAL_BASE_URL");
        if (!string.IsNullOrWhiteSpace(localApiBaseUrl)) Api.LocalBaseUrl = localApiBaseUrl.Trim();

        var remoteApiBaseUrl = Environment.GetEnvironmentVariable("DCR_API_REMOTE_BASE_URL");
        if (!string.IsNullOrWhiteSpace(remoteApiBaseUrl)) Api.RemoteBaseUrl = remoteApiBaseUrl.Trim();

        var connectionString = Environment.GetEnvironmentVariable("DCR_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(connectionString)) ConnectionString = connectionString;

        var smtpPassword = Environment.GetEnvironmentVariable("DCR_SMTP_PASSWORD");
        if (!string.IsNullOrWhiteSpace(smtpPassword)) Email.Password = smtpPassword;

        var m365TenantId = Environment.GetEnvironmentVariable("DCR_M365_TENANT_ID");
        if (!string.IsNullOrWhiteSpace(m365TenantId)) Email.TenantId = m365TenantId;
        var m365ClientId = Environment.GetEnvironmentVariable("DCR_M365_CLIENT_ID");
        if (!string.IsNullOrWhiteSpace(m365ClientId)) Email.ClientId = m365ClientId;
        var graphAccount = Environment.GetEnvironmentVariable("DCR_GRAPH_ACCOUNT_HINT");
        if (!string.IsNullOrWhiteSpace(graphAccount)) Email.AccountHint = graphAccount;

        var teamsWebhook = Environment.GetEnvironmentVariable("DCR_TEAMS_WEBHOOK_URL");
        if (!string.IsNullOrWhiteSpace(teamsWebhook)) Teams.WebhookUrl = teamsWebhook;

        var signingKey = Environment.GetEnvironmentVariable("DCR_APPROVAL_SIGNING_KEY_BASE64");
        if (!string.IsNullOrWhiteSpace(signingKey)) Security.ApprovalSigningKeyBase64 = signingKey;

        var storageServer = Environment.GetEnvironmentVariable("DCR_STORAGE_SERVER");
        if (!string.IsNullOrWhiteSpace(storageServer)) Storage.ServerAddress = storageServer;
        var storageShare = Environment.GetEnvironmentVariable("DCR_STORAGE_SHARE");
        if (!string.IsNullOrWhiteSpace(storageShare)) Storage.ShareName = storageShare;
    }

    private void FinalizeDataAccessConfiguration()
    {
        if (UseRemoteApi)
        {
            // Do not validate API URLs here. A broken/moved API endpoint must never prevent
            // the LoginForm from opening because the user needs that screen to repair the
            // connection configuration. ApiEndpointResolver validates before connecting.
            ConnectionString = string.Empty;
            return;
        }

        if (!DataAccessMode.Equals(DataAccessModes.DirectSql, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"DataAccessMode '{DataAccessMode}' không hợp lệ. Dùng '{DataAccessModes.RemoteApi}' hoặc '{DataAccessModes.DirectSql}'.");

        var environmentConnectionString = Environment.GetEnvironmentVariable("DCR_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(environmentConnectionString))
        {
            ConnectionString = environmentConnectionString.Trim();
            return;
        }

        DatabaseConfigurationStore.ApplyLocalOverride(this);
        if (string.IsNullOrWhiteSpace(ConnectionString))
            ConnectionString = Database.BuildConnectionString();
    }

    public void InitializeRuntimeSecrets()
    {
        if (!string.IsNullOrWhiteSpace(Security.ApprovalSigningKeyBase64))
        {
            try { SigningKeyBytes = Convert.FromBase64String(Security.ApprovalSigningKeyBase64); }
            catch (FormatException) { throw new InvalidOperationException("Security.ApprovalSigningKeyBase64 không phải Base64 hợp lệ."); }
            ValidateSigningKey(SigningKeyBytes);
            return;
        }

        var keyPath = GetAbsoluteSecurityKeyPath();
        Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
        if (TryReadSigningKey(keyPath, out var persistentKey))
        {
            SigningKeyBytes = persistentKey;
            return;
        }

        // Migrate a key from the old Debug/Release output or from a deployment package.
        // This keeps one approval key when switching Debug -> Release/Publish.
        foreach (var candidate in GetSigningKeyCandidates(includePersistentPath: false))
        {
            if (!TryReadSigningKey(candidate, out var migratedKey))
                continue;

            SigningKeyBytes = migratedKey;
            PersistSigningKey(keyPath, migratedKey);
            return;
        }

        // First database deployment only. SecurityKeyGuard will reject this generated
        // key if the database is already registered to another key.
        SigningKeyBytes = RandomNumberGenerator.GetBytes(64);
        PersistSigningKey(keyPath, SigningKeyBytes);
    }

    internal bool TryRepairSigningKey(string expectedFingerprintSha256)
    {
        if (string.IsNullOrWhiteSpace(expectedFingerprintSha256))
            return false;

        foreach (var candidate in GetSigningKeyCandidates(includePersistentPath: true))
        {
            if (!TryReadSigningKey(candidate, out var candidateKey))
                continue;

            var candidateFingerprint = Convert.ToHexString(SHA256.HashData(candidateKey)).ToLowerInvariant();
            if (!candidateFingerprint.Equals(expectedFingerprintSha256.Trim(), StringComparison.OrdinalIgnoreCase))
                continue;

            SigningKeyBytes = candidateKey;
            PersistSigningKey(GetAbsoluteSecurityKeyPath(), candidateKey);
            return true;
        }

        return false;
    }

    private IEnumerable<string> GetSigningKeyCandidates(bool includePersistentPath)
    {
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                var full = Path.GetFullPath(path);
                if (seen.Add(full)) candidates.Add(full);
            }
            catch
            {
                // Ignore malformed candidate paths.
            }
        }

        if (includePersistentPath)
            Add(GetAbsoluteSecurityKeyPath());

        Add(ApplicationDataPaths.ProvisionedSigningKeyPath);

        // Legacy behavior: relative paths were rooted at AppContext.BaseDirectory.
        if (!Path.IsPathRooted(Security.ApprovalSigningKeyFile))
            Add(Path.Combine(AppContext.BaseDirectory, Security.ApprovalSigningKeyFile));

        // Developer recovery: Release and Publish may run from another output tree.
        // Scan only a nearby bin folder, never arbitrary drives.
        try
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            for (var depth = 0; current is not null && depth < 6; depth++, current = current.Parent)
            {
                if (!current.Name.Equals("bin", StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var file in Directory.EnumerateFiles(
                             current.FullName,
                             "approval-signing.key",
                             SearchOption.AllDirectories))
                {
                    if (file.Contains(
                            $"{Path.DirectorySeparatorChar}Data{Path.DirectorySeparatorChar}Security{Path.DirectorySeparatorChar}",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        Add(file);
                    }
                }
                break;
            }
        }
        catch
        {
            // Candidate discovery is best effort.
        }

        return candidates;
    }

    private static bool TryReadSigningKey(string path, out byte[] key)
    {
        key = Array.Empty<byte>();
        if (!File.Exists(path)) return false;
        try
        {
            var text = File.ReadAllText(path).Trim();
            key = Convert.FromBase64String(text);
            ValidateSigningKey(key);
            return true;
        }
        catch
        {
            key = Array.Empty<byte>();
            return false;
        }
    }

    private static void PersistSigningKey(string path, byte[] key)
    {
        ValidateSigningKey(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, Convert.ToBase64String(key));
        File.Move(temp, path, true);
    }

    private static void ValidateSigningKey(byte[] key)
    {
        if (key.Length < 32)
            throw new InvalidOperationException("Approval signing key phải có ít nhất 32 bytes.");
    }

    public string GetAbsoluteStorageRoot() => ResolvePath(StorageRoot);
    public string GetAbsoluteApprovedPdfRoot() => ResolvePath(ApprovedPdfRoot);
    public string GetAbsoluteSecurityKeyPath() => ApplicationDataPaths.ResolvePersistentDataPath(Security.ApprovalSigningKeyFile);

    public string GetRequestLink(int requestId)
    {
        if (string.IsNullOrWhiteSpace(ApplicationLinkTemplate)) return string.Empty;
        try { return string.Format(ApplicationLinkTemplate, requestId); }
        catch (FormatException) { return string.Empty; }
    }

    private static string ResolvePath(string path) => Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
}

public static class DataAccessModes
{
    public const string RemoteApi = "RemoteApi";
    public const string DirectSql = "DirectSql";
}

public sealed class RemoteApiSettings
{
    // BaseUrl is the endpoint selected for the current process. In Auto mode it is
    // resolved once during startup: LocalBaseUrl first, then RemoteBaseUrl.
    public string BaseUrl { get; set; } = "https://dcr.ggpcontrol.cloud";
    public string LocalBaseUrl { get; set; } = "http://172.168.8.209:5080";
    public string RemoteBaseUrl { get; set; } = "https://dcr.ggpcontrol.cloud";
    public bool AutoSelectEndpoint { get; set; } = true;
    public int LocalProbeTimeoutMilliseconds { get; set; } = 1500;
    public int RemoteProbeTimeoutMilliseconds { get; set; } = 6000;
    public int TimeoutSeconds { get; set; } = 600;
    public int MaxConnectionsPerServer { get; set; } = 16;
    public bool AllowInvalidTlsCertificate { get; set; }

    [JsonIgnore]
    public string ActiveEndpointName { get; private set; } = "Chưa kết nối";

    [JsonIgnore]
    public bool IsEndpointResolved { get; private set; }

    [JsonIgnore]
    public bool UsingLocalEndpoint =>
        IsEndpointResolved &&
        ActiveEndpointName.Equals("Local API", StringComparison.OrdinalIgnoreCase);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RemoteBaseUrl)) RemoteBaseUrl = BaseUrl;
        if (string.IsNullOrWhiteSpace(BaseUrl)) BaseUrl = RemoteBaseUrl;

        ValidateUrl(RemoteBaseUrl, allowHttp: false, "Api.RemoteBaseUrl");
        if (!string.IsNullOrWhiteSpace(LocalBaseUrl))
            ValidateUrl(LocalBaseUrl, allowHttp: true, "Api.LocalBaseUrl");

        var baseIsLocal = !string.IsNullOrWhiteSpace(LocalBaseUrl) &&
                          Normalize(BaseUrl).Equals(Normalize(LocalBaseUrl), StringComparison.OrdinalIgnoreCase);
        ValidateUrl(BaseUrl, allowHttp: baseIsLocal, "Api.BaseUrl");

        LocalProbeTimeoutMilliseconds = Math.Clamp(LocalProbeTimeoutMilliseconds, 300, 10000);
        RemoteProbeTimeoutMilliseconds = Math.Clamp(RemoteProbeTimeoutMilliseconds, 1000, 30000);
        TimeoutSeconds = Math.Clamp(TimeoutSeconds, 10, 600);
        MaxConnectionsPerServer = Math.Clamp(MaxConnectionsPerServer, 4, 64);
        BaseUrl = Normalize(BaseUrl);
        LocalBaseUrl = Normalize(LocalBaseUrl);
        RemoteBaseUrl = Normalize(RemoteBaseUrl);
    }

    public void SetActiveEndpoint(string baseUrl, string endpointName)
    {
        BaseUrl = Normalize(baseUrl);
        ActiveEndpointName = string.IsNullOrWhiteSpace(endpointName) ? "API" : endpointName.Trim();
        IsEndpointResolved = true;
    }

    public void MarkDisconnected()
    {
        IsEndpointResolved = false;
        ActiveEndpointName = "Chưa kết nối";
    }

    private static void ValidateUrl(string value, bool allowHttp, string settingName)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException($"{settingName} phải là URL HTTP/HTTPS hợp lệ.");
        }

        if (uri.Scheme == Uri.UriSchemeHttp && !allowHttp && !uri.IsLoopback)
            throw new InvalidOperationException($"{settingName} bên ngoài mạng nội bộ phải sử dụng HTTPS.");
    }

    private static string Normalize(string? value) => (value ?? string.Empty).Trim().TrimEnd('/');
}

public sealed class FileStorageSettings
{
    public bool UseNetworkShare { get; set; } = true;
    public bool FallbackToLocalStorageWhenNetworkShareUnavailable { get; set; } = true;
    public string ServerAddress { get; set; } = "172.168.8.209";
    public string ShareName { get; set; } = "AutoUpdate";
    public string RootSubfolder { get; set; } = "DCR";
    public bool CompressLargeTechnicalFiles { get; set; } = true;
    public int CompressionThresholdMb { get; set; } = 50;
    public string CompressionExtensions { get; set; } = ".step;.stp;.stpz;.iges;.igs;.dwg;.dxf;.sldprt;.sldasm;.catpart;.catproduct;.prt;.asm;.x_t;.x_b;.raw;.tif;.tiff";
}

public sealed class AuthenticationSettings
{
    public string Mode { get; set; } = "WindowsPreferred";
    public bool AutoLoginWindows { get; set; } = true;
    public bool AllowLocalFallback { get; set; } = true;
    public bool AllowUsernameMatchForWindows { get; set; } = false; // legacy option; username inference is intentionally disabled
    public string AllowedWindowsDomain { get; set; } = string.Empty;
    public bool UseWindowsSessionForApproval { get; set; } = true;
    public LdapSettings Ldap { get; set; } = new();
}

public sealed class LdapSettings
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 389;
    public bool UseSsl { get; set; }
    public string Domain { get; set; } = string.Empty;
}

public static class EmailAuthenticationModes
{
    public const string MicrosoftGraphDelegated = "MicrosoftGraphDelegated";
    public const string SmtpBasic = "SmtpBasic";
    public const string SmtpRelay = "SmtpRelay";
}

public sealed class EmailSettings
{
    public bool Enabled { get; set; }
    public string AuthenticationMode { get; set; } = EmailAuthenticationModes.MicrosoftGraphDelegated;

    // Microsoft Graph delegated authentication for desktop/WinForms.
    // This is a public-client flow: no Client Secret is stored in the application.
    public string TenantId { get; set; } = "organizations";
    public string ClientId { get; set; } = string.Empty;
    public string AccountHint { get; set; } = string.Empty;

    // SMTP Basic / Relay fallback.
    public string SmtpHost { get; set; } = "smtp.office365.com";
    public int SmtpPort { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    // For Graph delegated mode, mail is always sent as the signed-in account via /me/sendMail.
    // FromAddress is retained for SMTP fallback modes only.
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "DCR Management System";
}

public sealed class TeamsSettings
{
    public bool Enabled { get; set; }
    public string WebhookUrl { get; set; } = string.Empty;
}

public sealed class ReminderSettings
{
    public bool Enabled { get; set; } = true;
    public int ApprovalDueHours { get; set; } = 24;
    public int ReminderRepeatHours { get; set; } = 24;
    public int EscalationHours { get; set; } = 48;
    public string EscalationEmail { get; set; } = string.Empty;
}

public sealed class SecuritySettings
{
    public string ApprovalSigningKeyBase64 { get; set; } = string.Empty;
    public string ApprovalSigningKeyFile { get; set; } = @"Data\Security\approval-signing.key";
    // Retained for old configuration files; decisions now use the authenticated session.
    public bool RequirePasswordReauthenticationForDecision { get; set; }
}
