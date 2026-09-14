using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DCRManagementSystem.Helpers;

namespace DCRManagementSystem.Api;

public sealed class ApiTokenPayload
{
    public int Version { get; set; } = 1;
    public string Purpose { get; set; } = string.Empty;
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string AuthMethod { get; set; } = string.Empty;
    public string WindowsIdentity { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public long ExpiresUnixSeconds { get; set; }
    public string Nonce { get; set; } = string.Empty;
}

public sealed class ApiTokenService
{
    public const string AccessPurpose = "access";
    public const string RefreshPurpose = "refresh";
    public const string DecisionPurpose = "decision";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly byte[] _key;
    private readonly ApiServerOptions _options;

    public ApiTokenService(IConfiguration configuration)
    {
        _options = configuration.GetSection("ApiServer").Get<ApiServerOptions>() ?? new ApiServerOptions();
        _options.AccessTokenHours = Math.Clamp(_options.AccessTokenHours, 1, 24);
        _options.RefreshTokenDays = Math.Clamp(_options.RefreshTokenDays, 1, 180);
        _options.DecisionProofMinutes = Math.Clamp(_options.DecisionProofMinutes, 1, 30);
        _key = LoadOrCreateKey();
    }

    public (string Token, DateTime ExpiresUtc) IssueAccess(
        int userId,
        string username,
        string role,
        string authMethod,
        string windowsIdentity,
        string machineName,
        string? sessionId = null)
        => Issue(AccessPurpose, userId, username, role, authMethod, windowsIdentity, machineName,
            TimeSpan.FromHours(_options.AccessTokenHours), sessionId);

    public (string Token, DateTime ExpiresUtc) IssueRefresh(
        int userId,
        string username,
        string role,
        string authMethod,
        string windowsIdentity,
        string machineName,
        string? sessionId = null)
        => Issue(RefreshPurpose, userId, username, role, authMethod, windowsIdentity, machineName,
            TimeSpan.FromDays(_options.RefreshTokenDays), sessionId);

    public (string Token, DateTime ExpiresUtc) IssueDecisionProof(
        int userId,
        string username,
        string role,
        string authMethod,
        string windowsIdentity,
        string machineName,
        string sessionId)
        => Issue(DecisionPurpose, userId, username, role, authMethod, windowsIdentity, machineName,
            TimeSpan.FromMinutes(_options.DecisionProofMinutes), sessionId);

    public bool TryValidate(string token, string expectedPurpose, out ApiTokenPayload payload)
    {
        payload = new ApiTokenPayload();
        if (string.IsNullOrWhiteSpace(token)) return false;
        var parts = token.Split('.');
        if (parts.Length != 2) return false;

        try
        {
            var payloadBytes = Base64UrlDecode(parts[0]);
            var signature = Base64UrlDecode(parts[1]);
            using var hmac = new HMACSHA256(_key);
            var expected = hmac.ComputeHash(payloadBytes);
            if (!CryptographicOperations.FixedTimeEquals(signature, expected)) return false;

            var parsed = JsonSerializer.Deserialize<ApiTokenPayload>(payloadBytes, JsonOptions);
            if (parsed is null || parsed.Version != 1 || parsed.UserId <= 0) return false;
            if (!string.Equals(parsed.Purpose, expectedPurpose, StringComparison.Ordinal)) return false;
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= parsed.ExpiresUnixSeconds) return false;
            payload = parsed;
            return true;
        }
        catch
        {
            payload = new ApiTokenPayload();
            return false;
        }
    }

    private (string Token, DateTime ExpiresUtc) Issue(
        string purpose,
        int userId,
        string username,
        string role,
        string authMethod,
        string windowsIdentity,
        string machineName,
        TimeSpan lifetime,
        string? sessionId)
    {
        var expires = DateTime.UtcNow.Add(lifetime);
        var payload = new ApiTokenPayload
        {
            Purpose = purpose,
            UserId = userId,
            Username = username ?? string.Empty,
            Role = role ?? string.Empty,
            AuthMethod = authMethod ?? string.Empty,
            WindowsIdentity = windowsIdentity ?? string.Empty,
            MachineName = machineName ?? string.Empty,
            SessionId = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString("N") : sessionId,
            ExpiresUnixSeconds = new DateTimeOffset(expires).ToUnixTimeSeconds(),
            Nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        using var hmac = new HMACSHA256(_key);
        var signature = hmac.ComputeHash(bytes);
        return ($"{Base64UrlEncode(bytes)}.{Base64UrlEncode(signature)}", expires);
    }

    private static byte[] LoadOrCreateKey()
    {
        var env = Environment.GetEnvironmentVariable("DCR_API_TOKEN_SECRET_BASE64");
        if (!string.IsNullOrWhiteSpace(env))
        {
            var decoded = Convert.FromBase64String(env.Trim());
            if (decoded.Length < 32)
                throw new InvalidOperationException("DCR_API_TOKEN_SECRET_BASE64 phải giải mã thành ít nhất 32 bytes.");
            return decoded;
        }

        var root = Path.Combine(ApplicationDataPaths.PersistentRoot, "Api");
        var path = Path.Combine(root, "api-token.key");
        Directory.CreateDirectory(root);
        if (File.Exists(path))
        {
            var decoded = Convert.FromBase64String(File.ReadAllText(path).Trim());
            if (decoded.Length < 32)
                throw new InvalidOperationException("api-token.key không hợp lệ.");
            return decoded;
        }

        var key = RandomNumberGenerator.GetBytes(64);
        var temp = path + ".tmp";
        File.WriteAllText(temp, Convert.ToBase64String(key));
        File.Move(temp, path, true);
        return key;
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var text = value.Replace('-', '+').Replace('_', '/');

        text = text.PadRight(text.Length + (4 - text.Length % 4) % 4, '=');

        return Convert.FromBase64String(text);
    }
}
