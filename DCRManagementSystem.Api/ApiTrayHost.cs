using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace DCRManagementSystem.Api;

public sealed class ApiTrayHost : IDisposable
{
    private readonly Thread _thread;
    private readonly WebApplication _app;
    private readonly CloudflareTunnelManager _tunnelManager;
    private readonly CloudflareTunnelOptions _tunnelOptions;
    private readonly ApiTrayOptions _trayOptions;
    private readonly ManualResetEventSlim _started = new(false);
    private volatile bool _stopRequested;
    private ApiTrayApplicationContext? _context;

    private ApiTrayHost(
        WebApplication app,
        CloudflareTunnelManager tunnelManager,
        CloudflareTunnelOptions tunnelOptions,
        ApiTrayOptions trayOptions)
    {
        _app = app;
        _tunnelManager = tunnelManager;
        _tunnelOptions = tunnelOptions;
        _trayOptions = trayOptions;
        _thread = new Thread(TrayThreadMain)
        {
            IsBackground = true,
            Name = "DCR API Tray"
        };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    public static ApiTrayHost? Start(
        WebApplication app,
        CloudflareTunnelManager tunnelManager,
        CloudflareTunnelOptions tunnelOptions,
        ApiTrayOptions trayOptions)
    {
        if (!trayOptions.Enabled || !Environment.UserInteractive)
            return null;

        var host = new ApiTrayHost(app, tunnelManager, tunnelOptions, trayOptions);
        host._thread.Start();
        host._started.Wait(TimeSpan.FromSeconds(5));
        return host;
    }

    private void TrayThreadMain()
    {
        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            _context = new ApiTrayApplicationContext(
                _app,
                _tunnelManager,
                _tunnelOptions,
                _trayOptions,
                () => _stopRequested,
                RequestApiStop);
            _started.Set();
            Application.Run(_context);
        }
        catch (Exception ex)
        {
            ApiLog.WriteEmergency("Tray error: " + ex);
            _started.Set();
        }
    }

    private void RequestApiStop()
    {
        try { _app.Lifetime.StopApplication(); }
        catch { }
    }

    public void Dispose()
    {
        _stopRequested = true;
        try
        {
            if (_thread.IsAlive)
                _thread.Join(2500);
        }
        catch
        {
        }
        _started.Dispose();
    }

    private sealed class ApiTrayApplicationContext : ApplicationContext
    {
        private readonly WebApplication _app;
        private readonly CloudflareTunnelManager _tunnelManager;
        private readonly CloudflareTunnelOptions _tunnelOptions;
        private readonly ApiTrayOptions _trayOptions;
        private readonly Func<bool> _shouldStop;
        private readonly Action _requestApiStop;
        private readonly NotifyIcon _notifyIcon;
        private readonly ToolStripMenuItem _apiStatusItem;
        private readonly ToolStripMenuItem _tunnelStatusItem;
        private readonly ToolStripMenuItem _startTunnelItem;
        private readonly ToolStripMenuItem _restartTunnelItem;
        private readonly ToolStripMenuItem _stopTunnelItem;
        private readonly ToolStripMenuItem _repairDnsItem;
        private readonly ToolStripMenuItem _windowsStartupItem;
        private readonly ToolStripMenuItem _databaseConfigurationItem;
        private readonly WindowsStartupManager _startupManager = new();
        private readonly System.Windows.Forms.Timer _timer;
        private ApiTrayStatusForm? _statusForm;
        private WindowsStartupStatus _startupStatus = new(
            WindowsStartupEntryState.Unknown,
            LegacyStartupTaskState.Unknown,
            "Đang kiểm tra...");
        private bool _exitRequested;
        private Icon? _ownedIcon;

        public ApiTrayApplicationContext(
            WebApplication app,
            CloudflareTunnelManager tunnelManager,
            CloudflareTunnelOptions tunnelOptions,
            ApiTrayOptions trayOptions,
            Func<bool> shouldStop,
            Action requestApiStop)
        {
            _app = app;
            _tunnelManager = tunnelManager;
            _tunnelOptions = tunnelOptions;
            _trayOptions = trayOptions;
            _shouldStop = shouldStop;
            _requestApiStop = requestApiStop;

            var menu = new ContextMenuStrip();
            _apiStatusItem = new ToolStripMenuItem("● DCR API đang chạy") { Enabled = false };
            _tunnelStatusItem = new ToolStripMenuItem("Cloudflare Tunnel: đang kiểm tra...") { Enabled = false };
            var openControl = new ToolStripMenuItem("Mở bảng điều khiển");
            var openHealth = new ToolStripMenuItem("Mở API Health");
            var openLogs = new ToolStripMenuItem("Mở thư mục Log");
            _startTunnelItem = new ToolStripMenuItem("Khởi động Tunnel");
            _restartTunnelItem = new ToolStripMenuItem("Khởi động lại Tunnel");
            _stopTunnelItem = new ToolStripMenuItem("Dừng Tunnel");
            _repairDnsItem = new ToolStripMenuItem($"Sửa/đăng ký DNS {_tunnelOptions.DnsHostname}");
            _windowsStartupItem = new ToolStripMenuItem("Khởi động cùng Windows")
            {
                CheckOnClick = false
            };
            _databaseConfigurationItem = new ToolStripMenuItem("Cấu hình SQL Server...");
            var exitApi = new ToolStripMenuItem("Thoát DCR API");

            openControl.Click += (_, _) => ShowStatusForm();
            openHealth.Click += (_, _) => OpenPath("http://127.0.0.1:5080/api/health");
            openLogs.Click += (_, _) => OpenPath(ApiLog.LogDirectory);
            _startTunnelItem.Click += (_, _) => RunTunnelAction(_tunnelManager.EnsureRunning());
            _restartTunnelItem.Click += (_, _) => RunTunnelAction(_tunnelManager.RestartTunnel());
            _stopTunnelItem.Click += (_, _) => RunTunnelAction(_tunnelManager.StopTunnel());
            _repairDnsItem.Click += (_, _) => RepairDnsRoute();
            _windowsStartupItem.Click += (_, _) => SetWindowsStartup(!_startupStatus.IsConfigured);
            _databaseConfigurationItem.Click += (_, _) => ConfigureDatabase();
            exitApi.Click += (_, _) => ConfirmExit();
            menu.Opening += (_, _) => RefreshWindowsStartupStatus();

            menu.Items.Add(_apiStatusItem);
            menu.Items.Add(_tunnelStatusItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(openControl);
            menu.Items.Add(openHealth);
            menu.Items.Add(openLogs);
            menu.Items.Add(_windowsStartupItem);
            menu.Items.Add(_databaseConfigurationItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_startTunnelItem);
            menu.Items.Add(_restartTunnelItem);
            menu.Items.Add(_stopTunnelItem);
            menu.Items.Add(_repairDnsItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitApi);

            _ownedIcon = TryLoadApplicationIcon();
            _notifyIcon = new NotifyIcon
            {
                Icon = _ownedIcon ?? SystemIcons.Application,
                Text = "DCR API",
                ContextMenuStrip = menu,
                Visible = true
            };
            _notifyIcon.DoubleClick += (_, _) => ShowStatusForm();

            _tunnelManager.StatusChanged += OnTunnelStatusChanged;
            _timer = new System.Windows.Forms.Timer { Interval = 1200 };
            _timer.Tick += (_, _) =>
            {
                if (_shouldStop())
                {
                    ExitTrayThread();
                    return;
                }
                RefreshStatus();
            };
            _timer.Start();
            RefreshWindowsStartupStatus();
            RefreshStatus();

            if (_trayOptions.ShowStartupBalloon)
            {
                _notifyIcon.BalloonTipTitle = "DCR API";
                _notifyIcon.BalloonTipText = "DCR API đang chạy ẩn ở System Tray.";
                _notifyIcon.ShowBalloonTip(2500);
            }
        }

        private void OnTunnelStatusChanged(object? sender, EventArgs e)
        {
            try
            {
                if (_statusForm is { IsDisposed: false } && _statusForm.IsHandleCreated)
                    _statusForm.BeginInvoke(new Action(RefreshStatus));
            }
            catch
            {
            }
        }

        private void ShowStatusForm()
        {
            if (_statusForm is null || _statusForm.IsDisposed)
            {
                _statusForm = new ApiTrayStatusForm(
                    _tunnelManager,
                    _tunnelOptions,
                    () => RunTunnelAction(_tunnelManager.EnsureRunning()),
                    () => RunTunnelAction(_tunnelManager.RestartTunnel()),
                    () => RunTunnelAction(_tunnelManager.StopTunnel()),
                    RepairDnsRoute,
                    SetWindowsStartup,
                    ConfirmExit);
            }

            RefreshWindowsStartupStatus();
            RefreshStatus();
            if (!_statusForm.Visible)
                _statusForm.Show();
            _statusForm.WindowState = FormWindowState.Normal;
            _statusForm.Activate();
            _statusForm.BringToFront();
        }

        private void ConfigureDatabase()
        {
            var apiSettingsPath = Path.Combine(AppContext.BaseDirectory, "api.appsettings.json");
            if (!ServerDatabaseConfiguration.OpenConfiguratorAndWait(apiSettingsPath))
                return;

            MessageBox.Show(
                "Đã lưu cấu hình SQL Server. DCR API và WebDashboard đang chạy cần được khởi động lại để nhận cấu hình mới.",
                "DCR API - Cấu hình SQL Server",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void RunTunnelAction(TunnelActionResult result)
        {
            RefreshStatus();
            MessageBox.Show(
                result.Message,
                "DCR API - Cloudflare Tunnel",
                MessageBoxButtons.OK,
                result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private void RepairDnsRoute()
        {
            var tunnelTarget = string.IsNullOrWhiteSpace(_tunnelOptions.TunnelName)
                ? _tunnelOptions.TunnelId
                : _tunnelOptions.TunnelName;
            var answer = MessageBox.Show(
                $"Đăng ký/cập nhật DNS Route cho '{_tunnelOptions.DnsHostname}' tới tunnel '{tunnelTarget}'?\r\n\r\nĐây là thao tác provisioning một lần. Không cần chạy mỗi lần API khởi động.",
                "DCR API - Cloudflare DNS Route",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;

            RunTunnelAction(_tunnelManager.RepairDnsRoute());
        }

        private void SetWindowsStartup(bool enabled)
        {
            var result = _startupManager.SetEnabled(enabled);
            _startupStatus = result.Status;
            ApplyWindowsStartupStatus();
            MessageBox.Show(
                result.Message,
                "DCR API - Khởi động cùng Windows",
                MessageBoxButtons.OK,
                result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private void RefreshWindowsStartupStatus()
        {
            _startupStatus = _startupManager.GetStatus();
            ApplyWindowsStartupStatus();
        }

        private void ApplyWindowsStartupStatus()
        {
            _windowsStartupItem.CheckState = _startupStatus.IsIndeterminate
                ? CheckState.Indeterminate
                : _startupStatus.IsConfigured ? CheckState.Checked : CheckState.Unchecked;
            _windowsStartupItem.Text = _startupStatus.LegacyTask == LegacyStartupTaskState.Present
                ? "Khởi động cùng Windows (Task Scheduler)"
                : "Khởi động cùng Windows";
            _windowsStartupItem.ToolTipText = _startupStatus.Message;
            _statusForm?.UpdateStatus(_tunnelManager.GetStatus(), _startupStatus);
        }

        private void ConfirmExit()
        {
            if (_exitRequested) return;
            var answer = MessageBox.Show(
                "Bạn muốn dừng DCR API?\r\n\r\nCác máy Client sẽ mất kết nối DCR API. Tunnel dcr-tunnel riêng của DCR cũng sẽ dừng; WebDashboard và tunnel của website không bị ảnh hưởng.",
                "Thoát DCR API",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;

            _exitRequested = true;
            _notifyIcon.Visible = false;
            _requestApiStop();
            ExitTrayThread();
        }

        private void RefreshStatus()
        {
            var tunnel = _tunnelManager.GetStatus();
            _apiStatusItem.Text = "● DCR API đang chạy";
            _tunnelStatusItem.Text = "Cloudflare Tunnel: " + tunnel.Message;
            _startTunnelItem.Enabled = tunnel.State is CloudflareTunnelState.Stopped or CloudflareTunnelState.Error;
            _restartTunnelItem.Enabled = tunnel.State == CloudflareTunnelState.RunningManaged;
            _stopTunnelItem.Enabled = tunnel.State == CloudflareTunnelState.RunningManaged;
            _repairDnsItem.Enabled = _tunnelOptions.EnableDnsRouteRepairAction;
            _notifyIcon.Text = BuildNotifyText(tunnel);
            _statusForm?.UpdateStatus(tunnel, _startupStatus);
        }

        private string BuildNotifyText(CloudflareTunnelStatus tunnel)
        {
            var text = $"DCR API | Tunnel: {tunnel.Message}";
            return text.Length <= 63 ? text : text[..63];
        }

        private void ExitTrayThread()
        {
            _timer.Stop();
            _tunnelManager.StatusChanged -= OnTunnelStatusChanged;
            _notifyIcon.Visible = false;
            _statusForm?.ForceClose();
            _notifyIcon.Dispose();
            _ownedIcon?.Dispose();
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _timer.Dispose(); } catch { }
                try { _notifyIcon.Visible = false; _notifyIcon.Dispose(); } catch { }
                try { _ownedIcon?.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }

        private static Icon? TryLoadApplicationIcon()
        {
            try
            {
                var assembly = typeof(DCRManagementSystem.Helpers.AppBranding).Assembly;
                using var stream = assembly.GetManifestResourceStream("DCRManagementSystem.Assets.DcrApp.ico");
                return stream is null ? null : new Icon(stream);
            }
            catch
            {
                return null;
            }
        }

        private static void OpenPath(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.GetBaseException().Message, "DCR API", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}

internal sealed class ApiTrayStatusForm : Form
{
    private readonly CloudflareTunnelManager _tunnelManager;
    private readonly CloudflareTunnelOptions _options;
    private readonly Label _lblApi = new();
    private readonly Label _lblTunnel = new();
    private readonly Label _lblPid = new();
    private readonly Label _lblConfig = new();
    private readonly Label _lblExecutable = new();
    private readonly Label _lblWorkingDirectory = new();
    private readonly Label _lblCommand = new();
    private readonly CheckBox _chkWindowsStartup = new();
    private readonly Label _lblWindowsStartup = new();
    private WindowsStartupStatus _startupStatus = new(
        WindowsStartupEntryState.Unknown,
        LegacyStartupTaskState.Unknown,
        "Đang kiểm tra...");
    private bool _forceClose;

    public ApiTrayStatusForm(
        CloudflareTunnelManager tunnelManager,
        CloudflareTunnelOptions options,
        Action startTunnel,
        Action restartTunnel,
        Action stopTunnel,
        Action repairDnsRoute,
        Action<bool> setWindowsStartup,
        Action exitApi)
    {
        _tunnelManager = tunnelManager;
        _options = options;
        Text = "DCR API - Bảng điều khiển";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        ShowInTaskbar = false;
        ClientSize = new Size(760, 610);
        BackColor = Color.White;
        Font = new Font("Segoe UI", 9.5F);

        var title = new Label
        {
            Text = "DCR API Server",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold),
            ForeColor = Color.FromArgb(17, 136, 79),
            Location = new Point(28, 24)
        };
        var sub = new Label
        {
            Text = $"Máy chủ: {Environment.MachineName}   |   Windows: {Environment.UserDomainName}\\{Environment.UserName}",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Location = new Point(31, 65)
        };

        _lblApi.SetBounds(32, 108, 650, 28);
        _lblApi.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
        _lblApi.ForeColor = Color.FromArgb(17, 136, 79);
        _lblApi.Text = "● DCR API: đang chạy | http://127.0.0.1:5080";

        _lblTunnel.SetBounds(32, 146, 690, 50);
        _lblTunnel.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
        _lblPid.SetBounds(32, 196, 690, 24);
        _lblConfig.SetBounds(32, 225, 690, 42);
        _lblExecutable.SetBounds(32, 270, 690, 42);
        _lblWorkingDirectory.SetBounds(32, 313, 690, 32);
        _lblCommand.SetBounds(32, 348, 690, 52);

        _chkWindowsStartup.SetBounds(32, 404, 315, 30);
        _chkWindowsStartup.Text = "Khởi động DCR API cùng Windows";
        _chkWindowsStartup.AutoCheck = false;
        _chkWindowsStartup.ThreeState = true;
        _chkWindowsStartup.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);
        _chkWindowsStartup.ForeColor = Color.FromArgb(28, 70, 48);
        _chkWindowsStartup.Click += (_, _) =>
            setWindowsStartup(!_startupStatus.IsConfigured);

        _lblWindowsStartup.SetBounds(354, 407, 390, 42);
        _lblWindowsStartup.ForeColor = Color.DimGray;

        var openHealth = MakeButton("Mở API Health", 32, 466, 130);
        var openLogs = MakeButton("Mở thư mục Log", 172, 466, 135);
        var repairDns = MakeButton("Repair DNS", 317, 466, 105);
        var start = MakeButton("Start Tunnel", 432, 466, 100);
        var restart = MakeButton("Restart Tunnel", 542, 466, 105);
        var stop = MakeButton("Stop Tunnel", 657, 466, 90);
        var close = MakeButton("Ẩn xuống Tray", 478, 535, 130);
        var exit = MakeButton("Thoát DCR API", 618, 535, 128);

        openHealth.Click += (_, _) => OpenPath("http://127.0.0.1:5080/api/health");
        openLogs.Click += (_, _) => OpenPath(ApiLog.LogDirectory);
        start.Click += (_, _) => startTunnel();
        restart.Click += (_, _) => restartTunnel();
        stop.Click += (_, _) => stopTunnel();
        repairDns.Click += (_, _) => repairDnsRoute();
        close.Click += (_, _) => Hide();
        exit.Click += (_, _) => exitApi();

        Controls.AddRange([
            title, sub, _lblApi, _lblTunnel, _lblPid, _lblConfig, _lblExecutable, _lblWorkingDirectory, _lblCommand,
            _chkWindowsStartup, _lblWindowsStartup,
            openHealth, openLogs, repairDns, start, restart, stop, close, exit
        ]);

        FormClosing += (_, e) =>
        {
            if (_forceClose) return;
            e.Cancel = true;
            Hide();
        };
    }

    public void UpdateStatus(CloudflareTunnelStatus status, WindowsStartupStatus startupStatus)
    {
        if (IsDisposed) return;
        _startupStatus = startupStatus;
        var ok = status.State is CloudflareTunnelState.RunningManaged or CloudflareTunnelState.RunningExternal;
        _lblTunnel.ForeColor = ok ? Color.FromArgb(17, 136, 79) : status.State == CloudflareTunnelState.Error ? Color.Firebrick : Color.DarkOrange;
        _lblTunnel.Text = $"Cloudflare Tunnel ({_options.TunnelName}): {status.Message}";
        _lblPid.Text = status.ProcessId.HasValue ? $"Process ID: {status.ProcessId.Value}" : "Process ID: -";
        _lblConfig.Text = "Config: " + (string.IsNullOrWhiteSpace(status.ConfigPath) ? "mặc định của cloudflared" : status.ConfigPath);
        _lblExecutable.Text = "cloudflared.exe: " + (string.IsNullOrWhiteSpace(status.ExecutablePath) ? "chưa tìm thấy" : status.ExecutablePath);
        _lblWorkingDirectory.Text = "Working directory: " + (string.IsNullOrWhiteSpace(status.WorkingDirectory) ? "-" : status.WorkingDirectory);
        _lblCommand.Text = "Lệnh gần nhất: " + (string.IsNullOrWhiteSpace(status.LastCommand) ? "-" : status.LastCommand);
        _chkWindowsStartup.CheckState = startupStatus.IsIndeterminate
            ? CheckState.Indeterminate
            : startupStatus.IsConfigured ? CheckState.Checked : CheckState.Unchecked;
        _lblWindowsStartup.Text = startupStatus.Message;
        _lblWindowsStartup.ForeColor = startupStatus.IsIndeterminate ||
                                       startupStatus.RegistryEntry == WindowsStartupEntryState.DifferentCommand
            ? Color.DarkOrange
            : startupStatus.IsConfigured ? Color.FromArgb(17, 136, 79) : Color.DimGray;
    }

    public void ForceClose()
    {
        _forceClose = true;
        try { Close(); } catch { }
    }

    private static Button MakeButton(string text, int x, int y, int width)
    {
        return new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, 36),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(28, 70, 48)
        };
    }

    private static void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.GetBaseException().Message, "DCR API", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
