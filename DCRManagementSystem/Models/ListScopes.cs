namespace DCRManagementSystem.Models;

public static class DcrListScopes
{
    public const string PendingMyApproval = "Chờ tôi duyệt";
    public const string RelatedToMe = "DCR liên quan đến tôi";
    public const string MyRequests = "Tôi đã tạo";
    public const string Approved = "Đã hoàn thành";
    public const string Rejected = "Đã từ chối";
    public const string All = "Tất cả";

    public static readonly string[] AllValues =
    {
        PendingMyApproval,
        RelatedToMe,
        MyRequests,
        Approved,
        Rejected,
        All
    };
}
