namespace DCRManagementSystem.Models;

public sealed class DcrAlreadyCreatedException(int requestId) : InvalidOperationException(
    "DCR đã được lưu từ lần gửi trước. Vui lòng kiểm tra bản đã lưu trước khi tiếp tục.")
{
    public int RequestId { get; } = requestId;
}
