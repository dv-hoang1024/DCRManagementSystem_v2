USE DCRManagement;
GO

SET NOCOUNT ON;

SELECT
    SYSDATETIME() AS SqlServerNow,
    GETDATE() AS SqlServerLocalNow;

SELECT TOP (100)
    Id,
    RequestId,
    ApprovalFlowId,
    NotificationType,
    Recipient,
    Status,
    RetryCount,
    MaxRetryCount,
    CreatedAt,
    NextAttemptAt,
    LastAttemptAt,
    SentAt,
    CASE
        WHEN Status = N'Pending' AND RetryCount >= MaxRetryCount THEN N'BLOCKED: RetryCount >= MaxRetryCount'
        WHEN Status = N'Pending' AND NextAttemptAt IS NOT NULL AND NextAttemptAt > SYSDATETIME() THEN N'DEFERRED: NextAttemptAt is in the future'
        WHEN Status = N'Pending' THEN N'READY'
        WHEN Status = N'Failed' THEN N'FAILED'
        WHEN Status = N'RequiresSignIn' THEN N'REQUIRES SIGN-IN'
        ELSE Status
    END AS WorkerEligibility,
    DATEDIFF(SECOND, SYSDATETIME(), NextAttemptAt) AS SecondsUntilNextAttempt,
    LastError
FROM dbo.EmailOutbox
ORDER BY Id DESC;
GO

-- Safe immediate repair for retry-eligible Pending messages that were stamped
-- with a client clock in the future. This does not reset failed/exhausted rows.
UPDATE dbo.EmailOutbox
SET NextAttemptAt = NULL
WHERE Status = N'Pending'
  AND RetryCount < MaxRetryCount
  AND NextAttemptAt IS NOT NULL
  AND NextAttemptAt > SYSDATETIME();

SELECT @@ROWCOUNT AS PendingRowsMadeImmediatelyDue;
GO
