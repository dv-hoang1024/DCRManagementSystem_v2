namespace DCRManagementSystem.Services.Remote;

internal static class RemoteApiSession
{
    private static readonly object Sync = new();
    private static string _accessToken = string.Empty;
    private static string _latestRefreshToken = string.Empty;
    private static DateTime _accessTokenExpiresUtc = DateTime.MinValue;
    private static DateTime _refreshTokenExpiresUtc = DateTime.MinValue;

    public static string AccessToken
    {
        get { lock (Sync) return _accessToken; }
    }

    public static string LatestRefreshToken
    {
        get { lock (Sync) return _latestRefreshToken; }
    }

    public static DateTime AccessTokenExpiresUtc
    {
        get { lock (Sync) return _accessTokenExpiresUtc; }
    }

    public static DateTime RefreshTokenExpiresUtc
    {
        get { lock (Sync) return _refreshTokenExpiresUtc; }
    }

    public static void Set(
        string accessToken,
        string refreshToken,
        DateTime accessTokenExpiresUtc,
        DateTime refreshTokenExpiresUtc)
    {
        lock (Sync)
        {
            _accessToken = accessToken ?? string.Empty;
            _latestRefreshToken = refreshToken ?? string.Empty;
            _accessTokenExpiresUtc = accessTokenExpiresUtc;
            _refreshTokenExpiresUtc = refreshTokenExpiresUtc;
        }
    }

    public static void ClearAccessToken()
    {
        lock (Sync)
        {
            _accessToken = string.Empty;
            _accessTokenExpiresUtc = DateTime.MinValue;
        }
    }

    public static void ClearAll()
    {
        lock (Sync)
        {
            _accessToken = string.Empty;
            _latestRefreshToken = string.Empty;
            _accessTokenExpiresUtc = DateTime.MinValue;
            _refreshTokenExpiresUtc = DateTime.MinValue;
        }
    }
}
