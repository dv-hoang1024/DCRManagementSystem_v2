using System.DirectoryServices.Protocols;
using System.Net;
using System.Security.Principal;
using DCRManagementSystem.Data;
using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace DCRManagementSystem.Services;

public sealed class AuthService : IAuthService
{
    private readonly Func<AppDbContext> _dbFactory;
    private readonly AppSettings _settings;
    private readonly RememberedWindowsLoginService _rememberedWindowsLogin = new();

    public AuthService(Func<AppDbContext> dbFactory, AppSettings settings)
    {
        _dbFactory = dbFactory;
        _settings = settings;
    }

    public async Task<AuthenticationResult?> LoginWithRememberedWindowsAsync()
    {
        if (_settings.Authentication.Mode.Equals(AuthenticationModes.LocalOnly, StringComparison.OrdinalIgnoreCase))
            return null;

        var remembered = _rememberedWindowsLogin.TryLoadForCurrentWindowsUser();
        if (remembered is null)
            return null;
        if (!IsAllowedDomain(remembered.WindowsIdentity))
            return null;

        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        var user = await db.Users
            .AsNoTracking()
            .Include(x => x.Department)
            .FirstOrDefaultAsync(x =>
                x.UserId == remembered.UserId &&
                x.Username == remembered.Username &&
                x.IsActive &&
                !x.IsDeleted);

        if (user is null)
        {
            _rememberedWindowsLogin.Forget();
            return null;
        }

        return new AuthenticationResult
        {
            User = user,
            AuthMethod = AuthMethods.WindowsIntegrated,
            WindowsIdentity = remembered.WindowsIdentity
        };
    }

    /// <summary>
    /// Compatibility entry point. Windows quick-login is intentionally available only
    /// after a successful credential login has remembered this Windows profile.
    /// Users.WindowsAccount and short Windows usernames are never used as authority.
    /// </summary>
    public Task<AuthenticationResult?> LoginWithWindowsAsync()
        => LoginWithRememberedWindowsAsync();

    public void RememberCurrentWindowsLogin(User user) => _rememberedWindowsLogin.Remember(user);

    public void ForgetRememberedWindowsLogin() => _rememberedWindowsLogin.Forget();

    public async Task<AuthenticationResult?> LoginAsync(string username, string password)
    {
        username = username.Trim();
        if (string.IsNullOrWhiteSpace(username)) return null;

        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        var user = await db.Users.AsNoTracking().Include(x => x.Department)
            .FirstOrDefaultAsync(x => x.Username == username && x.IsActive && !x.IsDeleted);
        if (user is null) return null;

        if (_settings.Authentication.Ldap.Enabled &&
            !_settings.Authentication.Mode.Equals(AuthenticationModes.LocalOnly, StringComparison.OrdinalIgnoreCase))
        {
            if (await ValidateLdapCredentialsAsync(username, password))
            {
                return new AuthenticationResult
                {
                    User = user,
                    AuthMethod = AuthMethods.Ldap,
                    WindowsIdentity = AuditEnvironment.WindowsIdentityName
                };
            }
        }

        if (_settings.Authentication.Mode.Equals(AuthenticationModes.WindowsOnly, StringComparison.OrdinalIgnoreCase))
            return null;
        if (!_settings.Authentication.AllowLocalFallback &&
            _settings.Authentication.Mode.Equals(AuthenticationModes.WindowsPreferred, StringComparison.OrdinalIgnoreCase))
            return null;
        if (string.IsNullOrWhiteSpace(user.PasswordHash)) return null;
        if (!PasswordHasher.Verify(password, user.PasswordHash)) return null;

        // Accounts migrated from legacy GGP Control keep their old SHA-256 password for the
        // first login only. After a successful verification, transparently upgrade the hash
        // to the normal DCR PBKDF2 format without asking the user to change password.
        if (PasswordHasher.IsLegacyHash(user.PasswordHash))
        {
            var trackedUser = await db.Users.SingleAsync(x => x.UserId == user.UserId);
            trackedUser.PasswordHash = PasswordHasher.Hash(password);
            await db.SaveChangesAsync();
        }

        return new AuthenticationResult
        {
            User = user,
            AuthMethod = AuthMethods.InternalPassword,
            WindowsIdentity = AuditEnvironment.WindowsIdentityName
        };
    }

    public async Task ChangePasswordAsync(int userId, string currentPassword, string newPassword)
    {
        currentPassword ??= string.Empty;
        newPassword ??= string.Empty;

        if (string.IsNullOrWhiteSpace(currentPassword))
            throw new InvalidOperationException("Vui lòng nhập mật khẩu DCR hiện tại.");
        if (string.IsNullOrWhiteSpace(newPassword))
            throw new InvalidOperationException("Vui lòng nhập mật khẩu mới.");
        if (newPassword.Length < 8)
            throw new InvalidOperationException("Mật khẩu mới phải có ít nhất 8 ký tự.");
        if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
            throw new InvalidOperationException("Mật khẩu mới phải khác mật khẩu hiện tại.");

        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        var user = await db.Users.SingleOrDefaultAsync(x => x.UserId == userId && x.IsActive && !x.IsDeleted)
            ?? throw new UnauthorizedAccessException("Tài khoản không tồn tại hoặc đã bị khóa.");

        if (string.IsNullOrWhiteSpace(user.PasswordHash) || !PasswordHasher.Verify(currentPassword, user.PasswordHash))
            throw new UnauthorizedAccessException("Mật khẩu DCR hiện tại không đúng.");

        user.PasswordHash = PasswordHasher.Hash(newPassword);
        await db.SaveChangesAsync();
    }

    public Task<DecisionAuthentication> AuthenticateDecisionAsync(int userId)
    {
        if (!CurrentUser.IsAuthenticated || CurrentUser.User?.UserId != userId ||
            string.IsNullOrWhiteSpace(CurrentUser.SessionId))
            throw new UnauthorizedAccessException("Phiên đăng nhập không hợp lệ. Vui lòng đăng nhập lại.");

        return AuthenticateDecisionForSessionAsync(userId, CurrentUser.WindowsIdentity);
    }

    // API callers obtain userId and identity from their validated access-token session.
    public async Task<DecisionAuthentication> AuthenticateDecisionForSessionAsync(int userId, string windowsIdentity)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        if (!await db.Users.AsNoTracking().AnyAsync(x => x.UserId == userId && x.IsActive && !x.IsDeleted))
            throw new UnauthorizedAccessException("Tài khoản không tồn tại hoặc đã bị khóa.");

        return new DecisionAuthentication
        {
            AuthMethod = AuthMethods.ApplicationSession,
            AuthenticatedAt = DateTime.Now,
            WindowsIdentity = windowsIdentity ?? string.Empty
        };
    }

    private Task<bool> ValidateLdapCredentialsAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(password)) return Task.FromResult(false);
        var cfg = _settings.Authentication.Ldap;
        if (!cfg.Enabled || string.IsNullOrWhiteSpace(cfg.Host)) return Task.FromResult(false);

        return Task.Run(() =>
        {
            try
            {
                var identifier = new LdapDirectoryIdentifier(cfg.Host.Trim(), cfg.Port, false, false);
                using var connection = new LdapConnection(identifier)
                {
                    AuthType = AuthType.Negotiate,
                    Timeout = TimeSpan.FromSeconds(12)
                };
                connection.SessionOptions.ProtocolVersion = 3;
                connection.SessionOptions.SecureSocketLayer = cfg.UseSsl;
                var credential = string.IsNullOrWhiteSpace(cfg.Domain)
                    ? new NetworkCredential(username, password)
                    : new NetworkCredential(username, password, cfg.Domain.Trim());
                connection.Bind(credential);
                return true;
            }
            catch (LdapException)
            {
                return false;
            }
            catch (DirectoryOperationException)
            {
                return false;
            }
        });
    }

    private bool IsAllowedDomain(string identityName)
    {
        var allowed = _settings.Authentication.AllowedWindowsDomain.Trim();
        if (string.IsNullOrWhiteSpace(allowed)) return true;
        var slash = identityName.IndexOf('\\');
        var domain = slash > 0 ? identityName[..slash] : string.Empty;
        return domain.Equals(allowed, StringComparison.OrdinalIgnoreCase);
    }
}
