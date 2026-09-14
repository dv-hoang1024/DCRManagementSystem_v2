/*
  Idempotent production-workflow upgrade for an existing DCRManagement database.
  The application also executes equivalent checks through DatabaseUpgradeService on startup.
  Review and execute through the plant DBA process before deployment when automatic DDL is disabled.
*/
USE [DCRManagement];
GO

/* This is an UPGRADE script, not the base schema installer. */
DECLARE @MissingBaseTables nvarchar(max) = N'';

IF OBJECT_ID(N'dbo.Users', N'U') IS NULL SET @MissingBaseTables += N'Users, ';
IF OBJECT_ID(N'dbo.Departments', N'U') IS NULL SET @MissingBaseTables += N'Departments, ';
IF OBJECT_ID(N'dbo.DCRRequests', N'U') IS NULL SET @MissingBaseTables += N'DCRRequests, ';
IF OBJECT_ID(N'dbo.DCRParts', N'U') IS NULL SET @MissingBaseTables += N'DCRParts, ';
IF OBJECT_ID(N'dbo.DCRImpactedDepartments', N'U') IS NULL SET @MissingBaseTables += N'DCRImpactedDepartments, ';
IF OBJECT_ID(N'dbo.DCRApprovalFlows', N'U') IS NULL SET @MissingBaseTables += N'DCRApprovalFlows, ';
IF OBJECT_ID(N'dbo.DCRAttachments', N'U') IS NULL SET @MissingBaseTables += N'DCRAttachments, ';
IF OBJECT_ID(N'dbo.AuditLogs', N'U') IS NULL SET @MissingBaseTables += N'AuditLogs, ';
IF OBJECT_ID(N'dbo.WorkflowStageTemplates', N'U') IS NULL SET @MissingBaseTables += N'WorkflowStageTemplates, ';
IF OBJECT_ID(N'dbo.DcrNumberSequences', N'U') IS NULL SET @MissingBaseTables += N'DcrNumberSequences, ';

IF LEN(@MissingBaseTables) > 0
BEGIN
    SET @MissingBaseTables = LEFT(@MissingBaseTables, LEN(@MissingBaseTables) - 1);
    DECLARE @BaseSchemaError nvarchar(2048) =
        N'Base DCR schema is missing: ' + @MissingBaseTables +
        N'. Run Database\\CreateDatabase.sql first. ProductionUpgrade.sql only upgrades an existing base schema.';
    THROW 51002, @BaseSchemaError, 1;
END;
GO

IF COL_LENGTH('DCRRequests','RevisionNo') IS NULL ALTER TABLE DCRRequests ADD RevisionNo int NOT NULL CONSTRAINT DF_DCRRequests_RevisionNo DEFAULT(1);
IF COL_LENGTH('DCRRequests','DraftStep') IS NULL ALTER TABLE DCRRequests ADD DraftStep int NOT NULL CONSTRAINT DF_DCRRequests_DraftStep DEFAULT(0);
IF COL_LENGTH('DCRRequests','LastSavedAt') IS NULL ALTER TABLE DCRRequests ADD LastSavedAt datetime2 NULL;
IF COL_LENGTH('DCRRequests','ReturnedDate') IS NULL ALTER TABLE DCRRequests ADD ReturnedDate datetime2 NULL;
IF COL_LENGTH('DCRRequests','LastReturnReason') IS NULL ALTER TABLE DCRRequests ADD LastReturnReason nvarchar(max) NOT NULL CONSTRAINT DF_DCRRequests_LastReturnReason DEFAULT('');
IF COL_LENGTH('DCRRequests','FinalPdfPath') IS NULL ALTER TABLE DCRRequests ADD FinalPdfPath nvarchar(1000) NOT NULL CONSTRAINT DF_DCRRequests_FinalPdfPath DEFAULT('');
IF COL_LENGTH('DCRRequests','FinalPdfSha256') IS NULL ALTER TABLE DCRRequests ADD FinalPdfSha256 nvarchar(64) NOT NULL CONSTRAINT DF_DCRRequests_FinalPdfSha256 DEFAULT('');
IF COL_LENGTH('DCRRequests','FinalPdfGeneratedAt') IS NULL ALTER TABLE DCRRequests ADD FinalPdfGeneratedAt datetime2 NULL;
IF COL_LENGTH('DCRRequests','MaterialChangeDescription') IS NULL ALTER TABLE DCRRequests ADD MaterialChangeDescription nvarchar(max) NOT NULL CONSTRAINT DF_DCRRequests_MaterialChangeDescription DEFAULT('');
IF COL_LENGTH('DCRRequests','RowVersion') IS NULL ALTER TABLE DCRRequests ADD RowVersion rowversion NOT NULL;
IF COL_LENGTH('DCRRequests','CreationToken') IS NULL ALTER TABLE DCRRequests ADD CreationToken uniqueidentifier NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_DCRRequests_CreatedBy_CreationToken' AND object_id=OBJECT_ID('DCRRequests'))
    CREATE UNIQUE INDEX UX_DCRRequests_CreatedBy_CreationToken ON DCRRequests(CreatedBy, CreationToken) WHERE CreationToken IS NOT NULL;

UPDATE r SET Status='Returned'
FROM DCRRequests r
WHERE r.Status='Draft' AND
    (r.ReturnedDate IS NOT NULL OR EXISTS
        (SELECT 1 FROM DCRApprovalFlows f WHERE f.RequestId=r.Id AND f.Decision='Returned'));
GO

IF COL_LENGTH('DCRApprovalFlows','RevisionNo') IS NULL ALTER TABLE DCRApprovalFlows ADD RevisionNo int NOT NULL CONSTRAINT DF_DCRApprovalFlows_RevisionNo DEFAULT(1);
IF COL_LENGTH('DCRApprovalFlows','AuthMethod') IS NULL ALTER TABLE DCRApprovalFlows ADD AuthMethod nvarchar(80) NOT NULL CONSTRAINT DF_DCRApprovalFlows_AuthMethod DEFAULT('');
IF COL_LENGTH('DCRApprovalFlows','AuthenticatedAt') IS NULL ALTER TABLE DCRApprovalFlows ADD AuthenticatedAt datetime2 NULL;
IF COL_LENGTH('DCRApprovalFlows','WindowsIdentity') IS NULL ALTER TABLE DCRApprovalFlows ADD WindowsIdentity nvarchar(256) NOT NULL CONSTRAINT DF_DCRApprovalFlows_WindowsIdentity DEFAULT('');
IF COL_LENGTH('DCRApprovalFlows','SignatureHash') IS NULL ALTER TABLE DCRApprovalFlows ADD SignatureHash nvarchar(64) NOT NULL CONSTRAINT DF_DCRApprovalFlows_SignatureHash DEFAULT('');
IF COL_LENGTH('DCRApprovalFlows','ReminderCount') IS NULL ALTER TABLE DCRApprovalFlows ADD ReminderCount int NOT NULL CONSTRAINT DF_DCRApprovalFlows_ReminderCount DEFAULT(0);
IF COL_LENGTH('DCRApprovalFlows','LastReminderAt') IS NULL ALTER TABLE DCRApprovalFlows ADD LastReminderAt datetime2 NULL;
GO

IF COL_LENGTH('DCRAttachments','Sha256Hash') IS NULL ALTER TABLE DCRAttachments ADD Sha256Hash nvarchar(64) NOT NULL CONSTRAINT DF_DCRAttachments_Sha256Hash DEFAULT('');
IF COL_LENGTH('DCRAttachments','StorageProvider') IS NULL ALTER TABLE DCRAttachments ADD StorageProvider nvarchar(40) NOT NULL CONSTRAINT DF_DCRAttachments_StorageProvider DEFAULT('FileSystem');
GO

IF COL_LENGTH('AuditLogs','IpAddress') IS NULL ALTER TABLE AuditLogs ADD IpAddress nvarchar(64) NOT NULL CONSTRAINT DF_AuditLogs_IpAddress DEFAULT('');
IF COL_LENGTH('AuditLogs','WindowsIdentity') IS NULL ALTER TABLE AuditLogs ADD WindowsIdentity nvarchar(256) NOT NULL CONSTRAINT DF_AuditLogs_WindowsIdentity DEFAULT('');
IF COL_LENGTH('AuditLogs','SessionId') IS NULL ALTER TABLE AuditLogs ADD SessionId nvarchar(64) NOT NULL CONSTRAINT DF_AuditLogs_SessionId DEFAULT('');
GO

IF OBJECT_ID('SystemSettings','U') IS NULL
BEGIN
    CREATE TABLE SystemSettings(
        [Key] nvarchar(120) NOT NULL PRIMARY KEY,
        [Value] nvarchar(max) NOT NULL CONSTRAINT DF_SystemSettings_Value DEFAULT(''),
        UpdatedAt datetime2 NOT NULL CONSTRAINT DF_SystemSettings_UpdatedAt DEFAULT(GETDATE())
    );
END
GO

IF OBJECT_ID('ApprovalMatrixRules','U') IS NULL
BEGIN
    CREATE TABLE ApprovalMatrixRules(
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        StageCode nvarchar(80) NOT NULL,
        RequestingDepartmentId int NULL,
        TargetDepartmentId int NULL,
        ApproverSource nvarchar(80) NOT NULL,
        ApproverRole nvarchar(80) NOT NULL CONSTRAINT DF_ApprovalMatrixRules_ApproverRole DEFAULT(''),
        ApproverUserId int NULL,
        Priority int NOT NULL CONSTRAINT DF_ApprovalMatrixRules_Priority DEFAULT(100),
        IsActive bit NOT NULL CONSTRAINT DF_ApprovalMatrixRules_IsActive DEFAULT(1),
        Description nvarchar(300) NOT NULL CONSTRAINT DF_ApprovalMatrixRules_Description DEFAULT('')
    );
END
GO

IF OBJECT_ID('DCRDeletionLogs','U') IS NULL
BEGIN
    CREATE TABLE DCRDeletionLogs(
        Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
        OriginalRequestId int NOT NULL,
        DCRNumber nvarchar(40) NOT NULL,
        DeletedBy int NOT NULL,
        DeletedAt datetime2 NOT NULL CONSTRAINT DF_DCRDeletionLogs_DeletedAt DEFAULT(GETDATE()),
        SnapshotJson nvarchar(max) NOT NULL,
        CleanupStatus nvarchar(40) NOT NULL CONSTRAINT DF_DCRDeletionLogs_CleanupStatus DEFAULT('Pending'),
        CleanupDetails nvarchar(max) NOT NULL CONSTRAINT DF_DCRDeletionLogs_CleanupDetails DEFAULT(''),
        ComputerName nvarchar(200) NOT NULL CONSTRAINT DF_DCRDeletionLogs_ComputerName DEFAULT(''),
        IpAddress nvarchar(64) NOT NULL CONSTRAINT DF_DCRDeletionLogs_IpAddress DEFAULT(''),
        WindowsIdentity nvarchar(256) NOT NULL CONSTRAINT DF_DCRDeletionLogs_WindowsIdentity DEFAULT(''),
        SessionId nvarchar(64) NOT NULL CONSTRAINT DF_DCRDeletionLogs_SessionId DEFAULT('')
    );
END
GO

IF OBJECT_ID('DCRNotificationLogs','U') IS NULL
BEGIN
    CREATE TABLE DCRNotificationLogs(
        Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
        RequestId int NOT NULL,
        ApprovalFlowId int NULL,
        NotificationType nvarchar(60) NOT NULL,
        Recipient nvarchar(300) NOT NULL,
        SentAt datetime2 NOT NULL,
        Success bit NOT NULL,
        Details nvarchar(1000) NOT NULL CONSTRAINT DF_DCRNotificationLogs_Details DEFAULT('')
    );
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name IN ('IX_DCRApprovalFlows_Request_Revision_Stage_Sequence','IX_DCRApprovalFlows_RequestId_RevisionNo_StageNumber_Sequence')
      AND object_id=OBJECT_ID('DCRApprovalFlows'))
BEGIN
    CREATE INDEX IX_DCRApprovalFlows_Request_Revision_Stage_Sequence
        ON DCRApprovalFlows(RequestId, RevisionNo, StageNumber, Sequence);
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name IN ('IX_ApprovalMatrixRules_Routing','IX_ApprovalMatrixRules_StageCode_RequestingDepartmentId_TargetDepartmentId_Priority')
      AND object_id=OBJECT_ID('ApprovalMatrixRules'))
BEGIN
    CREATE INDEX IX_ApprovalMatrixRules_Routing
        ON ApprovalMatrixRules(StageCode, RequestingDepartmentId, TargetDepartmentId, Priority);
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name IN ('IX_DCRNotificationLogs_Request_Type_SentAt','IX_DCRNotificationLogs_RequestId_ApprovalFlowId_NotificationType_SentAt')
      AND object_id=OBJECT_ID('DCRNotificationLogs'))
BEGIN
    CREATE INDEX IX_DCRNotificationLogs_Request_Type_SentAt
        ON DCRNotificationLogs(RequestId, ApprovalFlowId, NotificationType, SentAt);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name IN ('FK_ApprovalMatrixRules_RequestingDepartment','FK_ApprovalMatrixRules_Departments_RequestingDepartmentId'))
    ALTER TABLE ApprovalMatrixRules WITH CHECK ADD CONSTRAINT FK_ApprovalMatrixRules_RequestingDepartment FOREIGN KEY(RequestingDepartmentId) REFERENCES Departments(Id);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name IN ('FK_ApprovalMatrixRules_TargetDepartment','FK_ApprovalMatrixRules_Departments_TargetDepartmentId'))
    ALTER TABLE ApprovalMatrixRules WITH CHECK ADD CONSTRAINT FK_ApprovalMatrixRules_TargetDepartment FOREIGN KEY(TargetDepartmentId) REFERENCES Departments(Id);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name IN ('FK_ApprovalMatrixRules_ApproverUser','FK_ApprovalMatrixRules_Users_ApproverUserId'))
    ALTER TABLE ApprovalMatrixRules WITH CHECK ADD CONSTRAINT FK_ApprovalMatrixRules_ApproverUser FOREIGN KEY(ApproverUserId) REFERENCES Users(UserId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name IN ('FK_DCRNotificationLogs_Request','FK_DCRNotificationLogs_DCRRequests_RequestId'))
    ALTER TABLE DCRNotificationLogs WITH CHECK ADD CONSTRAINT FK_DCRNotificationLogs_Request FOREIGN KEY(RequestId) REFERENCES DCRRequests(Id) ON DELETE CASCADE;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name IN ('FK_DCRNotificationLogs_ApprovalFlow','FK_DCRNotificationLogs_DCRApprovalFlows_ApprovalFlowId'))
    ALTER TABLE DCRNotificationLogs WITH CHECK ADD CONSTRAINT FK_DCRNotificationLogs_ApprovalFlow FOREIGN KEY(ApprovalFlowId) REFERENCES DCRApprovalFlows(Id);
GO


/* Enterprise enhancement 2026-08-17: Windows/AD mapping, NAS storage metadata, automatic audit */
IF COL_LENGTH('Users','WindowsAccount') IS NULL
    ALTER TABLE Users ADD WindowsAccount nvarchar(256) NOT NULL CONSTRAINT DF_Users_WindowsAccount DEFAULT('');

IF COL_LENGTH('DCRAttachments','OriginalSha256Hash') IS NULL
    ALTER TABLE DCRAttachments ADD OriginalSha256Hash nvarchar(64) NOT NULL CONSTRAINT DF_DCRAttachments_OriginalSha256Hash DEFAULT('');
IF COL_LENGTH('DCRAttachments','StoredFileSize') IS NULL
    ALTER TABLE DCRAttachments ADD StoredFileSize bigint NOT NULL CONSTRAINT DF_DCRAttachments_StoredFileSize DEFAULT(0);
IF COL_LENGTH('DCRAttachments','IsCompressed') IS NULL
    ALTER TABLE DCRAttachments ADD IsCompressed bit NOT NULL CONSTRAINT DF_DCRAttachments_IsCompressed DEFAULT(0);
IF COL_LENGTH('DCRAttachments','CompressionType') IS NULL
    ALTER TABLE DCRAttachments ADD CompressionType nvarchar(20) NOT NULL CONSTRAINT DF_DCRAttachments_CompressionType DEFAULT('');

IF COL_LENGTH('AuditLogs','RequestId') IS NOT NULL
    ALTER TABLE AuditLogs ALTER COLUMN RequestId int NULL;
IF COL_LENGTH('AuditLogs','UserId') IS NOT NULL
    ALTER TABLE AuditLogs ALTER COLUMN UserId int NULL;


/* Enterprise hardening: unique Windows mapping and immutable audit relation */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_Users_WindowsAccount' AND object_id=OBJECT_ID('Users'))
    CREATE UNIQUE INDEX UX_Users_WindowsAccount ON Users(WindowsAccount) WHERE WindowsAccount <> '';
GO

IF OBJECT_ID('AuditLogs','U') IS NOT NULL
BEGIN
    DECLARE @AuditRequestFk sysname;
    SELECT TOP (1) @AuditRequestFk = fk.name
    FROM sys.foreign_keys fk
    INNER JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
    INNER JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
    WHERE fkc.parent_object_id = OBJECT_ID('AuditLogs')
      AND fkc.referenced_object_id = OBJECT_ID('DCRRequests')
      AND pc.name = 'RequestId'
      AND fk.delete_referential_action <> 0;

    IF @AuditRequestFk IS NOT NULL
    BEGIN
        DECLARE @DropAuditRequestFkSql nvarchar(max);
        SET @DropAuditRequestFkSql = N'ALTER TABLE [AuditLogs] DROP CONSTRAINT ' + QUOTENAME(@AuditRequestFk) + N';';
        EXEC sys.sp_executesql @DropAuditRequestFkSql;
    END

    IF NOT EXISTS (
        SELECT 1
        FROM sys.foreign_keys fk
        INNER JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
        INNER JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
        WHERE fkc.parent_object_id = OBJECT_ID('AuditLogs')
          AND fkc.referenced_object_id = OBJECT_ID('DCRRequests')
          AND pc.name = 'RequestId')
        ALTER TABLE AuditLogs WITH CHECK ADD CONSTRAINT FK_AuditLogs_DCRRequests_RequestId
            FOREIGN KEY(RequestId) REFERENCES DCRRequests(Id);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_DCRDeletionLogs_DCRNumber_DeletedAt' AND object_id=OBJECT_ID('DCRDeletionLogs'))
    CREATE INDEX IX_DCRDeletionLogs_DCRNumber_DeletedAt ON DCRDeletionLogs(DCRNumber, DeletedAt);
GO

IF OBJECT_ID('DCRDeletionLogs','U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_DCRDeletionLogs_Users_DeletedBy')
    ALTER TABLE DCRDeletionLogs WITH CHECK ADD CONSTRAINT FK_DCRDeletionLogs_Users_DeletedBy FOREIGN KEY(DeletedBy) REFERENCES Users(UserId);
GO


/* Server Mail Worker / Email Outbox 2026-08-17 */
IF OBJECT_ID('EmailOutbox','U') IS NULL
BEGIN
    CREATE TABLE EmailOutbox(
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmailOutbox PRIMARY KEY,
        RequestId int NULL,
        ApprovalFlowId int NULL,
        NotificationLogId bigint NULL,
        NotificationType nvarchar(60) NOT NULL CONSTRAINT DF_EmailOutbox_NotificationType DEFAULT(''),
        Recipient nvarchar(300) NOT NULL,
        Subject nvarchar(500) NOT NULL,
        Body nvarchar(max) NOT NULL,
        AttachmentFilePath nvarchar(1600) NOT NULL CONSTRAINT DF_EmailOutbox_AttachmentFilePath DEFAULT(''),
        AttachmentFileName nvarchar(260) NOT NULL CONSTRAINT DF_EmailOutbox_AttachmentFileName DEFAULT(''),
        AttachmentContentType nvarchar(120) NOT NULL CONSTRAINT DF_EmailOutbox_AttachmentContentType DEFAULT(''),
        Status nvarchar(40) NOT NULL CONSTRAINT DF_EmailOutbox_Status DEFAULT('Pending'),
        RetryCount int NOT NULL CONSTRAINT DF_EmailOutbox_RetryCount DEFAULT(0),
        MaxRetryCount int NOT NULL CONSTRAINT DF_EmailOutbox_MaxRetryCount DEFAULT(3),
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_EmailOutbox_CreatedAt DEFAULT(GETDATE()),
        NextAttemptAt datetime2 NULL,
        LastAttemptAt datetime2 NULL,
        SentAt datetime2 NULL,
        SenderAccount nvarchar(300) NOT NULL CONSTRAINT DF_EmailOutbox_SenderAccount DEFAULT(''),
        LastError nvarchar(2000) NOT NULL CONSTRAINT DF_EmailOutbox_LastError DEFAULT(''),
        RowVersion rowversion NOT NULL
    );
END
GO

IF COL_LENGTH('EmailOutbox','AttachmentFilePath') IS NULL
    ALTER TABLE EmailOutbox ADD AttachmentFilePath nvarchar(1600) NOT NULL CONSTRAINT DF_EmailOutbox_AttachmentFilePath_Upgrade DEFAULT('');
IF COL_LENGTH('EmailOutbox','AttachmentFileName') IS NULL
    ALTER TABLE EmailOutbox ADD AttachmentFileName nvarchar(260) NOT NULL CONSTRAINT DF_EmailOutbox_AttachmentFileName_Upgrade DEFAULT('');
IF COL_LENGTH('EmailOutbox','AttachmentContentType') IS NULL
    ALTER TABLE EmailOutbox ADD AttachmentContentType nvarchar(120) NOT NULL CONSTRAINT DF_EmailOutbox_AttachmentContentType_Upgrade DEFAULT('');
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_EmailOutbox_Status_NextAttemptAt_CreatedAt' AND object_id=OBJECT_ID('EmailOutbox'))
    CREATE INDEX IX_EmailOutbox_Status_NextAttemptAt_CreatedAt ON EmailOutbox(Status, NextAttemptAt, CreatedAt);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_EmailOutbox_RequestId_NotificationType_CreatedAt' AND object_id=OBJECT_ID('EmailOutbox'))
    CREATE INDEX IX_EmailOutbox_RequestId_NotificationType_CreatedAt ON EmailOutbox(RequestId, NotificationType, CreatedAt);
GO


/* Dynamic per-DCR approval plan: N levels + parallel co-approvers */
IF OBJECT_ID('DCRApprovalPlanEntries','U') IS NULL
BEGIN
    CREATE TABLE DCRApprovalPlanEntries(
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        RequestId int NOT NULL,
        LevelNumber int NOT NULL,
        LevelName nvarchar(200) NOT NULL CONSTRAINT DF_DCRApprovalPlanEntries_LevelName DEFAULT(''),
        ApproverId int NOT NULL,
        Sequence int NOT NULL CONSTRAINT DF_DCRApprovalPlanEntries_Sequence DEFAULT(1),
        IsRequired bit NOT NULL CONSTRAINT DF_DCRApprovalPlanEntries_IsRequired DEFAULT(1),
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_DCRApprovalPlanEntries_CreatedAt DEFAULT(GETDATE())
    );
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_DCRApprovalPlanEntries_Request_Level_Approver' AND object_id=OBJECT_ID('DCRApprovalPlanEntries'))
    DROP INDEX UX_DCRApprovalPlanEntries_Request_Level_Approver ON DCRApprovalPlanEntries;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_DCRApprovalPlanEntries_Request_Approver' AND object_id=OBJECT_ID('DCRApprovalPlanEntries'))
    CREATE UNIQUE INDEX UX_DCRApprovalPlanEntries_Request_Approver ON DCRApprovalPlanEntries(RequestId, ApproverId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_DCRApprovalPlanEntries_Request_Level_Sequence' AND object_id=OBJECT_ID('DCRApprovalPlanEntries'))
    CREATE INDEX IX_DCRApprovalPlanEntries_Request_Level_Sequence ON DCRApprovalPlanEntries(RequestId, LevelNumber, Sequence);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_DCRApprovalPlanEntries_DCRRequests_RequestId')
    ALTER TABLE DCRApprovalPlanEntries WITH CHECK ADD CONSTRAINT FK_DCRApprovalPlanEntries_DCRRequests_RequestId FOREIGN KEY(RequestId) REFERENCES DCRRequests(Id) ON DELETE CASCADE;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_DCRApprovalPlanEntries_Users_ApproverId')
    ALTER TABLE DCRApprovalPlanEntries WITH CHECK ADD CONSTRAINT FK_DCRApprovalPlanEntries_Users_ApproverId FOREIGN KEY(ApproverId) REFERENCES Users(UserId);
GO

/* 2026-08-17: Dynamic Role management + safe user delete + department Manager/Director hierarchy */
IF COL_LENGTH('Users','IsDeleted') IS NULL ALTER TABLE Users ADD IsDeleted bit NOT NULL CONSTRAINT DF_Users_IsDeleted DEFAULT(0);
IF COL_LENGTH('Users','DeletedAt') IS NULL ALTER TABLE Users ADD DeletedAt datetime2 NULL;
IF COL_LENGTH('Users','DeletedBy') IS NULL ALTER TABLE Users ADD DeletedBy int NULL;
IF COL_LENGTH('Departments','DirectorUserId') IS NULL ALTER TABLE Departments ADD DirectorUserId int NULL;

IF OBJECT_ID('Roles','U') IS NULL
BEGIN
    CREATE TABLE Roles(
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        RoleName nvarchar(80) NOT NULL,
        Description nvarchar(300) NOT NULL CONSTRAINT DF_Roles_Description DEFAULT(''),
        HierarchyLevel int NOT NULL CONSTRAINT DF_Roles_HierarchyLevel DEFAULT(10),
        IsSystemProtected bit NOT NULL CONSTRAINT DF_Roles_IsSystemProtected DEFAULT(0),
        IsActive bit NOT NULL CONSTRAINT DF_Roles_IsActive DEFAULT(1),
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_Roles_CreatedAt DEFAULT(GETDATE())
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_Roles_RoleName' AND object_id=OBJECT_ID('Roles'))
    CREATE UNIQUE INDEX UX_Roles_RoleName ON Roles(RoleName);

IF NOT EXISTS (SELECT 1 FROM Roles WHERE RoleName='Administrator') INSERT INTO Roles(RoleName,Description,HierarchyLevel,IsSystemProtected,IsActive) VALUES('Administrator','System administration and unrestricted configuration access.',100,1,1);
IF NOT EXISTS (SELECT 1 FROM Roles WHERE RoleName='Director') INSERT INTO Roles(RoleName,Description,HierarchyLevel,IsSystemProtected,IsActive) VALUES('Director','Department / functional Director.',30,0,1);
IF NOT EXISTS (SELECT 1 FROM Roles WHERE RoleName='Manager') INSERT INTO Roles(RoleName,Description,HierarchyLevel,IsSystemProtected,IsActive) VALUES('Manager','Direct Manager.',20,0,1);
IF NOT EXISTS (SELECT 1 FROM Roles WHERE RoleName='Staff') INSERT INTO Roles(RoleName,Description,HierarchyLevel,IsSystemProtected,IsActive) VALUES('Staff','Standard user / initiator role.',10,0,1);

UPDATE Users SET Role='Director' WHERE Role='ChiefEngineer';
UPDATE Users SET Role='Manager' WHERE Role IN ('DesignManager','MEManager');
UPDATE Users SET Role='Staff' WHERE Role IN ('Initiator','Viewer');
IF OBJECT_ID('WorkflowStageTemplates','U') IS NOT NULL
BEGIN
    UPDATE WorkflowStageTemplates SET ApproverRole='Director' WHERE ApproverRole='ChiefEngineer';
    UPDATE WorkflowStageTemplates SET ApproverRole='Manager' WHERE ApproverRole IN ('DesignManager','MEManager');
    UPDATE WorkflowStageTemplates SET ApproverRole='Staff' WHERE ApproverRole IN ('Initiator','Viewer');
END;
IF OBJECT_ID('ApprovalMatrixRules','U') IS NOT NULL
BEGIN
    UPDATE ApprovalMatrixRules SET ApproverRole='Director' WHERE ApproverRole='ChiefEngineer';
    UPDATE ApprovalMatrixRules SET ApproverRole='Manager' WHERE ApproverRole IN ('DesignManager','MEManager');
    UPDATE ApprovalMatrixRules SET ApproverRole='Staff' WHERE ApproverRole IN ('Initiator','Viewer');
END;
IF COL_LENGTH('Departments','DirectorUserId') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_Departments_Users_DirectorUserId')
    ALTER TABLE Departments WITH CHECK ADD CONSTRAINT FK_Departments_Users_DirectorUserId FOREIGN KEY(DirectorUserId) REFERENCES Users(UserId);

-- Per-user direct manager for default approval suggestion
IF COL_LENGTH('Users','DirectManagerUserId') IS NULL ALTER TABLE Users ADD DirectManagerUserId int NULL;
GO
IF COL_LENGTH('Users','DirectManagerUserId') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_Users_Users_DirectManagerUserId')
    ALTER TABLE Users WITH CHECK ADD CONSTRAINT FK_Users_Users_DirectManagerUserId FOREIGN KEY(DirectManagerUserId) REFERENCES Users(UserId);
GO

/* Rank-based official routing + reusable per-user approval-line templates */
IF COL_LENGTH('DCRRequests','Rank') IS NULL
    ALTER TABLE DCRRequests ADD Rank nvarchar(10) NOT NULL CONSTRAINT DF_DCRRequests_Rank DEFAULT('C');
UPDATE DCRRequests SET Rank='C' WHERE Rank IS NULL OR UPPER(LTRIM(RTRIM(Rank))) NOT IN ('A','B','C','S');
GO

IF OBJECT_ID('UserApprovalPlanEntries','U') IS NULL
BEGIN
    CREATE TABLE UserApprovalPlanEntries(
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        OwnerUserId int NOT NULL,
        Rank nvarchar(10) NOT NULL CONSTRAINT DF_UserApprovalPlanEntries_Rank DEFAULT('C'),
        LevelNumber int NOT NULL,
        LevelName nvarchar(200) NOT NULL CONSTRAINT DF_UserApprovalPlanEntries_LevelName DEFAULT(''),
        ApproverId int NOT NULL,
        BusinessUnitId int NULL,
        BusinessUnitCode nvarchar(40) NOT NULL CONSTRAINT DF_UserApprovalPlanEntries_BusinessUnitCode DEFAULT(''),
        BusinessUnitName nvarchar(160) NOT NULL CONSTRAINT DF_UserApprovalPlanEntries_BusinessUnitName DEFAULT(''),
        DepartmentId int NULL,
        DepartmentCode nvarchar(40) NOT NULL CONSTRAINT DF_UserApprovalPlanEntries_DepartmentCode DEFAULT(''),
        DepartmentName nvarchar(160) NOT NULL CONSTRAINT DF_UserApprovalPlanEntries_DepartmentName DEFAULT(''),
        ApproverRole nvarchar(80) NOT NULL CONSTRAINT DF_UserApprovalPlanEntries_ApproverRole DEFAULT(''),
        ApproverName nvarchar(160) NOT NULL CONSTRAINT DF_UserApprovalPlanEntries_ApproverName DEFAULT(''),
        ApproverEmail nvarchar(200) NOT NULL CONSTRAINT DF_UserApprovalPlanEntries_ApproverEmail DEFAULT(''),
        Sequence int NOT NULL CONSTRAINT DF_UserApprovalPlanEntries_Sequence DEFAULT(1),
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_UserApprovalPlanEntries_CreatedAt DEFAULT(GETDATE()),
        UpdatedAt datetime2 NOT NULL CONSTRAINT DF_UserApprovalPlanEntries_UpdatedAt DEFAULT(GETDATE())
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_UserApprovalPlanEntries_Owner_Rank_Approver' AND object_id=OBJECT_ID('UserApprovalPlanEntries'))
    CREATE UNIQUE INDEX UX_UserApprovalPlanEntries_Owner_Rank_Approver ON UserApprovalPlanEntries(OwnerUserId, Rank, ApproverId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_UserApprovalPlanEntries_Owner_Rank_Level_Sequence' AND object_id=OBJECT_ID('UserApprovalPlanEntries'))
    CREATE INDEX IX_UserApprovalPlanEntries_Owner_Rank_Level_Sequence ON UserApprovalPlanEntries(OwnerUserId, Rank, LevelNumber, Sequence);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_UserApprovalPlanEntries_Users_OwnerUserId')
    ALTER TABLE UserApprovalPlanEntries WITH CHECK ADD CONSTRAINT FK_UserApprovalPlanEntries_Users_OwnerUserId FOREIGN KEY(OwnerUserId) REFERENCES Users(UserId) ON DELETE CASCADE;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_UserApprovalPlanEntries_Users_ApproverId')
    ALTER TABLE UserApprovalPlanEntries WITH CHECK ADD CONSTRAINT FK_UserApprovalPlanEntries_Users_ApproverId FOREIGN KEY(ApproverId) REFERENCES Users(UserId);
GO

/* 2026-08-27: Named reusable approval plans by DCR Rank */
IF OBJECT_ID('ApprovalPlanTemplates','U') IS NULL
BEGIN
    CREATE TABLE ApprovalPlanTemplates(
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name nvarchar(160) NOT NULL,
        Rank nvarchar(10) NOT NULL CONSTRAINT DF_ApprovalPlanTemplates_Rank DEFAULT('C'),
        IsDefault bit NOT NULL CONSTRAINT DF_ApprovalPlanTemplates_IsDefault DEFAULT(0),
        IsActive bit NOT NULL CONSTRAINT DF_ApprovalPlanTemplates_IsActive DEFAULT(1),
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_ApprovalPlanTemplates_CreatedAt DEFAULT(GETDATE()),
        UpdatedAt datetime2 NOT NULL CONSTRAINT DF_ApprovalPlanTemplates_UpdatedAt DEFAULT(GETDATE())
    );
END
GO

IF OBJECT_ID('ApprovalPlanTemplateEntries','U') IS NULL
BEGIN
    CREATE TABLE ApprovalPlanTemplateEntries(
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        TemplateId int NOT NULL,
        LevelNumber int NOT NULL,
        LevelName nvarchar(200) NOT NULL CONSTRAINT DF_ApprovalPlanTemplateEntries_LevelName DEFAULT(''),
        ApproverId int NOT NULL,
        Sequence int NOT NULL CONSTRAINT DF_ApprovalPlanTemplateEntries_Sequence DEFAULT(1)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_ApprovalPlanTemplates_Rank_Name' AND object_id=OBJECT_ID('ApprovalPlanTemplates'))
    CREATE UNIQUE INDEX UX_ApprovalPlanTemplates_Rank_Name ON ApprovalPlanTemplates(Rank, Name);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_ApprovalPlanTemplateEntries_Template_Approver' AND object_id=OBJECT_ID('ApprovalPlanTemplateEntries'))
    CREATE UNIQUE INDEX UX_ApprovalPlanTemplateEntries_Template_Approver ON ApprovalPlanTemplateEntries(TemplateId, ApproverId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_ApprovalPlanTemplateEntries_Template_Level_Sequence' AND object_id=OBJECT_ID('ApprovalPlanTemplateEntries'))
    CREATE INDEX IX_ApprovalPlanTemplateEntries_Template_Level_Sequence ON ApprovalPlanTemplateEntries(TemplateId, LevelNumber, Sequence);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_ApprovalPlanTemplateEntries_ApprovalPlanTemplates_TemplateId')
    ALTER TABLE ApprovalPlanTemplateEntries WITH CHECK ADD CONSTRAINT FK_ApprovalPlanTemplateEntries_ApprovalPlanTemplates_TemplateId FOREIGN KEY(TemplateId) REFERENCES ApprovalPlanTemplates(Id) ON DELETE CASCADE;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_ApprovalPlanTemplateEntries_Users_ApproverId')
    ALTER TABLE ApprovalPlanTemplateEntries WITH CHECK ADD CONSTRAINT FK_ApprovalPlanTemplateEntries_Users_ApproverId FOREIGN KEY(ApproverId) REFERENCES Users(UserId);
GO
