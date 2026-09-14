using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;

namespace DCRManagementSystem.Services.Remote;

public static class ApiEndpointResolver
{
    public static async Task ResolveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        if (!settings.UseRemoteApi) return;
        settings.Api.MarkDisconnected();
        settings.Api.Validate();

        if (!settings.Api.AutoSelectEndpoint)
        {
            if (!await ProbeEndpointAsync(settings, settings.Api.BaseUrl, settings.Api.RemoteProbeTimeoutMilliseconds, cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException($"Không kết nối được DCR API: {settings.Api.BaseUrl}");

            settings.Api.SetActiveEndpoint(settings.Api.BaseUrl, InferName(settings.Api, settings.Api.BaseUrl));
            return;
        }

        var failures = new List<string>();
        if (!string.IsNullOrWhiteSpace(settings.Api.LocalBaseUrl))
        {
            if (await ProbeEndpointAsync(settings, settings.Api.LocalBaseUrl, settings.Api.LocalProbeTimeoutMilliseconds, cancellationToken).ConfigureAwait(false))
            {
                settings.Api.SetActiveEndpoint(settings.Api.LocalBaseUrl, "Local API");
                return;
            }
            failures.Add($"Local API: {settings.Api.LocalBaseUrl}");
        }

        if (await ProbeEndpointAsync(settings, settings.Api.RemoteBaseUrl, settings.Api.RemoteProbeTimeoutMilliseconds, cancellationToken).ConfigureAwait(false))
        {
            settings.Api.SetActiveEndpoint(settings.Api.RemoteBaseUrl, "Cloudflare API");
            return;
        }
        failures.Add($"Cloudflare API: {settings.Api.RemoteBaseUrl}");

        throw new InvalidOperationException(
            "Không kết nối được DCR API. Đã thử:\r\n- " + string.Join("\r\n- ", failures) +
            "\r\n\r\nKiểm tra mạng nội bộ, DCR API Server hoặc Cloudflare Tunnel.");
    }

    public static async Task<bool> ProbeEndpointAsync(
        AppSettings settings,
        string baseUrl,
        int timeoutMilliseconds,
        CancellationToken cancellationToken = default)
    {
        if (settings is null || !settings.UseRemoteApi || string.IsNullOrWhiteSpace(baseUrl))
            return false;

        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
            return false;

        try
        {
            using var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                UseCookies = false
            };
            if (settings.Api.AllowInvalidTlsCertificate)
                handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;

            using var client = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromMilliseconds(timeoutMilliseconds)
            };
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(timeoutMilliseconds);
            using var response = await client.GetAsync("api/health", HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return false;
            var health = await response.Content.ReadFromJsonAsync<ApiHealthResponse>(cancellationToken: linked.Token).ConfigureAwait(false);
            return health is not null &&
                   health.Status.Equals("ok", StringComparison.OrdinalIgnoreCase) &&
                   health.Database.Equals("connected", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string InferName(RemoteApiSettings api, string baseUrl)
    {
        if (!string.IsNullOrWhiteSpace(api.LocalBaseUrl) &&
            baseUrl.TrimEnd('/').Equals(api.LocalBaseUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            return "Local API";
        return "Cloudflare API";
    }
}
