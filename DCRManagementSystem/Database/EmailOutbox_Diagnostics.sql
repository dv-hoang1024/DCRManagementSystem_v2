USE [DCRManagement];
GO

SELECT TOP (100)
    Id,
    RequestId,
    ApprovalFlowId,
    NotificationType,
    Recipient,
    Subject,
    AttachmentFileName,
    AttachmentFilePath,
    AttachmentContentType,
    Status,
    RetryCount,
    MaxRetryCount,
    CreatedAt,
    NextAttemptAt,
    LastAttemptAt,
    SentAt,
    SenderAccount,
    LastError
FROM dbo.EmailOutbox
ORDER BY Id DESC;
GO

SELECT
    Status,
    COUNT(*) AS Qty
FROM dbo.EmailOutbox
GROUP BY Status
ORDER BY Status;
GO

SELECT [Key], [Value], UpdatedAt
FROM dbo.SystemSettings
WHERE [Key] LIKE N'Email.%'
   OR [Key] LIKE N'MailWorker.%'
ORDER BY [Key];
GO

SELECT TOP (100)
    Id,
    RequestId,
    ApprovalFlowId,
    NotificationType,
    Recipient,
    SentAt,
    Success,
    Details
FROM dbo.DCRNotificationLogs
ORDER BY Id DESC;
GO
