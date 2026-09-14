using DCRManagementSystem.Models;
using DCRManagementSystem.Services;
using Xunit;

namespace DCRManagementSystem.RegressionTests;

public sealed class DcrCreationPolicyRegressionTests
{
    [Theory]
    [InlineData(RoleNames.Director)]
    [InlineData(RoleNames.CTO)]
    [InlineData(RoleNames.COO)]
    [InlineData(RoleNames.DCEO)]
    [InlineData(RoleNames.CEO)]
    [InlineData(RoleNames.Administrator)]
    public void DirectorOrAboveCanCreateWithoutDepartment(string role)
    {
        Assert.True(DcrCreationPolicy.CanCreateWithoutAssignedDepartment(role, roleLevel: null, directorLevel: 30));
    }

    [Fact]
    public void CustomRoleAtDirectorLevelCanCreateWithoutDepartment()
    {
        Assert.True(DcrCreationPolicy.CanCreateWithoutAssignedDepartment("Plant Head", roleLevel: 35, directorLevel: 30));
    }

    [Fact]
    public void StaffWithoutDepartmentCannotStartDraft()
    {
        Assert.Throws<InvalidOperationException>(() =>
            DcrCreationPolicy.EnsureCanStartDraft(departmentId: null, RoleNames.Staff, roleLevel: 10, directorLevel: 30));
    }

    [Fact]
    public void AnyRoleWithDepartmentCanStartDraft()
    {
        DcrCreationPolicy.EnsureCanStartDraft(departmentId: 5, RoleNames.Staff, roleLevel: 10, directorLevel: 30);
    }
}
