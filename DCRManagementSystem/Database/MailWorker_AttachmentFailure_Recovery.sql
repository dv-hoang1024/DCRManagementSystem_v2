USE [DCRManagement];
GO

SET NOCOUNT ON;

PRINT N'--- Mail attachment failure rows before recovery ---';
SELECT
    Id,
    RequestId,
    ApprovalFlowId,
    NotificationType,
    Recipient,
    Status,
    RetryCount,
    MaxRetryCount,
    AttachmentFilePath,
    AttachmentFileName,
    LastAttemptAt,
    LastError
FROM dbo.EmailOutbox
WHERE Status = N'Failed'
  AND (
        LastError LIKE N'%PrimitiveValue%'
        OR LastError LIKE N'%Không tìm thấy file PDF%'
        OR LastError LIKE N'%Không tìm thấy PDF đính kèm%'
      )
ORDER BY Id DESC;

DECLARE @Recovered int = 0;

UPDATE dbo.EmailOutbox
SET
    Status = N'Pending',
    RetryCount = 0,
    NextAttemptAt = NULL,
    LastAttemptAt = NULL,
    SentAt = NULL,
    SenderAccount = N'',
    LastError = N'Requeued after Mail Worker PDF/Graph attachment fix.'
WHERE Status = N'Failed'
  AND (
        LastError LIKE N'%PrimitiveValue%'
        OR LastError LIKE N'%Không tìm thấy file PDF%'
        OR LastError LIKE N'%Không tìm thấy PDF đính kèm%'
      );

SET @Recovered = @@ROWCOUNT;
SELECT @Recovered AS FailedRowsRequeued;

PRINT N'--- Rows ready after recovery ---';
SELECT
    Id,
    RequestId,
    ApprovalFlowId,
    NotificationType,
    Recipient,
    Status,
    RetryCount,
    MaxRetryCount,
    AttachmentFilePath,
    AttachmentFileName,
    NextAttemptAt,
    LastError
FROM dbo.EmailOutbox
WHERE Status = N'Pending'
ORDER BY Id DESC;
GO
