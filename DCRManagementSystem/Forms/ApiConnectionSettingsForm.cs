using DCRManagementSystem.Helpers;
using DCRManagementSystem.Services.Remote;

namespace DCRManagementSystem.Forms;

public sealed class ApiConnectionSettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly TextBox _txtLocalApi = new();
    private readonly TextBox _txtRemoteApi = new();
    private readonly Button _btnTestLocal = new();
    private readonly Button _btnTestRemote = new();
    private readonly Button _btnSaveConnect = new();
    private readonly Button _btnClose = new();
    private readonly Label _lblStatus = new();

    public bool Connected { get; private set; }
    public string LastError { get; private set; } = string.Empty;

    public ApiConnectionSettingsForm(AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        Text = "Cấu hình kết nối hệ thống";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(700, 470);
        MinimumSize = new Size(700, 470);
        MaximumSize = new Size(700, 470);

        UiTheme.ApplyForm(this);
        BuildUi();
    }

    private void BuildUi()
    {
        var title = UiTheme.CreatePageTitle("Cấu hình kết nối hệ thống");
        title.Location = new Point(30, 28);

        var subtitle = new Label
        {
            Text = "DCR sẽ ưu tiên Local API trong mạng công ty. Nếu không kết nối được, chương trình tự chuyển sang Cloudflare API.",
            AutoSize = false,
            Location = new Point(32, 70),
            Size = new Size(630, 44),
            Font = new Font("Segoe UI", 9.5F),
            ForeColor = UiTheme.TextSecondary
        };

        var card = UiTheme.CreateCard();
        card.SetBounds(30, 125, 640, 255);

        var lblLocal = UiTheme.CreateFieldLabel("Local API");
        lblLocal.SetBounds(24, 20, 570, 22);

        _txtLocalApi.SetBounds(24, 46, 430, 34);
        _txtLocalApi.Text = _settings.Api.LocalBaseUrl;
        _txtLocalApi.PlaceholderText = "VD: http://172.168.8.209:5080 hoặc http://DCR-SERVER:5080";
        UiTheme.StyleInput(_txtLocalApi);

        _btnTestLocal.Text = "Kiểm tra";
        _btnTestLocal.SetBounds(466, 46, 145, 34);
        UiTheme.StyleSecondaryButton(_btnTestLocal);
        _btnTestLocal.Click += async (_, _) => await TestEndpointAsync(isLocal: true);

        var localHint = new Label
        {
            Text = "Chỉ dùng trong LAN công ty. Có thể nhập IP hoặc hostname của máy chạy DCR API.",
            AutoSize = false,
            Location = new Point(24, 84),
            Size = new Size(585, 30),
            Font = new Font("Segoe UI", 8.5F),
            ForeColor = UiTheme.TextSecondary
        };

        var lblRemote = UiTheme.CreateFieldLabel("Cloudflare API");
        lblRemote.SetBounds(24, 120, 570, 22);

        _txtRemoteApi.SetBounds(24, 146, 430, 34);
        _txtRemoteApi.Text = _settings.Api.RemoteBaseUrl;
        _txtRemoteApi.PlaceholderText = "https://dcr.ggpcontrol.cloud";
        UiTheme.StyleInput(_txtRemoteApi);

        _btnTestRemote.Text = "Kiểm tra";
        _btnTestRemote.SetBounds(466, 146, 145, 34);
        UiTheme.StyleSecondaryButton(_btnTestRemote);
        _btnTestRemote.Click += async (_, _) => await TestEndpointAsync(isLocal: false);

        var remoteHint = new Label
        {
            Text = "Dùng khi ở ngoài công ty. Endpoint Internet phải sử dụng HTTPS.",
            AutoSize = false,
            Location = new Point(24, 184),
            Size = new Size(585, 28),
            Font = new Font("Segoe UI", 8.5F),
            ForeColor = UiTheme.TextSecondary
        };

        _lblStatus.SetBounds(24, 214, 585, 30);
        _lblStatus.Font = new Font("Segoe UI", 9F);
        _lblStatus.ForeColor = UiTheme.TextSecondary;

        card.Controls.AddRange([
            lblLocal,
            _txtLocalApi,
            _btnTestLocal,
            localHint,
            lblRemote,
            _txtRemoteApi,
            _btnTestRemote,
            remoteHint,
            _lblStatus
        ]);

        var configPath = new Label
        {
            Text = $"Cấu hình máy này được lưu tại: {ClientApiConfigurationStore.GetConfigPath()}",
            AutoSize = false,
            Location = new Point(34, 390),
            Size = new Size(625, 24),
            Font = new Font("Segoe UI", 8F),
            ForeColor = UiTheme.TextSecondary
        };

        _btnClose.Text = "Đóng";
        _btnClose.SetBounds(405, 420, 120, 36);
        UiTheme.StyleSecondaryButton(_btnClose);
        _btnClose.Click += (_, _) => Close();

        _btnSaveConnect.Text = "Lưu & kết nối";
        _btnSaveConnect.SetBounds(535, 420, 135, 36);
        UiTheme.StylePrimaryButton(_btnSaveConnect);
        _btnSaveConnect.Click += async (_, _) => await SaveAndConnectAsync();

        Controls.AddRange([title, subtitle, card, configPath, _btnClose, _btnSaveConnect]);
    }

    private async Task TestEndpointAsync(bool isLocal)
    {
        var url = (isLocal ? _txtLocalApi.Text : _txtRemoteApi.Text).Trim();
        if (!ValidateSingleUrl(url, isLocal, out var validationMessage))
        {
            SetStatus(validationMessage, true);
            return;
        }

        try
        {
            SetBusy(true);
            SetStatus($"Đang kiểm tra {(isLocal ? "Local API" : "Cloudflare API")}...", false);

            var timeout = isLocal
                ? _settings.Api.LocalProbeTimeoutMilliseconds
                : _settings.Api.RemoteProbeTimeoutMilliseconds;

            var ok = await ApiEndpointResolver.ProbeEndpointAsync(_settings, url, timeout);
            if (ok)
            {
                SetStatus($"Kết nối {(isLocal ? "Local API" : "Cloudflare API")} thành công.", false, success: true);
                return;
            }

            SetStatus($"Không kết nối được {(isLocal ? "Local API" : "Cloudflare API")} hoặc SQL phía sau API chưa sẵn sàng.", true);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task SaveAndConnectAsync()
    {
        var localUrl = _txtLocalApi.Text.Trim();
        var remoteUrl = _txtRemoteApi.Text.Trim();

        if (!ValidateSingleUrl(localUrl, true, out var localValidation))
        {
            SetStatus(localValidation, true);
            _txtLocalApi.Focus();
            return;
        }

        if (!ValidateSingleUrl(remoteUrl, false, out var remoteValidation))
        {
            SetStatus(remoteValidation, true);
            _txtRemoteApi.Focus();
            return;
        }

        try
        {
            SetBusy(true);

            _settings.Api.LocalBaseUrl = localUrl;
            _settings.Api.RemoteBaseUrl = remoteUrl;
            _settings.Api.BaseUrl = remoteUrl;
            _settings.Api.AutoSelectEndpoint = true;
            _settings.Api.MarkDisconnected();
            _settings.Api.Validate();

            ClientApiConfigurationStore.Save(_settings.Api);
            AppServices.ClearRemoteSession();

            SetStatus("Đã lưu. Đang tìm đường kết nối tốt nhất...", false);
            await ApiEndpointResolver.ResolveAsync(_settings);

            Connected = true;
            LastError = string.Empty;
            SetStatus($"Đã kết nối qua {_settings.Api.ActiveEndpointName}.", false, success: true);

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            Connected = false;
            LastError = ex.Message;
            _settings.Api.MarkDisconnected();
            SetStatus("Đã lưu cấu hình nhưng chưa kết nối được.\r\n" + ex.Message, true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static bool ValidateSingleUrl(string value, bool isLocal, out string message)
    {
        message = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            message = isLocal ? "Local API không được để trống." : "Cloudflare API không được để trống.";
            return false;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            message = $"{(isLocal ? "Local API" : "Cloudflare API")} phải là URL HTTP/HTTPS hợp lệ.";
            return false;
        }

        if (!isLocal && uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback)
        {
            message = "Cloudflare API bên ngoài mạng nội bộ phải sử dụng HTTPS.";
            return false;
        }

        return true;
    }

    private void SetBusy(bool busy)
    {
        _btnTestLocal.Enabled = !busy;
        _btnTestRemote.Enabled = !busy;
        _btnSaveConnect.Enabled = !busy;
        _btnClose.Enabled = !busy;
        _txtLocalApi.Enabled = !busy;
        _txtRemoteApi.Enabled = !busy;
        UseWaitCursor = busy;
    }

    private void SetStatus(string text, bool error, bool success = false)
    {
        _lblStatus.ForeColor = error
            ? UiTheme.Danger
            : success
                ? UiTheme.Primary
                : UiTheme.TextSecondary;
        _lblStatus.Text = text;
    }
}
