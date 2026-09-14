USE [DCRManagement];
GO

SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.EmailOutbox', N'U') IS NULL
        THROW 51001, 'EmailOutbox does not exist. Run EmailOutbox_ServerWorker_Upgrade.sql first.', 1;

    IF COL_LENGTH(N'dbo.EmailOutbox', N'AttachmentFilePath') IS NULL
        ALTER TABLE dbo.EmailOutbox
            ADD AttachmentFilePath nvarchar(1600) NOT NULL
                CONSTRAINT DF_EmailOutbox_AttachmentFilePath_Upgrade DEFAULT(N'');

    IF COL_LENGTH(N'dbo.EmailOutbox', N'AttachmentFileName') IS NULL
        ALTER TABLE dbo.EmailOutbox
            ADD AttachmentFileName nvarchar(260) NOT NULL
                CONSTRAINT DF_EmailOutbox_AttachmentFileName_Upgrade DEFAULT(N'');

    IF COL_LENGTH(N'dbo.EmailOutbox', N'AttachmentContentType') IS NULL
        ALTER TABLE dbo.EmailOutbox
            ADD AttachmentContentType nvarchar(120) NOT NULL
                CONSTRAINT DF_EmailOutbox_AttachmentContentType_Upgrade DEFAULT(N'');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

SELECT
    COL_LENGTH(N'dbo.EmailOutbox', N'AttachmentFilePath') AS AttachmentFilePathLength,
    COL_LENGTH(N'dbo.EmailOutbox', N'AttachmentFileName') AS AttachmentFileNameLength,
    COL_LENGTH(N'dbo.EmailOutbox', N'AttachmentContentType') AS AttachmentContentTypeLength;
GO
