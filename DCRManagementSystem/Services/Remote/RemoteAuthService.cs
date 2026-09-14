using System.Security.Principal;
using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;

namespace DCRManagementSystem.Services.Remote;

internal sealed class RemoteAuthService : IAuthService
{
    private readonly AppSettings _settings;
    private readonly RemoteApiClient _api;
    private readonly RememberedWindowsLoginService _rememberedWindowsLogin = new();

    public RemoteAuthService(AppSettings settings)
    {
        _settings = settings;
        _api = new RemoteApiClient(settings);
    }

    public async Task<AuthenticationResult?> LoginWithRememberedWindowsAsync()
    {
        if (_settings.Authentication.Mode.Equals(AuthenticationModes.LocalOnly, StringComparison.OrdinalIgnoreCase))
            return null;

        var remembered = _rememberedWindowsLogin.TryLoadForCurrentWindowsUser();
        if (remembered is null || string.IsNullOrWhiteSpace(remembered.RefreshToken))
            return null;

        try
        {
            var response = await _api.PostAsync<ApiRefreshRequest, ApiLoginResponse>(
                "api/auth/refresh",
                new ApiRefreshRequest
                {
                    RefreshToken = remembered.RefreshToken,
                    WindowsIdentity = GetWindowsIdentity(),
                    MachineName = Environment.MachineName
                },
                anonymous: true).ConfigureAwait(false);

            RemoteApiSession.Set(response.AccessToken, response.RefreshToken, response.AccessTokenExpiresUtc, response.RefreshTokenExpiresUtc);
            return response.Authentication;
        }
        catch (UnauthorizedAccessException)
        {
            _rememberedWindowsLogin.Forget();
            RemoteApiSession.ClearAll();
            return null;
        }
    }

    public Task<AuthenticationResult?> LoginWithWindowsAsync() => LoginWithRememberedWindowsAsync();

    public async Task<AuthenticationResult?> LoginAsync(string username, string password)
    {
        username = username?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(username)) return null;

        try
        {
            var response = await _api.PostAsync<ApiLoginRequest, ApiLoginResponse>(
                "api/auth/login",
                new ApiLoginRequest
                {
                    Username = username,
                    Password = password ?? string.Empty,
                    WindowsIdentity = GetWindowsIdentity(),
                    MachineName = Environment.MachineName
                },
                anonymous: true).ConfigureAwait(false);
            RemoteApiSession.Set(response.AccessToken, response.RefreshToken, response.AccessTokenExpiresUtc, response.RefreshTokenExpiresUtc);
            return response.Authentication;
        }
        catch (UnauthorizedAccessException)
        {
            RemoteApiSession.ClearAll();
            return null;
        }
    }

    public async Task ChangePasswordAsync(int userId, string currentPassword, string newPassword)
    {
        await _api.PostAsync(
            "api/auth/change-password",
            new ApiChangePasswordRequest
            {
                CurrentPassword = currentPassword ?? string.Empty,
                NewPassword = newPassword ?? string.Empty
            }).ConfigureAwait(false);
    }

    public void RememberCurrentWindowsLogin(User user)
    {
        var refreshToken = RemoteApiSession.LatestRefreshToken;
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new InvalidOperationException("API chưa cấp refresh token cho phiên đăng nhập này.");
        _rememberedWindowsLogin.Remember(user, refreshToken);
    }

    public void ForgetRememberedWindowsLogin()
    {
        _rememberedWindowsLogin.Forget();
    }

    public Task<DecisionAuthentication> AuthenticateDecisionAsync(int userId) =>
        _api.PostAsync<object, DecisionAuthentication>(
            "api/auth/reauthenticate", new { });

    private static string GetWindowsIdentity()
    {
        try { return WindowsIdentity.GetCurrent().Name ?? Environment.UserName; }
        catch { return Environment.UserName; }
    }
}
