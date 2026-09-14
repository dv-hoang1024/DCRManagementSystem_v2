using System.Security.Cryptography;
using System.Text;
using DCRManagementSystem.Data;
using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace DCRManagementSystem.Services;

public sealed class EmailConfigurationService : IEmailConfigurationService
{
    private const string Prefix = "Email.";
    private const string PasswordKey = Prefix + "PasswordEncrypted";
    private readonly Func<AppDbContext> _dbFactory;
    private readonly AppSettings _settings;

    public EmailConfigurationService(Func<AppDbContext> dbFactory, AppSettings settings)
    {
        _dbFactory = dbFactory;
        _settings = settings;
    }

    public async Task<EmailSettings> GetAsync()
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        return await GetAsync(db);
    }

    public async Task<EmailSettings> GetAsync(AppDbContext db)
    {
        var result = Clone(_settings.Email);
        var rows = await db.SystemSettings.AsNoTracking()
            .Where(x => x.Key.StartsWith(Prefix))
            .ToDictionaryAsync(x => x.Key, x => x.Value);

        if (rows.TryGetValue(Prefix + nameof(EmailSettings.Enabled), out var enabled) && bool.TryParse(enabled, out var enabledValue))
            result.Enabled = enabledValue;
        if (rows.TryGetValue(Prefix + nameof(EmailSettings.AuthenticationMode), out var authMode) && !string.IsNullOrWhiteSpace(authMode))
            result.AuthenticationMode = authMode;
        if (rows.TryGetValue(Prefix + nameof(EmailSettings.TenantId), out var tenantId))
            result.TenantId = tenantId;
        if (rows.TryGetValue(Prefix + nameof(EmailSettings.ClientId), out var clientId))
            result.ClientId = clientId;
        if (rows.TryGetValue(Prefix + nameof(EmailSettings.AccountHint), out var accountHint))
            result.AccountHint = accountHint;

        if (rows.TryGetValue(Prefix + nameof(EmailSettings.SmtpHost), out var host))
            result.SmtpHost = host;
        if (rows.TryGetValue(Prefix + nameof(EmailSettings.SmtpPort), out var port) && int.TryParse(port, out var portValue))
            result.SmtpPort = Math.Clamp(portValue, 1, 65535);
        if (rows.TryGetValue(Prefix + nameof(EmailSettings.EnableSsl), out var ssl) && bool.TryParse(ssl, out var sslValue))
            result.EnableSsl = sslValue;
        if (rows.TryGetValue(Prefix + nameof(EmailSettings.Username), out var username))
            result.Username = username;
        if (rows.TryGetValue(PasswordKey, out var encryptedPassword) && !string.IsNullOrWhiteSpace(encryptedPassword))
            result.Password = DecryptSecret(encryptedPassword);
        if (rows.TryGetValue(Prefix + nameof(EmailSettings.FromAddress), out var fromAddress))
            result.FromAddress = fromAddress;
        if (rows.TryGetValue(Prefix + nameof(EmailSettings.FromName), out var fromName))
            result.FromName = fromName;

        result.AuthenticationMode = NormalizeMode(result.AuthenticationMode);
        return result;
    }

    public async Task SaveAsync(EmailSettings value)
    {
        Validate(value, requireEnabledConfiguration: value.Enabled);
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();

        var values = new Dictionary<string, string>
        {
            [Prefix + nameof(EmailSettings.Enabled)] = value.Enabled.ToString(),
            [Prefix + nameof(EmailSettings.AuthenticationMode)] = NormalizeMode(value.AuthenticationMode),
            [Prefix + nameof(EmailSettings.TenantId)] = string.IsNullOrWhiteSpace(value.TenantId) ? "organizations" : value.TenantId.Trim(),
            [Prefix + nameof(EmailSettings.ClientId)] = value.ClientId.Trim(),
            [Prefix + nameof(EmailSettings.AccountHint)] = value.AccountHint.Trim(),
            [Prefix + nameof(EmailSettings.SmtpHost)] = value.SmtpHost.Trim(),
            [Prefix + nameof(EmailSettings.SmtpPort)] = value.SmtpPort.ToString(),
            [Prefix + nameof(EmailSettings.EnableSsl)] = value.EnableSsl.ToString(),
            [Prefix + nameof(EmailSettings.Username)] = value.Username.Trim(),
            [PasswordKey] = string.IsNullOrWhiteSpace(value.Password) ? string.Empty : EncryptSecret(value.Password),
            [Prefix + nameof(EmailSettings.FromAddress)] = value.FromAddress.Trim(),
            [Prefix + nameof(EmailSettings.FromName)] = value.FromName.Trim()
        };

        foreach (var pair in values)
        {
            var row = await db.SystemSettings.SingleOrDefaultAsync(x => x.Key == pair.Key);
            if (row is null)
            {
                db.SystemSettings.Add(new SystemSetting
                {
                    Key = pair.Key,
                    Value = pair.Value,
                    UpdatedAt = DateTime.Now
                });
            }
            else
            {
                row.Value = pair.Value;
                row.UpdatedAt = DateTime.Now;
            }
        }

        // Remove obsolete app-only OAuth2 secret if it exists from an earlier build.
        var obsoleteSecret = await db.SystemSettings.SingleOrDefaultAsync(x => x.Key == Prefix + "ClientSecretEncrypted");
        if (obsoleteSecret is not null)
            db.SystemSettings.Remove(obsoleteSecret);

        await db.SaveChangesAsync();
    }

    public async Task<string> TestAsync(EmailSettings value, string recipient)
    {
        Validate(value, requireEnabledConfiguration: true);
        if (string.IsNullOrWhiteSpace(recipient))
            throw new InvalidOperationException("Hãy nhập địa chỉ email nhận thử.");
        if (!IsPlausibleEmail(recipient))
            throw new InvalidOperationException("Email nhận thử không đúng định dạng.");

        var service = new EmailService();
        return await service.SendAsync(
            value,
            recipient.Trim(),
            "[DCR] Kiểm tra cấu hình email",
            $"Đây là email kiểm tra từ DCR Management System.\r\n\r\n" +
            $"Authentication: {NormalizeMode(value.AuthenticationMode)}\r\n" +
            $"Máy gửi: {Environment.MachineName}\r\n" +
            $"Thời gian: {DateTime.Now:dd/MM/yyyy HH:mm:ss}",
            allowInteractive: true);
    }

    public async Task<string> SignInGraphAsync(EmailSettings value)
    {
        value.AuthenticationMode = EmailAuthenticationModes.MicrosoftGraphDelegated;
        Validate(value, requireEnabledConfiguration: true);
        var service = new EmailService();
        return await service.SignInGraphAsync(value);
    }

    public async Task<string> SignInGraphWithAccountSelectionAsync(EmailSettings value)
    {
        value.AuthenticationMode = EmailAuthenticationModes.MicrosoftGraphDelegated;
        Validate(value, requireEnabledConfiguration: true);
        var service = new GraphDelegatedMailService();
        return await service.SignInWithAccountSelectionAsync(value);
    }

    public async Task SignOutGraphAsync(EmailSettings value)
    {
        value.AuthenticationMode = EmailAuthenticationModes.MicrosoftGraphDelegated;
        if (string.IsNullOrWhiteSpace(value.ClientId))
            return;
        var service = new EmailService();
        await service.SignOutGraphAsync(value);
    }

    public async Task<string> GetSignedInGraphAccountAsync(EmailSettings value)
    {
        if (NormalizeMode(value.AuthenticationMode) != EmailAuthenticationModes.MicrosoftGraphDelegated || string.IsNullOrWhiteSpace(value.ClientId))
            return string.Empty;
        var service = new EmailService();
        return await service.GetSignedInGraphAccountAsync(value);
    }

    public static void Validate(EmailSettings value, bool requireEnabledConfiguration)
    {
        var mode = NormalizeMode(value.AuthenticationMode);
        if (!requireEnabledConfiguration)
            return;

        switch (mode)
        {
            case EmailAuthenticationModes.MicrosoftGraphDelegated:
                if (string.IsNullOrWhiteSpace(value.ClientId))
                    throw new InvalidOperationException("Microsoft Graph Delegated cần Application (Client) ID.");
                if (!Guid.TryParse(value.ClientId.Trim(), out _))
                    throw new InvalidOperationException("Application (Client) ID không đúng định dạng GUID.");
                if (string.IsNullOrWhiteSpace(value.TenantId))
                    value.TenantId = "organizations";
                break;

            case EmailAuthenticationModes.SmtpBasic:
                ValidateSmtpCommon(value);
                if (string.IsNullOrWhiteSpace(value.Username))
                    throw new InvalidOperationException("SMTP Basic cần Username.");
                if (string.IsNullOrWhiteSpace(value.Password))
                    throw new InvalidOperationException("SMTP Basic cần Password.");
                break;

            case EmailAuthenticationModes.SmtpRelay:
                ValidateSmtpCommon(value);
                break;

            default:
                throw new InvalidOperationException("Email Authentication Mode không hợp lệ.");
        }
    }

    private static void ValidateSmtpCommon(EmailSettings value)
    {
        if (value.SmtpPort < 1 || value.SmtpPort > 65535)
            throw new InvalidOperationException("SMTP Port phải nằm trong khoảng 1-65535.");
        if (string.IsNullOrWhiteSpace(value.SmtpHost))
            throw new InvalidOperationException("SMTP Host không được để trống.");
        if (string.IsNullOrWhiteSpace(value.FromAddress))
            throw new InvalidOperationException("From Address không được để trống.");
        if (!IsPlausibleEmail(value.FromAddress))
            throw new InvalidOperationException("From Address không đúng định dạng email.");
    }

    public static string NormalizeMode(string? value)
    {
        if (string.Equals(value, EmailAuthenticationModes.MicrosoftGraphDelegated, StringComparison.OrdinalIgnoreCase))
            return EmailAuthenticationModes.MicrosoftGraphDelegated;
        // Migration from the prior app-only Microsoft365OAuth2 build.
        if (string.Equals(value, "Microsoft365OAuth2", StringComparison.OrdinalIgnoreCase))
            return EmailAuthenticationModes.MicrosoftGraphDelegated;
        if (string.Equals(value, EmailAuthenticationModes.SmtpBasic, StringComparison.OrdinalIgnoreCase))
            return EmailAuthenticationModes.SmtpBasic;
        if (string.Equals(value, EmailAuthenticationModes.SmtpRelay, StringComparison.OrdinalIgnoreCase))
            return EmailAuthenticationModes.SmtpRelay;
        return EmailAuthenticationModes.MicrosoftGraphDelegated;
    }

    private string EncryptSecret(string plainText)
    {
        var key = DeriveEncryptionKey();
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var plain = Encoding.UTF8.GetBytes(plainText);
        var cipher = new byte[plain.Length];
        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(nonce, plain, cipher, tag);

        var payload = new byte[nonce.Length + tag.Length + cipher.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipher, 0, payload, nonce.Length + tag.Length, cipher.Length);
        return "enc:v1:" + Convert.ToBase64String(payload);
    }

    private string DecryptSecret(string storedValue)
    {
        if (!storedValue.StartsWith("enc:v1:", StringComparison.Ordinal))
            return storedValue;

        var payload = Convert.FromBase64String(storedValue[7..]);
        if (payload.Length < 28)
            throw new InvalidOperationException("Email secret đã mã hóa bị hỏng.");

        var nonce = payload[..12];
        var tag = payload[12..28];
        var cipher = payload[28..];
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(DeriveEncryptionKey(), tag.Length);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }

    private byte[] DeriveEncryptionKey()
    {
        if (_settings.SigningKeyBytes.Length < 32)
            throw new InvalidOperationException("Approval signing key chưa được khởi tạo; không thể bảo vệ Email secrets.");

        var purpose = Encoding.UTF8.GetBytes("DCRManagementSystem/SMTP/v1");
        var material = new byte[_settings.SigningKeyBytes.Length + purpose.Length];
        Buffer.BlockCopy(_settings.SigningKeyBytes, 0, material, 0, _settings.SigningKeyBytes.Length);
        Buffer.BlockCopy(purpose, 0, material, _settings.SigningKeyBytes.Length, purpose.Length);
        return SHA256.HashData(material);
    }

    private static bool IsPlausibleEmail(string value)
    {
        try
        {
            var address = new System.Net.Mail.MailAddress(value.Trim());
            return string.Equals(address.Address, value.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static EmailSettings Clone(EmailSettings value) => new()
    {
        Enabled = value.Enabled,
        AuthenticationMode = NormalizeMode(value.AuthenticationMode),
        TenantId = value.TenantId,
        ClientId = value.ClientId,
        AccountHint = value.AccountHint,
        SmtpHost = value.SmtpHost,
        SmtpPort = value.SmtpPort,
        EnableSsl = value.EnableSsl,
        Username = value.Username,
        Password = value.Password,
        FromAddress = value.FromAddress,
        FromName = value.FromName
    };
}
