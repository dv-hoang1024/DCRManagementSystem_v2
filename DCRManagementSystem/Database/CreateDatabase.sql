/*
  DCR Management System - BASE DATABASE INITIALIZATION / REPAIR

  Use this script for a NEW database or a database that was only partially
  initialized by an upgrade script. The script is idempotent: it creates only
  missing application tables, indexes, and foreign keys.

  IMPORTANT:
  - Run this BEFORE ProductionUpgrade.sql on a fresh database.
  - This script does not delete application data.
  - If a legacy underscore schema (DCR_Requests, DCR_Parts, ...) is detected,
    the script stops to avoid creating a second parallel schema.
*/

IF DB_ID(N'DCRManagement') IS NULL
BEGIN
    CREATE DATABASE [DCRManagement];
END
GO

USE [DCRManagement];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/* Guard against accidentally creating a second schema next to a legacy DB. */
IF OBJECT_ID(N'dbo.DCR_Requests', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.DCRRequests', N'U') IS NULL
    THROW 51001, N'Legacy table dbo.DCR_Requests detected. Do not initialize a parallel schema. Migrate/rename the legacy schema first.', 1;
IF OBJECT_ID(N'dbo.DCR_Parts', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.DCRParts', N'U') IS NULL
    THROW 51001, N'Legacy table dbo.DCR_Parts detected. Do not initialize a parallel schema. Migrate/rename the legacy schema first.', 1;
IF OBJECT_ID(N'dbo.DCR_ApprovalFlow', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.DCRApprovalFlows', N'U') IS NULL
    THROW 51001, N'Legacy table dbo.DCR_ApprovalFlow detected. Do not initialize a parallel schema. Migrate/rename the legacy schema first.', 1;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.Departments', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.Departments
        (
            Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Departments PRIMARY KEY,
            DepartmentCode nvarchar(40) NOT NULL,
            DepartmentName nvarchar(160) NOT NULL,
            BusinessUnitId int NULL,
            ManagerUserId int NULL,
            DirectorUserId int NULL,
            IsActive bit NOT NULL CONSTRAINT DF_Departments_IsActive DEFAULT(1)
        );
    END;

    IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.Users
        (
            UserId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
            Username nvarchar(80) NOT NULL,
            PasswordHash nvarchar(200) NOT NULL,
            WindowsAccount nvarchar(256) NOT NULL CONSTRAINT DF_Users_WindowsAccount_Base DEFAULT(N''),
            FullName nvarchar(160) NOT NULL,
            Email nvarchar(200) NOT NULL,
            Phone nvarchar(40) NOT NULL,
            DepartmentId int NULL,
            BusinessUnitId int NULL,
            DirectManagerUserId int NULL,
            Role nvarchar(80) NOT NULL,
            IsActive bit NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT(1),
            IsDeleted bit NOT NULL CONSTRAINT DF_Users_IsDeleted_Base DEFAULT(0),
            DeletedAt datetime2 NULL,
            DeletedBy int NULL,
            CreatedAt datetime2 NOT NULL CONSTRAINT DF_Users_CreatedAt DEFAULT(SYSDATETIME())
        );
    END;

    IF OBJECT_ID(N'dbo.BusinessUnits', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.BusinessUnits
        (
            Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_BusinessUnits PRIMARY KEY,
            UnitCode nvarchar(40) NOT NULL,
            UnitName nvarchar(160) NOT NULL,
            DirectorUserId int NULL,
            IsActive bit NOT NULL CONSTRAINT DF_BusinessUnits_IsActive_Base DEFAULT(1)
        );
    END;

    IF OBJECT_ID(N'dbo.Roles', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.Roles
        (
            Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Roles PRIMARY KEY,
            RoleName nvarchar(80) NOT NULL,
            Description nvarchar(300) NOT NULL CONSTRAINT DF_Roles_Description_Base DEFAULT(N''),
            HierarchyLevel int NOT NULL CONSTRAINT DF_Roles_HierarchyLevel_Base DEFAULT(10),
            IsSystemProtected bit NOT NULL CONSTRAINT DF_Roles_IsSystemProtected_Base DEFAULT(0),
            IsActive bit NOT NULL CONSTRAINT DF_Roles_IsActive_Base DEFAULT(1),
            CreatedAt datetime2 NOT NULL CONSTRAINT DF_Roles_CreatedAt_Base DEFAULT(SYSDATETIME())
        );
    END;

    IF OBJECT_ID(N'dbo.DCRRequests', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.DCRRequests
        (
            Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DCRRequests PRIMARY KEY,
            DCRNumber nvarchar(40) NOT NULL,
            Title nvarchar(500) NOT NULL,
            Rank nvarchar(10) NOT NULL CONSTRAINT DF_DCRRequests_Rank_Base DEFAULT(N'C'),
            RequestingDepartmentId int NOT NULL,
            ModuleGroup nvarchar(100) NOT NULL,
            RequestOwnerId int NOT NULL,
            Program nvarchar(100) NOT NULL,
            BuildStage nvarchar(100) NOT NULL,
            RelatedECR nvarchar(100) NOT NULL,
            RelatedPPS nvarchar(100) NOT NULL,
            RelatedECN nvarchar(100) NOT NULL,
            RelatedMCN nvarchar(100) NOT NULL,
            ProblemDescription nvarchar(max) NOT NULL,
            Solution nvarchar(max) NOT NULL,
            MaterialChangeDescription nvarchar(max) NOT NULL CONSTRAINT DF_DCRRequests_MaterialChangeDescription_Base DEFAULT(N''),
            FormFitFunctionDetail nvarchar(max) NOT NULL,
            RetrofitVolume nvarchar(100) NOT NULL,
            RetrofitInstruction nvarchar(max) NOT NULL,
            MaterialIdentificationRequired bit NOT NULL,
            MaterialUsageStation nvarchar(200) NOT NULL,
            SupplierSupportsMRD bit NOT NULL,
            ExpectedArrivalDate datetime2 NULL,
            TemporaryProcessRequired bit NOT NULL,
            ReworkRequired bit NOT NULL,
            PlannedStartDate datetime2 NULL,
            PlannedEndDate datetime2 NULL,
            ProductionOrderNumber nvarchar(100) NOT NULL,
            Status nvarchar(40) NOT NULL,
            CurrentStage int NOT NULL,
            RevisionNo int NOT NULL CONSTRAINT DF_DCRRequests_RevisionNo_Base DEFAULT(1),
            DraftStep int NOT NULL CONSTRAINT DF_DCRRequests_DraftStep_Base DEFAULT(0),
            LastSavedAt datetime2 NULL,
            ReturnedDate datetime2 NULL,
            LastReturnReason nvarchar(max) NOT NULL CONSTRAINT DF_DCRRequests_LastReturnReason_Base DEFAULT(N''),
            CreatedBy int NOT NULL,
            CreatedDate datetime2 NOT NULL CONSTRAINT DF_DCRRequests_CreatedDate DEFAULT(SYSDATETIME()),
            SubmittedDate datetime2 NULL,
            CompletedDate datetime2 NULL,
            RejectedDate datetime2 NULL,
            FinalPdfPath nvarchar(1000) NOT NULL CONSTRAINT DF_DCRRequests_FinalPdfPath_Base DEFAULT(N''),
            FinalPdfSha256 nvarchar(64) NOT NULL CONSTRAINT DF_DCRRequests_FinalPdfSha256_Base DEFAULT(N''),
            FinalPdfGeneratedAt datetime2 NULL,
            RowVersion rowversion NOT NULL
        );
    END;

    IF OBJECT_ID(N'dbo.DCRParts', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.DCRParts
        (
            Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DCRParts PRIMARY KEY,
            RequestId int NOT NULL,
            ChangeType nvarchar(50) NOT NULL,
            PartNumber nvarchar(100) NOT NULL,
            PartName nvarchar(300) NOT NULL,
            KPC nvarchar(100) NOT NULL,
            Quantity nvarchar(100) NOT NULL,
            ReplacedBy nvarchar(150) NOT NULL,
            SortOrder int NOT NULL
        );
    END;

    IF OBJECT_ID(N'dbo.DCRImpactedDepartments', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.DCRImpactedDepartments
        (
            Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DCRImpactedDepartments PRIMARY KEY,
            RequestId int NOT NULL,
            DepartmentId int NOT NULL,
            EstimatedCost decimal(18,2) NULL
        );
    END;

    IF OBJECT_ID(N'dbo.DCRApprovalFlows', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.DCRApprovalFlows
        (
            Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DCRApprovalFlows PRIMARY KEY,
            RequestId int NOT NULL,
            RevisionNo int NOT NULL CONSTRAINT DF_DCRApprovalFlows_RevisionNo_Base DEFAULT(1),
            StageNumber int NOT NULL,
            StageCode nvarchar(80) NOT NULL,
            StageName nvarchar(300) NOT NULL,
            ApproverId int NULL,
            DepartmentId int NULL,
            Decision nvarchar(40) NOT NULL,
            DecisionDate datetime2 NULL,
            Comments nvarchar(max) NOT NULL,
            AssignedDate datetime2 NULL,
            DueDate datetime2 NULL,
            IsRequired bit NOT NULL,
            Sequence int NOT NULL,
            CreatedAt datetime2 NOT NULL CONSTRAINT DF_DCRApprovalFlows_CreatedAt DEFAULT(SYSDATETIME()),
            AuthMethod nvarchar(80) NOT NULL CONSTRAINT DF_DCRApprovalFlows_AuthMethod_Base DEFAULT(N''),
            AuthenticatedAt datetime2 NULL,
            WindowsIdentity nvarchar(256) NOT NULL CONSTRAINT DF_DCRApprovalFlows_WindowsIdentity_Base DEFAULT(N''),
            SignatureHash nvarchar(64) NOT NULL CONSTRAINT DF_DCRApprovalFlows_SignatureHash_Base DEFAULT(N''),
            ReminderCount int NOT NULL CONSTRAINT DF_DCRApprovalFlows_ReminderCount_Base DEFAULT(0),
            LastReminderAt datetime2 NULL
        );
    END;

    IF OBJECT_ID(N'dbo.DCRApprovalPlanEntries', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.DCRApprovalPlanEntries
        (
            Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DCRApprovalPlanEntries PRIMARY KEY,
            RequestId int NOT NULL,
            LevelNumber int NOT NULL,
            LevelName nvarchar(200) NOT NULL CONSTRAINT DF_DCRApprovalPlanEntries_LevelName_Base DEFAULT(N''),
            ApproverId int NOT NULL,
            Sequence int NOT NULL CONSTRAINT DF_DCRApprovalPlanEntries_Sequence_Base DEFAULT(1),
            IsRequired bit NOT NULL CONSTRAINT DF_DCRApprovalPlanEntries_IsRequired_Base DEFAULT(1),
            CreatedAt datetime2 NOT NULL CONSTRAINT DF_DCRApprovalPlanEntries_CreatedAt_Base DEFAULT(SYSDATETIME())
        );
    END

    IF OBJECT_ID(N'dbo.UserApprovalPlanEntries', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.UserApprovalPlanEntries
        (
            Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_UserApprovalPlanEntries PRIMARY KEY,
            OwnerUserId int NOT NULL,
            Rank nvarchar(10) NOT NULL CONSTRAINT DF_UserApprovalPlanEntries_Rank_Base DEFAULT(N'C'),
            LevelNumber int NOT NULL,
            LevelName nvarchar(200) NOT NULL CONSTRAINT DF_UserApprovalPlanEntries_LevelName_Base DEFAULT(N''),
            ApproverId int NOT NULL,
            BusinessUnitId int NULL,
            BusinessUnitCode nvarchar(40) NOT NULL CONSTRAINT DF_UserApprovalPlanEntries_BusinessUnitCode_Base DEFAULT(N''),
            BusinessUnitName nvarchar(160) NOT NULL CONSTRAINT DF_UserApprovalPlanEntries_BusinessUnitName_Base DEFAULT(N''),
            DepartmentId int NULL,
            DepartmentCode nvarchar(40) NOT NULL CONSTRAINT DF_UserApprovalPlanEntries_DepartmentCode_Base DEFAULT(N''),
            DepartmentName nvarchar(160) NOT NULL CONSTRAINT DF_UserApprovalPlanEntries_DepartmentName_Base DEFAULT(N''),
            ApproverRole nvarchar(80) NOT NULL CONSTRAINT DF_UserApprovalPlanEntries_ApproverRole_Base DEFAULT(N''),
            ApproverName nvarchar(160) NOT NULL CONSTRAINT DF_UserApprovalPlanEntries_ApproverName_Base DEFAULT(N''),
            ApproverEmail nvarchar(200) NOT NULL CONSTRAINT DF_UserApprovalPlanEntries_ApproverEmail_Base DEFAULT(N''),
            Sequence int NOT NULL CONSTRAINT DF_UserApprovalPlanEntries_Sequence_Base DEFAULT(1),
            CreatedAt datetime2 NOT NULL CONSTRAINT DF_UserApprovalPlanEntries_CreatedAt_Base DEFAULT(SYSDATETIME()),
            UpdatedAt datetime2 NOT NULL CONSTRAINT DF_UserApprovalPlanEntries_UpdatedAt_Base DEFAULT(SYSDATETIME())
        );
    END

    IF OBJECT_ID(N'dbo.ApprovalPlanTemplates', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ApprovalPlanTemplates
        (
            Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ApprovalPlanTemplates PRIMARY KEY,
            Name nvarchar(160) NOT NULL,
            Rank nvarchar(10) NOT NULL CONSTRAINT DF_ApprovalPlanTemplates_Rank_Base DEFAULT(N'C'),
            IsDefault bit NOT NULL CONSTRAINT DF_ApprovalPlanTemplates_IsDefault_Base DEFAULT(0),
            IsActive bit NOT NULL CONSTRAINT DF_ApprovalPlanTemplates_IsActive_Base DEFAULT(1),
            CreatedAt datetime2 NOT NULL CONSTRAINT DF_ApprovalPlanTemplates_CreatedAt_Base DEFAULT(SYSDATETIME()),
            UpdatedAt datetime2 NOT NULL CONSTRAINT DF_ApprovalPlanTemplates_UpdatedAt_Base DEFAULT(SYSDATETIME())
        );
    END

    IF OBJECT_ID(N'dbo.ApprovalPlanTemplateEntries', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ApprovalPlanTemplateEntries
        (
            Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ApprovalPlanTemplateEntries PRIMARY KEY,
            TemplateId int NOT NULL,
            LevelNumber int NOT NULL,
            LevelName nvarchar(200) NOT NULL CONSTRAINT DF_ApprovalPlanTemplateEntries_LevelName_Base DEFAULT(N''),
            ApproverId int NOT NULL,
            Sequence int NOT NULL CONSTRAINT DF_ApprovalPlanTemplateEntries_Sequence_Base DEFAULT(1)
        );
    END

    IF OBJECT_ID(N'dbo.DCRAttachments', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.DCRAttachments
        (
            Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DCRAttachments PRIMARY KEY,
            RequestId int NOT NULL,
            AttachmentType nvarchar(80) NOT NULL,
            FileName nvarchar(260) NOT NULL,
            OriginalFileName nvarchar(260) NOT NULL,
            StoredFileName nvarchar(260) NOT NULL,
            FilePath nvarchar(1000) NOT NULL,
            FileExtension nvarchar(20) NOT NULL,
            FileSize bigint NOT NULL,
            StoredFileSize bigint NOT NULL CONSTRAINT DF_DCRAttachments_StoredFileSize_Base DEFAULT(0),
            IsCompressed bit NOT NULL CONSTRAINT DF_DCRAttachments_IsCompressed_Base DEFAULT(0),
            CompressionType nvarchar(20) NOT NULL CONSTRAINT DF_DCRAttachments_CompressionType_Base DEFAULT(N''),
            OriginalSha256Hash nvarchar(64) NOT NULL CONSTRAINT DF_DCRAttachments_OriginalSha256Hash_Base DEFAULT(N''),
            Sha256Hash nvarchar(64) NOT NULL CONSTRAINT DF_DCRAttachments_Sha256Hash_Base DEFAULT(N''),
            StorageProvider nvarchar(40) NOT NULL CONSTRAINT DF_DCRAttachments_StorageProvider_Base DEFAULT(N'FileSystem'),
            UploadedBy int NOT NULL,
            UploadedAt datetime2 NOT NULL CONSTRAINT DF_DCRAttachments_UploadedAt DEFAULT(SYSDATETIME()),
            IsDeleted bit NOT NULL CONSTRAINT DF_DCRAttachments_IsDeleted DEFAULT(0)
        );
    END;

    IF OBJECT_ID(N'dbo.AuditLogs', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.AuditLogs
        (
            Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuditLogs PRIMARY KEY,
            RequestId int NULL,
            UserId int NULL,
            Action nvarchar(100) NOT NULL,
            EntityName nvarchar(100) NOT NULL,
            EntityId nvarchar(100) NOT NULL,
            OldValue nvarchar(max) NOT NULL,
            NewValue nvarchar(max) NOT NULL,
            ComputerName nvarchar(200) NOT NULL,
            IpAddress nvarchar(64) NOT NULL CONSTRAINT DF_AuditLogs_IpAddress_Base DEFAULT(N''),
            WindowsIdentity nvarchar(256) NOT NULL CONSTRAINT DF_AuditLogs_WindowsIdentity_Base DEFAULT(N''),
            SessionId nvarchar(64) NOT NULL CONSTRAINT DF_AuditLogs_SessionId_Base DEFAULT(N''),
            CreatedAt datetime2 NOT NULL CONSTRAINT DF_AuditLogs_CreatedAt DEFAULT(SYSDATETIME())
        );
    END;

    IF OBJECT_ID(N'dbo.WorkflowStageTemplates', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.WorkflowStageTemplates
        (
            Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_WorkflowStageTemplates PRIMARY KEY,
            StageNumber int NOT NULL,
            StageCode nvarchar(80) NOT NULL,
            StageName nvarchar(300) NOT NULL,
            ApproverRole nvarchar(80) NOT NULL,
            IsImpactedDepartmentStage bit NOT NULL,
            IsActive bit NOT NULL CONSTRAINT DF_WorkflowStageTemplates_IsActive DEFAULT(1)
        );
    END;

    IF OBJECT_ID(N'dbo.ApprovalMatrixRules', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ApprovalMatrixRules
        (
            Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ApprovalMatrixRules PRIMARY KEY,
            StageCode nvarchar(80) NOT NULL,
            RequestingDepartmentId int NULL,
            TargetDepartmentId int NULL,
            ApproverSource nvarchar(80) NOT NULL,
            ApproverRole nvarchar(80) NOT NULL CONSTRAINT DF_ApprovalMatrixRules_ApproverRole_Base DEFAULT(N''),
            ApproverUserId int NULL,
            Priority int NOT NULL CONSTRAINT DF_ApprovalMatrixRules_Priority_Base DEFAULT(100),
            IsActive bit NOT NULL CONSTRAINT DF_ApprovalMatrixRules_IsActive_Base DEFAULT(1),
            Description nvarchar(300) NOT NULL CONSTRAINT DF_ApprovalMatrixRules_Description_Base DEFAULT(N'')
        );
    END;

    IF OBJECT_ID(N'dbo.DCRNotificationLogs', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.DCRNotificationLogs
        (
            Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_DCRNotificationLogs PRIMARY KEY,
            RequestId int NOT NULL,
            ApprovalFlowId int NULL,
            NotificationType nvarchar(60) NOT NULL,
            Recipient nvarchar(300) NOT NULL,
            SentAt datetime2 NOT NULL CONSTRAINT DF_DCRNotificationLogs_SentAt DEFAULT(SYSDATETIME()),
            Success bit NOT NULL,
            Details nvarchar(1000) NOT NULL CONSTRAINT DF_DCRNotificationLogs_Details_Base DEFAULT(N'')
        );
    END;


    IF OBJECT_ID(N'dbo.EmailOutbox', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.EmailOutbox
        (
            Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmailOutbox PRIMARY KEY,
            RequestId int NULL,
            ApprovalFlowId int NULL,
            NotificationLogId bigint NULL,
            NotificationType nvarchar(60) NOT NULL CONSTRAINT DF_EmailOutbox_NotificationType_Base DEFAULT(N''),
            Recipient nvarchar(300) NOT NULL,
            Subject nvarchar(500) NOT NULL,
            Body nvarchar(max) NOT NULL,
            AttachmentFilePath nvarchar(1600) NOT NULL CONSTRAINT DF_EmailOutbox_AttachmentFilePath_Base DEFAULT(N''),
            AttachmentFileName nvarchar(260) NOT NULL CONSTRAINT DF_EmailOutbox_AttachmentFileName_Base DEFAULT(N''),
            AttachmentContentType nvarchar(120) NOT NULL CONSTRAINT DF_EmailOutbox_AttachmentContentType_Base DEFAULT(N''),
            Status nvarchar(40) NOT NULL CONSTRAINT DF_EmailOutbox_Status_Base DEFAULT(N'Pending'),
            RetryCount int NOT NULL CONSTRAINT DF_EmailOutbox_RetryCount_Base DEFAULT(0),
            MaxRetryCount int NOT NULL CONSTRAINT DF_EmailOutbox_MaxRetryCount_Base DEFAULT(3),
            CreatedAt datetime2 NOT NULL CONSTRAINT DF_EmailOutbox_CreatedAt_Base DEFAULT(SYSDATETIME()),
            NextAttemptAt datetime2 NULL,
            LastAttemptAt datetime2 NULL,
            SentAt datetime2 NULL,
            SenderAccount nvarchar(300) NOT NULL CONSTRAINT DF_EmailOutbox_SenderAccount_Base DEFAULT(N''),
            LastError nvarchar(2000) NOT NULL CONSTRAINT DF_EmailOutbox_LastError_Base DEFAULT(N''),
            RowVersion rowversion NOT NULL
        );
    END;

    IF OBJECT_ID(N'dbo.DCRDeletionLogs', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.DCRDeletionLogs
        (
            Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_DCRDeletionLogs PRIMARY KEY,
            OriginalRequestId int NOT NULL,
            DCRNumber nvarchar(40) NOT NULL,
            DeletedBy int NOT NULL,
            DeletedAt datetime2 NOT NULL CONSTRAINT DF_DCRDeletionLogs_DeletedAt DEFAULT(SYSDATETIME()),
            SnapshotJson nvarchar(max) NOT NULL,
            CleanupStatus nvarchar(40) NOT NULL CONSTRAINT DF_DCRDeletionLogs_CleanupStatus DEFAULT(N'Pending'),
            CleanupDetails nvarchar(max) NOT NULL CONSTRAINT DF_DCRDeletionLogs_CleanupDetails DEFAULT(N''),
            ComputerName nvarchar(200) NOT NULL CONSTRAINT DF_DCRDeletionLogs_ComputerName DEFAULT(N''),
            IpAddress nvarchar(64) NOT NULL CONSTRAINT DF_DCRDeletionLogs_IpAddress DEFAULT(N''),
            WindowsIdentity nvarchar(256) NOT NULL CONSTRAINT DF_DCRDeletionLogs_WindowsIdentity DEFAULT(N''),
            SessionId nvarchar(64) NOT NULL CONSTRAINT DF_DCRDeletionLogs_SessionId DEFAULT(N'')
        );
    END;

    IF OBJECT_ID(N'dbo.DcrNumberSequences', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.DcrNumberSequences
        (
            [Year] int NOT NULL CONSTRAINT PK_DcrNumberSequences PRIMARY KEY,
            LastNumber int NOT NULL
        );
    END;

    IF OBJECT_ID(N'dbo.SystemSettings', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.SystemSettings
        (
            [Key] nvarchar(120) NOT NULL CONSTRAINT PK_SystemSettings PRIMARY KEY,
            [Value] nvarchar(max) NOT NULL CONSTRAINT DF_SystemSettings_Value_Base DEFAULT(N''),
            UpdatedAt datetime2 NOT NULL CONSTRAINT DF_SystemSettings_UpdatedAt_Base DEFAULT(SYSDATETIME())
        );
    END;

    /* Core indexes */
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Users') AND name = N'IX_Users_Username')
        CREATE UNIQUE INDEX IX_Users_Username ON dbo.Users(Username);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Users') AND name IN (N'UX_Users_WindowsAccount', N'IX_Users_WindowsAccount'))
        CREATE UNIQUE INDEX UX_Users_WindowsAccount ON dbo.Users(WindowsAccount) WHERE WindowsAccount <> N'';

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Departments') AND name = N'IX_Departments_DepartmentCode')
        CREATE UNIQUE INDEX IX_Departments_DepartmentCode ON dbo.Departments(DepartmentCode);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.BusinessUnits') AND name = N'UX_BusinessUnits_UnitCode')
        CREATE UNIQUE INDEX UX_BusinessUnits_UnitCode ON dbo.BusinessUnits(UnitCode);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.DCRRequests') AND name = N'IX_DCRRequests_DCRNumber')
        CREATE UNIQUE INDEX IX_DCRRequests_DCRNumber ON dbo.DCRRequests(DCRNumber);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.WorkflowStageTemplates') AND name = N'IX_WorkflowStageTemplates_StageNumber')
        CREATE UNIQUE INDEX IX_WorkflowStageTemplates_StageNumber ON dbo.WorkflowStageTemplates(StageNumber);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.DCRImpactedDepartments') AND name = N'IX_DCRImpactedDepartments_RequestId_DepartmentId')
        CREATE UNIQUE INDEX IX_DCRImpactedDepartments_RequestId_DepartmentId ON dbo.DCRImpactedDepartments(RequestId, DepartmentId);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.DCRApprovalPlanEntries') AND name = N'UX_DCRApprovalPlanEntries_Request_Approver')
        CREATE UNIQUE INDEX UX_DCRApprovalPlanEntries_Request_Approver ON dbo.DCRApprovalPlanEntries(RequestId, ApproverId);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.DCRApprovalPlanEntries') AND name = N'IX_DCRApprovalPlanEntries_Request_Level_Sequence')
        CREATE INDEX IX_DCRApprovalPlanEntries_Request_Level_Sequence ON dbo.DCRApprovalPlanEntries(RequestId, LevelNumber, Sequence);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.UserApprovalPlanEntries') AND name = N'UX_UserApprovalPlanEntries_Owner_Rank_Approver')
        CREATE UNIQUE INDEX UX_UserApprovalPlanEntries_Owner_Rank_Approver ON dbo.UserApprovalPlanEntries(OwnerUserId, Rank, ApproverId);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.UserApprovalPlanEntries') AND name = N'IX_UserApprovalPlanEntries_Owner_Rank_Level_Sequence')
        CREATE INDEX IX_UserApprovalPlanEntries_Owner_Rank_Level_Sequence ON dbo.UserApprovalPlanEntries(OwnerUserId, Rank, LevelNumber, Sequence);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ApprovalPlanTemplates') AND name = N'UX_ApprovalPlanTemplates_Rank_Name')
        CREATE UNIQUE INDEX UX_ApprovalPlanTemplates_Rank_Name ON dbo.ApprovalPlanTemplates(Rank, Name);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ApprovalPlanTemplateEntries') AND name = N'UX_ApprovalPlanTemplateEntries_Template_Approver')
        CREATE UNIQUE INDEX UX_ApprovalPlanTemplateEntries_Template_Approver ON dbo.ApprovalPlanTemplateEntries(TemplateId, ApproverId);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ApprovalPlanTemplateEntries') AND name = N'IX_ApprovalPlanTemplateEntries_Template_Level_Sequence')
        CREATE INDEX IX_ApprovalPlanTemplateEntries_Template_Level_Sequence ON dbo.ApprovalPlanTemplateEntries(TemplateId, LevelNumber, Sequence);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.DCRApprovalFlows') AND name IN (N'IX_DCRApprovalFlows_Request_Revision_Stage_Sequence', N'IX_DCRApprovalFlows_RequestId_RevisionNo_StageNumber_Sequence'))
        CREATE INDEX IX_DCRApprovalFlows_Request_Revision_Stage_Sequence ON dbo.DCRApprovalFlows(RequestId, RevisionNo, StageNumber, Sequence);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ApprovalMatrixRules') AND name IN (N'IX_ApprovalMatrixRules_Routing', N'IX_ApprovalMatrixRules_StageCode_RequestingDepartmentId_TargetDepartmentId_Priority'))
        CREATE INDEX IX_ApprovalMatrixRules_Routing ON dbo.ApprovalMatrixRules(StageCode, RequestingDepartmentId, TargetDepartmentId, Priority);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.DCRNotificationLogs') AND name IN (N'IX_DCRNotificationLogs_Request_Type_SentAt', N'IX_DCRNotificationLogs_RequestId_ApprovalFlowId_NotificationType_SentAt'))
        CREATE INDEX IX_DCRNotificationLogs_Request_Type_SentAt ON dbo.DCRNotificationLogs(RequestId, ApprovalFlowId, NotificationType, SentAt);


    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.EmailOutbox') AND name = N'IX_EmailOutbox_Status_NextAttemptAt_CreatedAt')
        CREATE INDEX IX_EmailOutbox_Status_NextAttemptAt_CreatedAt ON dbo.EmailOutbox(Status, NextAttemptAt, CreatedAt);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.EmailOutbox') AND name = N'IX_EmailOutbox_RequestId_NotificationType_CreatedAt')
        CREATE INDEX IX_EmailOutbox_RequestId_NotificationType_CreatedAt ON dbo.EmailOutbox(RequestId, NotificationType, CreatedAt);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.DCRDeletionLogs') AND name = N'IX_DCRDeletionLogs_DCRNumber_DeletedAt')
        CREATE INDEX IX_DCRDeletionLogs_DCRNumber_DeletedAt ON dbo.DCRDeletionLogs(DCRNumber, DeletedAt);

    /* Foreign keys. Add only when absent so this script can repair a partial DB. */
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.BusinessUnits') AND name=N'FK_BusinessUnits_Users_DirectorUserId')
        ALTER TABLE dbo.BusinessUnits WITH CHECK ADD CONSTRAINT FK_BusinessUnits_Users_DirectorUserId FOREIGN KEY(DirectorUserId) REFERENCES dbo.Users(UserId);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.Users') AND name=N'FK_Users_BusinessUnits_BusinessUnitId')
        ALTER TABLE dbo.Users WITH CHECK ADD CONSTRAINT FK_Users_BusinessUnits_BusinessUnitId FOREIGN KEY(BusinessUnitId) REFERENCES dbo.BusinessUnits(Id);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.Departments') AND name=N'FK_Departments_BusinessUnits_BusinessUnitId')
        ALTER TABLE dbo.Departments WITH CHECK ADD CONSTRAINT FK_Departments_BusinessUnits_BusinessUnitId FOREIGN KEY(BusinessUnitId) REFERENCES dbo.BusinessUnits(Id);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.Users') AND name=N'FK_Users_Departments_DepartmentId')
        ALTER TABLE dbo.Users WITH CHECK ADD CONSTRAINT FK_Users_Departments_DepartmentId FOREIGN KEY(DepartmentId) REFERENCES dbo.Departments(Id);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.Users') AND name=N'FK_Users_Users_DirectManagerUserId')
        ALTER TABLE dbo.Users WITH CHECK ADD CONSTRAINT FK_Users_Users_DirectManagerUserId FOREIGN KEY(DirectManagerUserId) REFERENCES dbo.Users(UserId);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.Departments') AND name=N'FK_Departments_Users_ManagerUserId')
        ALTER TABLE dbo.Departments WITH CHECK ADD CONSTRAINT FK_Departments_Users_ManagerUserId FOREIGN KEY(ManagerUserId) REFERENCES dbo.Users(UserId);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.Departments') AND name=N'FK_Departments_Users_DirectorUserId')
        ALTER TABLE dbo.Departments WITH CHECK ADD CONSTRAINT FK_Departments_Users_DirectorUserId FOREIGN KEY(DirectorUserId) REFERENCES dbo.Users(UserId);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.DCRRequests') AND name=N'FK_DCRRequests_Departments_RequestingDepartmentId')
        ALTER TABLE dbo.DCRRequests WITH CHECK ADD CONSTRAINT FK_DCRRequests_Departments_RequestingDepartmentId FOREIGN KEY(RequestingDepartmentId) REFERENCES dbo.Departments(Id);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.DCRRequests') AND name=N'FK_DCRRequests_Users_RequestOwnerId')
        ALTER TABLE dbo.DCRRequests WITH CHECK ADD CONSTRAINT FK_DCRRequests_Users_RequestOwnerId FOREIGN KEY(RequestOwnerId) REFERENCES dbo.Users(UserId);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.DCRRequests') AND name=N'FK_DCRRequests_Users_CreatedBy')
        ALTER TABLE dbo.DCRRequests WITH CHECK ADD CONSTRAINT FK_DCRRequests_Users_CreatedBy FOREIGN KEY(CreatedBy) REFERENCES dbo.Users(UserId);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.DCRParts') AND name=N'FK_DCRParts_DCRRequests_RequestId')
        ALTER TABLE dbo.DCRParts WITH CHECK ADD CONSTRAINT FK_DCRParts_DCRRequests_RequestId FOREIGN KEY(RequestId) REFERENCES dbo.DCRRequests(Id) ON DELETE CASCADE;

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.DCRImpactedDepartments') AND name=N'FK_DCRImpactedDepartments_DCRRequests_RequestId')
        ALTER TABLE dbo.DCRImpactedDepartments WITH CHECK ADD CONSTRAINT FK_DCRImpactedDepartments_DCRRequests_RequestId FOREIGN KEY(RequestId) REFERENCES dbo.DCRRequests(Id) ON DELETE CASCADE;

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.DCRImpactedDepartments') AND name=N'FK_DCRImpactedDepartments_Departments_DepartmentId')
        ALTER TABLE dbo.DCRImpactedDepartments WITH CHECK ADD CONSTRAINT FK_DCRImpactedDepartments_Departments_DepartmentId FOREIGN KEY(DepartmentId) REFERENCES dbo.Departments(Id);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.DCRApprovalPlanEntries') AND name=N'FK_DCRApprovalPlanEntries_DCRRequests_RequestId')
        ALTER TABLE dbo.DCRApprovalPlanEntries WITH CHECK ADD CONSTRAINT FK_DCRApprovalPlanEntries_DCRRequests_RequestId FOREIGN KEY(RequestId) REFERENCES dbo.DCRRequests(Id) ON DELETE CASCADE;

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.DCRApprovalPlanEntries') AND name=N'FK_DCRApprovalPlanEntries_Users_ApproverId')
        ALTER TABLE dbo.DCRApprovalPlanEntries WITH CHECK ADD CONSTRAINT FK_DCRApprovalPlanEntries_Users_ApproverId FOREIGN KEY(ApproverId) REFERENCES dbo.Users(UserId);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.UserApprovalPlanEntries') AND name=N'FK_UserApprovalPlanEntries_Users_OwnerUserId')
        ALTER TABLE dbo.UserApprovalPlanEntries WITH CHECK ADD CONSTRAINT FK_UserApprovalPlanEntries_Users_OwnerUserId FOREIGN KEY(OwnerUserId) REFERENCES dbo.Users(UserId) ON DELETE CASCADE;

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.UserApprovalPlanEntries') AND name=N'FK_UserApprovalPlanEntries_Users_ApproverId')
        ALTER TABLE dbo.UserApprovalPlanEntries WITH CHECK ADD CONSTRAINT FK_UserApprovalPlanEntries_Users_ApproverId FOREIGN KEY(ApproverId) REFERENCES dbo.Users(UserId);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.ApprovalPlanTemplateEntries') AND name=N'FK_ApprovalPlanTemplateEntries_ApprovalPlanTemplates_TemplateId')
        ALTER TABLE dbo.ApprovalPlanTemplateEntries WITH CHECK ADD CONSTRAINT FK_ApprovalPlanTemplateEntries_ApprovalPlanTemplates_TemplateId FOREIGN KEY(TemplateId) REFERENCES dbo.ApprovalPlanTemplates(Id) ON DELETE CASCADE;

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.ApprovalPlanTemplateEntries') AND name=N'FK_ApprovalPlanTemplateEntries_Users_ApproverId')
        ALTER TABLE dbo.ApprovalPlanTemplateEntries WITH CHECK ADD CONSTRAINT FK_ApprovalPlanTemplateEntries_Users_ApproverId FOREIGN KEY(ApproverId) REFERENCES dbo.Users(UserId);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.DCRApprovalFlows') AND name=N'FK_DCRApprovalFlows_DCRRequests_RequestId')
        ALTER TABLE dbo.DCRApprovalFlows WITH CHECK ADD CONSTRAINT FK_DCRApprovalFlows_DCRRequests_RequestId FOREIGN KEY(RequestId) REFERENCES dbo.DCRRequests(Id) ON DELETE CASCADE;

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.DCRApprovalFlows') AND name=N'FK_DCRApprovalFlows_Users_ApproverId')
        ALTER TABLE dbo.DCRApprovalFlows WITH CHECK ADD CONSTRAINT FK_DCRApprovalFlows_Users_ApproverId FOREIGN KEY(ApproverId) REFERENCES dbo.Users(UserId);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.DCRApprovalFlows') AND name=N'FK_DCRApprovalFlows_Departments_DepartmentId')
        ALTER TABLE dbo.DCRApprovalFlows WITH CHECK ADD CONSTRAINT FK_DCRApprovalFlows_Departments_DepartmentId FOREIGN KEY(DepartmentId) REFERENCES dbo.Departments(Id);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.DCRAttachments') AND name=N'FK_DCRAttachments_DCRRequests_RequestId')
        ALTER TABLE dbo.DCRAttachments WITH CHECK ADD CONSTRAINT FK_DCRAttachments_DCRRequests_RequestId FOREIGN KEY(RequestId) REFERENCES dbo.DCRRequests(Id) ON DELETE CASCADE;

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.DCRAttachments') AND name=N'FK_DCRAttachments_Users_UploadedBy')
        ALTER TABLE dbo.DCRAttachments WITH CHECK ADD CONSTRAINT FK_DCRAttachments_Users_UploadedBy FOREIGN KEY(UploadedBy) REFERENCES dbo.Users(UserId);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.AuditLogs') AND name=N'FK_AuditLogs_DCRRequests_RequestId')
        ALTER TABLE dbo.AuditLogs WITH CHECK ADD CONSTRAINT FK_AuditLogs_DCRRequests_RequestId FOREIGN KEY(RequestId) REFERENCES dbo.DCRRequests(Id);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.AuditLogs') AND name=N'FK_AuditLogs_Users_UserId')
        ALTER TABLE dbo.AuditLogs WITH CHECK ADD CONSTRAINT FK_AuditLogs_Users_UserId FOREIGN KEY(UserId) REFERENCES dbo.Users(UserId);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.ApprovalMatrixRules') AND name IN (N'FK_ApprovalMatrixRules_RequestingDepartment', N'FK_ApprovalMatrixRules_Departments_RequestingDepartmentId'))
        ALTER TABLE dbo.ApprovalMatrixRules WITH CHECK ADD CONSTRAINT FK_ApprovalMatrixRules_RequestingDepartment FOREIGN KEY(RequestingDepartmentId) REFERENCES dbo.Departments(Id);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.ApprovalMatrixRules') AND name IN (N'FK_ApprovalMatrixRules_TargetDepartment', N'FK_ApprovalMatrixRules_Departments_TargetDepartmentId'))
        ALTER TABLE dbo.ApprovalMatrixRules WITH CHECK ADD CONSTRAINT FK_ApprovalMatrixRules_TargetDepartment FOREIGN KEY(TargetDepartmentId) REFERENCES dbo.Departments(Id);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.ApprovalMatrixRules') AND name IN (N'FK_ApprovalMatrixRules_ApproverUser', N'FK_ApprovalMatrixRules_Users_ApproverUserId'))
        ALTER TABLE dbo.ApprovalMatrixRules WITH CHECK ADD CONSTRAINT FK_ApprovalMatrixRules_ApproverUser FOREIGN KEY(ApproverUserId) REFERENCES dbo.Users(UserId);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.DCRNotificationLogs') AND name IN (N'FK_DCRNotificationLogs_Request', N'FK_DCRNotificationLogs_DCRRequests_RequestId'))
        ALTER TABLE dbo.DCRNotificationLogs WITH CHECK ADD CONSTRAINT FK_DCRNotificationLogs_Request FOREIGN KEY(RequestId) REFERENCES dbo.DCRRequests(Id) ON DELETE CASCADE;

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.DCRNotificationLogs') AND name IN (N'FK_DCRNotificationLogs_ApprovalFlow', N'FK_DCRNotificationLogs_DCRApprovalFlows_ApprovalFlowId'))
        ALTER TABLE dbo.DCRNotificationLogs WITH CHECK ADD CONSTRAINT FK_DCRNotificationLogs_ApprovalFlow FOREIGN KEY(ApprovalFlowId) REFERENCES dbo.DCRApprovalFlows(Id);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.DCRDeletionLogs') AND name=N'FK_DCRDeletionLogs_Users_DeletedBy')
        ALTER TABLE dbo.DCRDeletionLogs WITH CHECK ADD CONSTRAINT FK_DCRDeletionLogs_Users_DeletedBy FOREIGN KEY(DeletedBy) REFERENCES dbo.Users(UserId);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

PRINT N'DCRManagement base schema is ready. Next run ProductionUpgrade.sql, then start the application.';
GO
