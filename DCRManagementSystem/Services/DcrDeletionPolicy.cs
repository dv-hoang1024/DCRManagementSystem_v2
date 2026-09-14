using DCRManagementSystem.Models;

namespace DCRManagementSystem.Services;

public static class DcrDeletionPolicy
{
    public static bool IsDeletableStatus(string? status) =>
        string.Equals(status, RequestStatuses.Draft, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, RequestStatuses.Returned, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, RequestStatuses.Rejected, StringComparison.OrdinalIgnoreCase);

    public static bool CanDelete(string? status, int createdBy, int actorUserId, bool isAdmin) =>
        isAdmin || (IsDeletableStatus(status) && createdBy == actorUserId);

    public static void EnsureCanDelete(string? status, int createdBy, int actorUserId, bool isAdmin)
    {
        if (isAdmin) return;

        if (!isAdmin && createdBy != actorUserId)
            throw new UnauthorizedAccessException("Bạn chỉ được xóa DCR do chính mình tạo.");

        if (!IsDeletableStatus(status))
        {
            throw new InvalidOperationException(
                "Người tạo chỉ được xóa DCR ở trạng thái Bản nháp, Trả về hoặc Bị từ chối. Hãy liên hệ Administrator để xóa DCR ở trạng thái khác.");
        }
    }
}
