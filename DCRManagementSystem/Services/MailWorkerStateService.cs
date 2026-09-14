using System.Security.Principal;
using DCRManagementSystem.Data;
using DCRManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace DCRManagementSystem.Services;

public sealed class MailWorkerState
{
    public string MachineName { get; init; } = string.Empty;
    public string WindowsIdentity { get; init; } = string.Empty;
    public string SignedInAccount { get; init; } = string.Empty;
    public DateTime? LastHeartbeat { get; init; }
    public DateTime? LastSuccessfulSend { get; init; }
    public string LastResult { get; init; } = string.Empty;
}

public sealed class MailWorkerStateService : IMailWorkerStateService
{
    private const string Prefix = "MailWorker.";
    private readonly Func<AppDbContext> _dbFactory;

    public MailWorkerStateService(Func<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<MailWorkerState> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync(cancellationToken);
        var rows = await db.SystemSettings.AsNoTracking()
            .Where(x => x.Key.StartsWith(Prefix))
            .ToDictionaryAsync(x => x.Key, x => x.Value, cancellationToken);
        return new MailWorkerState
        {
            MachineName = Get(rows, "MachineName"),
            WindowsIdentity = Get(rows, "WindowsIdentity"),
            SignedInAccount = Get(rows, "SignedInAccount"),
            LastHeartbeat = ParseDate(Get(rows, "LastHeartbeat")),
            LastSuccessfulSend = ParseDate(Get(rows, "LastSuccessfulSend")),
            LastResult = Get(rows, "LastResult")
        };
    }

    public async Task RecordSignInAsync(string signedInAccount, CancellationToken cancellationToken = default)
    {
        await SetManyAsync(new Dictionary<string, string>
        {
            ["MachineName"] = Environment.MachineName,
            ["WindowsIdentity"] = GetCurrentWindowsIdentity(),
            ["SignedInAccount"] = signedInAccount,
            ["LastResult"] = "Microsoft Graph sign-in completed on mail server."
        }, cancellationToken);
    }

    public async Task RecordSignOutAsync(CancellationToken cancellationToken = default)
    {
        await SetManyAsync(new Dictionary<string, string>
        {
            ["SignedInAccount"] = string.Empty,
            ["LastResult"] = "Microsoft Graph signed out on mail server."
        }, cancellationToken);
    }

    public async Task RecordRunAsync(string result, bool successfulSend, CancellationToken cancellationToken = default)
    {
        var values = new Dictionary<string, string>
        {
            ["LastHeartbeat"] = DateTime.Now.ToString("O"),
            ["LastResult"] = result
        };
        if (successfulSend)
            values["LastSuccessfulSend"] = DateTime.Now.ToString("O");
        await SetManyAsync(values, cancellationToken);
    }

    public async Task EnsureCorrectWindowsIdentityAsync(CancellationToken cancellationToken = default)
    {
        var state = await GetAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(state.MachineName) &&
            !string.Equals(state.MachineName, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Mail Worker được đăng ký trên máy '{state.MachineName}', nhưng tác vụ hiện đang chạy trên '{Environment.MachineName}'. " +
                "Hãy chạy Mail Worker trên đúng máy đã đăng nhập Microsoft hoặc đăng nhập lại từ System Settings trên máy mới.");
        }

        if (string.IsNullOrWhiteSpace(state.WindowsIdentity))
            return;

        var current = GetCurrentWindowsIdentity();
        if (!string.Equals(state.WindowsIdentity, current, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Mail Worker đang chạy bằng Windows account '{current}', nhưng token Microsoft Graph được cấu hình bằng '{state.WindowsIdentity}'. " +
                "Hãy chạy Scheduled Task/worker bằng đúng Windows account đã bấm Đăng nhập Microsoft trong System Settings.");
        }
    }

    public static string GetCurrentWindowsIdentity()
    {
        try { return WindowsIdentity.GetCurrent().Name ?? Environment.UserName; }
        catch { return Environment.UserName; }
    }

    private async Task SetManyAsync(Dictionary<string, string> values, CancellationToken cancellationToken)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync(cancellationToken);
        foreach (var pair in values)
        {
            var key = Prefix + pair.Key;
            var row = await db.SystemSettings.SingleOrDefaultAsync(x => x.Key == key, cancellationToken);
            if (row is null)
            {
                db.SystemSettings.Add(new SystemSetting { Key = key, Value = pair.Value, UpdatedAt = DateTime.Now });
            }
            else
            {
                row.Value = pair.Value;
                row.UpdatedAt = DateTime.Now;
            }
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string Get(IReadOnlyDictionary<string, string> rows, string name) =>
        rows.TryGetValue(Prefix + name, out var value) ? value : string.Empty;

    private static DateTime? ParseDate(string value) => DateTime.TryParse(value, out var parsed) ? parsed : null;
}
