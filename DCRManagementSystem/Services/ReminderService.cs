using DCRManagementSystem.Data;
using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace DCRManagementSystem.Services;

public sealed class ReminderService
{
    private readonly Func<AppDbContext> _dbFactory;
    private readonly AppSettings _settings;
    private readonly NotificationService _notifications;

    public ReminderService(
        Func<AppDbContext> dbFactory,
        AppSettings settings,
        NotificationService notifications)
    {
        _dbFactory = dbFactory;
        _settings = settings;
        _notifications = notifications;
    }

    public async Task<int> RunOnceAsync()
    {
        if (!_settings.Reminder.Enabled)
            return 0;

        await using var lockDb = _dbFactory();
        await lockDb.OpenSqlConnectionWithRetryAsync();
        var connection = lockDb.Database.GetDbConnection();
        await using var acquire = connection.CreateCommand();
        acquire.CommandText = "DECLARE @r int; EXEC @r = sp_getapplock @Resource='DCRManagementSystem.ReminderWorker', @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=0; SELECT @r;";
        var lockResult = Convert.ToInt32(await acquire.ExecuteScalarAsync());
        if (lockResult < 0)
            return 0;

        try
        {
            return await ProcessDueApprovalsAsync();
        }
        finally
        {
            await using var release = connection.CreateCommand();
            release.CommandText = "EXEC sp_releaseapplock @Resource='DCRManagementSystem.ReminderWorker', @LockOwner='Session';";
            await release.ExecuteNonQueryAsync();
        }
    }

    private async Task<int> ProcessDueApprovalsAsync()
    {
        var now = DateTime.Now;
        List<int> ids;

        await using (var db = _dbFactory())
        {
            await db.OpenSqlConnectionWithRetryAsync();
            ids = await db.DCRApprovalFlows
                .AsNoTracking()
                .Where(x => x.Decision == ApprovalDecisions.Pending &&
                            x.DueDate.HasValue && x.DueDate <= now &&
                            x.Request != null &&
                            x.Request.Status == RequestStatuses.InApproval &&
                            x.Request.RevisionNo == x.RevisionNo &&
                            x.Request.CurrentStage == x.StageNumber)
                .Where(x => !x.LastReminderAt.HasValue ||
                            x.LastReminderAt.Value.AddHours(_settings.Reminder.ReminderRepeatHours) <= now)
                .Select(x => x.Id)
                .ToListAsync();
        }

        var sent = 0;
        foreach (var id in ids)
        {
            bool escalation;
            await using (var db = _dbFactory())
            {
                await db.OpenSqlConnectionWithRetryAsync();
                var row = await db.DCRApprovalFlows
                    .Include(x => x.Request)
                    .SingleOrDefaultAsync(x => x.Id == id);
                if (row?.Request is null ||
                    row.Decision != ApprovalDecisions.Pending ||
                    row.Request.Status != RequestStatuses.InApproval ||
                    row.Request.RevisionNo != row.RevisionNo ||
                    row.Request.CurrentStage != row.StageNumber)
                {
                    continue;
                }

                if (row.LastReminderAt.HasValue &&
                    row.LastReminderAt.Value.AddHours(_settings.Reminder.ReminderRepeatHours) > now)
                {
                    continue;
                }

                escalation = row.AssignedDate.HasValue &&
                             row.AssignedDate.Value.AddHours(_settings.Reminder.EscalationHours) <= now;

                row.LastReminderAt = now;
                row.ReminderCount++;
                await db.SaveChangesAsync();
            }

            await _notifications.SendReminderAsync(id, escalation);
            sent++;
        }

        return sent;
    }
}
