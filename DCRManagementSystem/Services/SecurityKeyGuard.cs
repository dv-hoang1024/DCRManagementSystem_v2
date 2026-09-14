using System.Security.Cryptography;
using DCRManagementSystem.Data;
using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;

namespace DCRManagementSystem.Services;

public static class SecurityKeyGuard
{
    private const string FingerprintSettingKey = "ApprovalSigningKeyFingerprintSha256";

    public static void EnsureSigningKeyMatches(AppDbContext db, AppSettings settings)
    {
        var key = settings.SigningKeyBytes;
        if (key.Length < 32)
            throw new InvalidOperationException("Approval signing key chưa được khởi tạo.");

        var fingerprint = Fingerprint(key);
        var setting = db.SystemSettings.SingleOrDefault(x => x.Key == FingerprintSettingKey);
        if (setting is null)
        {
            db.SystemSettings.Add(new SystemSetting
            {
                Key = FingerprintSettingKey,
                Value = fingerprint,
                UpdatedAt = DateTime.Now
            });
            db.SaveChanges();
            return;
        }

        var expectedFingerprint = setting.Value?.Trim() ?? string.Empty;
        if (FingerprintsEqual(expectedFingerprint, fingerprint))
            return;

        // Release/Publish often runs from another output directory. Try the packaged
        // provisioning key and legacy Debug/Release keys before refusing startup.
        if (settings.TryRepairSigningKey(expectedFingerprint))
        {
            var repairedFingerprint = Fingerprint(settings.SigningKeyBytes);
            if (FingerprintsEqual(expectedFingerprint, repairedFingerprint))
                return;
        }

        throw new InvalidOperationException(
            "Approval signing key của bản chạy hiện tại không khớp với database.\r\n\r\n" +
            $"Key đang dùng: {ShortFingerprint(fingerprint)}\r\n" +
            $"Key database:  {ShortFingerprint(expectedFingerprint)}\r\n" +
            $"Vị trí key ổn định: {settings.GetAbsoluteSecurityKeyPath()}\r\n\r\n" +
            "Không xóa fingerprint trong database và không tạo key mới vì các chữ ký approval cũ sẽ mất khả năng xác minh. " +
            "Hãy Publish bằng Scripts\\Publish-DCR.cmd để gói đúng signing key đã dùng khi Debug vào bản Release.");
    }

    private static string Fingerprint(byte[] key) =>
        Convert.ToHexString(SHA256.HashData(key)).ToLowerInvariant();

    private static bool FingerprintsEqual(string left, string right)
    {
        try
        {
            var leftBytes = Convert.FromHexString(left);
            var rightBytes = Convert.FromHexString(right);
            return leftBytes.Length == rightBytes.Length &&
                   CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string ShortFingerprint(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint)) return "(không hợp lệ)";
        var value = fingerprint.Trim();
        return value.Length <= 16 ? value : $"{value[..8]}...{value[^8..]}";
    }
}
