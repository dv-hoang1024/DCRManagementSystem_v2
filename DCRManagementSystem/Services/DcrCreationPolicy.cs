using DCRManagementSystem.Models;

namespace DCRManagementSystem.Services;

public static class DcrCreationPolicy
{
    private static readonly HashSet<string> KnownDirectorOrAboveRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        RoleNames.Administrator,
        RoleNames.Director,
        RoleNames.CTO,
        RoleNames.COO,
        RoleNames.DCEO,
        RoleNames.CEO
    };

    public static bool CanCreateWithoutAssignedDepartment(string? role, int? roleLevel, int directorLevel) =>
        KnownDirectorOrAboveRoles.Contains((role ?? string.Empty).Trim()) ||
        (roleLevel.HasValue && roleLevel.Value >= directorLevel);

    public static void EnsureCanStartDraft(int? departmentId, string? role, int? roleLevel, int directorLevel)
    {
        if (departmentId is > 0 || CanCreateWithoutAssignedDepartment(role, roleLevel, directorLevel))
            return;

        throw new InvalidOperationException(
            "Tài khoản chưa được gán phòng ban. Chỉ người dùng cấp Director trở lên được chọn Phòng ban yêu cầu khi tạo DCR.");
    }
}
