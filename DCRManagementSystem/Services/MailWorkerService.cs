using System.Data;
using DCRManagementSystem.Data;
using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace DCRManagementSystem.Services;

public sealed class MailWorkerRunResult
{
    public int Claimed { get; set; }
    public int Sent { get; set; }
    public int Failed { get; set; }
    public int Deferred { get; set; }
    public int Cancelled { get; set; }
    public bool RequiresSignIn { get; set; }

    public override string ToString() =>
        $"Claimed={Claimed}; Sent={Sent}; Failed={Failed}; Deferred={Deferred}; Cancelled={Cancelled}; RequiresSignIn={RequiresSignIn}";
}

public sealed class MailWorkerService : IMailWorkerService
{
    private readonly Func<AppDbContext> _dbFactory;
    private readonly AppSettings _settings;
    private readonly EmailConfigurationService _emailConfiguration;
    private readonly EmailService _email = new();
    private readonly MailWorkerStateService _state;
    private readonly PdfService _pdf;

    public MailWorkerService(Func<AppDbContext> dbFactory, AppSettings settings)
    {
        _dbFactory = dbFactory;
        _settings = settings;
        _emailConfiguration = new EmailConfigurationService(dbFactory, settings);
        _state = new MailWorkerStateService(dbFactory);
        _pdf = new PdfService(dbFactory, settings);
    }

    public Task<MailWorkerRunResult> RunOnceAsync(int batchSize = 25, CancellationToken cancellationToken = default)
        => RunCoreAsync(batchSize, forcePendingNow: false, cancellationToken: cancellationToken);

    // Used by the Administrator "Process" button. Manual processing intentionally
    // ignores a future NextAttemptAt so an operator can immediately retry a Pending row.
    public Task<MailWorkerRunResult> RunPendingNowAsync(int batchSize = 25, CancellationToken cancellationToken = default)
        => RunCoreAsync(batchSize, forcePendingNow: true, cancellationToken: cancellationToken);

    private async Task<MailWorkerRunResult> RunCoreAsync(int batchSize, bool forcePendingNow, CancellationToken cancellationToken)
    {
        await _state.EnsureCorrectWindowsIdentityAsync(cancellationToken);
        var result = new MailWorkerRunResult();

        await using var lockDb = _dbFactory();
        await lockDb.OpenSqlConnectionWithRetryAsync(cancellationToken);
        var connection = lockDb.Database.GetDbConnection();
        await using var acquire = connection.CreateCommand();
        acquire.CommandText = "DECLARE @r int; EXEC @r = sp_getapplock @Resource='DCRManagementSystem.EmailOutboxWorker', @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=0; SELECT @r;";
        var lockResult = Convert.ToInt32(await acquire.ExecuteScalarAsync(cancellationToken));
        if (lockResult < 0)
        {
            await _state.RecordRunAsync("Worker skipped because another worker instance owns the SQL application lock.", false, cancellationToken);
            return result;
        }

        try
        {
            await RecoverStaleProcessingAsync(cancellationToken);

            // Self-heal workflow notifications before claiming the queue. If an approval
            // transaction committed but the client closed or the network failed before
            // the next-stage email was enqueued, the server worker recreates it here.
            try
            {
                var reconciliation = new NotificationService(_dbFactory, _settings);
                await reconciliation.ReconcileWorkflowNotificationsAsync(null, cancellationToken);
            }
            catch (Exception ex)
            {
                await _state.RecordRunAsync(
                    "Workflow notification reconciliation warning: " + ex.GetBaseException().Message,
                    false,
                    cancellationToken);
            }

            var emailSettings = await _emailConfiguration.GetAsync();
            if (!emailSettings.Enabled)
            {
                await _state.RecordRunAsync("Email Notification is disabled; outbox was not processed.", false, cancellationToken);
                return result;
            }
            EmailConfigurationService.Validate(emailSettings, requireEnabledConfiguration: true);

            var ids = await GetDueIdsAsync(batchSize, forcePendingNow, cancellationToken);
            foreach (var id in ids)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var claimed = await ClaimAsync(id, cancellationToken);
                if (!claimed)
                    continue;
                result.Claimed++;

                EmailOutboxItem? item;
                await using (var db = _dbFactory())
                {
                    await db.OpenSqlConnectionWithRetryAsync(cancellationToken);
                    item = await db.EmailOutbox.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
                }
                if (item is null)
                    continue;

                try
                {
                    if (!await IsStillProcessableAsync(item, cancellationToken))
                    {
                        await CancelIfOrphanedAsync(id, cancellationToken);
                        result.Cancelled++;
                        continue;
                    }

                    var attachment = await ResolveAttachmentAsync(item, cancellationToken);

                    if (!await IsStillProcessableAsync(item, cancellationToken))
                    {
                        await CancelIfOrphanedAsync(id, cancellationToken);
                        result.Cancelled++;
                        continue;
                    }

                    using var monitorStop = new CancellationTokenSource();
                    using var sendCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    var cancellationMonitor = MonitorCancellationAsync(
                        item,
                        sendCancellation,
                        monitorStop.Token);
                    try
                    {
                        var body = attachment is null
                            ? item.Body
                            : DcrMailAttachmentPolicy.RemoveLegacyUnavailableNotice(item.Body);
                        var sender = await _email.SendAsync(
                             emailSettings,
                             item.Recipient,
                             item.Subject,
                             body,
                             attachment,
                            allowInteractive: false,
                            sendCancellation.Token);
                        if (await MarkSentAsync(id, sender, cancellationToken))
                            result.Sent++;
                        else
                            result.Cancelled++;
                    }
                    finally
                    {
                        monitorStop.Cancel();
                        await cancellationMonitor;
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // The DCR was deleted/cancelled while SMTP or Graph was still sending.
                    // Both transports receive the cancellation token so an in-flight request
                    // is aborted whenever the transport still permits it.
                    await CancelIfOrphanedAsync(id, cancellationToken);
                    result.Cancelled++;
                }
                catch (MailWorkerAuthenticationRequiredException ex)
                {
                    if (await MarkRequiresSignInAsync(id, ex.GetBaseException().Message, cancellationToken))
                    {
                        result.RequiresSignIn = true;
                        result.Deferred++;
                        break;
                    }
                    result.Cancelled++;
                }
                catch (Exception ex)
                {
                    var permanentlyFailed = await MarkRetryOrFailedAsync(id, ex.GetBaseException().Message, cancellationToken);
                    if (!permanentlyFailed.HasValue) result.Cancelled++;
                    else if (permanentlyFailed.Value) result.Failed++;
                    else result.Deferred++;
                }
            }

            await _state.RecordRunAsync(result.ToString(), result.Sent > 0, cancellationToken);
            return result;
        }
        finally
        {
            await using var release = connection.CreateCommand();
            release.CommandText = "EXEC sp_releaseapplock @Resource='DCRManagementSystem.EmailOutboxWorker', @LockOwner='Session';";
            await release.ExecuteNonQueryAsync(cancellationToken);
        }
    }


    private async Task<bool> IsStillProcessableAsync(
        EmailOutboxItem item,
        CancellationToken cancellationToken)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync(cancellationToken);
        var outboxActive = await db.EmailOutbox.AsNoTracking().AnyAsync(
            x => x.Id == item.Id && x.Status == EmailOutboxStatuses.Processing,
            cancellationToken);
        if (!outboxActive)
            return false;

        return !item.RequestId.HasValue || await db.DCRRequests.AsNoTracking().AnyAsync(
            x => x.Id == item.RequestId.Value,
            cancellationToken);
    }

    private async Task MonitorCancellationAsync(
        EmailOutboxItem item,
        CancellationTokenSource sendCancellation,
        CancellationToken stopToken)
    {
        try
        {
            while (!stopToken.IsCancellationRequested && !sendCancellation.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300), stopToken);
                if (!await IsStillProcessableAsync(item, stopToken))
                {
                    sendCancellation.Cancel();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
        {
            // Normal completion: the send operation finished before deletion/cancellation.
        }
        catch
        {
            // If cancellation state cannot be verified, fail closed and ask the
            // transport to stop. The durable outbox can retry after SQL recovers.
            sendCancellation.Cancel();
        }
    }

    private async Task CancelIfOrphanedAsync(long id, CancellationToken cancellationToken)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync(cancellationToken);
        var row = await db.EmailOutbox.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (row is null || row.Status == EmailOutboxStatuses.Cancelled)
            return;

        var requestExists = !row.RequestId.HasValue || await db.DCRRequests.AsNoTracking().AnyAsync(
            x => x.Id == row.RequestId.Value,
            cancellationToken);
        if (requestExists)
            return;

        row.Status = EmailOutboxStatuses.Cancelled;
        row.NextAttemptAt = null;
        row.LastError = "DCR was deleted by Administrator; email delivery was cancelled.";
        await db.SaveChangesAsync(cancellationToken);
    }


    private async Task<EmailAttachmentInfo?> ResolveAttachmentAsync(
        EmailOutboxItem item,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(item.AttachmentFilePath))
        {
            if (!DcrMailAttachmentPolicy.RequiresAutomaticPdf(item.RequestId, item.NotificationType))
                return null;

            var generated = await _pdf.GenerateWorkerMailAttachmentPdfAsync(
                item.RequestId!.Value,
                cancellationToken);
            return await RetainGeneratedAttachmentAsync(
                item,
                generated.FilePath,
                generated.FileName,
                generated.ContentType,
                cancellationToken);
        }

        var fileName = string.IsNullOrWhiteSpace(item.AttachmentFileName)
            ? Path.GetFileName(item.AttachmentFilePath)
            : item.AttachmentFileName;
        var contentType = string.IsNullOrWhiteSpace(item.AttachmentContentType)
            ? "application/pdf"
            : item.AttachmentContentType;

        if (File.Exists(item.AttachmentFilePath))
            return new EmailAttachmentInfo(item.AttachmentFilePath, fileName, contentType);

        if (!item.RequestId.HasValue)
            throw new FileNotFoundException(
                "Không tìm thấy file PDF cần đính kèm email và EmailOutbox không có RequestId để sinh lại PDF.",
                item.AttachmentFilePath);

        // The path stored by a client may be unavailable to the Windows identity running
        // the central Mail Server (for example a stale client-local path or a share that
        // was temporarily unavailable). Rebuild a fresh PDF locally on the Mail Server.
        // This prevents an approval notification from being permanently blocked by a
        // missing staging file. GeneratePreviewPdfAsync writes under LocalAppData and is
        // therefore independent from the client PC and the network share.
        var regeneratedPath = await _pdf.GeneratePreviewPdfAsync(item.RequestId.Value, cancellationToken);

        return await RetainGeneratedAttachmentAsync(
            item,
            regeneratedPath,
            fileName,
            contentType,
            cancellationToken);
    }

    private async Task<EmailAttachmentInfo?> RetainGeneratedAttachmentAsync(
        EmailOutboxItem item,
        string generatedPath,
        string fileName,
        string contentType,
        CancellationToken cancellationToken)
    {
        if (!item.RequestId.HasValue)
        {
            TryDeleteGeneratedFile(generatedPath);
            throw new InvalidOperationException(
                "Không thể lưu PDF đính kèm email vì EmailOutbox không có RequestId.");
        }

        var requestId = item.RequestId.Value;
        var retainedForDelivery = false;
        await using (var db = _dbFactory())
        {
            await db.OpenSqlConnectionWithRetryAsync(cancellationToken);
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var requestStillExists = await db.DCRRequests.AsNoTracking().AnyAsync(
                x => x.Id == requestId,
                cancellationToken);
            var tracked = await db.EmailOutbox.SingleOrDefaultAsync(x => x.Id == item.Id, cancellationToken);
            if (requestStillExists && tracked is not null && tracked.Status == EmailOutboxStatuses.Processing)
            {
                tracked.AttachmentFilePath = generatedPath;
                tracked.AttachmentFileName = fileName;
                tracked.AttachmentContentType = contentType;
                await db.SaveChangesAsync(cancellationToken);
                retainedForDelivery = true;
            }
            await tx.CommitAsync(cancellationToken);
        }

        if (!retainedForDelivery)
        {
            TryDeleteGeneratedFile(generatedPath);
            return null;
        }

        return new EmailAttachmentInfo(generatedPath, fileName, contentType);
    }

    private static void TryDeleteGeneratedFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // AdminService will make another best-effort cleanup from persisted paths.
        }
    }


    private async Task RecoverStaleProcessingAsync(CancellationToken cancellationToken)
    {
        var threshold = DateTime.Now.AddMinutes(-10);
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync(cancellationToken);
        var rows = await db.EmailOutbox
            .Where(x => x.Status == EmailOutboxStatuses.Processing &&
                        (!x.LastAttemptAt.HasValue || x.LastAttemptAt < threshold))
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            row.Status = EmailOutboxStatuses.Pending;
            row.NextAttemptAt = DateTime.Now;
            row.LastError = "Recovered stale Processing row after previous worker interruption.";
        }
        if (rows.Count > 0)
            await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<List<long>> GetDueIdsAsync(int batchSize, bool forcePendingNow, CancellationToken cancellationToken)
    {
        var now = DateTime.Now;
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync(cancellationToken);

        var query = db.EmailOutbox.AsNoTracking()
            .Where(x => x.Status == EmailOutboxStatuses.Pending &&
                        x.RetryCount < x.MaxRetryCount);

        if (!forcePendingNow)
        {
            query = query.Where(x => !x.NextAttemptAt.HasValue || x.NextAttemptAt <= now);
        }

        return await query
            .OrderBy(x => x.CreatedAt)
            .Select(x => x.Id)
            .Take(Math.Clamp(batchSize, 1, 100))
            .ToListAsync(cancellationToken);
    }

    private async Task<bool> ClaimAsync(long id, CancellationToken cancellationToken)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync(cancellationToken);
        var row = await db.EmailOutbox.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (row is null || row.Status != EmailOutboxStatuses.Pending)
            return false;
        row.Status = EmailOutboxStatuses.Processing;
        row.LastAttemptAt = DateTime.Now;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    private async Task<bool> MarkSentAsync(long id, string senderAccount, CancellationToken cancellationToken)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync(cancellationToken);
        var row = await db.EmailOutbox.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (row is null || row.Status != EmailOutboxStatuses.Processing)
            return false;
        row.Status = EmailOutboxStatuses.Sent;
        row.SentAt = DateTime.Now;
        row.SenderAccount = senderAccount;
        row.LastError = string.Empty;
        row.NextAttemptAt = null;
        if (row.NotificationLogId.HasValue)
        {
            var log = await db.DCRNotificationLogs.SingleOrDefaultAsync(x => x.Id == row.NotificationLogId.Value, cancellationToken);
            if (log is not null)
            {
                log.Success = true;
                log.SentAt = row.SentAt.Value;
                log.Details = $"Sent by server mail worker. Sender={senderAccount}; OutboxId={row.Id}";
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<bool> MarkRequiresSignInAsync(long id, string error, CancellationToken cancellationToken)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync(cancellationToken);
        var row = await db.EmailOutbox.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (row is null || row.Status != EmailOutboxStatuses.Processing)
            return false;
        row.Status = EmailOutboxStatuses.RequiresSignIn;
        row.LastError = Limit(error, 2000);
        row.NextAttemptAt = null;
        if (row.NotificationLogId.HasValue)
        {
            var log = await db.DCRNotificationLogs.SingleOrDefaultAsync(x => x.Id == row.NotificationLogId.Value, cancellationToken);
            if (log is not null)
            {
                log.Success = false;
                log.Details = $"Queued but server Microsoft sign-in is required. OutboxId={row.Id}; {Limit(error, 700)}";
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<bool?> MarkRetryOrFailedAsync(long id, string error, CancellationToken cancellationToken)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync(cancellationToken);
        var row = await db.EmailOutbox.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (row is null || row.Status != EmailOutboxStatuses.Processing)
            return null;
        row.RetryCount++;
        row.LastError = Limit(error, 2000);
        var failed = row.RetryCount >= row.MaxRetryCount;
        row.Status = failed ? EmailOutboxStatuses.Failed : EmailOutboxStatuses.Pending;
        row.NextAttemptAt = failed ? null : DateTime.Now.AddMinutes(GetRetryDelayMinutes(row.RetryCount));
        if (row.NotificationLogId.HasValue)
        {
            var log = await db.DCRNotificationLogs.SingleOrDefaultAsync(x => x.Id == row.NotificationLogId.Value, cancellationToken);
            if (log is not null)
            {
                log.Success = false;
                log.Details = failed
                    ? $"Mail worker failed permanently after {row.RetryCount} attempts. OutboxId={row.Id}; {Limit(error, 700)}"
                    : $"Mail worker attempt {row.RetryCount} failed; queued for retry. OutboxId={row.Id}; {Limit(error, 700)}";
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        return failed;
    }

    private static int GetRetryDelayMinutes(int retryCount) => retryCount switch
    {
        <= 1 => 1,
        2 => 2,
        _ => 5
    };

    private static string Limit(string value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];
}
