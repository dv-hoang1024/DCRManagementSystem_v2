using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;

namespace DCRManagementSystem.Services;

/// <summary>
/// Stores the association between the current Windows profile and a DCR user only
/// after a successful credential login. The payload is protected with Windows DPAPI
/// CurrentUser scope, so copying a published package to another PC cannot auto-login
/// as an arbitrary DCR account.
/// </summary>
public sealed class RememberedWindowsLoginService
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DCRManagementSystem.RememberedWindowsLogin.v1");

    public void Remember(User user, string refreshToken = "")
    {
        ArgumentNullException.ThrowIfNull(user);
        using var identity = WindowsIdentity.GetCurrent();
        var identityName = identity?.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(identityName) || identity?.IsAnonymous == true)
            throw new InvalidOperationException("Không xác định được tài khoản Windows hiện tại để ghi nhớ đăng nhập.");

        var record = new RememberedWindowsLoginRecord
        {
            UserId = user.UserId,
            Username = user.Username,
            WindowsIdentity = identityName,
            MachineName = Environment.MachineName,
            RememberedAtUtc = DateTime.UtcNow,
            RefreshToken = refreshToken ?? string.Empty
        };

        var plain = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record));
        var encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(ApplicationDataPaths.RememberedWindowsLoginPath)!);
        var temp = ApplicationDataPaths.RememberedWindowsLoginPath + ".tmp";
        File.WriteAllBytes(temp, encrypted);
        File.Move(temp, ApplicationDataPaths.RememberedWindowsLoginPath, true);
    }

    public RememberedWindowsLoginRecord? TryLoadForCurrentWindowsUser()
    {
        if (!File.Exists(ApplicationDataPaths.RememberedWindowsLoginPath))
            return null;

        try
        {
            var encrypted = File.ReadAllBytes(ApplicationDataPaths.RememberedWindowsLoginPath);
            var plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            var record = JsonSerializer.Deserialize<RememberedWindowsLoginRecord>(plain);
            if (record is null || record.UserId <= 0 || string.IsNullOrWhiteSpace(record.WindowsIdentity))
                return null;

            using var identity = WindowsIdentity.GetCurrent();
            var currentIdentity = identity?.Name?.Trim() ?? string.Empty;
            if (!record.WindowsIdentity.Equals(currentIdentity, StringComparison.OrdinalIgnoreCase))
                return null;
            if (!record.MachineName.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
                return null;

            return record;
        }
        catch (CryptographicException)
        {
            // The file belongs to another Windows profile or was corrupted. Do not trust it.
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public bool IsRememberedForCurrentWindowsUser(int userId)
        => TryLoadForCurrentWindowsUser()?.UserId == userId;

    public void Forget()
    {
        try
        {
            if (File.Exists(ApplicationDataPaths.RememberedWindowsLoginPath))
                File.Delete(ApplicationDataPaths.RememberedWindowsLoginPath);
        }
        catch (IOException)
        {
            // Best effort. Login itself must remain usable even if cleanup fails.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort.
        }
    }
}

public sealed class RememberedWindowsLoginRecord
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string WindowsIdentity { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public DateTime RememberedAtUtc { get; set; }
    public string RefreshToken { get; set; } = string.Empty;
}
