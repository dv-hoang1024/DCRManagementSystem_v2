USE [DCRManagement];
GO

SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID('dbo.ProductLines', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ProductLines
        (
            Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProductLines PRIMARY KEY,
            Name nvarchar(100) NOT NULL,
            SortOrder int NOT NULL CONSTRAINT DF_ProductLines_SortOrder DEFAULT(0),
            IsActive bit NOT NULL CONSTRAINT DF_ProductLines_IsActive DEFAULT(1),
            CreatedAt datetime2 NOT NULL CONSTRAINT DF_ProductLines_CreatedAt DEFAULT(GETDATE())
        );
    END;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE name = 'UX_ProductLines_Name'
          AND object_id = OBJECT_ID('dbo.ProductLines')
    )
        CREATE UNIQUE INDEX UX_ProductLines_Name ON dbo.ProductLines(Name);

    IF OBJECT_ID('dbo.PartChangeTypes', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.PartChangeTypes
        (
            Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PartChangeTypes PRIMARY KEY,
            Name nvarchar(80) NOT NULL,
            SortOrder int NOT NULL CONSTRAINT DF_PartChangeTypes_SortOrder DEFAULT(0),
            IsActive bit NOT NULL CONSTRAINT DF_PartChangeTypes_IsActive DEFAULT(1),
            CreatedAt datetime2 NOT NULL CONSTRAINT DF_PartChangeTypes_CreatedAt DEFAULT(GETDATE())
        );
    END;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE name = 'UX_PartChangeTypes_Name'
          AND object_id = OBJECT_ID('dbo.PartChangeTypes')
    )
        CREATE UNIQUE INDEX UX_PartChangeTypes_Name ON dbo.PartChangeTypes(Name);

    IF OBJECT_ID('dbo.SystemSettings', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.SystemSettings
        (
            [Key] nvarchar(120) NOT NULL CONSTRAINT PK_SystemSettings PRIMARY KEY,
            [Value] nvarchar(max) NOT NULL CONSTRAINT DF_SystemSettings_Value DEFAULT(''),
            UpdatedAt datetime2 NOT NULL CONSTRAINT DF_SystemSettings_UpdatedAt DEFAULT(GETDATE())
        );
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE [Key] = N'MasterDataDefaultsSeededV1')
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.ProductLines WHERE Name = N'S16')
            INSERT INTO dbo.ProductLines(Name, SortOrder, IsActive) VALUES(N'S16', 10, 1);
        IF NOT EXISTS (SELECT 1 FROM dbo.ProductLines WHERE Name = N'S16Pre')
            INSERT INTO dbo.ProductLines(Name, SortOrder, IsActive) VALUES(N'S16Pre', 20, 1);
        IF NOT EXISTS (SELECT 1 FROM dbo.ProductLines WHERE Name = N'S16New')
            INSERT INTO dbo.ProductLines(Name, SortOrder, IsActive) VALUES(N'S16New', 30, 1);

        IF NOT EXISTS (SELECT 1 FROM dbo.PartChangeTypes WHERE Name = N'Material')
            INSERT INTO dbo.PartChangeTypes(Name, SortOrder, IsActive) VALUES(N'Material', 10, 1);
        IF NOT EXISTS (SELECT 1 FROM dbo.PartChangeTypes WHERE Name = N'Machine')
            INSERT INTO dbo.PartChangeTypes(Name, SortOrder, IsActive) VALUES(N'Machine', 20, 1);
        IF NOT EXISTS (SELECT 1 FROM dbo.PartChangeTypes WHERE Name = N'Man')
            INSERT INTO dbo.PartChangeTypes(Name, SortOrder, IsActive) VALUES(N'Man', 30, 1);
        IF NOT EXISTS (SELECT 1 FROM dbo.PartChangeTypes WHERE Name = N'Method')
            INSERT INTO dbo.PartChangeTypes(Name, SortOrder, IsActive) VALUES(N'Method', 40, 1);
        IF NOT EXISTS (SELECT 1 FROM dbo.PartChangeTypes WHERE Name = N'Other')
            INSERT INTO dbo.PartChangeTypes(Name, SortOrder, IsActive) VALUES(N'Other', 50, 1);

        INSERT INTO dbo.SystemSettings([Key], [Value], UpdatedAt)
        VALUES(N'MasterDataDefaultsSeededV1', N'1', GETDATE());
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

SELECT Id, Name, SortOrder, IsActive, CreatedAt
FROM dbo.ProductLines
ORDER BY SortOrder, Name;

SELECT Id, Name, SortOrder, IsActive, CreatedAt
FROM dbo.PartChangeTypes
ORDER BY SortOrder, Name;
GO
