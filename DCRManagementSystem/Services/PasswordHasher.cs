using System.Security.Cryptography;
using System.Text;

namespace DCRManagementSystem.Services;

public static class PasswordHasher
{
    private const string LegacyPrefix = "legacy-sha256$";
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 120_000;

    public static string Hash(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Mật khẩu không được để trống.", nameof(password));
        }

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            KeySize);

        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public static bool IsLegacyHash(string passwordHash) =>
        TryReadLegacySha256(passwordHash, out _);

    public static bool Verify(string password, string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(passwordHash))
        {
            return false;
        }

        // Older GGP Control records contain a plain 64-character SHA-256 hex value.
        // The DCR migration tool normally adds "legacy-sha256$", but accounts that
        // already existed or were copied manually can still contain the unprefixed
        // value. Accept both representations once, then AuthService upgrades the
        // successful login to the current PBKDF2 format.
        if (TryReadLegacySha256(passwordHash, out var expectedLegacy))
        {
            var actualLegacy = SHA256.HashData(Encoding.UTF8.GetBytes(password));
            return CryptographicOperations.FixedTimeEquals(actualLegacy, expectedLegacy);
        }

        var parts = passwordHash.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations))
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[1]);
            var expected = Convert.FromBase64String(parts[2]);

            var actual = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expected.Length);

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadLegacySha256(string passwordHash, out byte[] digest)
    {
        digest = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(passwordHash)) return false;

        var value = passwordHash.Trim();
        if (value.StartsWith(LegacyPrefix, StringComparison.OrdinalIgnoreCase))
            value = value[LegacyPrefix.Length..].Trim();

        if (value.Length != 64 || !value.All(Uri.IsHexDigit)) return false;

        try
        {
            digest = Convert.FromHexString(value);
            return digest.Length == 32;
        }
        catch (FormatException)
        {
            digest = Array.Empty<byte>();
            return false;
        }
    }
}
