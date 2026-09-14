USE [DCRManagement];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH('Users','BusinessUnitId') IS NULL
    ALTER TABLE Users ADD BusinessUnitId int NULL;
IF COL_LENGTH('Departments','BusinessUnitId') IS NULL
    ALTER TABLE Departments ADD BusinessUnitId int NULL;

IF OBJECT_ID('BusinessUnits','U') IS NULL
BEGIN
    CREATE TABLE BusinessUnits(
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        UnitCode nvarchar(40) NOT NULL,
        UnitName nvarchar(160) NOT NULL,
        DirectorUserId int NULL,
        IsActive bit NOT NULL CONSTRAINT DF_BusinessUnits_IsActive DEFAULT(1)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_BusinessUnits_UnitCode' AND object_id=OBJECT_ID('BusinessUnits'))
    CREATE UNIQUE INDEX UX_BusinessUnits_UnitCode ON BusinessUnits(UnitCode);

IF NOT EXISTS (SELECT 1 FROM BusinessUnits WHERE UnitCode='PROD')
    INSERT INTO BusinessUnits(UnitCode,UnitName,IsActive) VALUES('PROD',N'Khối Sản xuất',1);

DECLARE @ProdUnitId int = (SELECT TOP(1) Id FROM BusinessUnits WHERE UnitCode='PROD');

/* Existing departments are assigned to the default Production block as a safe starting point.
   Administrator can create more blocks and move departments later in Quản trị -> Khối. */
UPDATE Departments SET BusinessUnitId=@ProdUnitId WHERE BusinessUnitId IS NULL;

/* Users belonging to departments inherit the department's block. */
UPDATE u
SET u.BusinessUnitId=d.BusinessUnitId
FROM Users u
INNER JOIN Departments d ON d.Id=u.DepartmentId
WHERE u.BusinessUnitId IS NULL;

/* Ensure the extended leadership roles exist. HierarchyLevel can be edited later. */
IF OBJECT_ID('Roles','U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM Roles WHERE RoleName='CEO')
        INSERT INTO Roles(RoleName,Description,HierarchyLevel,IsSystemProtected,IsActive) VALUES('CEO','Chief Executive Officer.',90,0,1);
    IF NOT EXISTS (SELECT 1 FROM Roles WHERE RoleName='DCEO')
        INSERT INTO Roles(RoleName,Description,HierarchyLevel,IsSystemProtected,IsActive) VALUES('DCEO','Deputy Chief Executive Officer.',80,0,1);
    IF NOT EXISTS (SELECT 1 FROM Roles WHERE RoleName='COO')
        INSERT INTO Roles(RoleName,Description,HierarchyLevel,IsSystemProtected,IsActive) VALUES('COO','Chief Operating Officer.',70,0,1);
    IF NOT EXISTS (SELECT 1 FROM Roles WHERE RoleName='CTO')
        INSERT INTO Roles(RoleName,Description,HierarchyLevel,IsSystemProtected,IsActive) VALUES('CTO','Chief Technology Officer.',70,0,1);
END;

/* Migrate legacy Department.DirectorUserId to the block-level Director where possible. */
IF COL_LENGTH('Departments','DirectorUserId') IS NOT NULL
BEGIN
    DECLARE @LegacyDirectorId int = (
        SELECT TOP(1) DirectorUserId
        FROM Departments
        WHERE BusinessUnitId=@ProdUnitId AND DirectorUserId IS NOT NULL
        ORDER BY Id
    );
    IF @LegacyDirectorId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM BusinessUnits WHERE Id=@ProdUnitId AND DirectorUserId IS NOT NULL)
        UPDATE BusinessUnits SET DirectorUserId=@LegacyDirectorId WHERE Id=@ProdUnitId;
END;

/* Director and higher roles are block-scoped, not department-scoped. */
DECLARE @DirectorLevel int = ISNULL((SELECT TOP(1) HierarchyLevel FROM Roles WHERE RoleName='Director'),30);
UPDATE u
SET u.BusinessUnitId = COALESCE(u.BusinessUnitId,d.BusinessUnitId,@ProdUnitId),
    u.DepartmentId = NULL
FROM Users u
LEFT JOIN Departments d ON d.Id=u.DepartmentId
LEFT JOIN Roles r ON r.RoleName=u.Role
WHERE u.Role <> 'Administrator' AND ISNULL(r.HierarchyLevel,0) >= @DirectorLevel;

/* If a block still has no configured Director, use the first active Director already assigned to that block.
   Administrator can change this later in Quản trị -> Khối. */
UPDATE b
SET b.DirectorUserId = candidate.UserId
FROM BusinessUnits b
CROSS APPLY (
    SELECT TOP(1) u.UserId
    FROM Users u
    WHERE u.IsDeleted=0 AND u.IsActive=1 AND u.Role='Director' AND u.BusinessUnitId=b.Id
    ORDER BY u.UserId
) candidate
WHERE b.DirectorUserId IS NULL;

/* Ensure configured block Directors are really block-scoped. */
UPDATE u
SET u.BusinessUnitId=b.Id,
    u.DepartmentId=NULL
FROM Users u
INNER JOIN BusinessUnits b ON b.DirectorUserId=u.UserId;

/* Manager reports to the Director of the block containing the Manager's department. */
UPDATE u
SET u.BusinessUnitId=d.BusinessUnitId,
    u.DirectManagerUserId=b.DirectorUserId
FROM Users u
INNER JOIN Departments d ON d.Id=u.DepartmentId
INNER JOIN BusinessUnits b ON b.Id=d.BusinessUnitId
WHERE u.Role='Manager';

/* Staff reports to the configured Manager of its department. */
UPDATE u
SET u.BusinessUnitId=d.BusinessUnitId,
    u.DirectManagerUserId=d.ManagerUserId
FROM Users u
INNER JOIN Departments d ON d.Id=u.DepartmentId
WHERE u.Role='Staff';

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_BusinessUnits_Users_DirectorUserId')
BEGIN
    ALTER TABLE BusinessUnits WITH CHECK ADD CONSTRAINT FK_BusinessUnits_Users_DirectorUserId
    FOREIGN KEY(DirectorUserId) REFERENCES Users(UserId);
END;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_Users_BusinessUnits_BusinessUnitId')
BEGIN
    ALTER TABLE Users WITH CHECK ADD CONSTRAINT FK_Users_BusinessUnits_BusinessUnitId
    FOREIGN KEY(BusinessUnitId) REFERENCES BusinessUnits(Id);
END;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_Departments_BusinessUnits_BusinessUnitId')
BEGIN
    ALTER TABLE Departments WITH CHECK ADD CONSTRAINT FK_Departments_BusinessUnits_BusinessUnitId
    FOREIGN KEY(BusinessUnitId) REFERENCES BusinessUnits(Id);
END;

COMMIT TRANSACTION;
GO

SELECT Id,UnitCode,UnitName,DirectorUserId,IsActive FROM BusinessUnits ORDER BY UnitCode;
SELECT Id,DepartmentCode,DepartmentName,BusinessUnitId,ManagerUserId FROM Departments ORDER BY DepartmentCode;
SELECT UserId,Username,FullName,Role,BusinessUnitId,DepartmentId,DirectManagerUserId FROM Users WHERE IsDeleted=0 ORDER BY Role,FullName;
GO
