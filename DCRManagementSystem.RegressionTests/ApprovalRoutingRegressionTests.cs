using DCRManagementSystem.Data;
using DCRManagementSystem.Models;
using DCRManagementSystem.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DCRManagementSystem.RegressionTests;

public sealed class ApprovalRoutingRegressionTests
{
    [Fact]
    public async Task DepartmentSpecificRule_TakesPrecedenceOverGlobalRule()
    {
        await using var db = CreateDb();
        db.Users.AddRange(
            ActiveUser(100, 10),
            ActiveUser(200, 20));
        db.ApprovalMatrixRules.AddRange(
            SpecificUserRule(1, "TEST_STAGE", 10, 100, 100),
            SpecificUserRule(2, "TEST_STAGE", null, 200, 1));
        await db.SaveChangesAsync();

        var service = new ApprovalRoutingService();
        var result = await service.ResolveAsync(
            db,
            new DCRRequest { RequestingDepartmentId = 10 },
            new WorkflowStageTemplate { StageCode = "TEST_STAGE" });

        var assignment = Assert.Single(result);
        Assert.Equal(100, assignment.ApproverId);
        Assert.Equal(10, assignment.DepartmentId);
    }

    [Fact]
    public async Task GlobalRule_IsUsedWhenNoDepartmentSpecificRuleExists()
    {
        await using var db = CreateDb();
        db.Users.Add(ActiveUser(200, 20));
        db.ApprovalMatrixRules.Add(SpecificUserRule(2, "TEST_STAGE", null, 200, 1));
        await db.SaveChangesAsync();

        var service = new ApprovalRoutingService();
        var result = await service.ResolveAsync(
            db,
            new DCRRequest { RequestingDepartmentId = 10 },
            new WorkflowStageTemplate { StageCode = "TEST_STAGE" });

        Assert.Equal(200, Assert.Single(result).ApproverId);
    }

    [Fact]
    public async Task InactiveSpecificApprover_IsRejected()
    {
        await using var db = CreateDb();
        var user = ActiveUser(100, 10);
        user.IsActive = false;
        db.Users.Add(user);
        db.ApprovalMatrixRules.Add(SpecificUserRule(1, "TEST_STAGE", 10, 100, 1));
        await db.SaveChangesAsync();

        var service = new ApprovalRoutingService();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResolveAsync(
            db,
            new DCRRequest { RequestingDepartmentId = 10 },
            new WorkflowStageTemplate { StageCode = "TEST_STAGE" }));

        Assert.Contains("specific approver", ex.Message.ToLowerInvariant());
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("approval-routing-" + Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

    private static User ActiveUser(int id, int departmentId) => new()
    {
        UserId = id,
        Username = "user" + id,
        FullName = "Regression User " + id,
        DepartmentId = departmentId,
        Role = RoleNames.Manager,
        IsActive = true,
        IsDeleted = false
    };

    private static ApprovalMatrixRule SpecificUserRule(
        int id,
        string stage,
        int? requestingDepartmentId,
        int userId,
        int priority) => new()
    {
        Id = id,
        StageCode = stage,
        RequestingDepartmentId = requestingDepartmentId,
        ApproverSource = ApproverSources.SpecificUser,
        ApproverUserId = userId,
        Priority = priority,
        IsActive = true
    };
}
