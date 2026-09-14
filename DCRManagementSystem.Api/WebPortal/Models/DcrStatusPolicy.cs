namespace DCRManagementSystem.Api.WebPortal.Models;

public static class DcrStatusPolicy
{
    public static bool IsEditable(string? status) =>
        string.Equals(status, "Draft", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "Returned", StringComparison.OrdinalIgnoreCase);

    public static bool CanDelete(string? status, int createdBy, int? actorUserId, bool isAdmin) =>
        isAdmin || (createdBy == actorUserId &&
            (IsEditable(status) || string.Equals(status, "Rejected", StringComparison.OrdinalIgnoreCase)));
}
