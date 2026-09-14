USE [DCRManagement];
GO

DECLARE @DcrNumber nvarchar(40) = N'DCR2026-00004';
DECLARE @UserId int = NULL; -- optional: set an approver UserId to test related visibility

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
FROM DCRRequests r
LEFT JOIN Users creator ON creator.UserId = r.CreatedBy
WHERE r.DCRNumber = @DcrNumber;

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
    u.UserId AS ApproverId,
    u.FullName AS ApproverName,
    u.Email AS ApproverEmail
FROM DCRApprovalFlows f
INNER JOIN DCRRequests r ON r.Id = f.RequestId
LEFT JOIN Users u ON u.UserId = f.ApproverId
WHERE r.DCRNumber = @DcrNumber
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
    o.SentAt,
    o.LastError
FROM EmailOutbox o
INNER JOIN DCRRequests r ON r.Id = o.RequestId
WHERE r.DCRNumber = @DcrNumber
ORDER BY o.Id;

-- The currently active approver rows. Every row returned here should have an
-- ApprovalAssigned outbox row unless email notification is disabled/not configured.
SELECT
    f.Id AS ApprovalFlowId,
    f.StageNumber,
    f.StageName,
    u.FullName,
    u.Email,
    CASE WHEN EXISTS (
        SELECT 1
        FROM EmailOutbox o
        WHERE o.ApprovalFlowId = f.Id
          AND o.NotificationType = N'ApprovalAssigned'
          AND o.Status <> N'Cancelled'
    ) THEN 1 ELSE 0 END AS HasApprovalEmailOutbox
FROM DCRApprovalFlows f
INNER JOIN DCRRequests r ON r.Id = f.RequestId
LEFT JOIN Users u ON u.UserId = f.ApproverId
WHERE r.DCRNumber = @DcrNumber
  AND r.Status = N'InApproval'
  AND f.RevisionNo = r.RevisionNo
  AND f.StageNumber = r.CurrentStage
  AND f.Decision = N'Pending'
ORDER BY f.Sequence, f.Id;

IF @UserId IS NOT NULL
BEGIN
    SELECT
        r.Id,
        r.DCRNumber,
        r.Title,
        r.Status,
        r.CurrentStage,
        r.RevisionNo
    FROM DCRRequests r
    WHERE r.CreatedBy = @UserId
       OR r.RequestOwnerId = @UserId
       OR EXISTS (
            SELECT 1
            FROM DCRApprovalFlows f
            WHERE f.RequestId = r.Id
              AND f.ApproverId = @UserId
       )
    ORDER BY r.CreatedDate DESC;
END;
