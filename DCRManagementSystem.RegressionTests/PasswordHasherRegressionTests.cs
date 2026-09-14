using System.Security.Cryptography;
using System.Text;
using DCRManagementSystem.Services;
using Xunit;

namespace DCRManagementSystem.RegressionTests;

public sealed class PasswordHasherRegressionTests
{
    [Fact]
    public void CurrentPbkdf2Hash_VerifiesCorrectPassword_AndRejectsWrongPassword()
    {
        const string password = "Dcr-Test@2026";
        var hash = PasswordHasher.Hash(password);

        Assert.True(PasswordHasher.Verify(password, hash));
        Assert.False(PasswordHasher.Verify("wrong-password", hash));
        Assert.False(PasswordHasher.IsLegacyHash(hash));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacySha256Hash_RemainsReadableForOneTimeMigration(bool withPrefix)
    {
        const string password = "legacy-password";
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password))).ToLowerInvariant();
        var stored = withPrefix ? "legacy-sha256$" + digest : digest;

        Assert.True(PasswordHasher.IsLegacyHash(stored));
        Assert.True(PasswordHasher.Verify(password, stored));
        Assert.False(PasswordHasher.Verify("wrong-password", stored));
    }

    [Fact]
    public void MalformedHash_IsRejectedWithoutThrowing()
    {
        Assert.False(PasswordHasher.Verify("test", "120000.not-base64.not-base64"));
        Assert.False(PasswordHasher.Verify("test", string.Empty));
    }
}
