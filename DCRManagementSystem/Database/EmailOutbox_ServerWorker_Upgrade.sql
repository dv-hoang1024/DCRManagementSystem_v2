USE [DCRManagement];
GO

SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.EmailOutbox', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.EmailOutbox
        (
            Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmailOutbox PRIMARY KEY,
            RequestId int NULL,
            ApprovalFlowId int NULL,
            NotificationLogId bigint NULL,
            NotificationType nvarchar(60) NOT NULL CONSTRAINT DF_EmailOutbox_NotificationType_Standalone DEFAULT(N''),
            Recipient nvarchar(300) NOT NULL,
            Subject nvarchar(500) NOT NULL,
            Body nvarchar(max) NOT NULL,
            AttachmentFilePath nvarchar(1600) NOT NULL CONSTRAINT DF_EmailOutbox_AttachmentFilePath_Standalone DEFAULT(N''),
            AttachmentFileName nvarchar(260) NOT NULL CONSTRAINT DF_EmailOutbox_AttachmentFileName_Standalone DEFAULT(N''),
            AttachmentContentType nvarchar(120) NOT NULL CONSTRAINT DF_EmailOutbox_AttachmentContentType_Standalone DEFAULT(N''),
            Status nvarchar(40) NOT NULL CONSTRAINT DF_EmailOutbox_Status_Standalone DEFAULT(N'Pending'),
            RetryCount int NOT NULL CONSTRAINT DF_EmailOutbox_RetryCount_Standalone DEFAULT(0),
            MaxRetryCount int NOT NULL CONSTRAINT DF_EmailOutbox_MaxRetryCount_Standalone DEFAULT(3),
            CreatedAt datetime2 NOT NULL CONSTRAINT DF_EmailOutbox_CreatedAt_Standalone DEFAULT(SYSDATETIME()),
            NextAttemptAt datetime2 NULL,
            LastAttemptAt datetime2 NULL,
            SentAt datetime2 NULL,
            SenderAccount nvarchar(300) NOT NULL CONSTRAINT DF_EmailOutbox_SenderAccount_Standalone DEFAULT(N''),
            LastError nvarchar(2000) NOT NULL CONSTRAINT DF_EmailOutbox_LastError_Standalone DEFAULT(N''),
            RowVersion rowversion NOT NULL
        );
    END;

    IF COL_LENGTH(N'dbo.EmailOutbox', N'AttachmentFilePath') IS NULL
        ALTER TABLE dbo.EmailOutbox ADD AttachmentFilePath nvarchar(1600) NOT NULL CONSTRAINT DF_EmailOutbox_AttachmentFilePath_Upgrade DEFAULT(N'');
    IF COL_LENGTH(N'dbo.EmailOutbox', N'AttachmentFileName') IS NULL
        ALTER TABLE dbo.EmailOutbox ADD AttachmentFileName nvarchar(260) NOT NULL CONSTRAINT DF_EmailOutbox_AttachmentFileName_Upgrade DEFAULT(N'');
    IF COL_LENGTH(N'dbo.EmailOutbox', N'AttachmentContentType') IS NULL
        ALTER TABLE dbo.EmailOutbox ADD AttachmentContentType nvarchar(120) NOT NULL CONSTRAINT DF_EmailOutbox_AttachmentContentType_Upgrade DEFAULT(N'');

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.EmailOutbox')
          AND name = N'IX_EmailOutbox_Status_NextAttemptAt_CreatedAt')
    BEGIN
        CREATE INDEX IX_EmailOutbox_Status_NextAttemptAt_CreatedAt
            ON dbo.EmailOutbox(Status, NextAttemptAt, CreatedAt);
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.EmailOutbox')
          AND name = N'IX_EmailOutbox_RequestId_NotificationType_CreatedAt')
    BEGIN
        CREATE INDEX IX_EmailOutbox_RequestId_NotificationType_CreatedAt
            ON dbo.EmailOutbox(RequestId, NotificationType, CreatedAt);
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

SELECT
    OBJECT_ID(N'dbo.EmailOutbox', N'U') AS EmailOutboxObjectId,
    (SELECT COUNT(*) FROM dbo.EmailOutbox) AS CurrentRows;
GO
