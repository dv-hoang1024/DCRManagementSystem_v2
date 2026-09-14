using DCRManagementSystem.Data;
using System.Data;
using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DCRManagementSystem.Services;

public sealed class NotificationDispatchResult
{
    public int QueuedCount { get; private set; }
    public int SentCount { get; private set; }
    public int FailedCount { get; private set; }
    public int SkippedCount { get; private set; }
    public List<string> Details { get; } = new();

    public bool HasProblems => FailedCount > 0 || SkippedCount > 0;
    public int TotalCount => QueuedCount + SentCount + FailedCount + SkippedCount;

    public void AddQueued(string recipient)
    {
        QueuedCount++;
        Details.Add($"Đã xếp hàng: {recipient}");
    }

    public void AddSent(string recipient)
    {
        SentCount++;
        Details.Add($"Đã gửi: {recipient}");
    }

    public void AddFailed(string recipient, string details)
    {
        FailedCount++;
        Details.Add($"Lỗi {recipient}: {details}");
    }

    public void AddSkipped(string recipient, string details)
    {
        SkippedCount++;
        Details.Add($"Chưa xếp hàng {recipient}: {details}");
    }

    public void MergeFrom(NotificationDispatchResult other)
    {
        if (other is null)
            return;

        QueuedCount += other.QueuedCount;
        SentCount += other.SentCount;
        FailedCount += other.FailedCount;
        SkippedCount += other.SkippedCount;
        Details.AddRange(other.Details);
    }

    public string ToUserMessage()
    {
        if (TotalCount == 0)
            return "Không có email notification cần tạo.";

        var parts = new List<string>();
        if (QueuedCount > 0) parts.Add($"{QueuedCount} đã đưa vào hàng đợi Mail Server");
        if (SentCount > 0) parts.Add($"{SentCount} đã gửi trực tiếp");
        if (FailedCount > 0) parts.Add($"{FailedCount} lỗi");
        if (SkippedCount > 0) parts.Add($"{SkippedCount} chưa xếp hàng");
        var summary = "Email: " + string.Join(", ", parts);

        var important = Details
            .Where(x => x.StartsWith("Lỗi ", StringComparison.Ordinal) || x.StartsWith("Chưa xếp hàng ", StringComparison.Ordinal))
            .Take(3)
            .ToList();
        if (important.Count > 0)
            summary += "\r\n" + string.Join("\r\n", important);
        return summary;
    }
}

public sealed class NotificationService
{
    private readonly Func<AppDbContext> _dbFactory;
    private readonly AppSettings _settings;
    private readonly EmailConfigurationService _emailConfiguration;
    private readonly TeamsWebhookService _teams;
    private readonly PdfService _pdf;

    public NotificationService(Func<AppDbContext> dbFactory, AppSettings settings)
    {
        _dbFactory = dbFactory;
        _settings = settings;
        _emailConfiguration = new EmailConfigurationService(dbFactory, settings);
        _teams = new TeamsWebhookService(settings);
        _pdf = new PdfService(dbFactory, settings);
    }

    public async Task<NotificationDispatchResult> SendCurrentApprovalAssignedAsync(int requestId)
    {
        var result = new NotificationDispatchResult();
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        var request = await db.DCRRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == requestId);
        if (request is null || request.Status != RequestStatuses.InApproval)
            return result;

        var rows = await db.DCRApprovalFlows
            .AsNoTracking()
            .Include(x => x.Approver)
            .Where(x => x.RequestId == request.Id &&
                        x.RevisionNo == request.RevisionNo &&
                        x.StageNumber == request.CurrentStage &&
                        x.Decision == ApprovalDecisions.Pending &&
                        x.ApproverId.HasValue)
            .ToListAsync();

        var rowIds = rows.Select(x => x.Id).ToList();
        var alreadyQueuedFlowIds = rowIds.Count == 0
            ? new HashSet<int>()
            : (await db.EmailOutbox
                .AsNoTracking()
                .Where(x => x.ApprovalFlowId.HasValue &&
                            rowIds.Contains(x.ApprovalFlowId.Value) &&
                            x.NotificationType == NotificationTypes.ApprovalAssigned &&
                            x.Status != EmailOutboxStatuses.Cancelled)
                .Select(x => x.ApprovalFlowId!.Value)
                .Distinct()
                .ToListAsync())
                .ToHashSet();

        var emailSettings = await _emailConfiguration.GetAsync(db);
        var link = await GetRequestLinkAsync(db, request.Id);
        var approvalPdf = emailSettings.Enabled && IsEmailConfigurationReady(emailSettings, out _)
            ? await TryGeneratePdfAttachmentAsync(request.Id)
            : null;
        foreach (var row in rows)
        {
            if (alreadyQueuedFlowIds.Contains(row.Id))
                continue;

            var approver = row.Approver;
            if (approver is null)
            {
                result.AddFailed("(không xác định)", $"ApprovalFlow #{row.Id} không tải được user approver.");
                continue;
            }

            var subject = $"[GGP] DCR cần phê duyệt: {request.DCRNumber}";
            var body =
                $"Xin chào {approver.FullName},\r\n\r\n" +
                $"DCR {request.DCRNumber} đang chờ bạn phê duyệt.\r\n" +
                 $"Tiêu đề: {request.Title}\r\n" +
                 $"Stage: {row.StageName}\r\n" +
                 $"Hạn xử lý: {(row.DueDate.HasValue ? row.DueDate.Value.ToString("dd/MM/yyyy HH:mm") : "-")}\r\n" +
                 (string.IsNullOrWhiteSpace(link) ? string.Empty : $"Mở DCR: {link}\r\n") +
                 "\r\nVui lòng xử lý trong DCR Management System.";

            MergeResult(result, await TryQueueAndLogAsync(
                db, emailSettings, request.Id, row.Id, NotificationTypes.ApprovalAssigned,
                approver.Email, subject, body, approvalPdf));
        }

        if (rows.Count == 0)
            result.AddFailed("(workflow)", $"Không tìm thấy approver Pending tại Stage {request.CurrentStage}.");

        await TrySendTeamsAsync(db, request.Id, null, NotificationTypes.ApprovalAssigned,
            $"DCR {request.DCRNumber} cần phê duyệt",
            $"Stage {request.CurrentStage}: {request.Title}" +
            (string.IsNullOrWhiteSpace(link) ? string.Empty : $"\n{link}"));
        return result;
    }

    public async Task<NotificationDispatchResult> SendOutcomeAsync(int requestId, string notificationType, string comment)
    {
        var result = new NotificationDispatchResult();
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        var request = await db.DCRRequests
            .AsNoTracking()
            .Include(x => x.RequestOwner)
            .SingleOrDefaultAsync(x => x.Id == requestId);
        if (request is null)
            return result;

        List<int> recipientIds;
        var creatorOnly = notificationType is NotificationTypes.Approved or NotificationTypes.Returned or NotificationTypes.Rejected;
        if (creatorOnly)
        {
            // Outcomes belong to the person who raised the DCR, including when an admin submitted it.
            recipientIds = new List<int> { request.CreatedBy };
        }
        else
        {
            recipientIds = await db.DCRApprovalFlows.AsNoTracking()
                .Where(x => x.RequestId == requestId && x.ApproverId.HasValue)
                .Select(x => x.ApproverId!.Value)
                .Distinct()
                .ToListAsync();
            recipientIds.Add(request.RequestOwnerId);
            recipientIds.Add(request.CreatedBy);
        }

        var distinctRecipientIds = recipientIds.Distinct().ToList();
        var recipientQuery = db.Users.AsNoTracking()
            .Where(x => distinctRecipientIds.Contains(x.UserId));

        // Preserve delivery to the creator even if their account was disabled after submission.
        if (!creatorOnly)
            recipientQuery = recipientQuery.Where(x => x.IsActive && !x.IsDeleted);

        var recipients = await recipientQuery.ToListAsync();

        if (creatorOnly && recipients.All(x => x.UserId != request.CreatedBy))
            result.AddFailed($"UserId={request.CreatedBy}", "Không tìm thấy tài khoản người tạo DCR để gửi thông báo kết quả.");

        // Each return is a separate event: a second return after resubmission must notify again.
        int? outcomeFlowId = null;
        if (notificationType is NotificationTypes.Returned or NotificationTypes.Rejected)
        {
            outcomeFlowId = await db.DCRApprovalFlows.AsNoTracking()
                .Where(x => x.RequestId == requestId && x.Decision == notificationType)
                .OrderByDescending(x => x.RevisionNo).ThenByDescending(x => x.DecisionDate).ThenByDescending(x => x.Id)
                .Select(x => (int?)x.Id).FirstOrDefaultAsync();
        }

        var statusText = notificationType switch
        {
            NotificationTypes.Approved => "đã được phê duyệt hoàn tất",
            NotificationTypes.Rejected => "đã bị từ chối",
            NotificationTypes.Returned => "đã được trả về để bổ sung thông tin",
            _ => "đã được cập nhật"
        };

        var emailSettings = await _emailConfiguration.GetAsync(db);
        var link = await GetRequestLinkAsync(db, request.Id);
        var outcomePdf = emailSettings.Enabled && IsEmailConfigurationReady(emailSettings, out _)
            ? await TryGeneratePdfAttachmentAsync(request.Id)
            : null;

        var existingOutcomeRecipients = (await db.EmailOutbox
            .AsNoTracking()
            .Where(x => x.RequestId == request.Id &&
                        x.ApprovalFlowId == outcomeFlowId &&
                        x.NotificationType == notificationType &&
                        x.Status != EmailOutboxStatuses.Cancelled)
            .Select(x => x.Recipient)
            .ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var user in recipients
                     .GroupBy(x => string.IsNullOrWhiteSpace(x.Email) ? $"USER:{x.UserId}" : x.Email, StringComparer.OrdinalIgnoreCase)
                     .Select(x => x.First()))
        {
            if (!string.IsNullOrWhiteSpace(user.Email) && existingOutcomeRecipients.Contains(user.Email.Trim()))
                continue;
            var subject = $"[GGP] DCR {request.DCRNumber} {statusText}";
            var body =
                $"Xin chào {user.FullName},\r\n\r\n" +
                $"DCR {request.DCRNumber} {statusText}.\r\n" +
                $"Tiêu đề: {request.Title}\r\n" +
                (string.IsNullOrWhiteSpace(comment) ? string.Empty : $"Ý kiến: {comment}\r\n") +
                (string.IsNullOrWhiteSpace(link) ? string.Empty : $"Mở DCR: {link}\r\n");

            MergeResult(result, await TryQueueAndLogAsync(
                db, emailSettings, request.Id, outcomeFlowId, notificationType,
                user.Email, subject, body, outcomePdf));
        }

        // The shared Teams webhook would broadcast these private outcomes to the whole channel.
        if (notificationType is not (NotificationTypes.Returned or NotificationTypes.Rejected))
            await TrySendTeamsAsync(db, request.Id, null, notificationType,
                $"DCR {request.DCRNumber}: {statusText}", request.Title);
        return result;
    }

    public async Task<NotificationDispatchResult> ReconcileWorkflowNotificationsAsync(
        int? requestId = null,
        CancellationToken cancellationToken = default)
    {
        var result = new NotificationDispatchResult();

        // 1) Repair any missing "next approver" notification for the stage that is
        // currently active. This makes workflow email delivery durable even when the
        // application closes or the network drops immediately after an approval commit.
        List<int> activeRequestIds;
        await using (var db = _dbFactory())
        {
            await db.OpenSqlConnectionWithRetryAsync(cancellationToken);
            var pendingRows = db.DCRApprovalFlows
                .AsNoTracking()
                .Where(x => x.ApproverId.HasValue &&
                            x.Decision == ApprovalDecisions.Pending &&
                            x.Request != null &&
                            x.Request.Status == RequestStatuses.InApproval &&
                            x.RevisionNo == x.Request.RevisionNo &&
                            x.StageNumber == x.Request.CurrentStage);

            if (requestId.HasValue)
                pendingRows = pendingRows.Where(x => x.RequestId == requestId.Value);

            activeRequestIds = await pendingRows
                .Where(x => !db.EmailOutbox.Any(o =>
                    o.ApprovalFlowId == x.Id &&
                    o.NotificationType == NotificationTypes.ApprovalAssigned &&
                    o.Status != EmailOutboxStatuses.Cancelled))
                .Select(x => x.RequestId)
                .Distinct()
                .ToListAsync(cancellationToken);
        }

        foreach (var id in activeRequestIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.MergeFrom(await SendCurrentApprovalAssignedAsync(id));
        }

        // 2) Repair the final notification to the DCR creator after every required
        // approval line has finished. Only the creator receives the completion email.
        List<int> approvedRequestIds;
        await using (var db = _dbFactory())
        {
            await db.OpenSqlConnectionWithRetryAsync(cancellationToken);
            var approved = db.DCRRequests
                .AsNoTracking()
                .Where(x => x.Status == RequestStatuses.Approved);
            if (requestId.HasValue)
                approved = approved.Where(x => x.Id == requestId.Value);

            approvedRequestIds = await approved
                .Where(x => !db.EmailOutbox.Any(o =>
                    o.RequestId == x.Id &&
                    o.ApprovalFlowId == null &&
                    o.NotificationType == NotificationTypes.Approved &&
                    o.Status != EmailOutboxStatuses.Cancelled))
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);
        }

        foreach (var id in approvedRequestIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.MergeFrom(await SendOutcomeAsync(id, NotificationTypes.Approved, string.Empty));
        }

        return result;
    }

    public async Task<NotificationDispatchResult> SendReminderAsync(int approvalFlowId, bool escalation)
    {
        var result = new NotificationDispatchResult();
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        var row = await db.DCRApprovalFlows
            .Include(x => x.Request)
            .Include(x => x.Approver)
            .SingleOrDefaultAsync(x => x.Id == approvalFlowId);
        if (row?.Request is null || row.Approver is null || row.Decision != ApprovalDecisions.Pending)
            return result;

        var emailSettings = await _emailConfiguration.GetAsync(db);
        var type = escalation ? NotificationTypes.Escalation : NotificationTypes.Reminder;
        var request = row.Request;
        var link = await GetRequestLinkAsync(db, request.Id);
        var reminderPdf = emailSettings.Enabled && IsEmailConfigurationReady(emailSettings, out _)
            ? await TryGeneratePdfAttachmentAsync(request.Id)
            : null;
        var subject = escalation
            ? $"[GGP][ESCALATION] DCR quá hạn: {request.DCRNumber}"
            : $"[GGP] Nhắc duyệt DCR: {request.DCRNumber}";
        var body =
            $"DCR {request.DCRNumber} đang chờ phê duyệt tại stage '{row.StageName}'.\r\n" +
            $"Assigned: {row.AssignedDate:dd/MM/yyyy HH:mm}\r\n" +
            $"Due: {row.DueDate:dd/MM/yyyy HH:mm}\r\n" +
            (string.IsNullOrWhiteSpace(link) ? string.Empty : $"Mở DCR: {link}\r\n");

        MergeResult(result, await TryQueueAndLogAsync(
            db, emailSettings, request.Id, row.Id, type, row.Approver.Email, subject, body, reminderPdf));

        if (escalation && !string.IsNullOrWhiteSpace(_settings.Reminder.EscalationEmail))
        {
            MergeResult(result, await TryQueueAndLogAsync(
                db, emailSettings, request.Id, row.Id, type,
                _settings.Reminder.EscalationEmail, subject, body, reminderPdf));
        }
        return result;
    }

    private async Task<NotificationQueueOutcome> TryQueueAndLogAsync(
        AppDbContext db,
        EmailSettings emailSettings,
        int requestId,
        int? approvalFlowId,
        string type,
        string recipient,
        string subject,
        string body,
        PdfMailAttachment? attachment)
    {
        if (string.IsNullOrWhiteSpace(recipient))
            return NotificationQueueOutcome.Failed("(thiếu email)", "User approver chưa được khai báo Email trong Quản lý người dùng.");
        if (!emailSettings.Enabled)
            return NotificationQueueOutcome.Skipped(recipient, "Email notification đang tắt trên Mail Server.");
        if (!IsEmailConfigurationReady(emailSettings, out var configurationProblem))
            return NotificationQueueOutcome.Skipped(recipient, configurationProblem);

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            // Deletion deliberately keeps EmailOutbox history without foreign keys.
            // Re-check the parent inside the queue transaction so a reminder/client
            // racing an Administrator deletion cannot create a new orphaned mail job.
            var requestStillExists = await db.DCRRequests.AsNoTracking()
                .AnyAsync(x => x.Id == requestId);
            if (!requestStillExists)
            {
                await tx.CommitAsync();
                TryDeleteGeneratedAttachment(attachment);
                return NotificationQueueOutcome.Skipped(recipient.Trim(), "DCR đã bị xóa; tác vụ gửi mail đã được hủy.");
            }

            if (approvalFlowId.HasValue)
            {
                var approvalStillExists = await db.DCRApprovalFlows.AsNoTracking()
                    .AnyAsync(x => x.Id == approvalFlowId.Value && x.RequestId == requestId);
                if (!approvalStillExists)
                {
                    await tx.CommitAsync();
                    TryDeleteGeneratedAttachment(attachment);
                    return NotificationQueueOutcome.Skipped(recipient.Trim(), "Thời hạn/phê duyệt của DCR đã bị hủy.");
                }
            }

            var idempotentNotification =
                type == NotificationTypes.ApprovalAssigned ||
                type == NotificationTypes.Approved ||
                type == NotificationTypes.Rejected ||
                type == NotificationTypes.Returned;

            if (idempotentNotification)
            {
                // Serialize queue creation across client + Mail Server reconciliation.
                // This closes the race where both sides see "no outbox yet" and insert
                // the same next-stage/final notification at the same time.
                var resource = approvalFlowId.HasValue
                    ? $"DCR.Notification.Flow.{approvalFlowId.Value}.{type}"
                    : $"DCR.Notification.Request.{requestId}.{type}.{recipient.Trim().ToUpperInvariant()}";
                if (resource.Length > 240)
                    resource = resource[..240];

                var connection = db.Database.GetDbConnection();
                await using var command = connection.CreateCommand();
                command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
                command.CommandText = "DECLARE @r int; EXEC @r=sp_getapplock @Resource=@resource, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=5000; SELECT @r;";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@resource";
                parameter.Value = resource;
                command.Parameters.Add(parameter);
                var lockResult = Convert.ToInt32(await command.ExecuteScalarAsync());
                if (lockResult < 0)
                    throw new InvalidOperationException("Không thể khóa notification để chống gửi trùng.");

                var normalizedRecipient = recipient.Trim();
                var alreadyExists = approvalFlowId.HasValue
                    ? await db.EmailOutbox.AsNoTracking().AnyAsync(x =>
                        x.ApprovalFlowId == approvalFlowId &&
                        x.NotificationType == type &&
                        x.Status != EmailOutboxStatuses.Cancelled)
                    : await db.EmailOutbox.AsNoTracking().AnyAsync(x =>
                        x.RequestId == requestId &&
                        x.ApprovalFlowId == null &&
                        x.NotificationType == type &&
                        x.Recipient == normalizedRecipient &&
                        x.Status != EmailOutboxStatuses.Cancelled);

                if (alreadyExists)
                {
                    await tx.CommitAsync();
                    return NotificationQueueOutcome.Queued(normalizedRecipient);
                }
            }

            var log = new DCRNotificationLog
            {
                RequestId = requestId,
                ApprovalFlowId = approvalFlowId,
                NotificationType = type,
                Recipient = recipient.Trim(),
                SentAt = DateTime.Now,
                Success = false,
                Details = attachment is null
                    ? DcrMailAttachmentPolicy.RequiresAutomaticPdf(requestId, type)
                        ? "Queued for DCR Mail Worker; PDF attachment will be generated by the worker."
                        : "Queued for DCR Mail Worker without PDF attachment."
                    : $"Queued for DCR Mail Worker with PDF attachment '{attachment.FileName}'."
            };
            db.DCRNotificationLogs.Add(log);
            await db.SaveChangesAsync();

            var outbox = new EmailOutboxItem
            {
                RequestId = requestId,
                ApprovalFlowId = approvalFlowId,
                NotificationLogId = log.Id,
                NotificationType = type,
                Recipient = recipient.Trim(),
                Subject = subject,
                Body = body,
                AttachmentFilePath = attachment?.FilePath ?? string.Empty,
                AttachmentFileName = attachment?.FileName ?? string.Empty,
                AttachmentContentType = attachment?.ContentType ?? string.Empty,
                Status = EmailOutboxStatuses.Pending,
                RetryCount = 0,
                MaxRetryCount = 3,
                CreatedAt = DateTime.Now,
                NextAttemptAt = DateTime.Now
            };
            db.EmailOutbox.Add(outbox);
            await db.SaveChangesAsync();
            log.Details = $"Queued for DCR Mail Worker. OutboxId={outbox.Id}";
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return NotificationQueueOutcome.Queued(recipient.Trim());
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private static void TryDeleteGeneratedAttachment(PdfMailAttachment? attachment)
    {
        if (attachment is null || string.IsNullOrWhiteSpace(attachment.FilePath))
            return;

        try
        {
            if (File.Exists(attachment.FilePath))
                File.Delete(attachment.FilePath);
        }
        catch
        {
            // AdminService also records storage cleanup failures in DCRDeletionLogs.
        }
    }

    private async Task<PdfMailAttachment?> TryGeneratePdfAttachmentAsync(int requestId)
    {
        try
        {
            await using var db = _dbFactory();
            await db.OpenSqlConnectionWithRetryAsync();
            var request = await db.DCRRequests
                .AsNoTracking()
                .Where(x => x.Id == requestId)
                .Select(x => new { x.DCRNumber, x.RevisionNo, x.Status, x.FinalPdfPath })
                .SingleOrDefaultAsync();

            if (request is not null &&
                request.Status == RequestStatuses.Approved &&
                !string.IsNullOrWhiteSpace(request.FinalPdfPath))
            {
                var finalPath = await _pdf.GetVerifiedFinalApprovedPdfPathAsync(requestId);
                var fileInfo = new FileInfo(finalPath);
                return new PdfMailAttachment(
                    finalPath,
                    $"{request.DCRNumber}_R{request.RevisionNo}_APPROVED.pdf",
                    "application/pdf",
                    string.Empty,
                    fileInfo.Length);
            }

            return await _pdf.GenerateMailAttachmentPdfAsync(requestId);
        }
        catch
        {
            // Notification itself remains available even if the file server is temporarily unavailable.
            // The mail body tells the approver to open the DCR in the application in that case.
            return null;
        }
    }

    private async Task<string> GetRequestLinkAsync(AppDbContext db, int requestId)
    {
        try
        {
            var web = await new WebPortalConfigurationService(_dbFactory).GetAsync(db);
            if (web.Enabled && web.AllowApproval)
                return web.BuildRequestUrl(requestId);
        }
        catch
        {
            // Link web là tiện ích. Nếu cấu hình web lỗi, notification vẫn phải tiếp tục
            // và quay về desktop protocol cũ thay vì làm hỏng transaction mail.
        }

        return _settings.GetRequestLink(requestId);
    }

    private static bool IsEmailConfigurationReady(EmailSettings settings, out string problem)
    {
        var mode = EmailConfigurationService.NormalizeMode(settings.AuthenticationMode);
        if (mode == EmailAuthenticationModes.MicrosoftGraphDelegated)
        {
            if (string.IsNullOrWhiteSpace(settings.ClientId))
            {
                problem = "Mail Server chưa có Microsoft Graph Application (Client) ID.";
                return false;
            }
            problem = string.Empty;
            return true;
        }

        if (string.IsNullOrWhiteSpace(settings.SmtpHost) || string.IsNullOrWhiteSpace(settings.FromAddress))
        {
            problem = "Mail Server chưa cấu hình SMTP Host hoặc From Address.";
            return false;
        }
        problem = string.Empty;
        return true;
    }

    private async Task TrySendTeamsAsync(
        AppDbContext db,
        int requestId,
        int? approvalFlowId,
        string type,
        string title,
        string body)
    {
        if (!_settings.Teams.Enabled || string.IsNullOrWhiteSpace(_settings.Teams.WebhookUrl))
            return;
        var success = true;
        var details = string.Empty;
        try { await _teams.SendAsync(title, body); }
        catch (Exception ex) { success = false; details = ex.Message; }

        db.DCRNotificationLogs.Add(new DCRNotificationLog
        {
            RequestId = requestId,
            ApprovalFlowId = approvalFlowId,
            NotificationType = type,
            Recipient = "MS Teams Webhook",
            SentAt = DateTime.Now,
            Success = success,
            Details = details
        });
        await db.SaveChangesAsync();
    }

    private static void MergeResult(NotificationDispatchResult result, NotificationQueueOutcome outcome)
    {
        switch (outcome.Status)
        {
            case NotificationQueueStatus.Queued:
                result.AddQueued(outcome.Recipient);
                break;
            case NotificationQueueStatus.Skipped:
                result.AddSkipped(outcome.Recipient, outcome.Details);
                break;
            default:
                result.AddFailed(outcome.Recipient, outcome.Details);
                break;
        }
    }

    private enum NotificationQueueStatus { Queued, Failed, Skipped }

    private sealed record NotificationQueueOutcome(NotificationQueueStatus Status, string Recipient, string Details)
    {
        public static NotificationQueueOutcome Queued(string recipient) => new(NotificationQueueStatus.Queued, recipient, string.Empty);
        public static NotificationQueueOutcome Failed(string recipient, string details) => new(NotificationQueueStatus.Failed, recipient, details);
        public static NotificationQueueOutcome Skipped(string recipient, string details) => new(NotificationQueueStatus.Skipped, recipient, details);
    }
}
