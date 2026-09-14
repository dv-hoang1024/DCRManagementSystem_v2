USE [DCRManagement];
GO

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH('Users','IsDeleted') IS NULL
    ALTER TABLE Users ADD IsDeleted bit NOT NULL CONSTRAINT DF_Users_IsDeleted DEFAULT(0);
IF COL_LENGTH('Users','DeletedAt') IS NULL
    ALTER TABLE Users ADD DeletedAt datetime2 NULL;
IF COL_LENGTH('Users','DeletedBy') IS NULL
    ALTER TABLE Users ADD DeletedBy int NULL;

IF COL_LENGTH('Users','DirectManagerUserId') IS NULL
    ALTER TABLE Users ADD DirectManagerUserId int NULL;
IF COL_LENGTH('Departments','DirectorUserId') IS NULL
    ALTER TABLE Departments ADD DirectorUserId int NULL;

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

IF NOT EXISTS (SELECT 1 FROM Roles WHERE RoleName='Administrator')
    INSERT INTO Roles(RoleName,Description,HierarchyLevel,IsSystemProtected,IsActive)
    VALUES('Administrator','System administration and unrestricted configuration access.',100,1,1);
IF NOT EXISTS (SELECT 1 FROM Roles WHERE RoleName='Director')
    INSERT INTO Roles(RoleName,Description,HierarchyLevel,IsSystemProtected,IsActive)
    VALUES('Director','Department / functional Director.',30,0,1);
IF NOT EXISTS (SELECT 1 FROM Roles WHERE RoleName='Manager')
    INSERT INTO Roles(RoleName,Description,HierarchyLevel,IsSystemProtected,IsActive)
    VALUES('Manager','Direct Manager.',20,0,1);
IF NOT EXISTS (SELECT 1 FROM Roles WHERE RoleName='Staff')
    INSERT INTO Roles(RoleName,Description,HierarchyLevel,IsSystemProtected,IsActive)
    VALUES('Staff','Standard user / initiator role.',10,0,1);

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

IF COL_LENGTH('Departments','DirectorUserId') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_Departments_Users_DirectorUserId')
BEGIN
    ALTER TABLE Departments WITH CHECK ADD CONSTRAINT FK_Departments_Users_DirectorUserId
    FOREIGN KEY(DirectorUserId) REFERENCES Users(UserId);
END;

IF COL_LENGTH('Users','DirectManagerUserId') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_Users_Users_DirectManagerUserId')
BEGIN
    ALTER TABLE Users WITH CHECK ADD CONSTRAINT FK_Users_Users_DirectManagerUserId
    FOREIGN KEY(DirectManagerUserId) REFERENCES Users(UserId);
END;

COMMIT TRANSACTION;
GO

SELECT RoleName, Description, HierarchyLevel, IsSystemProtected, IsActive
FROM Roles
ORDER BY HierarchyLevel DESC, RoleName;
GO
