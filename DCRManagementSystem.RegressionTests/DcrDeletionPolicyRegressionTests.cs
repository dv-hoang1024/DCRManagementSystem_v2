using DCRManagementSystem.Models;
using DCRManagementSystem.Services;
using Xunit;

namespace DCRManagementSystem.RegressionTests;

public sealed class DcrDeletionPolicyRegressionTests
{
    [Theory]
    [InlineData(RequestStatuses.Draft)]
    [InlineData(RequestStatuses.Returned)]
    [InlineData(RequestStatuses.Rejected)]
    public void CreatorCanDeleteOnlyEligibleStatuses(string status)
    {
        Assert.True(DcrDeletionPolicy.CanDelete(status, createdBy: 7, actorUserId: 7, isAdmin: false));
    }

    [Theory]
    [InlineData(RequestStatuses.InApproval)]
    [InlineData(RequestStatuses.Approved)]
    [InlineData(RequestStatuses.Cancelled)]
    [InlineData(RequestStatuses.Expired)]
    [InlineData(RequestStatuses.Returned)]
    public void AdministratorCanDeleteOtherUsersDcrInEveryStatus(string status)
    {
        Assert.True(DcrDeletionPolicy.CanDelete(status, createdBy: 7, actorUserId: 1, isAdmin: true));
        DcrDeletionPolicy.EnsureCanDelete(status, createdBy: 7, actorUserId: 1, isAdmin: true);
    }

    [Theory]
    [InlineData(RequestStatuses.InApproval)]
    [InlineData(RequestStatuses.Approved)]
    [InlineData(RequestStatuses.Cancelled)]
    [InlineData(RequestStatuses.Expired)]
    public void CreatorStillCannotDeleteSubmittedOrClosedDcr(string status)
    {
        Assert.False(DcrDeletionPolicy.CanDelete(status, 7, 7, false));
        Assert.Throws<InvalidOperationException>(() => DcrDeletionPolicy.EnsureCanDelete(status, 7, 7, false));
    }

    [Fact]
    public void AnotherUserCannotDeleteCreatorsDraft()
    {
        Assert.False(DcrDeletionPolicy.CanDelete(RequestStatuses.Draft, createdBy: 7, actorUserId: 8, isAdmin: false));
        Assert.Throws<UnauthorizedAccessException>(() =>
            DcrDeletionPolicy.EnsureCanDelete(RequestStatuses.Draft, createdBy: 7, actorUserId: 8, isAdmin: false));
    }

    [Fact]
    public void AdministratorCanDeleteEligibleDcrOwnedByAnotherUser()
    {
        Assert.True(DcrDeletionPolicy.CanDelete(RequestStatuses.Rejected, createdBy: 7, actorUserId: 1, isAdmin: true));
    }
}
