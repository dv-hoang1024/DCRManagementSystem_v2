namespace DCRManagementSystem.Api;

public sealed class CloudflareTunnelOptions
{
    public bool Enabled { get; set; } = true;
    public bool AutoStart { get; set; } = true;
    public bool StopWithApi { get; set; } = true;

    // DCR dùng tunnel/config/hostname riêng, không dùng chung tunnel WebDashboard.
    public string TunnelName { get; set; } = "dcr-tunnel";
    public string TunnelId { get; set; } = string.Empty;
    public string DnsHostname { get; set; } = "dcr.ggpcontrol.cloud";

    // Máy hiện tại chạy cloudflared thủ công từ C:\Cloudflare.
    public string ExecutablePath { get; set; } = @"C:\Cloudflare\cloudflared.exe";
    public string WorkingDirectory { get; set; } = @"C:\Cloudflare";
    public string ConfigPath { get; set; } = @"%USERPROFILE%\.cloudflared\dcr-config.yml";

    // Chỉ bật khi cần fallback tương thích cũ. Lệnh fallback vẫn chỉ rõ TunnelName;
    // luồng mặc định luôn truyền dcr-config.yml để không dùng nhầm config WebDashboard.
    public bool PreferManualCompatibleCommand { get; set; } = false;

    // DNS route là thao tác provisioning một lần, KHÔNG chạy mỗi lần startup.
    // Người vận hành có thể bấm "Sửa/đăng ký DNS Route" trong System Tray.
    public bool EnableDnsRouteRepairAction { get; set; } = true;

    public string RemoteApiUrl { get; set; } = "https://dcr.ggpcontrol.cloud";
    public int MonitorIntervalSeconds { get; set; } = 15;
    public int StartupValidationMilliseconds { get; set; } = 1800;
}

public sealed class ApiTrayOptions
{
    public bool Enabled { get; set; } = true;
    public bool ShowStartupBalloon { get; set; } = true;
}
