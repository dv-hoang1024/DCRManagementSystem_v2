using Microsoft.EntityFrameworkCore;

namespace DCRManagementSystem.Data;

public static class DatabaseUpgradeService
{
    private const string CurrentSchemaVersion = "2026.09.06.1";

    public static void Apply(AppDbContext db)
    {
        EnsureBaseSchemaExists(db);
        EnsureSchemaVersionTable(db);
        if (IsSchemaVersionApplied(db, CurrentSchemaVersion)) return;
        var commands = new[]
        {
            "IF COL_LENGTH('DCRRequests','CreationToken') IS NULL ALTER TABLE DCRRequests ADD CreationToken uniqueidentifier NULL;",
            @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_DCRRequests_CreatedBy_CreationToken' AND object_id=OBJECT_ID('DCRRequests'))
              CREATE UNIQUE INDEX UX_DCRRequests_CreatedBy_CreationToken ON DCRRequests(CreatedBy, CreationToken) WHERE CreationToken IS NOT NULL;",
            "IF COL_LENGTH('DCRRequests','RevisionNo') IS NULL ALTER TABLE DCRRequests ADD RevisionNo int NOT NULL CONSTRAINT DF_DCRRequests_RevisionNo DEFAULT(1);",
            "IF COL_LENGTH('DCRRequests','DraftStep') IS NULL ALTER TABLE DCRRequests ADD DraftStep int NOT NULL CONSTRAINT DF_DCRRequests_DraftStep DEFAULT(0);",
            "IF COL_LENGTH('DCRRequests','LastSavedAt') IS NULL ALTER TABLE DCRRequests ADD LastSavedAt datetime2 NULL;",
            "IF COL_LENGTH('DCRRequests','ReturnedDate') IS NULL ALTER TABLE DCRRequests ADD ReturnedDate datetime2 NULL;",
            "IF COL_LENGTH('DCRRequests','LastReturnReason') IS NULL ALTER TABLE DCRRequests ADD LastReturnReason nvarchar(max) NOT NULL CONSTRAINT DF_DCRRequests_LastReturnReason DEFAULT('');",
            "IF COL_LENGTH('DCRRequests','FinalPdfPath') IS NULL ALTER TABLE DCRRequests ADD FinalPdfPath nvarchar(1000) NOT NULL CONSTRAINT DF_DCRRequests_FinalPdfPath DEFAULT('');",
            "IF COL_LENGTH('DCRRequests','FinalPdfSha256') IS NULL ALTER TABLE DCRRequests ADD FinalPdfSha256 nvarchar(64) NOT NULL CONSTRAINT DF_DCRRequests_FinalPdfSha256 DEFAULT('');",
            "IF COL_LENGTH('DCRRequests','FinalPdfGeneratedAt') IS NULL ALTER TABLE DCRRequests ADD FinalPdfGeneratedAt datetime2 NULL;",
            "IF COL_LENGTH('DCRRequests','MaterialChangeDescription') IS NULL ALTER TABLE DCRRequests ADD MaterialChangeDescription nvarchar(max) NOT NULL CONSTRAINT DF_DCRRequests_MaterialChangeDescription DEFAULT('');",
            "IF COL_LENGTH('DCRRequests','Rank') IS NULL ALTER TABLE DCRRequests ADD Rank nvarchar(10) NOT NULL CONSTRAINT DF_DCRRequests_Rank DEFAULT('C');",
            "IF COL_LENGTH('DCRRequests','Rank') IS NOT NULL UPDATE DCRRequests SET Rank='C' WHERE Rank IS NULL OR UPPER(LTRIM(RTRIM(Rank))) NOT IN ('A','B','C','S');",

            "IF COL_LENGTH('DCRRequests','RowVersion') IS NULL ALTER TABLE DCRRequests ADD RowVersion rowversion NOT NULL;",

            "IF COL_LENGTH('DCRApprovalFlows','RevisionNo') IS NULL ALTER TABLE DCRApprovalFlows ADD RevisionNo int NOT NULL CONSTRAINT DF_DCRApprovalFlows_RevisionNo DEFAULT(1);",
            "IF COL_LENGTH('DCRApprovalFlows','AuthMethod') IS NULL ALTER TABLE DCRApprovalFlows ADD AuthMethod nvarchar(80) NOT NULL CONSTRAINT DF_DCRApprovalFlows_AuthMethod DEFAULT('');",
            "IF COL_LENGTH('DCRApprovalFlows','AuthenticatedAt') IS NULL ALTER TABLE DCRApprovalFlows ADD AuthenticatedAt datetime2 NULL;",
            "IF COL_LENGTH('DCRApprovalFlows','WindowsIdentity') IS NULL ALTER TABLE DCRApprovalFlows ADD WindowsIdentity nvarchar(256) NOT NULL CONSTRAINT DF_DCRApprovalFlows_WindowsIdentity DEFAULT('');",
            "IF COL_LENGTH('DCRApprovalFlows','SignatureHash') IS NULL ALTER TABLE DCRApprovalFlows ADD SignatureHash nvarchar(64) NOT NULL CONSTRAINT DF_DCRApprovalFlows_SignatureHash DEFAULT('');",
            "IF COL_LENGTH('DCRApprovalFlows','ReminderCount') IS NULL ALTER TABLE DCRApprovalFlows ADD ReminderCount int NOT NULL CONSTRAINT DF_DCRApprovalFlows_ReminderCount DEFAULT(0);",
            "IF COL_LENGTH('DCRApprovalFlows','LastReminderAt') IS NULL ALTER TABLE DCRApprovalFlows ADD LastReminderAt datetime2 NULL;",

            "IF COL_LENGTH('Users','WindowsAccount') IS NULL ALTER TABLE Users ADD WindowsAccount nvarchar(256) NOT NULL CONSTRAINT DF_Users_WindowsAccount DEFAULT('');",
            @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_Users_WindowsAccount' AND object_id=OBJECT_ID('Users'))
              CREATE UNIQUE INDEX UX_Users_WindowsAccount ON Users(WindowsAccount) WHERE WindowsAccount <> '';",
            "IF COL_LENGTH('Users','IsDeleted') IS NULL ALTER TABLE Users ADD IsDeleted bit NOT NULL CONSTRAINT DF_Users_IsDeleted DEFAULT(0);",
            "IF COL_LENGTH('Users','DeletedAt') IS NULL ALTER TABLE Users ADD DeletedAt datetime2 NULL;",
            "IF COL_LENGTH('Users','DeletedBy') IS NULL ALTER TABLE Users ADD DeletedBy int NULL;",
            "IF COL_LENGTH('Users','DirectManagerUserId') IS NULL ALTER TABLE Users ADD DirectManagerUserId int NULL;",
            "IF COL_LENGTH('Users','BusinessUnitId') IS NULL ALTER TABLE Users ADD BusinessUnitId int NULL;",
            @"IF COL_LENGTH('Users','CanUseProductionTracking') IS NULL
              BEGIN
                ALTER TABLE Users ADD CanUseProductionTracking bit NOT NULL CONSTRAINT DF_Users_CanUseProductionTracking DEFAULT(0);
                EXEC(N'UPDATE u SET CanUseProductionTracking = 1
                  FROM Users u
                  LEFT JOIN Departments d ON d.Id = u.DepartmentId
                  WHERE u.IsDeleted = 0
                    AND (
                      u.Role = ''Administrator''
                      OR d.DepartmentName = N''Phòng Chất lượng''
                    );');
              END",
            @"IF COL_LENGTH('Users','CanUseWarehouseManagement') IS NULL
              BEGIN
                ALTER TABLE Users ADD CanUseWarehouseManagement bit NOT NULL CONSTRAINT DF_Users_CanUseWarehouseManagement DEFAULT(0);
                EXEC(N'UPDATE u SET CanUseWarehouseManagement = 1
                  FROM Users u
                  LEFT JOIN Departments d ON d.Id = u.DepartmentId
                  WHERE u.IsDeleted = 0
                    AND (
                      u.Role = ''Administrator''
                      OR d.DepartmentName = N''Phòng Sản xuất''
                    );');
              END",
            "IF COL_LENGTH('Departments','BusinessUnitId') IS NULL ALTER TABLE Departments ADD BusinessUnitId int NULL;",
            "IF COL_LENGTH('Departments','DirectorUserId') IS NULL ALTER TABLE Departments ADD DirectorUserId int NULL;",

            @"IF OBJECT_ID('Roles','U') IS NULL
              CREATE TABLE Roles(
                Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                RoleName nvarchar(80) NOT NULL,
                Description nvarchar(300) NOT NULL CONSTRAINT DF_Roles_Description DEFAULT(''),
                HierarchyLevel int NOT NULL CONSTRAINT DF_Roles_HierarchyLevel DEFAULT(10),
                IsSystemProtected bit NOT NULL CONSTRAINT DF_Roles_IsSystemProtected DEFAULT(0),
                IsActive bit NOT NULL CONSTRAINT DF_Roles_IsActive DEFAULT(1),
                CreatedAt datetime2 NOT NULL CONSTRAINT DF_Roles_CreatedAt DEFAULT(GETDATE())
              );",
            @"IF OBJECT_ID('Roles','U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_Roles_RoleName' AND object_id=OBJECT_ID('Roles'))
              CREATE UNIQUE INDEX UX_Roles_RoleName ON Roles(RoleName);",
            @"IF OBJECT_ID('ProductLines','U') IS NULL
              CREATE TABLE ProductLines(
                Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                Name nvarchar(100) NOT NULL,
                SortOrder int NOT NULL CONSTRAINT DF_ProductLines_SortOrder DEFAULT(0),
                IsActive bit NOT NULL CONSTRAINT DF_ProductLines_IsActive DEFAULT(1),
                CreatedAt datetime2 NOT NULL CONSTRAINT DF_ProductLines_CreatedAt DEFAULT(GETDATE())
              );",
            @"IF OBJECT_ID('ProductLines','U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_ProductLines_Name' AND object_id=OBJECT_ID('ProductLines'))
              CREATE UNIQUE INDEX UX_ProductLines_Name ON ProductLines(Name);",
            @"IF OBJECT_ID('PartChangeTypes','U') IS NULL
              CREATE TABLE PartChangeTypes(
                Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                Name nvarchar(80) NOT NULL,
                SortOrder int NOT NULL CONSTRAINT DF_PartChangeTypes_SortOrder DEFAULT(0),
                IsActive bit NOT NULL CONSTRAINT DF_PartChangeTypes_IsActive DEFAULT(1),
                CreatedAt datetime2 NOT NULL CONSTRAINT DF_PartChangeTypes_CreatedAt DEFAULT(GETDATE())
              );",
            @"IF OBJECT_ID('PartChangeTypes','U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_PartChangeTypes_Name' AND object_id=OBJECT_ID('PartChangeTypes'))
              CREATE UNIQUE INDEX UX_PartChangeTypes_Name ON PartChangeTypes(Name);",
            @"IF OBJECT_ID('BusinessUnits','U') IS NULL
              CREATE TABLE BusinessUnits(
                Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                UnitCode nvarchar(40) NOT NULL,
                UnitName nvarchar(160) NOT NULL,
                DirectorUserId int NULL,
                IsActive bit NOT NULL CONSTRAINT DF_BusinessUnits_IsActive DEFAULT(1)
              );",
            @"IF OBJECT_ID('BusinessUnits','U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_BusinessUnits_UnitCode' AND object_id=OBJECT_ID('BusinessUnits'))
              CREATE UNIQUE INDEX UX_BusinessUnits_UnitCode ON BusinessUnits(UnitCode);",
            @"IF OBJECT_ID('BusinessUnits','U') IS NOT NULL AND OBJECT_ID('Departments','U') IS NOT NULL
              UPDATE d SET BusinessUnitId = b.Id
              FROM Departments d CROSS JOIN (SELECT TOP(1) Id FROM BusinessUnits WHERE UnitCode='PROD') b
              WHERE d.BusinessUnitId IS NULL;",
            @"IF OBJECT_ID('BusinessUnits','U') IS NOT NULL AND OBJECT_ID('Users','U') IS NOT NULL
              UPDATE u SET BusinessUnitId = d.BusinessUnitId
              FROM Users u INNER JOIN Departments d ON d.Id = u.DepartmentId
              WHERE u.BusinessUnitId IS NULL AND d.BusinessUnitId IS NOT NULL;",
            @"IF OBJECT_ID('Users','U') IS NOT NULL
              BEGIN
                UPDATE Users SET Role='Director' WHERE Role='ChiefEngineer';
                UPDATE Users SET Role='Manager' WHERE Role IN ('DesignManager','MEManager');
                UPDATE Users SET Role='Staff' WHERE Role IN ('Initiator','Viewer');
              END",
            @"IF OBJECT_ID('WorkflowStageTemplates','U') IS NOT NULL
              BEGIN
                UPDATE WorkflowStageTemplates SET ApproverRole='Director' WHERE ApproverRole='ChiefEngineer';
                UPDATE WorkflowStageTemplates SET ApproverRole='Manager' WHERE ApproverRole IN ('DesignManager','MEManager');
                UPDATE WorkflowStageTemplates SET ApproverRole='Staff' WHERE ApproverRole IN ('Initiator','Viewer');
              END",
            @"IF OBJECT_ID('ApprovalMatrixRules','U') IS NOT NULL
              BEGIN
                UPDATE ApprovalMatrixRules SET ApproverRole='Director' WHERE ApproverRole='ChiefEngineer';
                UPDATE ApprovalMatrixRules SET ApproverRole='Manager' WHERE ApproverRole IN ('DesignManager','MEManager');
                UPDATE ApprovalMatrixRules SET ApproverRole='Staff' WHERE ApproverRole IN ('Initiator','Viewer');
              END",

            "IF COL_LENGTH('DCRAttachments','Sha256Hash') IS NULL ALTER TABLE DCRAttachments ADD Sha256Hash nvarchar(64) NOT NULL CONSTRAINT DF_DCRAttachments_Sha256Hash DEFAULT('');",
            "IF COL_LENGTH('DCRAttachments','OriginalSha256Hash') IS NULL ALTER TABLE DCRAttachments ADD OriginalSha256Hash nvarchar(64) NOT NULL CONSTRAINT DF_DCRAttachments_OriginalSha256Hash DEFAULT('');",
            "IF COL_LENGTH('DCRAttachments','StoredFileSize') IS NULL ALTER TABLE DCRAttachments ADD StoredFileSize bigint NOT NULL CONSTRAINT DF_DCRAttachments_StoredFileSize DEFAULT(0);",
            "IF COL_LENGTH('DCRAttachments','IsCompressed') IS NULL ALTER TABLE DCRAttachments ADD IsCompressed bit NOT NULL CONSTRAINT DF_DCRAttachments_IsCompressed DEFAULT(0);",
            "IF COL_LENGTH('DCRAttachments','CompressionType') IS NULL ALTER TABLE DCRAttachments ADD CompressionType nvarchar(20) NOT NULL CONSTRAINT DF_DCRAttachments_CompressionType DEFAULT('');",
            "IF COL_LENGTH('DCRAttachments','StorageProvider') IS NULL ALTER TABLE DCRAttachments ADD StorageProvider nvarchar(40) NOT NULL CONSTRAINT DF_DCRAttachments_StorageProvider DEFAULT('FileSystem');",

            "IF COL_LENGTH('AuditLogs','IpAddress') IS NULL ALTER TABLE AuditLogs ADD IpAddress nvarchar(64) NOT NULL CONSTRAINT DF_AuditLogs_IpAddress DEFAULT('');",
            "IF COL_LENGTH('AuditLogs','WindowsIdentity') IS NULL ALTER TABLE AuditLogs ADD WindowsIdentity nvarchar(256) NOT NULL CONSTRAINT DF_AuditLogs_WindowsIdentity DEFAULT('');",
            "IF COL_LENGTH('AuditLogs','SessionId') IS NULL ALTER TABLE AuditLogs ADD SessionId nvarchar(64) NOT NULL CONSTRAINT DF_AuditLogs_SessionId DEFAULT('');",
            "IF COL_LENGTH('AuditLogs','RequestId') IS NOT NULL ALTER TABLE AuditLogs ALTER COLUMN RequestId int NULL;",
            "IF COL_LENGTH('AuditLogs','UserId') IS NOT NULL ALTER TABLE AuditLogs ALTER COLUMN UserId int NULL;",
            @"IF OBJECT_ID('AuditLogs','U') IS NOT NULL
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
              END",

            @"IF OBJECT_ID('SystemSettings','U') IS NULL
              CREATE TABLE SystemSettings(
                [Key] nvarchar(120) NOT NULL PRIMARY KEY,
                [Value] nvarchar(max) NOT NULL CONSTRAINT DF_SystemSettings_Value DEFAULT(''),
                UpdatedAt datetime2 NOT NULL CONSTRAINT DF_SystemSettings_UpdatedAt DEFAULT(GETDATE())
              );",

            @"IF OBJECT_ID('UserBusinessUnitAssignments','U') IS NULL
              CREATE TABLE UserBusinessUnitAssignments(
                UserId int NOT NULL,
                BusinessUnitId int NOT NULL,
                IsPrimary bit NOT NULL CONSTRAINT DF_UserBusinessUnitAssignments_IsPrimary DEFAULT(0),
                CONSTRAINT PK_UserBusinessUnitAssignments PRIMARY KEY(UserId, BusinessUnitId),
                CONSTRAINT FK_UserBusinessUnitAssignments_Users_UserId FOREIGN KEY(UserId) REFERENCES Users(UserId) ON DELETE CASCADE,
                CONSTRAINT FK_UserBusinessUnitAssignments_BusinessUnits_BusinessUnitId FOREIGN KEY(BusinessUnitId) REFERENCES BusinessUnits(Id) ON DELETE CASCADE
              );",
            @"IF OBJECT_ID('UserDepartmentAssignments','U') IS NULL
              CREATE TABLE UserDepartmentAssignments(
                UserId int NOT NULL,
                DepartmentId int NOT NULL,
                IsPrimary bit NOT NULL CONSTRAINT DF_UserDepartmentAssignments_IsPrimary DEFAULT(0),
                CONSTRAINT PK_UserDepartmentAssignments PRIMARY KEY(UserId, DepartmentId),
                CONSTRAINT FK_UserDepartmentAssignments_Users_UserId FOREIGN KEY(UserId) REFERENCES Users(UserId) ON DELETE CASCADE,
                CONSTRAINT FK_UserDepartmentAssignments_Departments_DepartmentId FOREIGN KEY(DepartmentId) REFERENCES Departments(Id) ON DELETE CASCADE
              );",
            "IF COL_LENGTH('BusinessUnits','ParentBusinessUnitId') IS NULL ALTER TABLE BusinessUnits ADD ParentBusinessUnitId int NULL;",
            "IF COL_LENGTH('BusinessUnits','UnitType') IS NULL ALTER TABLE BusinessUnits ADD UnitType nvarchar(40) NOT NULL CONSTRAINT DF_BusinessUnits_UnitType DEFAULT('Division');",
            "IF COL_LENGTH('BusinessUnits','SortOrder') IS NULL ALTER TABLE BusinessUnits ADD SortOrder int NOT NULL CONSTRAINT DF_BusinessUnits_SortOrder DEFAULT(0);",
            @"IF COL_LENGTH('BusinessUnits','ParentBusinessUnitId') IS NOT NULL
              AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_BusinessUnits_BusinessUnits_ParentBusinessUnitId')
              ALTER TABLE BusinessUnits WITH CHECK ADD CONSTRAINT FK_BusinessUnits_BusinessUnits_ParentBusinessUnitId
              FOREIGN KEY(ParentBusinessUnitId) REFERENCES BusinessUnits(Id);",

            "IF COL_LENGTH('UserBusinessUnitAssignments','JobTitle') IS NULL ALTER TABLE UserBusinessUnitAssignments ADD JobTitle nvarchar(200) NOT NULL CONSTRAINT DF_UserBusinessUnitAssignments_JobTitle DEFAULT('');",
            "IF COL_LENGTH('UserBusinessUnitAssignments','ReportsToUserId') IS NULL ALTER TABLE UserBusinessUnitAssignments ADD ReportsToUserId int NULL;",
            "IF COL_LENGTH('UserBusinessUnitAssignments','IsActing') IS NULL ALTER TABLE UserBusinessUnitAssignments ADD IsActing bit NOT NULL CONSTRAINT DF_UserBusinessUnitAssignments_IsActing DEFAULT(0);",
            "IF COL_LENGTH('UserBusinessUnitAssignments','SortOrder') IS NULL ALTER TABLE UserBusinessUnitAssignments ADD SortOrder int NOT NULL CONSTRAINT DF_UserBusinessUnitAssignments_SortOrder DEFAULT(0);",
            @"IF COL_LENGTH('UserBusinessUnitAssignments','ReportsToUserId') IS NOT NULL
              AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_UserBusinessUnitAssignments_Users_ReportsToUserId')
              ALTER TABLE UserBusinessUnitAssignments WITH CHECK ADD CONSTRAINT FK_UserBusinessUnitAssignments_Users_ReportsToUserId
              FOREIGN KEY(ReportsToUserId) REFERENCES Users(UserId);",

            "IF COL_LENGTH('UserDepartmentAssignments','JobTitle') IS NULL ALTER TABLE UserDepartmentAssignments ADD JobTitle nvarchar(200) NOT NULL CONSTRAINT DF_UserDepartmentAssignments_JobTitle DEFAULT('');",
            "IF COL_LENGTH('UserDepartmentAssignments','ReportsToUserId') IS NULL ALTER TABLE UserDepartmentAssignments ADD ReportsToUserId int NULL;",
            "IF COL_LENGTH('UserDepartmentAssignments','IsActing') IS NULL ALTER TABLE UserDepartmentAssignments ADD IsActing bit NOT NULL CONSTRAINT DF_UserDepartmentAssignments_IsActing DEFAULT(0);",
            "IF COL_LENGTH('UserDepartmentAssignments','SortOrder') IS NULL ALTER TABLE UserDepartmentAssignments ADD SortOrder int NOT NULL CONSTRAINT DF_UserDepartmentAssignments_SortOrder DEFAULT(0);",
            @"IF COL_LENGTH('UserDepartmentAssignments','ReportsToUserId') IS NOT NULL
              AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_UserDepartmentAssignments_Users_ReportsToUserId')
              ALTER TABLE UserDepartmentAssignments WITH CHECK ADD CONSTRAINT FK_UserDepartmentAssignments_Users_ReportsToUserId
              FOREIGN KEY(ReportsToUserId) REFERENCES Users(UserId);",
            @"IF NOT EXISTS (SELECT 1 FROM SystemSettings WHERE [Key]='UserOrganizationAssignmentsMigratedV1')
              BEGIN
                INSERT INTO UserBusinessUnitAssignments(UserId, BusinessUnitId, IsPrimary)
                SELECT UserId, BusinessUnitId, 1 FROM Users
                WHERE BusinessUnitId IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM UserBusinessUnitAssignments a WHERE a.UserId=Users.UserId AND a.BusinessUnitId=Users.BusinessUnitId);

                INSERT INTO UserDepartmentAssignments(UserId, DepartmentId, IsPrimary)
                SELECT UserId, DepartmentId, 1 FROM Users
                WHERE DepartmentId IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM UserDepartmentAssignments a WHERE a.UserId=Users.UserId AND a.DepartmentId=Users.DepartmentId);

                INSERT INTO SystemSettings([Key],[Value],UpdatedAt)
                VALUES('UserOrganizationAssignmentsMigratedV1','1',GETDATE());
              END",
            @"IF NOT EXISTS (SELECT 1 FROM SystemSettings WHERE [Key]='OrganizationAssignmentDetailsMigratedV1')
              BEGIN
                UPDATE a SET JobTitle=u.Role, ReportsToUserId=u.DirectManagerUserId
                FROM UserBusinessUnitAssignments a INNER JOIN Users u ON u.UserId=a.UserId
                WHERE a.JobTitle='';

                UPDATE a SET JobTitle=u.Role, ReportsToUserId=u.DirectManagerUserId
                FROM UserDepartmentAssignments a INNER JOIN Users u ON u.UserId=a.UserId
                WHERE a.JobTitle='';

                INSERT INTO SystemSettings([Key],[Value],UpdatedAt)
                VALUES('OrganizationAssignmentDetailsMigratedV1','1',GETDATE());
              END",

            @"IF OBJECT_ID('ApprovalMatrixRules','U') IS NULL
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
              );",

            @"IF OBJECT_ID('DCRApprovalPlanEntries','U') IS NULL
              CREATE TABLE DCRApprovalPlanEntries(
                Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                RequestId int NOT NULL,
                LevelNumber int NOT NULL,
                LevelName nvarchar(200) NOT NULL CONSTRAINT DF_DCRApprovalPlanEntries_LevelName DEFAULT(''),
                ApproverId int NOT NULL,
                Sequence int NOT NULL CONSTRAINT DF_DCRApprovalPlanEntries_Sequence DEFAULT(1),
                IsRequired bit NOT NULL CONSTRAINT DF_DCRApprovalPlanEntries_IsRequired DEFAULT(1),
                CreatedAt datetime2 NOT NULL CONSTRAINT DF_DCRApprovalPlanEntries_CreatedAt DEFAULT(GETDATE())
              );",

            @"IF OBJECT_ID('UserApprovalPlanEntries','U') IS NULL
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
              );",

            "IF OBJECT_ID('UserApprovalPlanEntries','U') IS NOT NULL AND COL_LENGTH('UserApprovalPlanEntries','Rank') IS NULL ALTER TABLE UserApprovalPlanEntries ADD Rank nvarchar(10) NOT NULL CONSTRAINT DF_UserApprovalPlanEntries_Rank_Upgrade DEFAULT('C');",
            "IF OBJECT_ID('UserApprovalPlanEntries','U') IS NOT NULL AND COL_LENGTH('UserApprovalPlanEntries','BusinessUnitId') IS NULL ALTER TABLE UserApprovalPlanEntries ADD BusinessUnitId int NULL;",
            "IF OBJECT_ID('UserApprovalPlanEntries','U') IS NOT NULL AND COL_LENGTH('UserApprovalPlanEntries','BusinessUnitCode') IS NULL ALTER TABLE UserApprovalPlanEntries ADD BusinessUnitCode nvarchar(40) NOT NULL CONSTRAINT DF_UserApprovalPlanEntries_BusinessUnitCode_Upgrade DEFAULT('');",
            "IF OBJECT_ID('UserApprovalPlanEntries','U') IS NOT NULL AND COL_LENGTH('UserApprovalPlanEntries','BusinessUnitName') IS NULL ALTER TABLE UserApprovalPlanEntries ADD BusinessUnitName nvarchar(160) NOT NULL CONSTRAINT DF_UserApprovalPlanEntries_BusinessUnitName_Upgrade DEFAULT('');",
            "IF OBJECT_ID('UserApprovalPlanEntries','U') IS NOT NULL AND COL_LENGTH('UserApprovalPlanEntries','DepartmentId') IS NULL ALTER TABLE UserApprovalPlanEntries ADD DepartmentId int NULL;",
            "IF OBJECT_ID('UserApprovalPlanEntries','U') IS NOT NULL AND COL_LENGTH('UserApprovalPlanEntries','DepartmentCode') IS NULL ALTER TABLE UserApprovalPlanEntries ADD DepartmentCode nvarchar(40) NOT NULL CONSTRAINT DF_UserApprovalPlanEntries_DepartmentCode_Upgrade DEFAULT('');",
            "IF OBJECT_ID('UserApprovalPlanEntries','U') IS NOT NULL AND COL_LENGTH('UserApprovalPlanEntries','DepartmentName') IS NULL ALTER TABLE UserApprovalPlanEntries ADD DepartmentName nvarchar(160) NOT NULL CONSTRAINT DF_UserApprovalPlanEntries_DepartmentName_Upgrade DEFAULT('');",
            "IF OBJECT_ID('UserApprovalPlanEntries','U') IS NOT NULL AND COL_LENGTH('UserApprovalPlanEntries','ApproverRole') IS NULL ALTER TABLE UserApprovalPlanEntries ADD ApproverRole nvarchar(80) NOT NULL CONSTRAINT DF_UserApprovalPlanEntries_ApproverRole_Upgrade DEFAULT('');",
            "IF OBJECT_ID('UserApprovalPlanEntries','U') IS NOT NULL AND COL_LENGTH('UserApprovalPlanEntries','ApproverName') IS NULL ALTER TABLE UserApprovalPlanEntries ADD ApproverName nvarchar(160) NOT NULL CONSTRAINT DF_UserApprovalPlanEntries_ApproverName_Upgrade DEFAULT('');",
            "IF OBJECT_ID('UserApprovalPlanEntries','U') IS NOT NULL AND COL_LENGTH('UserApprovalPlanEntries','ApproverEmail') IS NULL ALTER TABLE UserApprovalPlanEntries ADD ApproverEmail nvarchar(200) NOT NULL CONSTRAINT DF_UserApprovalPlanEntries_ApproverEmail_Upgrade DEFAULT('');",

            @"IF OBJECT_ID('ApprovalPlanTemplates','U') IS NULL
              CREATE TABLE ApprovalPlanTemplates(
                Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                Name nvarchar(160) NOT NULL,
                Rank nvarchar(10) NOT NULL CONSTRAINT DF_ApprovalPlanTemplates_Rank DEFAULT('C'),
                IsDefault bit NOT NULL CONSTRAINT DF_ApprovalPlanTemplates_IsDefault DEFAULT(0),
                IsActive bit NOT NULL CONSTRAINT DF_ApprovalPlanTemplates_IsActive DEFAULT(1),
                CreatedAt datetime2 NOT NULL CONSTRAINT DF_ApprovalPlanTemplates_CreatedAt DEFAULT(GETDATE()),
                UpdatedAt datetime2 NOT NULL CONSTRAINT DF_ApprovalPlanTemplates_UpdatedAt DEFAULT(GETDATE())
              );",
            @"IF OBJECT_ID('ApprovalPlanTemplateEntries','U') IS NULL
              CREATE TABLE ApprovalPlanTemplateEntries(
                Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                TemplateId int NOT NULL,
                LevelNumber int NOT NULL,
                LevelName nvarchar(200) NOT NULL CONSTRAINT DF_ApprovalPlanTemplateEntries_LevelName DEFAULT(''),
                ApproverId int NOT NULL,
                Sequence int NOT NULL CONSTRAINT DF_ApprovalPlanTemplateEntries_Sequence DEFAULT(1)
              );",
            @"IF OBJECT_ID('ApprovalPlanTemplates','U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_ApprovalPlanTemplates_Rank_Name' AND object_id=OBJECT_ID('ApprovalPlanTemplates'))
              CREATE UNIQUE INDEX UX_ApprovalPlanTemplates_Rank_Name ON ApprovalPlanTemplates(Rank, Name);",
            @"IF OBJECT_ID('ApprovalPlanTemplateEntries','U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_ApprovalPlanTemplateEntries_Template_Approver' AND object_id=OBJECT_ID('ApprovalPlanTemplateEntries'))
              CREATE UNIQUE INDEX UX_ApprovalPlanTemplateEntries_Template_Approver ON ApprovalPlanTemplateEntries(TemplateId, ApproverId);",
            @"IF OBJECT_ID('ApprovalPlanTemplateEntries','U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_ApprovalPlanTemplateEntries_Template_Level_Sequence' AND object_id=OBJECT_ID('ApprovalPlanTemplateEntries'))
              CREATE INDEX IX_ApprovalPlanTemplateEntries_Template_Level_Sequence ON ApprovalPlanTemplateEntries(TemplateId, LevelNumber, Sequence);",
            @"IF OBJECT_ID('ApprovalPlanTemplateEntries','U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_ApprovalPlanTemplateEntries_ApprovalPlanTemplates_TemplateId')
              ALTER TABLE ApprovalPlanTemplateEntries WITH CHECK ADD CONSTRAINT FK_ApprovalPlanTemplateEntries_ApprovalPlanTemplates_TemplateId
              FOREIGN KEY(TemplateId) REFERENCES ApprovalPlanTemplates(Id) ON DELETE CASCADE;",
            @"IF OBJECT_ID('ApprovalPlanTemplateEntries','U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_ApprovalPlanTemplateEntries_Users_ApproverId')
              ALTER TABLE ApprovalPlanTemplateEntries WITH CHECK ADD CONSTRAINT FK_ApprovalPlanTemplateEntries_Users_ApproverId
              FOREIGN KEY(ApproverId) REFERENCES Users(UserId);",

            @"IF OBJECT_ID('EmailOutbox','U') IS NULL
              CREATE TABLE EmailOutbox(
                Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
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
              );",

            @"IF OBJECT_ID('EmailOutbox','U') IS NOT NULL AND COL_LENGTH('EmailOutbox','AttachmentFilePath') IS NULL
              ALTER TABLE EmailOutbox ADD AttachmentFilePath nvarchar(1600) NOT NULL CONSTRAINT DF_EmailOutbox_AttachmentFilePath_Upgrade DEFAULT('');",

            @"IF OBJECT_ID('EmailOutbox','U') IS NOT NULL AND COL_LENGTH('EmailOutbox','AttachmentFileName') IS NULL
              ALTER TABLE EmailOutbox ADD AttachmentFileName nvarchar(260) NOT NULL CONSTRAINT DF_EmailOutbox_AttachmentFileName_Upgrade DEFAULT('');",

            @"IF OBJECT_ID('EmailOutbox','U') IS NOT NULL AND COL_LENGTH('EmailOutbox','AttachmentContentType') IS NULL
              ALTER TABLE EmailOutbox ADD AttachmentContentType nvarchar(120) NOT NULL CONSTRAINT DF_EmailOutbox_AttachmentContentType_Upgrade DEFAULT('');",

            @"IF OBJECT_ID('DCRDeletionLogs','U') IS NULL
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
              );",

            @"IF OBJECT_ID('DCRNotificationLogs','U') IS NULL
              CREATE TABLE DCRNotificationLogs(
                Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                RequestId int NOT NULL,
                ApprovalFlowId int NULL,
                NotificationType nvarchar(60) NOT NULL,
                Recipient nvarchar(300) NOT NULL,
                SentAt datetime2 NOT NULL,
                Success bit NOT NULL,
                Details nvarchar(1000) NOT NULL CONSTRAINT DF_DCRNotificationLogs_Details DEFAULT('')
              );",

            @"IF OBJECT_ID('DCRApprovalPlanEntries','U') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_DCRApprovalPlanEntries_Request_Level_Approver' AND object_id=OBJECT_ID('DCRApprovalPlanEntries'))
              DROP INDEX UX_DCRApprovalPlanEntries_Request_Level_Approver ON DCRApprovalPlanEntries;",

            @"IF OBJECT_ID('DCRApprovalPlanEntries','U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_DCRApprovalPlanEntries_Request_Approver' AND object_id=OBJECT_ID('DCRApprovalPlanEntries'))
              CREATE UNIQUE INDEX UX_DCRApprovalPlanEntries_Request_Approver
              ON DCRApprovalPlanEntries(RequestId, ApproverId);",

            @"IF OBJECT_ID('DCRApprovalPlanEntries','U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_DCRApprovalPlanEntries_Request_Level_Sequence' AND object_id=OBJECT_ID('DCRApprovalPlanEntries'))
              CREATE INDEX IX_DCRApprovalPlanEntries_Request_Level_Sequence
              ON DCRApprovalPlanEntries(RequestId, LevelNumber, Sequence);",

            @"IF OBJECT_ID('UserApprovalPlanEntries','U') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_UserApprovalPlanEntries_Owner_Approver' AND object_id=OBJECT_ID('UserApprovalPlanEntries'))
              DROP INDEX UX_UserApprovalPlanEntries_Owner_Approver ON UserApprovalPlanEntries;",
            @"IF OBJECT_ID('UserApprovalPlanEntries','U') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_UserApprovalPlanEntries_OwnerUserId_ApproverId' AND object_id=OBJECT_ID('UserApprovalPlanEntries'))
              DROP INDEX IX_UserApprovalPlanEntries_OwnerUserId_ApproverId ON UserApprovalPlanEntries;",
            @"IF OBJECT_ID('UserApprovalPlanEntries','U') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_UserApprovalPlanEntries_Owner_Level_Sequence' AND object_id=OBJECT_ID('UserApprovalPlanEntries'))
              DROP INDEX IX_UserApprovalPlanEntries_Owner_Level_Sequence ON UserApprovalPlanEntries;",
            @"IF OBJECT_ID('UserApprovalPlanEntries','U') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_UserApprovalPlanEntries_OwnerUserId_LevelNumber_Sequence' AND object_id=OBJECT_ID('UserApprovalPlanEntries'))
              DROP INDEX IX_UserApprovalPlanEntries_OwnerUserId_LevelNumber_Sequence ON UserApprovalPlanEntries;",

            @"IF OBJECT_ID('UserApprovalPlanEntries','U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_UserApprovalPlanEntries_Owner_Rank_Approver' AND object_id=OBJECT_ID('UserApprovalPlanEntries'))
              CREATE UNIQUE INDEX UX_UserApprovalPlanEntries_Owner_Rank_Approver
              ON UserApprovalPlanEntries(OwnerUserId, Rank, ApproverId);",

            @"IF OBJECT_ID('UserApprovalPlanEntries','U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_UserApprovalPlanEntries_Owner_Rank_Level_Sequence' AND object_id=OBJECT_ID('UserApprovalPlanEntries'))
              CREATE INDEX IX_UserApprovalPlanEntries_Owner_Rank_Level_Sequence
              ON UserApprovalPlanEntries(OwnerUserId, Rank, LevelNumber, Sequence);",

            @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name IN ('IX_DCRApprovalFlows_Request_Revision_Stage_Sequence','IX_DCRApprovalFlows_RequestId_RevisionNo_StageNumber_Sequence') AND object_id=OBJECT_ID('DCRApprovalFlows'))
              CREATE INDEX IX_DCRApprovalFlows_Request_Revision_Stage_Sequence
              ON DCRApprovalFlows(RequestId, RevisionNo, StageNumber, Sequence);",

            @"IF OBJECT_ID('DCRRequests','U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_DCRRequests_Status_CreatedDate' AND object_id=OBJECT_ID('DCRRequests'))
              CREATE INDEX IX_DCRRequests_Status_CreatedDate ON DCRRequests(Status, CreatedDate DESC);",

            @"IF OBJECT_ID('DCRRequests','U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_DCRRequests_CreatedBy_CreatedDate' AND object_id=OBJECT_ID('DCRRequests'))
              CREATE INDEX IX_DCRRequests_CreatedBy_CreatedDate ON DCRRequests(CreatedBy, CreatedDate DESC);",

            @"IF OBJECT_ID('DCRRequests','U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_DCRRequests_RequestOwnerId_CreatedDate' AND object_id=OBJECT_ID('DCRRequests'))
              CREATE INDEX IX_DCRRequests_RequestOwnerId_CreatedDate ON DCRRequests(RequestOwnerId, CreatedDate DESC);",

            @"IF OBJECT_ID('DCRApprovalFlows','U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_DCRApprovalFlows_Approver_Decision_Request_Revision_Stage' AND object_id=OBJECT_ID('DCRApprovalFlows'))
              CREATE INDEX IX_DCRApprovalFlows_Approver_Decision_Request_Revision_Stage
              ON DCRApprovalFlows(ApproverId, Decision, RequestId, RevisionNo, StageNumber);",

            @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name IN ('IX_ApprovalMatrixRules_Routing','IX_ApprovalMatrixRules_StageCode_RequestingDepartmentId_TargetDepartmentId_Priority') AND object_id=OBJECT_ID('ApprovalMatrixRules'))
              CREATE INDEX IX_ApprovalMatrixRules_Routing
              ON ApprovalMatrixRules(StageCode, RequestingDepartmentId, TargetDepartmentId, Priority);",

            @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name IN ('IX_DCRNotificationLogs_Request_Type_SentAt','IX_DCRNotificationLogs_RequestId_ApprovalFlowId_NotificationType_SentAt') AND object_id=OBJECT_ID('DCRNotificationLogs'))
              CREATE INDEX IX_DCRNotificationLogs_Request_Type_SentAt
              ON DCRNotificationLogs(RequestId, ApprovalFlowId, NotificationType, SentAt);",

            @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_EmailOutbox_Status_NextAttemptAt_CreatedAt' AND object_id=OBJECT_ID('EmailOutbox'))
              CREATE INDEX IX_EmailOutbox_Status_NextAttemptAt_CreatedAt
              ON EmailOutbox(Status, NextAttemptAt, CreatedAt);",

            @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_EmailOutbox_RequestId_NotificationType_CreatedAt' AND object_id=OBJECT_ID('EmailOutbox'))
              CREATE INDEX IX_EmailOutbox_RequestId_NotificationType_CreatedAt
              ON EmailOutbox(RequestId, NotificationType, CreatedAt);",

            @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_DCRDeletionLogs_DCRNumber_DeletedAt' AND object_id=OBJECT_ID('DCRDeletionLogs'))
              CREATE INDEX IX_DCRDeletionLogs_DCRNumber_DeletedAt
              ON DCRDeletionLogs(DCRNumber, DeletedAt);",

            @"IF OBJECT_ID('ApprovalMatrixRules','U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name IN ('FK_ApprovalMatrixRules_RequestingDepartment','FK_ApprovalMatrixRules_Departments_RequestingDepartmentId'))
              ALTER TABLE ApprovalMatrixRules WITH CHECK ADD CONSTRAINT FK_ApprovalMatrixRules_RequestingDepartment
              FOREIGN KEY(RequestingDepartmentId) REFERENCES Departments(Id);",

            @"IF OBJECT_ID('ApprovalMatrixRules','U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name IN ('FK_ApprovalMatrixRules_TargetDepartment','FK_ApprovalMatrixRules_Departments_TargetDepartmentId'))
              ALTER TABLE ApprovalMatrixRules WITH CHECK ADD CONSTRAINT FK_ApprovalMatrixRules_TargetDepartment
              FOREIGN KEY(TargetDepartmentId) REFERENCES Departments(Id);",

            @"IF OBJECT_ID('ApprovalMatrixRules','U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name IN ('FK_ApprovalMatrixRules_ApproverUser','FK_ApprovalMatrixRules_Users_ApproverUserId'))
              ALTER TABLE ApprovalMatrixRules WITH CHECK ADD CONSTRAINT FK_ApprovalMatrixRules_ApproverUser
              FOREIGN KEY(ApproverUserId) REFERENCES Users(UserId);",

            @"IF OBJECT_ID('DCRApprovalPlanEntries','U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_DCRApprovalPlanEntries_DCRRequests_RequestId')
              ALTER TABLE DCRApprovalPlanEntries WITH CHECK ADD CONSTRAINT FK_DCRApprovalPlanEntries_DCRRequests_RequestId
              FOREIGN KEY(RequestId) REFERENCES DCRRequests(Id) ON DELETE CASCADE;",

            @"IF OBJECT_ID('DCRApprovalPlanEntries','U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_DCRApprovalPlanEntries_Users_ApproverId')
              ALTER TABLE DCRApprovalPlanEntries WITH CHECK ADD CONSTRAINT FK_DCRApprovalPlanEntries_Users_ApproverId
              FOREIGN KEY(ApproverId) REFERENCES Users(UserId);",

            @"IF OBJECT_ID('UserApprovalPlanEntries','U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_UserApprovalPlanEntries_Users_OwnerUserId')
              ALTER TABLE UserApprovalPlanEntries WITH CHECK ADD CONSTRAINT FK_UserApprovalPlanEntries_Users_OwnerUserId
              FOREIGN KEY(OwnerUserId) REFERENCES Users(UserId) ON DELETE CASCADE;",

            @"IF OBJECT_ID('UserApprovalPlanEntries','U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_UserApprovalPlanEntries_Users_ApproverId')
              ALTER TABLE UserApprovalPlanEntries WITH CHECK ADD CONSTRAINT FK_UserApprovalPlanEntries_Users_ApproverId
              FOREIGN KEY(ApproverId) REFERENCES Users(UserId);",

            @"IF OBJECT_ID('DCRNotificationLogs','U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name IN ('FK_DCRNotificationLogs_Request','FK_DCRNotificationLogs_DCRRequests_RequestId'))
              ALTER TABLE DCRNotificationLogs WITH CHECK ADD CONSTRAINT FK_DCRNotificationLogs_Request
              FOREIGN KEY(RequestId) REFERENCES DCRRequests(Id) ON DELETE CASCADE;",

            @"IF OBJECT_ID('DCRNotificationLogs','U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name IN ('FK_DCRNotificationLogs_ApprovalFlow','FK_DCRNotificationLogs_DCRApprovalFlows_ApprovalFlowId'))
              ALTER TABLE DCRNotificationLogs WITH CHECK ADD CONSTRAINT FK_DCRNotificationLogs_ApprovalFlow
              FOREIGN KEY(ApprovalFlowId) REFERENCES DCRApprovalFlows(Id);",
            @"IF OBJECT_ID('BusinessUnits','U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_BusinessUnits_Users_DirectorUserId')
              ALTER TABLE BusinessUnits WITH CHECK ADD CONSTRAINT FK_BusinessUnits_Users_DirectorUserId
              FOREIGN KEY(DirectorUserId) REFERENCES Users(UserId);",
            @"IF COL_LENGTH('Users','BusinessUnitId') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_Users_BusinessUnits_BusinessUnitId')
              ALTER TABLE Users WITH CHECK ADD CONSTRAINT FK_Users_BusinessUnits_BusinessUnitId
              FOREIGN KEY(BusinessUnitId) REFERENCES BusinessUnits(Id);",
            @"IF COL_LENGTH('Departments','BusinessUnitId') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_Departments_BusinessUnits_BusinessUnitId')
              ALTER TABLE Departments WITH CHECK ADD CONSTRAINT FK_Departments_BusinessUnits_BusinessUnitId
              FOREIGN KEY(BusinessUnitId) REFERENCES BusinessUnits(Id);",
            @"IF COL_LENGTH('Departments','DirectorUserId') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_Departments_Users_DirectorUserId')
              ALTER TABLE Departments WITH CHECK ADD CONSTRAINT FK_Departments_Users_DirectorUserId
              FOREIGN KEY(DirectorUserId) REFERENCES Users(UserId);",
            @"IF COL_LENGTH('Users','DirectManagerUserId') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_Users_Users_DirectManagerUserId')
              ALTER TABLE Users WITH CHECK ADD CONSTRAINT FK_Users_Users_DirectManagerUserId
              FOREIGN KEY(DirectManagerUserId) REFERENCES Users(UserId);",

            @"IF OBJECT_ID('DCRDeletionLogs','U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_DCRDeletionLogs_Users_DeletedBy')
              ALTER TABLE DCRDeletionLogs WITH CHECK ADD CONSTRAINT FK_DCRDeletionLogs_Users_DeletedBy
              FOREIGN KEY(DeletedBy) REFERENCES Users(UserId);",

            // Older versions reset returned requests to Draft. Preserve true, never-submitted drafts.
            @"UPDATE r SET Status='Returned'
              FROM DCRRequests r
              WHERE r.Status='Draft' AND
                (r.ReturnedDate IS NOT NULL OR EXISTS
                  (SELECT 1 FROM DCRApprovalFlows f WHERE f.RequestId=r.Id AND f.Decision='Returned'));"
        };

        using var transaction = db.Database.BeginTransaction();
        try
        {
            for (var i = 0; i < commands.Length; i++)
            {
                db.Database.ExecuteSqlRaw(commands[i]);
            }

            db.Database.ExecuteSqlRaw(
                "INSERT INTO DCRSchemaVersions(Version, AppliedAtUtc) VALUES ({0}, SYSUTCDATETIME());",
                CurrentSchemaVersion);
            transaction.Commit();
        }
        catch (Microsoft.Data.SqlClient.SqlException)
        {
            transaction.Rollback();
            throw;
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            throw new InvalidOperationException(
                $"Database upgrade failed. No upgrade changes were committed. {ex.Message}",
                ex);
        }
    }

    private static void EnsureSchemaVersionTable(AppDbContext db)
    {
        db.Database.ExecuteSqlRaw(@"
            IF OBJECT_ID('DCRSchemaVersions','U') IS NULL
            BEGIN
                CREATE TABLE DCRSchemaVersions(
                    Version nvarchar(50) NOT NULL PRIMARY KEY,
                    AppliedAtUtc datetime2 NOT NULL
                );
            END");
    }

    private static bool IsSchemaVersionApplied(AppDbContext db, string version)
    {
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State == System.Data.ConnectionState.Closed;
        try
        {
            if (wasClosed) connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM DCRSchemaVersions WHERE Version=@version;";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@version";
            parameter.Value = version;
            command.Parameters.Add(parameter);
            return Convert.ToInt32(command.ExecuteScalar()) > 0;
        }
        finally
        {
            if (wasClosed) connection.Close();
        }
    }

    private static void EnsureBaseSchemaExists(AppDbContext db)
    {
        var requiredTables = new[]
        {
            "Users",
            "Departments",
            "DCRRequests",
            "DCRParts",
            "DCRImpactedDepartments",
            "DCRApprovalFlows",
            "DCRAttachments",
            "AuditLogs",
            "WorkflowStageTemplates",
            "DcrNumberSequences"
        };

        var missing = new List<string>();
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State == System.Data.ConnectionState.Closed;

        try
        {
            if (wasClosed)
                connection.Open();

            foreach (var table in requiredTables)
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT CASE WHEN OBJECT_ID(@tableName, 'U') IS NULL THEN 0 ELSE 1 END;";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@tableName";
                parameter.Value = $"dbo.{table}";
                command.Parameters.Add(parameter);

                var exists = Convert.ToInt32(command.ExecuteScalar()) == 1;
                if (!exists)
                    missing.Add(table);
            }
        }
        finally
        {
            if (wasClosed)
                connection.Close();
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "Database DCRManagement chưa có base schema đầy đủ. Thiếu bảng: " +
                string.Join(", ", missing) +
                ". Hãy chạy Database\\CreateDatabase.sql trước, sau đó chạy ProductionUpgrade.sql. " +
                "ProductionUpgrade.sql chỉ dùng để nâng cấp database đã có schema cơ sở.");
        }
    }

}
