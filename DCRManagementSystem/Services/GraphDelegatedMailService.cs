using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DCRManagementSystem.Helpers;
using AppEmailSettings = DCRManagementSystem.Helpers.EmailSettings;
using Microsoft.Identity.Client;

namespace DCRManagementSystem.Services;

public sealed class GraphDelegatedMailService
{
    private static readonly string[] Scopes = ["Mail.Send"];
    private static readonly object CacheSync = new();
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(45)
    };

    public async Task<string> SignInAsync(AppEmailSettings settings, CancellationToken cancellationToken = default)
    {
        ValidateGraphSettings(settings);
        var app = CreateApplication(settings);
        var result = await AcquireTokenAsync(app, settings, allowInteractive: true, cancellationToken);
        return result.Account?.Username ?? settings.AccountHint;
    }

    public async Task<string> SignInWithAccountSelectionAsync(AppEmailSettings settings, CancellationToken cancellationToken = default)
    {
        ValidateGraphSettings(settings);
        var app = CreateApplication(settings);

        var interactive = app.AcquireTokenInteractive(Scopes)
            .WithUseEmbeddedWebView(false)
            .WithPrompt(Prompt.SelectAccount);

        try
        {
            var result = await interactive.ExecuteAsync(cancellationToken);
            return result.Account?.Username ?? settings.AccountHint;
        }
        catch (MsalServiceException ex) when (ex.ErrorCode.Contains("consent", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Microsoft Entra từ chối quyền Mail.Send. Tenant có thể đang chặn user consent; khi đó cần IT/Admin cho phép ứng dụng hoặc cấp consent.", ex);
        }
    }

    public async Task SignOutAsync(AppEmailSettings settings, CancellationToken cancellationToken = default)
    {
        ValidateGraphSettings(settings);
        var app = CreateApplication(settings);
        var accounts = await app.GetAccountsAsync();
        foreach (var account in accounts)
            await app.RemoveAsync(account);

        var cachePath = GetCachePath(settings);
        lock (CacheSync)
        {
            if (File.Exists(cachePath))
                File.Delete(cachePath);
        }
    }

    public async Task<string> SendAsync(
        AppEmailSettings settings,
        string recipient,
        string subject,
        string body,
        EmailAttachmentInfo? attachment,
        bool allowInteractive,
        CancellationToken cancellationToken = default)
    {
        ValidateGraphSettings(settings);
        var app = CreateApplication(settings);
        var authResult = await AcquireTokenAsync(app, settings, allowInteractive, cancellationToken);

        Dictionary<string, object?>? attachmentJson = null;
        if (attachment is not null)
        {
            if (!File.Exists(attachment.FilePath))
                throw new FileNotFoundException("Không tìm thấy PDF đính kèm trên File Server.", attachment.FilePath);

            var fileInfo = new FileInfo(attachment.FilePath);
            if (fileInfo.Length > 3 * 1024 * 1024)
            {
                throw new InvalidOperationException(
                    $"PDF đính kèm '{attachment.FileName}' có dung lượng {fileInfo.Length / 1024d / 1024d:N2} MB. " +
                    "Graph /me/sendMail dạng fileAttachment trực tiếp được giới hạn cho file nhỏ; hãy giảm kích thước PDF trước khi gửi.");
            }

            var content = await File.ReadAllBytesAsync(attachment.FilePath, cancellationToken).ConfigureAwait(false);
            attachmentJson = new Dictionary<string, object?>
            {
                ["@odata.type"] = "#microsoft.graph.fileAttachment",
                ["name"] = string.IsNullOrWhiteSpace(attachment.FileName)
                    ? Path.GetFileName(attachment.FilePath)
                    : attachment.FileName,
                ["contentType"] = string.IsNullOrWhiteSpace(attachment.ContentType)
                    ? "application/pdf"
                    : attachment.ContentType,
                // Graph expects contentBytes as a base64 JSON string. Serializing byte[] through
                // different Graph/Kiota package versions can produce an OData PrimitiveValue error,
                // so encode it explicitly and send the documented JSON payload directly.
                ["contentBytes"] = Convert.ToBase64String(content)
            };
        }

        var message = new Dictionary<string, object?>
        {
            ["subject"] = subject,
            ["body"] = new Dictionary<string, object?>
            {
                ["contentType"] = "Text",
                ["content"] = body
            },
            ["toRecipients"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["emailAddress"] = new Dictionary<string, object?>
                    {
                        ["address"] = recipient
                    }
                }
            }
        };

        if (attachmentJson is not null)
            message["attachments"] = new object[] { attachmentJson };

        var payload = new Dictionary<string, object?>
        {
            ["message"] = message,
            ["saveToSentItems"] = true
        };

        var json = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://graph.microsoft.com/v1.0/me/sendMail")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authResult.AccessToken);

        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (responseText.Length > 1800)
                responseText = responseText[..1800];
            throw new InvalidOperationException(
                $"Microsoft Graph sendMail lỗi HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). {responseText}".Trim());
        }

        return authResult.Account?.Username ?? settings.AccountHint;
    }

    public async Task<string> GetSignedInAccountAsync(AppEmailSettings settings)
    {
        ValidateGraphSettings(settings);
        var app = CreateApplication(settings);
        var accounts = await app.GetAccountsAsync();
        var account = SelectAccount(accounts, settings.AccountHint);
        return account?.Username ?? string.Empty;
    }

    private static IPublicClientApplication CreateApplication(AppEmailSettings settings)
    {
        var tenant = string.IsNullOrWhiteSpace(settings.TenantId) ? "organizations" : settings.TenantId.Trim();
        var app = PublicClientApplicationBuilder
            .Create(settings.ClientId.Trim())
            .WithAuthority($"https://login.microsoftonline.com/{tenant}")
            .WithRedirectUri("http://localhost")
            .Build();

        ConfigurePersistentTokenCache(app.UserTokenCache, settings);
        return app;
    }

    private static async Task<AuthenticationResult> AcquireTokenAsync(
        IPublicClientApplication app,
        AppEmailSettings settings,
        bool allowInteractive,
        CancellationToken cancellationToken)
    {
        var accounts = await app.GetAccountsAsync();
        var account = SelectAccount(accounts, settings.AccountHint);

        if (account is not null)
        {
            try
            {
                return await app.AcquireTokenSilent(Scopes, account)
                    .ExecuteAsync(cancellationToken);
            }
            catch (MsalUiRequiredException ex)
            {
                if (!allowInteractive)
                    throw new MailWorkerAuthenticationRequiredException(
                        "Token Microsoft Graph trên máy chủ đã hết phiên hoặc Microsoft yêu cầu xác thực lại.", ex);
            }
        }

        if (!allowInteractive)
        {
            throw new MailWorkerAuthenticationRequiredException(
                "Microsoft Graph cần đăng nhập lại trên máy chủ Mail Worker. Hãy đăng nhập DCR bằng tài khoản Administrator, mở Quản trị > Thiết lập hệ thống > Email Notification và bấm 'Đăng nhập / Đổi tài khoản'.");
        }

        var interactive = app.AcquireTokenInteractive(Scopes)
            .WithUseEmbeddedWebView(false);

        if (!string.IsNullOrWhiteSpace(settings.AccountHint))
            interactive = interactive.WithLoginHint(settings.AccountHint.Trim());

        try
        {
            return await interactive.ExecuteAsync(cancellationToken);
        }
        catch (MsalServiceException ex) when (ex.ErrorCode.Contains("consent", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Microsoft Entra từ chối quyền Mail.Send. Tenant có thể đang chặn user consent; khi đó cần IT/Admin cho phép ứng dụng hoặc cấp consent.", ex);
        }
    }

    private static IAccount? SelectAccount(IEnumerable<IAccount> accounts, string accountHint)
    {
        var list = accounts.ToList();
        if (!string.IsNullOrWhiteSpace(accountHint))
        {
            var match = list.FirstOrDefault(x => string.Equals(x.Username, accountHint.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;
        }
        return list.FirstOrDefault();
    }

    private static void ValidateGraphSettings(AppEmailSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ClientId))
            throw new InvalidOperationException("Microsoft Graph Delegated cần Application (Client) ID của App Registration.");
        if (!Guid.TryParse(settings.ClientId.Trim(), out _))
            throw new InvalidOperationException("Application (Client) ID không đúng định dạng GUID.");
    }

    private static string GetCachePath(AppEmailSettings settings)
    {
        var safeClientId = new string(settings.ClientId.Where(char.IsLetterOrDigit).ToArray());
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DCRManagementSystem", "Auth");
        Directory.CreateDirectory(root);
        return Path.Combine(root, $"msal-{safeClientId}.cache");
    }

    private static void ConfigurePersistentTokenCache(ITokenCache tokenCache, AppEmailSettings settings)
    {
        var cachePath = GetCachePath(settings);
        tokenCache.SetBeforeAccess(args =>
        {
            lock (CacheSync)
            {
                if (!File.Exists(cachePath))
                    return;
                try
                {
                    var protectedBytes = File.ReadAllBytes(cachePath);
                    var clearBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                    args.TokenCache.DeserializeMsalV3(clearBytes, shouldClearExistingCache: true);
                }
                catch
                {
                    try { File.Delete(cachePath); } catch { }
                }
            }
        });

        tokenCache.SetAfterAccess(args =>
        {
            if (!args.HasStateChanged)
                return;
            lock (CacheSync)
            {
                var clearBytes = args.TokenCache.SerializeMsalV3();
                var protectedBytes = ProtectedData.Protect(clearBytes, null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(cachePath, protectedBytes);
            }
        });
    }
}
