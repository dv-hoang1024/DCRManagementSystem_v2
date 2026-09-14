namespace DCRManagementSystem.Models;

public sealed class DcrConcurrencyException : InvalidOperationException
{
    public int RequestId { get; }

    public DcrConcurrencyException(int requestId, Exception? innerException = null)
        : base(
            "DCR đã được thay đổi bởi một người dùng hoặc tiến trình khác. " +
            "Bản dữ liệu hiện tại trên màn hình không còn là phiên bản mới nhất. " +
            "Hãy tải lại dữ liệu từ server trước khi tiếp tục.",
            innerException)
    {
        RequestId = requestId;
    }
}
