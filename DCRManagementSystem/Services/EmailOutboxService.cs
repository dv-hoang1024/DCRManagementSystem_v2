using DCRManagementSystem.Data;
using DCRManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace DCRManagementSystem.Services;

public sealed class EmailOutboxSnapshot
{
    public int Pending { get; init; }
    public int RequiresSignIn { get; init; }
    public int Failed { get; init; }
    public int SentToday { get; init; }
    public DateTime? OldestPendingAt { get; init; }
}

public sealed class EmailOutboxService : IEmailOutboxService
{
    private readonly Func<AppDbContext> _dbFactory;

    public EmailOutboxService(Func<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<long> EnqueueAsync(
        int? requestId,
        int? approvalFlowId,
        long? notificationLogId,
        string notificationType,
        string recipient,
        string subject,
        string body,
        string attachmentFilePath = "",
        string attachmentFileName = "",
        string attachmentContentType = "",
        CancellationToken cancellationToken = default)
    {
        recipient = recipient.Trim();
        if (string.IsNullOrWhiteSpace(recipient))
            throw new InvalidOperationException("Không thể đưa email vào hàng đợi vì người nhận đang trống.");

        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync(cancellationToken);
        var item = new EmailOutboxItem
        {
            RequestId = requestId,
            ApprovalFlowId = approvalFlowId,
            NotificationLogId = notificationLogId,
            NotificationType = notificationType.Trim(),
            Recipient = recipient,
            Subject = subject,
            Body = body,
            AttachmentFilePath = attachmentFilePath ?? string.Empty,
            AttachmentFileName = attachmentFileName ?? string.Empty,
            AttachmentContentType = attachmentContentType ?? string.Empty,
            Status = EmailOutboxStatuses.Pending,
            RetryCount = 0,
            MaxRetryCount = 3,
            CreatedAt = DateTime.Now,
            NextAttemptAt = DateTime.Now
        };
        db.EmailOutbox.Add(item);
        await db.SaveChangesAsync(cancellationToken);
        return item.Id;
    }

    public async Task<long> EnqueueTestAsync(string recipient, CancellationToken cancellationToken = default)
    {
        return await EnqueueAsync(
            null,
            null,
            null,
            "MailServerTest",
            recipient,
            "[DCR] Kiểm tra Email Outbox / Mail Worker",
            $"Đây là email kiểm tra từ DCR Management System.\r\n\r\n" +
            $"Máy đưa vào hàng đợi: {Environment.MachineName}\r\n" +
            $"Thời gian: {DateTime.Now:dd/MM/yyyy HH:mm:ss}\r\n\r\n" +
            "Nếu nhận được email này thì cơ chế SQL EmailOutbox -> DCR Mail Worker -> Microsoft Graph đang hoạt động.",
            cancellationToken: cancellationToken);
    }

    public async Task<int> RequeueAuthenticationBlockedAsync(CancellationToken cancellationToken = default)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync(cancellationToken);
        var rows = await db.EmailOutbox
            .Where(x => x.Status == EmailOutboxStatuses.RequiresSignIn && x.RetryCount < x.MaxRetryCount)
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            row.Status = EmailOutboxStatuses.Pending;
            row.NextAttemptAt = DateTime.Now;
            row.LastError = string.Empty;
        }
        await db.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }

    public async Task<EmailOutboxSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync(cancellationToken);
        var today = DateTime.Today;
        return new EmailOutboxSnapshot
        {
            Pending = await db.EmailOutbox.CountAsync(x => x.Status == EmailOutboxStatuses.Pending || x.Status == EmailOutboxStatuses.Processing, cancellationToken),
            RequiresSignIn = await db.EmailOutbox.CountAsync(x => x.Status == EmailOutboxStatuses.RequiresSignIn, cancellationToken),
            Failed = await db.EmailOutbox.CountAsync(x => x.Status == EmailOutboxStatuses.Failed, cancellationToken),
            SentToday = await db.EmailOutbox.CountAsync(x => x.Status == EmailOutboxStatuses.Sent && x.SentAt >= today, cancellationToken),
            OldestPendingAt = await db.EmailOutbox
                .Where(x => x.Status == EmailOutboxStatuses.Pending)
                .OrderBy(x => x.CreatedAt)
                .Select(x => (DateTime?)x.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken)
        };
    }
}
