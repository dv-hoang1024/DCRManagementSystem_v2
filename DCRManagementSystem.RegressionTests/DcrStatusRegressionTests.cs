using DCRManagementSystem.Models;
using DCRManagementSystem.Services;
using WebDashboard.Models;
using Xunit;

namespace DCRManagementSystem.RegressionTests;

public sealed class DcrStatusRegressionTests
{
    [Theory]
    [InlineData(RequestStatuses.Draft, true)]
    [InlineData(RequestStatuses.Returned, true)]
    [InlineData(RequestStatuses.Rejected, false)]
    [InlineData(RequestStatuses.InApproval, false)]
    [InlineData(RequestStatuses.Approved, false)]
    [InlineData(RequestStatuses.Cancelled, false)]
    [InlineData(RequestStatuses.Expired, false)]
    public void WebAndDesktopShareEditAndDeleteRules(string status, bool editable)
    {
        Assert.Equal(editable, RequestStatuses.IsEditable(status));
        Assert.Equal(editable, DcrStatusPolicy.IsEditable(status));
        foreach (var isAdmin in new[] { true, false })
        foreach (var actor in new[] { 1, 2 })
            Assert.Equal(DcrDeletionPolicy.CanDelete(status, 1, actor, isAdmin),
                DcrStatusPolicy.CanDelete(status, 1, actor, isAdmin));
    }
}
