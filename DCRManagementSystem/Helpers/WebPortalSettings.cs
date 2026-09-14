namespace DCRManagementSystem.Helpers;

public sealed class WebPortalSettings
{
    public bool Enabled { get; set; }
    public bool AllowCreate { get; set; } = true;
    public bool AllowApproval { get; set; } = true;
    public string BaseUrl { get; set; } = "https://dcr.ggpcontrol.cloud";

    public void Validate()
    {
        BaseUrl = (BaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(BaseUrl))
            throw new InvalidOperationException("Web Portal Base URL không được để trống.");

        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new InvalidOperationException("Web Portal Base URL phải là URL HTTP/HTTPS hợp lệ.");

        if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
            throw new InvalidOperationException("Web Portal public phải sử dụng HTTPS.");
    }

    public string BuildRequestUrl(int requestId)
    {
        Validate();
        return $"{BaseUrl}/request/{requestId}";
    }
}
