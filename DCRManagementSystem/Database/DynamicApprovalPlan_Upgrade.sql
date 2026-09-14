USE [DCRManagement];
GO

IF OBJECT_ID('DCRRequests','U') IS NULL OR OBJECT_ID('Users','U') IS NULL
    THROW 50010, 'Base schema is missing. DCRRequests and Users must exist before DynamicApprovalPlan_Upgrade.sql is executed.', 1;
GO

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
