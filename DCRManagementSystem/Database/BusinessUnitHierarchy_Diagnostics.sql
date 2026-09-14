USE [DCRManagement];
GO
SET NOCOUNT ON;

PRINT N'=== 1. KHỐI / DIRECTOR ===';
SELECT
    b.Id,
    b.UnitCode,
    b.UnitName,
    b.IsActive,
    b.DirectorUserId,
    u.Username AS DirectorUsername,
    u.FullName AS DirectorName,
    u.Role AS DirectorRole,
    u.BusinessUnitId AS DirectorBusinessUnitId,
    u.DepartmentId AS DirectorDepartmentId
FROM BusinessUnits b
LEFT JOIN Users u ON u.UserId = b.DirectorUserId
ORDER BY b.UnitCode;

PRINT N'=== 2. PHÒNG BAN / MANAGER / DIRECTOR KHỐI ===';
SELECT
    d.Id,
    d.DepartmentCode,
    d.DepartmentName,
    b.UnitCode,
    b.UnitName,
    d.ManagerUserId,
    m.Username AS ManagerUsername,
    m.FullName AS ManagerName,
    m.Role AS ManagerRole,
    m.DirectManagerUserId AS ManagerDirectManagerId,
    b.DirectorUserId AS ExpectedDirectorId,
    dir.FullName AS ExpectedDirectorName,
    CASE
        WHEN d.ManagerUserId IS NULL THEN N'NO MANAGER'
        WHEN b.DirectorUserId IS NULL THEN N'NO BLOCK DIRECTOR'
        WHEN m.DirectManagerUserId = b.DirectorUserId THEN N'OK'
        ELSE N'MISMATCH'
    END AS ManagerChainStatus
FROM Departments d
LEFT JOIN BusinessUnits b ON b.Id = d.BusinessUnitId
LEFT JOIN Users m ON m.UserId = d.ManagerUserId
LEFT JOIN Users dir ON dir.UserId = b.DirectorUserId
ORDER BY b.UnitCode, d.DepartmentCode;

PRINT N'=== 3. STAFF -> MANAGER PHÒNG BAN ===';
SELECT
    s.UserId,
    s.Username,
    s.FullName,
    s.Role,
    d.DepartmentCode,
    b.UnitCode,
    s.DirectManagerUserId,
    d.ManagerUserId AS ExpectedManagerId,
    m.FullName AS ExpectedManagerName,
    CASE WHEN s.DirectManagerUserId = d.ManagerUserId THEN N'OK' ELSE N'MISMATCH' END AS StaffChainStatus
FROM Users s
INNER JOIN Departments d ON d.Id = s.DepartmentId
LEFT JOIN BusinessUnits b ON b.Id = d.BusinessUnitId
LEFT JOIN Users m ON m.UserId = d.ManagerUserId
WHERE s.IsDeleted = 0 AND s.Role = 'Staff'
ORDER BY b.UnitCode, d.DepartmentCode, s.FullName;

PRINT N'=== 4. DIRECTOR VÀ CÁC CẤP CAO HƠN: KHÔNG ĐƯỢC THUỘC PHÒNG BAN ===';
DECLARE @DirectorLevel int = ISNULL((SELECT TOP(1) HierarchyLevel FROM Roles WHERE RoleName='Director'),30);
SELECT
    u.UserId,
    u.Username,
    u.FullName,
    u.Role,
    r.HierarchyLevel,
    b.UnitCode,
    b.UnitName,
    u.DepartmentId,
    u.DirectManagerUserId,
    dm.FullName AS DirectManagerName,
    dm.Role AS DirectManagerRole,
    CASE WHEN u.DepartmentId IS NULL AND u.BusinessUnitId IS NOT NULL THEN N'OK' ELSE N'CHECK' END AS ScopeStatus
FROM Users u
LEFT JOIN Roles r ON r.RoleName = u.Role
LEFT JOIN BusinessUnits b ON b.Id = u.BusinessUnitId
LEFT JOIN Users dm ON dm.UserId = u.DirectManagerUserId
WHERE u.IsDeleted = 0
  AND u.Role <> 'Administrator'
  AND ISNULL(r.HierarchyLevel,0) >= @DirectorLevel
ORDER BY r.HierarchyLevel DESC, u.FullName;
GO
