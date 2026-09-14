using System.Security.Cryptography;
using System.Text;
using DCRManagementSystem.Helpers;

namespace DCRManagementSystem.Services;

public sealed class ApprovalSignatureService
{
    private readonly byte[] _key;

    public ApprovalSignatureService(AppSettings settings)
    {
        _key = settings.SigningKeyBytes;
        if (_key.Length < 32)
        {
            throw new InvalidOperationException("Approval signing key chưa được khởi tạo.");
        }
    }

    public string Sign(
        int requestId,
        int revisionNo,
        int stageNumber,
        int userId,
        string decision,
        string comment,
        DateTime authenticatedAt,
        string windowsIdentity)
    {
        var payload = string.Join("|",
            requestId,
            revisionNo,
            stageNumber,
            userId,
            decision.Trim(),
            comment.Trim(),
            authenticatedAt.ToUniversalTime().ToString("O"),
            windowsIdentity.Trim());

        using var hmac = new HMACSHA256(_key);
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
    public bool Verify(
        int requestId,
        int revisionNo,
        int stageNumber,
        int userId,
        string decision,
        string comment,
        DateTime authenticatedAt,
        string windowsIdentity,
        string signatureHash)
    {
        if (string.IsNullOrWhiteSpace(signatureHash))
            return false;

        var expected = Sign(
            requestId,
            revisionNo,
            stageNumber,
            userId,
            decision,
            comment,
            authenticatedAt,
            windowsIdentity);

        try
        {
            var expectedBytes = Convert.FromHexString(expected);
            var actualBytes = Convert.FromHexString(signatureHash);
            return expectedBytes.Length == actualBytes.Length &&
                   CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }

}
