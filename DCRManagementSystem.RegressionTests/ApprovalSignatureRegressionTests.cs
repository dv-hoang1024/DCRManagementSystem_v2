using DCRManagementSystem.Helpers;
using DCRManagementSystem.Services;
using Xunit;

namespace DCRManagementSystem.RegressionTests;

public sealed class ApprovalSignatureRegressionTests
{
    [Fact]
    public void Signature_VerifiesOnlyForExactApprovalPayload()
    {
        var settings = new AppSettings();
        settings.Security.ApprovalSigningKeyBase64 = Convert.ToBase64String(
            Enumerable.Range(1, 64).Select(x => (byte)(255 - x)).ToArray());
        settings.InitializeRuntimeSecrets();
        var service = new ApprovalSignatureService(settings);
        var authenticatedAt = new DateTime(2026, 8, 29, 3, 30, 0, DateTimeKind.Utc);

        var signature = service.Sign(1001, 2, 3, 55, "Approved", "checked", authenticatedAt, "GGI\\approver");

        Assert.True(service.Verify(1001, 2, 3, 55, "Approved", "checked", authenticatedAt, "GGI\\approver", signature));
        Assert.False(service.Verify(1001, 2, 3, 55, "Rejected", "checked", authenticatedAt, "GGI\\approver", signature));
        Assert.False(service.Verify(1001, 2, 3, 56, "Approved", "checked", authenticatedAt, "GGI\\approver", signature));
        Assert.False(service.Verify(1001, 2, 3, 55, "Approved", "changed", authenticatedAt, "GGI\\approver", signature));
    }
}
