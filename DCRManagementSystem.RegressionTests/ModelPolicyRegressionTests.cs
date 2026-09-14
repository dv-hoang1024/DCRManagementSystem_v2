using DCRManagementSystem.Data;
using DCRManagementSystem.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace DCRManagementSystem.RegressionTests;

public sealed class ModelPolicyRegressionTests
{
    [Theory]
    [InlineData("A", "A")]
    [InlineData("b", "B")]
    [InlineData("unknown", "C")]
    [InlineData("", "C")]
    public void DcrRankNormalization_RemainsCompatible(string input, string expected)
    {
        Assert.Equal(expected, DcrRanks.Normalize(input));
    }

    [Theory]
    [InlineData("General", AttachmentTypes.General)]
    [InlineData("TechnicalDocument", AttachmentTypes.SupportingDocument)]
    [InlineData("Drawing", AttachmentTypes.PartIllustration)]
    [InlineData("Photo", AttachmentTypes.PartIllustration)]
    public void AttachmentTypeNormalization_PreservesDesktopAndWebCompatibility(string input, string expected)
    {
        Assert.Equal(expected, AttachmentTypes.Normalize(input));
    }

    [Fact]
    public void DcrRequestRowVersion_RemainsAnOptimisticConcurrencyToken()
    {
        using var db = CreateDb();
        var entity = db.Model.FindEntityType(typeof(DCRRequest));
        var property = entity?.FindProperty(nameof(DCRRequest.RowVersion));

        Assert.NotNull(property);
        Assert.True(property!.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, property.ValueGenerated);
    }

    [Fact]
    public void ModulePermissions_RemainIndependentBooleanFieldsOnUser()
    {
        var user = new User
        {
            Role = RoleNames.Staff,
            CanUseProductionTracking = true,
            CanUseWarehouseManagement = false
        };

        Assert.True(user.CanUseProductionTracking);
        Assert.False(user.CanUseWarehouseManagement);
        Assert.Equal(RoleNames.Staff, user.Role);
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("model-policy-" + Guid.NewGuid().ToString("N"))
            .Options;
        return new AppDbContext(options);
    }
}
