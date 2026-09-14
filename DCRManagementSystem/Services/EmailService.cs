using System.Net;
using System.Net.Mail;
using DCRManagementSystem.Helpers;
using AppEmailSettings = DCRManagementSystem.Helpers.EmailSettings;

namespace DCRManagementSystem.Services;

public sealed record EmailAttachmentInfo(string FilePath, string FileName, string ContentType);

public sealed class EmailService
{
    private readonly GraphDelegatedMailService _graph = new();

    public Task<string> SendAsync(
        AppEmailSettings email,
        string recipient,
        string subject,
        string body,
        bool allowInteractive = true,
        CancellationToken cancellationToken = default)
    {
        return SendAsync(email, recipient, subject, body, null, allowInteractive, cancellationToken);
    }

    public async Task<string> SendAsync(
        AppEmailSettings email,
        string recipient,
        string subject,
        string body,
        EmailAttachmentInfo? attachment,
        bool allowInteractive = true,
        CancellationToken cancellationToken = default)
    {
        if (!email.Enabled)
            throw new InvalidOperationException("Email notification đang bị tắt trong Thiết lập hệ thống.");
        if (string.IsNullOrWhiteSpace(recipient))
            throw new InvalidOperationException("Người nhận chưa có địa chỉ email.");

        if (attachment is not null && !File.Exists(attachment.FilePath))
            throw new FileNotFoundException("Không tìm thấy file PDF cần đính kèm email.", attachment.FilePath);

        EmailConfigurationService.Validate(email, requireEnabledConfiguration: true);
        var mode = EmailConfigurationService.NormalizeMode(email.AuthenticationMode);

        if (mode == EmailAuthenticationModes.MicrosoftGraphDelegated)
        {
            return await _graph.SendAsync(
                email,
                recipient.Trim(),
                subject,
                body,
                attachment,
                allowInteractive,
                cancellationToken);
        }

        await SendViaSmtpAsync(email, recipient.Trim(), subject, body, attachment, cancellationToken);
        return string.IsNullOrWhiteSpace(email.Username) ? email.FromAddress : email.Username;
    }

    public Task<string> SignInGraphAsync(AppEmailSettings email, CancellationToken cancellationToken = default) =>
        _graph.SignInAsync(email, cancellationToken);

    public Task SignOutGraphAsync(AppEmailSettings email, CancellationToken cancellationToken = default) =>
        _graph.SignOutAsync(email, cancellationToken);

    public Task<string> GetSignedInGraphAccountAsync(AppEmailSettings email) =>
        _graph.GetSignedInAccountAsync(email);

    private static async Task SendViaSmtpAsync(
        AppEmailSettings email,
        string recipient,
        string subject,
        string body,
        EmailAttachmentInfo? attachment,
        CancellationToken cancellationToken)
    {
        using var client = new SmtpClient(email.SmtpHost.Trim(), email.SmtpPort)
        {
            EnableSsl = email.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Timeout = 20000
        };

        var mode = EmailConfigurationService.NormalizeMode(email.AuthenticationMode);
        if (mode == EmailAuthenticationModes.SmtpBasic)
            client.Credentials = new NetworkCredential(email.Username.Trim(), email.Password);

        using var message = new MailMessage
        {
            From = new MailAddress(
                email.FromAddress.Trim(),
                string.IsNullOrWhiteSpace(email.FromName) ? "DCR Management System" : email.FromName.Trim()),
            Subject = subject,
            Body = body,
            IsBodyHtml = false
        };
        message.To.Add(new MailAddress(recipient));

        if (attachment is not null)
        {
            var mailAttachment = new Attachment(
                attachment.FilePath,
                string.IsNullOrWhiteSpace(attachment.ContentType) ? "application/octet-stream" : attachment.ContentType);
            mailAttachment.Name = string.IsNullOrWhiteSpace(attachment.FileName)
                ? Path.GetFileName(attachment.FilePath)
                : attachment.FileName;
            message.Attachments.Add(mailAttachment);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await client.SendMailAsync(message, cancellationToken);
    }
}
