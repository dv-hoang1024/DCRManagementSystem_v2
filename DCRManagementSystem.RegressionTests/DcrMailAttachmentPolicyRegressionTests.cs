using DCRManagementSystem.Models;
using DCRManagementSystem.Services;
using Xunit;

namespace DCRManagementSystem.RegressionTests;

public sealed class DcrMailAttachmentPolicyRegressionTests
{
    [Theory]
    [InlineData(NotificationTypes.ApprovalAssigned)]
    [InlineData(NotificationTypes.Reminder)]
    [InlineData(NotificationTypes.Escalation)]
    [InlineData(NotificationTypes.Returned)]
    [InlineData(NotificationTypes.Rejected)]
    [InlineData(NotificationTypes.Approved)]
    public void WorkflowEmailRequiresAutomaticPdf(string notificationType)
    {
        Assert.True(DcrMailAttachmentPolicy.RequiresAutomaticPdf(48, notificationType));
    }

    [Fact]
    public void NonDcrOrPdfGeneratedEmailDoesNotRequireAutomaticPdf()
    {
        Assert.False(DcrMailAttachmentPolicy.RequiresAutomaticPdf(null, NotificationTypes.ApprovalAssigned));
        Assert.False(DcrMailAttachmentPolicy.RequiresAutomaticPdf(48, NotificationTypes.PdfGenerated));
    }

    [Fact]
    public void LegacyUnavailableNoticeIsRemovedAfterWorkerCreatesAttachment()
    {
        var body = $"Mở DCR: https://dcr.ggpcontrol.cloud/request/48\r\n{DcrMailAttachmentPolicy.LegacyUnavailableNotice}\r\n\r\nVui lòng xử lý.";

        var cleaned = DcrMailAttachmentPolicy.RemoveLegacyUnavailableNotice(body);

        Assert.DoesNotContain(DcrMailAttachmentPolicy.LegacyUnavailableNotice, cleaned);
        Assert.Contains("Mở DCR:", cleaned);
        Assert.Contains("Vui lòng xử lý.", cleaned);
    }
}
