using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using DCRManagementSystem.Api.WebPortal.Models;

namespace DCRManagementSystem.Api.WebPortal.Services;

public sealed class DcrApiClient
{
    private const string AccessTokenKey = "DCR.AccessToken";
    private const string RefreshTokenKey = "DCR.RefreshToken";
    private const string AccessExpiryKey = "DCR.AccessExpiry";
    private const string RefreshExpiryKey = "DCR.RefreshExpiry";
    private const string CurrentUserKey = "DCR.CurrentUser";
    private const string AuthMethodKey = "DCR.AuthMethod";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DcrApiClient(IHttpClientFactory httpClientFactory, IHttpContextAccessor httpContextAccessor)
    {
        _httpClientFactory = httpClientFactory;
        _httpContextAccessor = httpContextAccessor;
    }

    private ISession Session => _httpContextAccessor.HttpContext?.Session
        ?? throw new InvalidOperationException("DCR Web session chưa sẵn sàng.");

    private HttpClient CreateClient() => _httpClientFactory.CreateClient("DcrApi");

    public DcrUser? CurrentUser
    {
        get
        {
            var json = Session.GetString(CurrentUserKey);
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonSerializer.Deserialize<DcrUser>(json, JsonOptions); }
            catch { return null; }
        }
    }

    public string CurrentAuthMethod => Session.GetString(AuthMethodKey) ?? string.Empty;
    public bool IsAuthenticated => CurrentUser is not null && !string.IsNullOrWhiteSpace(Session.GetString(AccessTokenKey));

    public async Task<DcrPortalSettings> GetPortalStatusAsync(CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("api/web-portal/status", cancellationToken);
        return await ReadJsonAsync<DcrPortalSettings>(response, cancellationToken);
    }

    public async Task LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();
        using var response = await client.PostAsJsonAsync("api/auth/login", new DcrApiLoginRequest
        {
            Username = username?.Trim() ?? string.Empty,
            Password = password ?? string.Empty,
            WindowsIdentity = "WebPortal",
            MachineName = "WEB:" + Environment.MachineName
        }, JsonOptions, cancellationToken);

        var login = await ReadJsonAsync<DcrApiLoginResponse>(response, cancellationToken);
        SaveLogin(login);
    }

    public void Logout()
    {
        Session.Remove(AccessTokenKey);
        Session.Remove(RefreshTokenKey);
        Session.Remove(AccessExpiryKey);
        Session.Remove(RefreshExpiryKey);
        Session.Remove(CurrentUserKey);
        Session.Remove(AuthMethodKey);
    }

    public async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken = default)
    {
        await EnsureAccessTokenAsync(cancellationToken);
        using var client = CreateClient();
        using var request = CreateAuthorizedRequest(HttpMethod.Get, path);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await RefreshAsync(cancellationToken, force: true);
            using var retry = CreateAuthorizedRequest(HttpMethod.Get, path);
            using var retryResponse = await client.SendAsync(retry, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return await ReadJsonAsync<T>(retryResponse, cancellationToken);
        }
        return await ReadJsonAsync<T>(response, cancellationToken);
    }

    public async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken cancellationToken = default)
    {
        await EnsureAccessTokenAsync(cancellationToken);
        using var client = CreateClient();
        using var request = CreateAuthorizedRequest(HttpMethod.Post, path);
        request.Content = JsonContent.Create(body, options: JsonOptions);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await RefreshAsync(cancellationToken, force: true);
            using var retry = CreateAuthorizedRequest(HttpMethod.Post, path);
            retry.Content = JsonContent.Create(body, options: JsonOptions);
            using var retryResponse = await client.SendAsync(retry, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return await ReadJsonAsync<TResponse>(retryResponse, cancellationToken);
        }
        return await ReadJsonAsync<TResponse>(response, cancellationToken);
    }

    public async Task PostAsync<TRequest>(string path, TRequest body, CancellationToken cancellationToken = default)
    {
        await EnsureAccessTokenAsync(cancellationToken);
        using var client = CreateClient();
        using var request = CreateAuthorizedRequest(HttpMethod.Post, path);
        request.Content = JsonContent.Create(body, options: JsonOptions);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        await EnsureAccessTokenAsync(cancellationToken);
        using var client = CreateClient();
        using var request = CreateAuthorizedRequest(HttpMethod.Delete, path);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await RefreshAsync(cancellationToken, force: true);
            using var retry = CreateAuthorizedRequest(HttpMethod.Delete, path);
            using var retryResponse = await client.SendAsync(retry, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            await EnsureSuccessAsync(retryResponse, cancellationToken);
            return;
        }
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<List<DcrListItem>> GetListAsync(string scope, string search, CancellationToken cancellationToken = default)
        => GetAsync<List<DcrListItem>>(
            $"api/dcr?scope={Uri.EscapeDataString(scope ?? DcrScopes.PendingMyApproval)}&search={Uri.EscapeDataString(search ?? string.Empty)}",
            cancellationToken);

    public Task<DcrEditModel> GetDcrAsync(int id, CancellationToken cancellationToken = default)
        => GetAsync<DcrEditModel>($"api/dcr/{id}", cancellationToken);

    public Task<DcrEditModel> GetNewDcrAsync(CancellationToken cancellationToken = default)
        => GetAsync<DcrEditModel>("api/dcr/new", cancellationToken);

    public Task<List<DcrDepartment>> GetDepartmentsAsync(CancellationToken cancellationToken = default)
        => GetAsync<List<DcrDepartment>>("api/dcr/reference/departments", cancellationToken);

    public Task<List<DcrProductLine>> GetProductLinesAsync(CancellationToken cancellationToken = default)
        => GetAsync<List<DcrProductLine>>("api/dcr/reference/product-lines", cancellationToken);

    public Task<List<DcrPartChangeType>> GetChangeTypesAsync(CancellationToken cancellationToken = default)
        => GetAsync<List<DcrPartChangeType>>("api/dcr/reference/change-types", cancellationToken);

    public Task<DcrApprovalPlanSuggestionResult> GetSuggestedApprovalPlanAsync(string rank, CancellationToken cancellationToken = default)
        => GetAsync<DcrApprovalPlanSuggestionResult>($"api/dcr/approval-plan/suggested?rank={Uri.EscapeDataString(NormalizeRank(rank))}", cancellationToken);

    public Task<List<DcrApprovalPlanEditItem>> GetSavedApprovalPlanAsync(string rank, CancellationToken cancellationToken = default)
        => GetAsync<List<DcrApprovalPlanEditItem>>($"api/dcr/approval-plan/saved?rank={Uri.EscapeDataString(NormalizeRank(rank))}", cancellationToken);

    public Task<List<DcrApprovalPlanTemplate>> GetApprovalPlanTemplatesAsync(string rank, CancellationToken cancellationToken = default)
        => GetAsync<List<DcrApprovalPlanTemplate>>($"api/dcr/approval-plan/templates?rank={Uri.EscapeDataString(NormalizeRank(rank))}", cancellationToken);

    public Task<List<DcrApproverSearchItem>> SearchApproversAsync(string query, CancellationToken cancellationToken = default)
        => GetAsync<List<DcrApproverSearchItem>>($"api/dcr/approvers/search?q={Uri.EscapeDataString(query ?? string.Empty)}&maxResults=100", cancellationToken);

    public Task<List<DcrApprovalHistoryItem>> GetApprovalHistoryAsync(int id, CancellationToken cancellationToken = default)
        => GetAsync<List<DcrApprovalHistoryItem>>($"api/dcr/{id}/approval-history", cancellationToken);

    public Task<List<DcrAttachmentItem>> GetAttachmentsAsync(int id, CancellationToken cancellationToken = default)
        => GetAsync<List<DcrAttachmentItem>>($"api/dcr/{id}/attachments", cancellationToken);

    public Task<List<DcrAuditItem>> GetAuditAsync(int id, CancellationToken cancellationToken = default)
        => GetAsync<List<DcrAuditItem>>($"api/dcr/{id}/audit-logs", cancellationToken);

    public Task<bool> CanApproveAsync(int id, CancellationToken cancellationToken = default)
        => GetAsync<bool>($"api/dcr/{id}/can-approve", cancellationToken);

    private static string NormalizeRank(string? rank)
    {
        var value = (rank ?? string.Empty).Trim().ToUpperInvariant();
        return value is "A" or "B" or "C" or "S" ? value : "C";
    }

    public Task<DcrApiSaveDraftResponse> SaveDraftAsync(DcrEditModel model, int draftStep = 5, CancellationToken cancellationToken = default)
        => PostAsync<DcrApiSaveDraftRequest, DcrApiSaveDraftResponse>(
            "api/dcr/save-draft",
            new DcrApiSaveDraftRequest { Model = model, DraftStep = draftStep, IsAutoSave = false },
            cancellationToken);

    public Task<DcrSubmitResult> SubmitAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default)
        => PostAsync<DcrApiSubmitRequest, DcrSubmitResult>(
            $"api/dcr/{id}/submit",
            new DcrApiSubmitRequest { ExpectedRowVersion = rowVersion ?? Array.Empty<byte>() },
            cancellationToken);

    public Task DeleteDcrAsync(int id, CancellationToken cancellationToken = default)
        => DeleteAsync($"api/dcr/{id}", cancellationToken);

    public Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default)
        => PostAsync(
            "api/auth/change-password",
            new DcrApiChangePasswordRequest
            {
                CurrentPassword = currentPassword ?? string.Empty,
                NewPassword = newPassword ?? string.Empty
            },
            cancellationToken);

    public Task<DcrDecisionAuthentication> AuthenticateDecisionAsync(CancellationToken cancellationToken = default)
        => PostAsync<object, DcrDecisionAuthentication>(
            "api/auth/reauthenticate",
            new { },
            cancellationToken);

    public Task<DcrDecisionResult> DecideAsync(
        int id,
        string decision,
        string comment,
        DcrDecisionAuthentication authentication,
        byte[] rowVersion,
        CancellationToken cancellationToken = default)
        => PostAsync<DcrApiDecisionRequest, DcrDecisionResult>(
            $"api/dcr/{id}/decision",
            new DcrApiDecisionRequest
            {
                Decision = decision,
                Comment = comment ?? string.Empty,
                Authentication = authentication,
                ExpectedRowVersion = rowVersion ?? Array.Empty<byte>()
            },
            cancellationToken);

    public async Task<DcrDownload> DownloadAsync(string path, CancellationToken cancellationToken = default)
    {
        await EnsureAccessTokenAsync(cancellationToken);
        var client = CreateClient();
        HttpRequestMessage request = null;
        HttpResponseMessage response = null;
        try
        {
            request = CreateAuthorizedRequest(HttpMethod.Get, path);
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            request.Dispose();
            request = null;

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                response.Dispose();
                response = null;
                await RefreshAsync(cancellationToken, force: true);
                request = CreateAuthorizedRequest(HttpMethod.Get, path);
                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                request.Dispose();
                request = null;
            }

            await EnsureSuccessAsync(response, cancellationToken);
            var name = response.Content.Headers.ContentDisposition?.FileNameStar
                       ?? response.Content.Headers.ContentDisposition?.FileName
                       ?? "download.bin";
            name = Path.GetFileName(name.Trim().Trim('"'));
            var type = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return new DcrDownload(new OwnedHttpContentStream(stream, response, client), name, type);
        }
        catch
        {
            request?.Dispose();
            response?.Dispose();
            client.Dispose();
            throw;
        }
    }

    public async Task UploadAttachmentAsync(
        int requestId,
        IFormFile file,
        string attachmentType,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length <= 0)
            throw new InvalidOperationException("Vui lòng chọn file cần tải lên.");
        attachmentType = string.IsNullOrWhiteSpace(attachmentType) ? "General" : attachmentType.Trim();

        await EnsureAccessTokenAsync(cancellationToken);
        const long smallLimit = 8L * 1024 * 1024;
        if (file.Length <= smallLimit)
        {
            using var client = CreateClient();
            using var request = CreateAuthorizedRequest(HttpMethod.Post, $"api/dcr/{requestId}/attachments/upload");
            using var multipart = new MultipartFormDataContent();
            await using var stream = file.OpenReadStream();
            using var content = new StreamContent(stream);
            content.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType);
            multipart.Add(content, "file", Path.GetFileName(file.FileName));
            multipart.Add(new StringContent(attachmentType), "attachmentType");
            request.Content = multipart;
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            return;
        }

        var start = await PostAsync<DcrUploadStartRequest, DcrUploadStartResponse>(
            $"api/dcr/{requestId}/attachments/upload/start",
            new DcrUploadStartRequest
            {
                FileName = Path.GetFileName(file.FileName),
                FileSize = file.Length,
                AttachmentType = attachmentType
            },
            cancellationToken);

        var chunkSize = Math.Clamp(start.ChunkSizeBytes, 256 * 1024, 16 * 1024 * 1024);
        var buffer = new byte[chunkSize];
        await using var source = file.OpenReadStream();
        long offset = 0;
        while (offset < file.Length)
        {
            var wanted = (int)Math.Min(buffer.Length, file.Length - offset);
            var read = 0;
            while (read < wanted)
            {
                var n = await source.ReadAsync(buffer.AsMemory(read, wanted - read), cancellationToken);
                if (n == 0) break;
                read += n;
            }
            if (read <= 0) throw new EndOfStreamException("Không đọc đủ dữ liệu attachment.");

            using var client = CreateClient();
            using var request = CreateAuthorizedRequest(
                HttpMethod.Post,
                $"api/dcr/{requestId}/attachments/upload/{Uri.EscapeDataString(start.UploadId)}/chunk?offset={offset}");
            request.Content = new ByteArrayContent(buffer, 0, read);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            offset += read;
        }

        await PostAsync<object>(
            $"api/dcr/{requestId}/attachments/upload/{Uri.EscapeDataString(start.UploadId)}/complete",
            new { },
            cancellationToken);
    }

    private async Task EnsureAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!IsAuthenticated)
            throw new UnauthorizedAccessException("Bạn chưa đăng nhập DCR Web.");

        var expires = ReadUtc(AccessExpiryKey);
        if (expires == DateTime.MinValue || expires > DateTime.UtcNow.AddMinutes(2))
            return;

        await RefreshAsync(cancellationToken, force: false);
    }

    private async Task RefreshAsync(CancellationToken cancellationToken, bool force)
    {
        var refreshToken = Session.GetString(RefreshTokenKey);
        var refreshExpiry = ReadUtc(RefreshExpiryKey);
        if (string.IsNullOrWhiteSpace(refreshToken) ||
            (refreshExpiry != DateTime.MinValue && refreshExpiry <= DateTime.UtcNow))
        {
            Logout();
            throw new UnauthorizedAccessException("Phiên DCR Web đã hết hạn. Vui lòng đăng nhập lại.");
        }

        if (!force)
        {
            var currentExpiry = ReadUtc(AccessExpiryKey);
            if (currentExpiry != DateTime.MinValue && currentExpiry > DateTime.UtcNow.AddMinutes(2))
                return;
        }

        using var client = CreateClient();
        using var response = await client.PostAsJsonAsync("api/auth/refresh", new DcrApiRefreshRequest
        {
            RefreshToken = refreshToken,
            WindowsIdentity = "WebPortal",
            MachineName = "WEB:" + Environment.MachineName
        }, JsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            Logout();
            throw new UnauthorizedAccessException("Phiên DCR Web đã hết hạn. Vui lòng đăng nhập lại.");
        }

        var login = await ReadJsonAsync<DcrApiLoginResponse>(response, cancellationToken);
        SaveLogin(login);
    }

    private void SaveLogin(DcrApiLoginResponse login)
    {
        Session.SetString(AccessTokenKey, login.AccessToken);
        Session.SetString(RefreshTokenKey, login.RefreshToken);
        Session.SetString(AccessExpiryKey, login.AccessTokenExpiresUtc.ToString("O"));
        Session.SetString(RefreshExpiryKey, login.RefreshTokenExpiresUtc.ToString("O"));
        Session.SetString(CurrentUserKey, JsonSerializer.Serialize(login.Authentication.User, JsonOptions));
        Session.SetString(AuthMethodKey, login.Authentication.AuthMethod ?? string.Empty);
    }

    private DateTime ReadUtc(string key)
        => DateTime.TryParse(Session.GetString(key), null, System.Globalization.DateTimeStyles.RoundtripKind, out var value)
            ? value.ToUniversalTime()
            : DateTime.MinValue;

    private HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string path)
    {
        var token = Session.GetString(AccessTokenKey);
        if (string.IsNullOrWhiteSpace(token))
            throw new UnauthorizedAccessException("Bạn chưa đăng nhập DCR Web.");

        var request = new HttpRequestMessage(method, path.TrimStart('/'));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("DCR API trả về dữ liệu rỗng hoặc không đúng định dạng.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        DcrApiErrorResponse? error = null;
        try { error = await response.Content.ReadFromJsonAsync<DcrApiErrorResponse>(JsonOptions, cancellationToken); }
        catch { }

        var message = error?.Error;
        if (string.IsNullOrWhiteSpace(message))
            message = $"{(int)response.StatusCode} {response.ReasonPhrase}";

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new UnauthorizedAccessException(message);
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new UnauthorizedAccessException(message);
        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new InvalidOperationException("Dữ liệu đã thay đổi trên server. Hãy tải lại DCR rồi thao tác lại.");
        throw new InvalidOperationException(message);
    }
}

internal sealed class OwnedHttpContentStream : Stream
{
    private readonly Stream _inner;
    private HttpResponseMessage _response;
    private HttpClient _client;

    public OwnedHttpContentStream(Stream inner, HttpResponseMessage response, HttpClient client)
    {
        _inner = inner;
        _response = response;
        _client = client;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;
    public override long Position { get => _inner.Position; set => _inner.Position = value; }
    public override void Flush() => _inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => _inner.ReadAsync(buffer, offset, count, cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _inner.Dispose(); } catch { }
            try { _response?.Dispose(); } catch { }
            try { _client?.Dispose(); } catch { }
            _response = null;
            _client = null;
        }
        base.Dispose(disposing);
    }
}
