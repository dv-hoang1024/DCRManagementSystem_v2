using DCRManagementSystem.Api;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DCRManagementSystem.RegressionTests;

public sealed class ApiTokenRegressionTests
{
    [Fact]
    public void IssuedAccessToken_PreservesAuthenticationMethodAndSession()
    {
        WithTokenSecret(() =>
        {
            var service = CreateService();
            const string sessionId = "regression-session-001";
            var issued = service.IssueAccess(
                42, "user.test", "Manager", "LDAP", "GGI\\user.test", "TEST-PC", sessionId);

            Assert.True(service.TryValidate(issued.Token, ApiTokenService.AccessPurpose, out var payload));
            Assert.Equal(42, payload.UserId);
            Assert.Equal("user.test", payload.Username);
            Assert.Equal("LDAP", payload.AuthMethod);
            Assert.Equal(sessionId, payload.SessionId);
            Assert.Equal("GGI\\user.test", payload.WindowsIdentity);
        });
    }

    [Fact]
    public void TokenPurpose_IsEnforced()
    {
        WithTokenSecret(() =>
        {
            var service = CreateService();
            var issued = service.IssueRefresh(7, "user", "Staff", "InternalPassword", string.Empty, "TEST-PC", "sid");

            Assert.True(service.TryValidate(issued.Token, ApiTokenService.RefreshPurpose, out _));
            Assert.False(service.TryValidate(issued.Token, ApiTokenService.AccessPurpose, out _));
        });
    }

    [Fact]
    public void TamperedToken_IsRejected()
    {
        WithTokenSecret(() =>
        {
            var service = CreateService();
            var issued = service.IssueAccess(7, "user", "Staff", "InternalPassword", string.Empty, "TEST-PC", "sid");
            var chars = issued.Token.ToCharArray();
            var index = issued.Token.IndexOf('.');
            var changeAt = Math.Max(0, index - 2);
            chars[changeAt] = chars[changeAt] == 'A' ? 'B' : 'A';

            Assert.False(service.TryValidate(new string(chars), ApiTokenService.AccessPurpose, out _));
        });
    }

    private static ApiTokenService CreateService()
    {
        var values = new Dictionary<string, string?>
        {
            ["ApiServer:AccessTokenHours"] = "2",
            ["ApiServer:RefreshTokenDays"] = "30",
            ["ApiServer:DecisionProofMinutes"] = "5"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new ApiTokenService(configuration);
    }

    private static void WithTokenSecret(Action action)
    {
        var previous = Environment.GetEnvironmentVariable("DCR_API_TOKEN_SECRET_BASE64");
        try
        {
            Environment.SetEnvironmentVariable(
                "DCR_API_TOKEN_SECRET_BASE64",
                Convert.ToBase64String(Enumerable.Range(1, 64).Select(x => (byte)x).ToArray()));
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable("DCR_API_TOKEN_SECRET_BASE64", previous);
        }
    }
}
