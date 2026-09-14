using System.Globalization;
using DCRManagementSystem.Data;
using DCRManagementSystem.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DCRManagementSystem.Services;

/// <summary>
/// One-time migration from the legacy GGP Control Sys_Users table into DCR Users.
/// Existing DCR usernames are never overwritten. Legacy SHA-256 password hashes are
/// imported with a prefix and verified by PasswordHasher for backward compatibility.
/// </summary>
public static class LegacyGgpUserMigrationService
{
    private const string ProductionDepartment = "Phòng Chất lượng";
    private const string WarehouseDepartment = "Phòng Sản xuất";
    private const string LegacyHashPrefix = "legacy-sha256$";

    public static async Task RunAsync(Func<AppDbContext> dbFactory, string legacyConnectionString)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("============================================================");
        Console.WriteLine(" GGP CONTROL -> DCR USER MIGRATION");
        Console.WriteLine("============================================================");
        Console.WriteLine("Quy tắc:");
        Console.WriteLine("  ProductionStaff -> DCR Staff / Phòng Chất lượng");
        Console.WriteLine("  WarehouseStaff  -> DCR Staff / Phòng Sản xuất");
        Console.WriteLine("  duynd           -> DCR Staff / Phòng Sản xuất (override)");
        Console.WriteLine("  luonghv         -> DCR Staff / Phòng Chất lượng (override)");
        Console.WriteLine("  Username đã có trong DCR -> SKIP, không sửa dữ liệu hiện hữu");
        Console.WriteLine();

        var legacyUsers = await LoadLegacyUsersAsync(legacyConnectionString);
        await using var db = dbFactory();
        await db.Database.OpenConnectionAsync();

        var existing = await db.Users.Where(x => !x.IsDeleted).ToListAsync();
        var existingNames = new HashSet<string>(existing.Select(x => x.Username), StringComparer.OrdinalIgnoreCase);
        var departments = await db.Departments.AsNoTracking().Where(x => x.IsActive).ToListAsync();
        var prodDept = departments.FirstOrDefault(x => string.Equals(x.DepartmentName, ProductionDepartment, StringComparison.OrdinalIgnoreCase));
        var whDept = departments.FirstOrDefault(x => string.Equals(x.DepartmentName, WarehouseDepartment, StringComparison.OrdinalIgnoreCase));

        if (prodDept is null) throw new InvalidOperationException($"Không tìm thấy Phòng ban DCR '{ProductionDepartment}'. Hãy tạo/cấu hình phòng ban trước.");
        if (whDept is null) throw new InvalidOperationException($"Không tìm thấy Phòng ban DCR '{WarehouseDepartment}'. Hãy tạo/cấu hình phòng ban trước.");
        if (!prodDept.ManagerUserId.HasValue) throw new InvalidOperationException($"Phòng ban '{ProductionDepartment}' chưa có Manager. Hãy cấu hình Manager trước.");
        if (!whDept.ManagerUserId.HasValue) throw new InvalidOperationException($"Phòng ban '{WarehouseDepartment}' chưa có Manager. Hãy cấu hình Manager trước.");

        var report = new List<string> { "Username,FullName,LegacyRole,DcrRole,Department,Result" };
        var created = 0; var skipped = 0; var ignored = 0;

        foreach (var old in legacyUsers.OrderBy(x => x.Username, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(old.Username)) continue;
            if (existingNames.Contains(old.Username))
            {
                Console.WriteLine($"SKIP    {old.Username,-16} đã tồn tại trong DCR");
                report.Add(Row(old, "", "", "SKIP_EXISTING"));
                skipped++;
                continue;
            }

            var target = Map(old, prodDept, whDept);
            if (target is null)
            {
                Console.WriteLine($"IGNORE  {old.Username,-16} role cũ '{old.Role}' chưa có mapping");
                report.Add(Row(old, "", "", "IGNORE_UNMAPPED"));
                ignored++;
                continue;
            }

            var passwordHash = NormalizeLegacyHash(old.PasswordHash);
            if (string.IsNullOrWhiteSpace(passwordHash))
            {
                Console.WriteLine($"IGNORE  {old.Username,-16} không có PasswordHash legacy hợp lệ");
                report.Add(Row(old, target.Value.Role, target.Value.DepartmentName, "IGNORE_BAD_PASSWORD_HASH"));
                ignored++;
                continue;
            }

            var user = new User
            {
                Username = old.Username.Trim(),
                FullName = string.IsNullOrWhiteSpace(old.FullName) ? old.Username.Trim() : old.FullName.Trim(),
                Email = old.Username.Trim() + "@ggp.vn",
                Phone = string.Empty,
                Role = target.Value.Role,
                IsActive = old.IsActive,
                IsDeleted = false,
                CreatedAt = DateTime.Now,
                PasswordHash = passwordHash,
                WindowsAccount = string.Empty,
                DepartmentId = target.Value.DepartmentId,
                BusinessUnitId = target.Value.BusinessUnitId,
                DirectManagerUserId = target.Value.DirectManagerUserId
            };
            db.Users.Add(user);
            existingNames.Add(user.Username);
            created++;
            Console.WriteLine($"CREATE  {old.Username,-16} {target.Value.Role,-14} {target.Value.DepartmentName}");
            report.Add(Row(old, target.Value.Role, target.Value.DepartmentName, "CREATED"));
        }

        await db.SaveChangesAsync();

        var reportDir = Path.Combine(AppContext.BaseDirectory, "MigrationLogs");
        Directory.CreateDirectory(reportDir);
        var reportPath = Path.Combine(reportDir, $"GGP_to_DCR_users_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        await File.WriteAllLinesAsync(reportPath, report, System.Text.Encoding.UTF8);

        Console.WriteLine();
        Console.WriteLine($"Hoàn tất. Created={created}, Skipped={skipped}, Ignored={ignored}");
        Console.WriteLine("Report: " + reportPath);
        Console.WriteLine();
        Console.WriteLine("Mật khẩu của user được giữ nguyên từ hệ thống GGP cũ bằng compatibility hash.");
        Console.WriteLine("Khi user đổi mật khẩu trong DCR/GGP, password sẽ được ghi lại bằng PBKDF2 của DCR.");
    }

    private static async Task<List<LegacyUser>> LoadLegacyUsersAsync(string connectionString)
    {
        const string sql = "SELECT Username, FullName, Role, IsActive, PasswordHash FROM Sys_Users ORDER BY Username";
        var users = new List<LegacyUser>();
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            users.Add(new LegacyUser(
                Convert.ToString(reader["Username"], CultureInfo.InvariantCulture) ?? string.Empty,
                Convert.ToString(reader["FullName"], CultureInfo.InvariantCulture) ?? string.Empty,
                Convert.ToString(reader["Role"], CultureInfo.InvariantCulture) ?? string.Empty,
                Convert.ToBoolean(reader["IsActive"], CultureInfo.InvariantCulture),
                Convert.ToString(reader["PasswordHash"], CultureInfo.InvariantCulture) ?? string.Empty));
        }
        return users;
    }

    private static TargetMapping? Map(LegacyUser old, Department prodDept, Department whDept)
    {
        if (old.Username.Equals("duynd", StringComparison.OrdinalIgnoreCase)) return Staff(whDept);
        if (old.Username.Equals("luonghv", StringComparison.OrdinalIgnoreCase)) return Staff(prodDept);

        if (old.Role.Equals("ProductionStaff", StringComparison.OrdinalIgnoreCase) || old.Role.Equals("QAQC", StringComparison.OrdinalIgnoreCase))
            return Staff(prodDept);
        if (old.Role.Equals("WarehouseStaff", StringComparison.OrdinalIgnoreCase))
            return Staff(whDept);
        if (old.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
            return new TargetMapping(RoleNames.Administrator, null, null, null, string.Empty);
        return null;
    }

    private static TargetMapping Staff(Department department) =>
        new(RoleNames.Staff, department.Id, department.BusinessUnitId, department.ManagerUserId, department.DepartmentName);

    private static string NormalizeLegacyHash(string hash)
    {
        hash = (hash ?? string.Empty).Trim().ToLowerInvariant();
        if (hash.StartsWith(LegacyHashPrefix, StringComparison.OrdinalIgnoreCase)) return hash;
        if (hash.Length == 64 && hash.All(Uri.IsHexDigit)) return LegacyHashPrefix + hash;
        return string.Empty;
    }

    private static string Row(LegacyUser old, string role, string dept, string result) =>
        string.Join(',', Csv(old.Username), Csv(old.FullName), Csv(old.Role), Csv(role), Csv(dept), Csv(result));

    private static string Csv(string value) => '"' + (value ?? string.Empty).Replace("\"", "\"\"") + '"';

    private sealed record LegacyUser(string Username, string FullName, string Role, bool IsActive, string PasswordHash);
    private readonly record struct TargetMapping(string Role, int? DepartmentId, int? BusinessUnitId, int? DirectManagerUserId, string DepartmentName);
}
