using DCRManagementSystem.Helpers;
using Xunit;

namespace DCRManagementSystem.RegressionTests;

public sealed class WebPortalRoutingRegressionTests
{
    [Fact]
    public void DefaultApprovalLinkUsesDedicatedDcrHostname()
    {
        var settings = new WebPortalSettings();

        Assert.Equal("https://dcr.ggpcontrol.cloud/request/42", settings.BuildRequestUrl(42));
    }

    [Fact]
    public void ApprovalLinkDoesNotReintroduceLegacyDcrPrefix()
    {
        var settings = new WebPortalSettings
        {
            BaseUrl = "https://dcr.ggpcontrol.cloud/"
        };

        var url = settings.BuildRequestUrl(7);

        Assert.Equal("https://dcr.ggpcontrol.cloud/request/7", url);
        Assert.DoesNotContain("/dcr/request", url, StringComparison.OrdinalIgnoreCase);
    }
}
