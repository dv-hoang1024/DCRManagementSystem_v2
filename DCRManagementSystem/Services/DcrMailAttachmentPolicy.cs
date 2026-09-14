using DCRManagementSystem.Models;

namespace DCRManagementSystem.Services;

public static class DcrMailAttachmentPolicy
{
    public const string LegacyUnavailableNotice =
        "PDF DCR: chưa tạo được bản đính kèm tự động. Vui lòng mở DCR trong hệ thống.";

    private static readonly HashSet<string> RequiredNotificationTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        NotificationTypes.ApprovalAssigned,
        NotificationTypes.Reminder,
        NotificationTypes.Escalation,
        NotificationTypes.Returned,
        NotificationTypes.Rejected,
        NotificationTypes.Approved
    };

    public static bool RequiresAutomaticPdf(int? requestId, string? notificationType) =>
        requestId is > 0 &&
        RequiredNotificationTypes.Contains((notificationType ?? string.Empty).Trim());

    public static string RemoveLegacyUnavailableNotice(string? body)
    {
        var value = body ?? string.Empty;
        return value
            .Replace(LegacyUnavailableNotice + "\r\n", string.Empty, StringComparison.Ordinal)
            .Replace(LegacyUnavailableNotice + "\n", string.Empty, StringComparison.Ordinal)
            .Replace(LegacyUnavailableNotice, string.Empty, StringComparison.Ordinal);
    }
}
