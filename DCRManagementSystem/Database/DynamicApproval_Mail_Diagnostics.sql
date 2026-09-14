USE [DCRManagement];
GO

/*
   Diagnostics for dynamic N-level approval, co-approval and Mail Worker.
   Change @DcrNumber before running.
*/
DECLARE @DcrNumber nvarchar(40) = N'DCR2026-00004';
DECLARE @RequestId int = (SELECT TOP (1) Id FROM dbo.DCRRequests WHERE DCRNumber = @DcrNumber);

IF @RequestId IS NULL
BEGIN
    THROW 50001, 'DCR number not found.', 1;
END;

SELECT
    r.Id,
    r.DCRNumber,
    r.Title,
    r.Status,
    r.RevisionNo,
    r.CurrentStage,
    r.CreatedBy,
    creator.FullName AS CreatorName,
    creator.Email AS CreatorEmail,
    r.CompletedDate
FROM dbo.DCRRequests r
LEFT JOIN dbo.Users creator ON creator.UserId = r.CreatedBy
WHERE r.Id = @RequestId;

SELECT
    p.LevelNumber,
    p.LevelName,
    p.Sequence,
    u.UserId AS ApproverId,
    u.FullName AS Approver,
    u.Email,
    d.DepartmentName
FROM dbo.DCRApprovalPlanEntries p
INNER JOIN dbo.Users u ON u.UserId = p.ApproverId
LEFT JOIN dbo.Departments d ON d.Id = u.DepartmentId
WHERE p.RequestId = @RequestId
ORDER BY p.LevelNumber, p.Sequence, p.Id;

SELECT
    f.Id AS ApprovalFlowId,
    f.RevisionNo,
    f.StageNumber,
    f.StageCode,
    f.StageName,
    f.Sequence,
    f.Decision,
    f.AssignedDate,
    f.DecisionDate,
    u.FullName AS Approver,
    u.Email,
    f.Comments
FROM dbo.DCRApprovalFlows f
LEFT JOIN dbo.Users u ON u.UserId = f.ApproverId
WHERE f.RequestId = @RequestId
ORDER BY f.RevisionNo, f.StageNumber, f.Sequence, f.Id;

SELECT
    o.Id AS OutboxId,
    o.ApprovalFlowId,
    o.NotificationType,
    o.Recipient,
    o.Subject,
    o.Status,
    o.RetryCount,
    o.CreatedAt,
    o.LastAttemptAt,
    o.SentAt,
    o.SenderAccount,
    o.LastError,
    o.AttachmentFileName
FROM dbo.EmailOutbox o
WHERE o.RequestId = @RequestId
ORDER BY o.Id;

SELECT
    l.Id AS NotificationLogId,
    l.ApprovalFlowId,
    l.NotificationType,
    l.Recipient,
    l.SentAt,
    l.Success,
    l.Details
FROM dbo.DCRNotificationLogs l
WHERE l.RequestId = @RequestId
ORDER BY l.Id;

SELECT [Key], [Value], UpdatedAt
FROM dbo.SystemSettings
WHERE [Key] LIKE N'MailWorker.%' OR [Key] LIKE N'Email.%'
ORDER BY [Key];
GO
