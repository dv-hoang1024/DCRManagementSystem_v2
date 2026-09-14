using System.Net;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Buffers;
using System.Text.Json;
using System.Security.Principal;
using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;

namespace DCRManagementSystem.Services.Remote;

internal sealed class RemoteApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly SemaphoreSlim RefreshSync = new(1, 1);
    private static readonly ConcurrentDictionary<string, HttpClient> HttpClients = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient _http;

    public RemoteApiClient(AppSettings settings)
    {
        settings.Api.Validate();
        _http = GetOrCreateHttpClient(settings);
    }

    private static HttpClient GetOrCreateHttpClient(AppSettings settings)
    {
        var key = $"{settings.Api.BaseUrl}|{settings.Api.TimeoutSeconds}|{settings.Api.MaxConnectionsPerServer}|{settings.Api.AllowInvalidTlsCertificate}";
        return HttpClients.GetOrAdd(key, _ =>
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                MaxConnectionsPerServer = settings.Api.MaxConnectionsPerServer,
                UseCookies = false
            };
            if (settings.Api.AllowInvalidTlsCertificate)
                handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;

            var client = new HttpClient(handler)
            {
                BaseAddress = new Uri(settings.Api.BaseUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(settings.Api.TimeoutSeconds)
            };
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DCRManagementSystem-WinForms/1.0");
            return client;
        });
    }

    public async Task<T> GetAsync<T>(string path, bool anonymous = false, CancellationToken cancellationToken = default)
    {
        if (!anonymous) await EnsureAccessTokenFreshAsync(cancellationToken).ConfigureAwait(false);
        using var request = CreateRequest(HttpMethod.Get, path, anonymous);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task PostAsync<TRequest>(string path, TRequest body, bool anonymous = false, CancellationToken cancellationToken = default)
    {
        if (!anonymous) await EnsureAccessTokenFreshAsync(cancellationToken).ConfigureAwait(false);
        using var request = CreateRequest(HttpMethod.Post, path, anonymous);
        request.Content = JsonContent.Create(body, options: JsonOptions);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest body, bool anonymous = false, CancellationToken cancellationToken = default)
    {
        if (!anonymous) await EnsureAccessTokenFreshAsync(cancellationToken).ConfigureAwait(false);
        using var request = CreateRequest(HttpMethod.Post, path, anonymous);
        request.Content = JsonContent.Create(body, options: JsonOptions);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync<TResponse>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        await EnsureAccessTokenFreshAsync(cancellationToken).ConfigureAwait(false);
        using var request = CreateRequest(HttpMethod.Delete, path, false);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task UploadFileAsync(string path, string sourceFile, string attachmentType, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourceFile)) throw new FileNotFoundException("Không tìm thấy file cần tải lên.", sourceFile);
        var info = new FileInfo(sourceFile);
        await EnsureAccessTokenFreshAsync(cancellationToken).ConfigureAwait(false);
        if (info.Length <= 8L * 1024 * 1024)
        {
            await UploadSmallMultipartAsync(path, sourceFile, attachmentType, cancellationToken).ConfigureAwait(false);
            return;
        }

        var startPath = path.TrimEnd('/') + "/start";
        var start = await PostAsync<ApiUploadStartRequest, ApiUploadStartResponse>(
            startPath,
            new ApiUploadStartRequest
            {
                FileName = Path.GetFileName(sourceFile),
                FileSize = info.Length,
                AttachmentType = attachmentType ?? string.Empty
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(start.UploadId))
            throw new InvalidOperationException("API không cấp UploadId cho attachment.");
        var chunkSize = Math.Clamp(start.ChunkSizeBytes, 256 * 1024, 16 * 1024 * 1024);
        var buffer = ArrayPool<byte>.Shared.Rent(chunkSize);
        try
        {
            await using var stream = new FileStream(
                sourceFile,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                chunkSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            long offset = 0;
            while (offset < info.Length)
            {
                var requested = (int)Math.Min(chunkSize, info.Length - offset);
                var read = 0;
                while (read < requested)
                {
                    var n = await stream.ReadAsync(buffer.AsMemory(read, requested - read), cancellationToken).ConfigureAwait(false);
                    if (n == 0) break;
                    read += n;
                }
                if (read <= 0)
                    throw new EndOfStreamException("Không đọc đủ dữ liệu attachment để upload.");

                await EnsureAccessTokenFreshAsync(cancellationToken).ConfigureAwait(false);
                using var request = CreateRequest(
                    HttpMethod.Post,
                    $"{path.TrimEnd('/')}/{Uri.EscapeDataString(start.UploadId)}/chunk?offset={offset}",
                    false);
                using var content = new ByteArrayContent(buffer, 0, read);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                request.Content = content;
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
                offset += read;
            }

            await PostAsync<object>(
                $"{path.TrimEnd('/')}/{Uri.EscapeDataString(start.UploadId)}/complete",
                new { },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task UploadSmallMultipartAsync(string path, string sourceFile, string attachmentType, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, path, false);
        using var multipart = new MultipartFormDataContent();
        await using var stream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, useAsync: true);
        using var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(fileContent, "file", Path.GetFileName(sourceFile));
        multipart.Add(new StringContent(attachmentType ?? string.Empty), "attachmentType");
        request.Content = multipart;

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task DownloadToFileAsync(string path, string outputPath, CancellationToken cancellationToken = default)
    {
        await EnsureAccessTokenFreshAsync(cancellationToken).ConfigureAwait(false);
        using var request = CreateRequest(HttpMethod.Get, path, false);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temp = outputPath + ".download";
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var destination = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, useAsync: true))
        {
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        }
        File.Move(temp, outputPath, true);
    }

    public async Task<string> DownloadToTempAsync(string path, string suggestedExtension, CancellationToken cancellationToken = default)
    {
        var root = Path.Combine(ApplicationDataPaths.PersistentRoot, "Temp");
        Directory.CreateDirectory(root);
        var extension = string.IsNullOrWhiteSpace(suggestedExtension) ? ".bin" : suggestedExtension;
        if (!extension.StartsWith('.')) extension = "." + extension;
        var output = Path.Combine(root, $"{Guid.NewGuid():N}{extension}");
        await DownloadToFileAsync(path, output, cancellationToken).ConfigureAwait(false);
        return output;
    }

    public async Task<string> DownloadToTempAsync(string path, CancellationToken cancellationToken = default)
    {
        await EnsureAccessTokenFreshAsync(cancellationToken).ConfigureAwait(false);
        using var request = CreateRequest(HttpMethod.Get, path, false);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var root = Path.Combine(ApplicationDataPaths.PersistentRoot, "Temp");
        Directory.CreateDirectory(root);
        var headerName = response.Content.Headers.ContentDisposition?.FileNameStar
                         ?? response.Content.Headers.ContentDisposition?.FileName
                         ?? string.Empty;
        headerName = headerName.Trim().Trim('"');
        var safeName = string.IsNullOrWhiteSpace(headerName) ? $"{Guid.NewGuid():N}.bin" : Path.GetFileName(headerName);
        var output = Path.Combine(root, $"{Guid.NewGuid():N}_{safeName}");
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var destination = new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, useAsync: true))
        {
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        }
        return output;
    }

    public async Task<ApiHealthResponse> HealthAsync(CancellationToken cancellationToken = default)
        => await GetAsync<ApiHealthResponse>("api/health", anonymous: true, cancellationToken).ConfigureAwait(false);

    private async Task EnsureAccessTokenFreshAsync(CancellationToken cancellationToken)
    {
        var token = RemoteApiSession.AccessToken;
        if (string.IsNullOrWhiteSpace(token))
            throw new UnauthorizedAccessException("Phiên đăng nhập API không còn hiệu lực. Hãy đăng nhập lại.");

        var expiresUtc = RemoteApiSession.AccessTokenExpiresUtc;
        if (expiresUtc == DateTime.MinValue || expiresUtc > DateTime.UtcNow.AddMinutes(2))
            return;

        await RefreshSync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            token = RemoteApiSession.AccessToken;
            expiresUtc = RemoteApiSession.AccessTokenExpiresUtc;
            if (!string.IsNullOrWhiteSpace(token) &&
                (expiresUtc == DateTime.MinValue || expiresUtc > DateTime.UtcNow.AddMinutes(2)))
                return;

            var refreshToken = RemoteApiSession.LatestRefreshToken;
            if (string.IsNullOrWhiteSpace(refreshToken) ||
                (RemoteApiSession.RefreshTokenExpiresUtc != DateTime.MinValue && RemoteApiSession.RefreshTokenExpiresUtc <= DateTime.UtcNow))
            {
                RemoteApiSession.ClearAll();
                throw new UnauthorizedAccessException("Phiên đăng nhập đã hết hạn. Hãy đăng nhập lại.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/refresh")
            {
                Content = JsonContent.Create(new ApiRefreshRequest
                {
                    RefreshToken = refreshToken,
                    WindowsIdentity = GetWindowsIdentity(),
                    MachineName = Environment.MachineName
                }, options: JsonOptions)
            };
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            try
            {
                var refreshed = await ReadJsonAsync<ApiLoginResponse>(response, cancellationToken).ConfigureAwait(false);
                RemoteApiSession.Set(
                    refreshed.AccessToken,
                    refreshed.RefreshToken,
                    refreshed.AccessTokenExpiresUtc,
                    refreshed.RefreshTokenExpiresUtc);
            }
            catch (UnauthorizedAccessException)
            {
                RemoteApiSession.ClearAll();
                throw;
            }
        }
        finally
        {
            RefreshSync.Release();
        }
    }

    private static string GetWindowsIdentity()
    {
        try { return WindowsIdentity.GetCurrent().Name ?? Environment.UserName; }
        catch { return Environment.UserName; }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, bool anonymous)
    {
        var request = new HttpRequestMessage(method, path.TrimStart('/'));
        if (!anonymous)
        {
            var token = RemoteApiSession.AccessToken;
            if (string.IsNullOrWhiteSpace(token))
                throw new UnauthorizedAccessException("Phiên đăng nhập API không còn hiệu lực. Hãy đăng nhập lại.");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return request;
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false);
        return value ?? throw new InvalidOperationException("API trả về dữ liệu rỗng hoặc không đúng định dạng.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        ApiErrorResponse? error = null;
        string raw = string.Empty;
        try
        {
            raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(raw))
                error = JsonSerializer.Deserialize<ApiErrorResponse>(raw, JsonOptions);
        }
        catch
        {
            // Fall through to the HTTP status below.
        }

        var message = !string.IsNullOrWhiteSpace(error?.Error)
            ? error!.Error
            : !string.IsNullOrWhiteSpace(raw)
                ? raw.Length > 1600 ? raw[..1600] : raw
                : $"API lỗi HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).";

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new UnauthorizedAccessException(message);
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new UnauthorizedAccessException(message);
        if (response.StatusCode == HttpStatusCode.Conflict &&
            string.Equals(error?.ErrorType, nameof(DcrConcurrencyException), StringComparison.OrdinalIgnoreCase))
        {
            throw new DcrConcurrencyException(0, new InvalidOperationException(message));
        }

        throw new InvalidOperationException(message);
    }
}
