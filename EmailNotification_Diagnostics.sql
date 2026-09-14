USE [DCRManagement];
GO

-- 1) Shared SMTP settings. Password is stored encrypted.
SELECT [Key],
       CASE WHEN [Key] LIKE '%Password%' THEN '***ENCRYPTED***' ELSE [Value] END AS [Value],
       UpdatedAt
FROM SystemSettings
WHERE [Key] LIKE 'Email.%'
ORDER BY [Key];
GO

-- 2) Replace the DCR number below to verify exactly which user/email is active at the current stage.
DECLARE @DcrNumber nvarchar(40) = N'DCR2026-00003';

SELECT r.DCRNumber,
       r.RevisionNo,
       r.CurrentStage,
       af.StageNumber,
       af.StageCode,
       af.StageName,
       af.Decision,
       af.ApproverId,
       u.Username,
       u.FullName,
       u.Email,
       af.AssignedDate,
       af.DueDate
FROM DCRRequests r
JOIN DCRApprovalFlows af
  ON af.RequestId = r.Id
 AND af.RevisionNo = r.RevisionNo
LEFT JOIN Users u ON u.UserId = af.ApproverId
WHERE r.DCRNumber = @DcrNumber
  AND af.StageNumber = r.CurrentStage
ORDER BY af.Sequence, af.Id;
GO

-- 3) Latest email/notification delivery results.
SELECT TOP (100)
       n.Id,
       r.DCRNumber,
       n.NotificationType,
       n.Recipient,
       n.SentAt,
       n.Success,
       n.Details
FROM DCRNotificationLogs n
JOIN DCRRequests r ON r.Id = n.RequestId
ORDER BY n.Id DESC;
GO
