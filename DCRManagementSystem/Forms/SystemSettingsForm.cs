using DCRManagementSystem.Helpers;
using DCRManagementSystem.Services;
using DCRManagementSystem.UserControls;

namespace DCRManagementSystem.Forms;

public sealed class SystemSettingsForm : Form
{
    private readonly IStorageConfigurationService _storageService = AppServices.CreateStorageConfigurationService();
    private readonly DatabaseConfigurationService _databaseService = AppServices.CreateDatabaseConfigurationService();
    private readonly IWebPortalConfigurationService _webPortalService = AppServices.CreateWebPortalConfigurationService();

    private readonly TextBox _txtSqlServer = new();
    private readonly NumericUpDown _numSqlPort = new();
    private readonly TextBox _txtSqlDatabase = new();
    private readonly ComboBox _cmbSqlAuth = new();
    private readonly TextBox _txtSqlUsername = new();
    private readonly TextBox _txtSqlPassword = new();
    private readonly CheckBox _chkShowSqlPassword = new();
    private readonly CheckBox _chkSqlEncrypt = new();
    private readonly CheckBox _chkSqlTrustCertificate = new();
    private readonly Label _lblSqlStatus = new();
    private readonly Label _lblApiStatus = new();

    private readonly CheckBox _chkWebPortalEnabled = new();
    private readonly CheckBox _chkWebAllowCreate = new();
    private readonly CheckBox _chkWebAllowApproval = new();
    private readonly TextBox _txtWebPortalBaseUrl = new();
    private readonly Label _lblWebPortalStatus = new();

    private readonly CheckBox _chkUseNetwork = new();
    private readonly CheckBox _chkFallbackLocal = new();
    private readonly TextBox _txtServer = new();
    private readonly TextBox _txtShare = new();
    private readonly TextBox _txtSubfolder = new();
    private readonly CheckBox _chkCompress = new();
    private readonly NumericUpDown _numThreshold = new();
    private readonly TextBox _txtExtensions = new();
    private readonly Label _lblResolvedPath = new();
    private readonly Label _lblStatus = new();

    public SystemSettingsForm()
    {
        if (!CurrentUser.IsAdmin)
            throw new UnauthorizedAccessException("Chỉ Administrator được thay đổi System Settings.");

        Text = "System Settings";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(960, 920);
        MinimumSize = new Size(860, 760);
        MaximumSize = new Size(1920, 1080);
        UiTheme.ApplyForm(this);
        BuildUi();
        Shown += async (_, _) => await LoadAsync();
    }

    private void BuildUi()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 102, BackColor = Color.White };
        var title = UiTheme.CreatePageTitle("Thiết lập hệ thống");
        title.Location = new Point(28, 17);
        var subtitle = UiTheme.CreatePageSubtitle(AppServices.UseRemoteApi
            ? "Client đang kết nối DCR API qua HTTPS. SQL Server, Mail Worker và File Server được xử lý tập trung trên server."
            : "Cấu hình SQL Server, Mail Server Microsoft 365, Local Server/NAS và cơ chế nén tài liệu kỹ thuật dùng trong nhà máy.");
        subtitle.Location = new Point(30, 59);
        var btnClose = new Button { Text = "Đóng", Size = new Size(86, 38), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        UiTheme.StyleSecondaryButton(btnClose);
        btnClose.Click += (_, _) => Close();
        header.Resize += (_, _) => btnClose.Location = new Point(header.ClientSize.Width - btnClose.Width - 28, 31);
        header.Controls.AddRange([title, subtitle, btnClose]);

        var page = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(28, 24, 28, 32),
            BackColor = UiTheme.Background
        };

        var sqlCard = AppServices.UseRemoteApi ? BuildApiServerCard() : BuildSqlServerCard();
        var webPortalCard = BuildWebPortalCard();
        var emailCard = new MailServerSettingsControl { Size = new Size(860, 735) };
        var storageCard = BuildStorageCard();
        var compressionCard = BuildCompressionCard();
        var actionCard = BuildStorageActionCard();

        page.Controls.AddRange([sqlCard, webPortalCard, emailCard, storageCard, compressionCard, actionCard]);
        page.Resize += (_, _) =>
        {
            var width = Math.Min(900, Math.Max(780, page.ClientSize.Width - 70));
            foreach (Control card in page.Controls)
                card.Width = width;
        };

        Controls.Add(page);
        Controls.Add(UiTheme.CreateDivider());
        Controls.Add(header);
    }

    private Control BuildApiServerCard()
    {
        var card = UiTheme.CreateCard();
        card.Size = new Size(860, 220);
        var title = new Label
        {
            Text = $"DCR API / {AppServices.ApiConnectionLabel}",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold),
            ForeColor = UiTheme.TextPrimary,
            Location = new Point(28, 22)
        };
        var hint = new Label
        {
            Text = "WinForms client không còn kết nối SQL trực tiếp. Tất cả dữ liệu, attachment, PDF và tác vụ quản trị đi qua DCR API; SQL credentials chỉ nằm trên server. Client tự ưu tiên Local API rồi mới dùng Cloudflare API.",
            AutoSize = false,
            Size = new Size(800, 42),
            ForeColor = UiTheme.TextSecondary,
            Location = new Point(28, 54)
        };
        var lblUrl = UiTheme.CreateFieldLabel("API đang sử dụng");
        lblUrl.SetBounds(28, 105, 180, 24);
        var txtUrl = new TextBox
        {
            ReadOnly = true,
            Text = AppServices.Settings.Api.BaseUrl
        };
        txtUrl.SetBounds(28, 132, 560, 32);
        UiTheme.StyleInput(txtUrl);
        var btnTest = new Button { Text = "Test API", Size = new Size(110, 36), Location = new Point(604, 130) };
        UiTheme.StyleSecondaryButton(btnTest);
        btnTest.Click += async (_, _) => await TestApiAsync();
        _lblApiStatus.SetBounds(28, 174, 800, 30);
        _lblApiStatus.ForeColor = UiTheme.TextSecondary;
        card.Controls.AddRange([title, hint, lblUrl, txtUrl, btnTest, _lblApiStatus]);
        return card;
    }

    private async Task TestApiAsync()
    {
        try
        {
            UseWaitCursor = true;
            _lblApiStatus.ForeColor = UiTheme.TextSecondary;
            _lblApiStatus.Text = "Đang kiểm tra DCR API...";
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var health = await AppServices.TestRemoteApiAsync(timeout.Token);
            _lblApiStatus.ForeColor = UiTheme.Success;
            _lblApiStatus.Text = $"OK: {health.Server} / Database: {health.Database} / {health.ServerTimeUtc.ToLocalTime():dd/MM/yyyy HH:mm:ss}";
        }
        catch (Exception ex)
        {
            _lblApiStatus.ForeColor = UiTheme.Danger;
            _lblApiStatus.Text = "Không kết nối được API: " + ex.GetBaseException().Message;
        }
        finally { UseWaitCursor = false; }
    }

    private Control BuildSqlServerCard()
    {
        var card = UiTheme.CreateCard();
        card.Size = new Size(860, 438);

        var title = new Label
        {
            Text = "SQL Server / Database",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold),
            ForeColor = UiTheme.TextPrimary,
            Location = new Point(28, 22)
        };
        var hint = new Label
        {
            Text = "Database mặc định: 172.168.8.183:3333 / DCRManagement. Cấu hình này được đọc trước khi ứng dụng kết nối database.",
            AutoSize = false,
            Size = new Size(800, 40),
            ForeColor = UiTheme.TextSecondary,
            Location = new Point(28, 54)
        };

        _numSqlPort.Minimum = 0;
        _numSqlPort.Maximum = 65535;
        _cmbSqlAuth.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbSqlAuth.Items.AddRange(["SQL Server Authentication", "Windows Authentication"]);
        _cmbSqlAuth.SelectedIndexChanged += (_, _) => UpdateSqlAuthenticationState();
        _txtSqlPassword.UseSystemPasswordChar = true;

        AddField(card, 100, "SQL Server Address", _txtSqlServer, 280);
        AddField(card, 100, "Port", _numSqlPort, 150, 430);
        AddField(card, 168, "Database Name", _txtSqlDatabase, 280);
        AddField(card, 168, "Authentication", _cmbSqlAuth, 300, 430);
        AddField(card, 236, "Username", _txtSqlUsername, 280);
        AddField(card, 236, "Password", _txtSqlPassword, 300, 430);

        _chkShowSqlPassword.Text = "Hiện mật khẩu";
        _chkShowSqlPassword.AutoSize = true;
        _chkShowSqlPassword.Location = new Point(430, 301);
        _chkShowSqlPassword.CheckedChanged += (_, _) => _txtSqlPassword.UseSystemPasswordChar = !_chkShowSqlPassword.Checked;

        _chkSqlEncrypt.Text = "Encrypt connection";
        _chkSqlEncrypt.AutoSize = true;
        _chkSqlEncrypt.Location = new Point(28, 304);
        _chkSqlTrustCertificate.Text = "Trust server certificate";
        _chkSqlTrustCertificate.AutoSize = true;
        _chkSqlTrustCertificate.Location = new Point(190, 304);

        var configPath = new Label
        {
            Text = "Cấu hình SQL được lưu cục bộ tại Data\\Config\\database.config.json và có hiệu lực đầy đủ sau khi khởi động lại ứng dụng.",
            AutoSize = false,
            Size = new Size(800, 34),
            Location = new Point(28, 332),
            ForeColor = UiTheme.TextSecondary
        };

        var btnTest = new Button { Text = "Test SQL", Size = new Size(120, 38), Location = new Point(28, 374) };
        UiTheme.StyleSecondaryButton(btnTest);
        btnTest.Click += async (_, _) => await TestDatabaseAsync();

        var btnSave = new Button { Text = "Lưu SQL Server", Size = new Size(145, 38), Location = new Point(158, 374) };
        UiTheme.StylePrimaryButton(btnSave);
        btnSave.Click += async (_, _) => await SaveDatabaseAsync();

        _lblSqlStatus.SetBounds(320, 370, 510, 46);
        _lblSqlStatus.TextAlign = ContentAlignment.MiddleLeft;
        _lblSqlStatus.ForeColor = UiTheme.TextSecondary;

        card.Controls.AddRange([
            title, hint, _chkShowSqlPassword, _chkSqlEncrypt, _chkSqlTrustCertificate,
            configPath, btnTest, btnSave, _lblSqlStatus
        ]);
        return card;
    }

    private Control BuildWebPortalCard()
    {
        var card = UiTheme.CreateCard();
        card.Size = new Size(860, 300);

        var title = new Label
        {
            Text = "DCR Web Portal / dcr.ggpcontrol.cloud",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold),
            ForeColor = UiTheme.TextPrimary,
            Location = new Point(28, 22)
        };
        var hint = new Label
        {
            Text = "Cho phép người dùng tạo/xem/phê duyệt DCR tại https://dcr.ggpcontrol.cloud/. Link phê duyệt trong email có dạng https://dcr.ggpcontrol.cloud/request/<id>. Web dùng DCR API và cùng dữ liệu/phân quyền với WinForms. Web login cần Username/Password DCR hoặc LDAP; WindowsOnly không hỗ trợ đăng nhập qua Internet.",
            AutoSize = false,
            Size = new Size(800, 52),
            ForeColor = UiTheme.TextSecondary,
            Location = new Point(28, 54)
        };

        _chkWebPortalEnabled.Text = "Bật DCR Web Portal";
        _chkWebPortalEnabled.AutoSize = true;
        _chkWebPortalEnabled.Location = new Point(28, 112);

        _chkWebAllowCreate.Text = "Cho phép tạo/chỉnh sửa DCR trên Web";
        _chkWebAllowCreate.AutoSize = true;
        _chkWebAllowCreate.Location = new Point(210, 112);

        _chkWebAllowApproval.Text = "Phê duyệt Web + link trong email";
        _chkWebAllowApproval.AutoSize = true;
        _chkWebAllowApproval.Location = new Point(500, 112);

        var lblUrl = UiTheme.CreateFieldLabel("Web Portal Base URL");
        lblUrl.SetBounds(28, 150, 200, 24);
        _txtWebPortalBaseUrl.SetBounds(28, 177, 520, 32);
        UiTheme.StyleInput(_txtWebPortalBaseUrl);
        _txtWebPortalBaseUrl.PlaceholderText = "https://dcr.ggpcontrol.cloud";

        var btnTest = new Button { Text = "Test Web", Size = new Size(110, 36), Location = new Point(564, 175) };
        UiTheme.StyleSecondaryButton(btnTest);
        btnTest.Click += async (_, _) => await TestWebPortalAsync();

        var btnSave = new Button { Text = "Lưu Web Portal", Size = new Size(140, 36), Location = new Point(684, 175) };
        UiTheme.StylePrimaryButton(btnSave);
        btnSave.Click += async (_, _) => await SaveWebPortalAsync();

        _lblWebPortalStatus.SetBounds(28, 224, 796, 52);
        _lblWebPortalStatus.ForeColor = UiTheme.TextSecondary;

        card.Controls.AddRange([
            title, hint,
            _chkWebPortalEnabled, _chkWebAllowCreate, _chkWebAllowApproval,
            lblUrl, _txtWebPortalBaseUrl, btnTest, btnSave, _lblWebPortalStatus
        ]);
        return card;
    }

    private Control BuildStorageCard()
    {
        var card = UiTheme.CreateCard();
        card.Size = new Size(860, 350);
        var title = new Label { Text = "Local Server / Network Share", AutoSize = true, Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold), ForeColor = UiTheme.TextPrimary, Location = new Point(28, 22) };
        var hint = new Label { Text = AppServices.UseRemoteApi
            ? "File Server/NAS được truy cập bởi DCR API server. Máy client ở nhà chỉ upload/download qua HTTPS và không cần quyền SMB trực tiếp."
            : @"Mặc định: \\172.168.8.209\AutoUpdate\DCR. Ứng dụng sử dụng quyền Windows hiện tại để truy cập share.", AutoSize = false, Size = new Size(790, 40), ForeColor = UiTheme.TextSecondary, Location = new Point(28, 54) };
        _chkUseNetwork.Text = "Lưu attachment lên Network Share";
        _chkUseNetwork.AutoSize = true;
        _chkUseNetwork.Location = new Point(28, 100);
        _chkUseNetwork.CheckedChanged += (_, _) => UpdateResolvedPath();
        _chkFallbackLocal.Text = "Tự lưu dự phòng trên DCR API khi Network Share không truy cập được";
        _chkFallbackLocal.AutoSize = true;
        _chkFallbackLocal.Location = new Point(28, 250);
        AddField(card, 140, "Server Address", _txtServer, 250);
        AddField(card, 140, "Folder Share", _txtShare, 250, 430);
        AddField(card, 208, "Subfolder", _txtSubfolder, 250);
        _txtServer.TextChanged += (_, _) => UpdateResolvedPath();
        _txtShare.TextChanged += (_, _) => UpdateResolvedPath();
        _txtSubfolder.TextChanged += (_, _) => UpdateResolvedPath();
        _lblResolvedPath.SetBounds(28, 282, 790, 42);
        _lblResolvedPath.ForeColor = UiTheme.Primary;
        _lblResolvedPath.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);
        card.Controls.AddRange([title, hint, _chkUseNetwork, _chkFallbackLocal, _lblResolvedPath]);
        return card;
    }

    private Control BuildCompressionCard()
    {
        var card = UiTheme.CreateCard();
        card.Size = new Size(860, 250);
        var title = new Label { Text = "Nén tự động file kỹ thuật", AutoSize = true, Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold), ForeColor = UiTheme.TextPrimary, Location = new Point(28, 22) };
        var hint = new Label { Text = "Chỉ ZIP khi file thuộc danh sách extension kỹ thuật và có dung lượng lớn hơn ngưỡng. Database chỉ lưu metadata/path/hash, không lưu byte[].", AutoSize = false, Size = new Size(790, 38), ForeColor = UiTheme.TextSecondary, Location = new Point(28, 54) };
        _chkCompress.Text = "Bật ZIP tự động";
        _chkCompress.AutoSize = true;
        _chkCompress.Location = new Point(28, 96);
        var lblThreshold = UiTheme.CreateFieldLabel("Ngưỡng nén (MB)");
        lblThreshold.SetBounds(28, 136, 160, 24);
        _numThreshold.SetBounds(190, 133, 120, 32);
        _numThreshold.Minimum = 1;
        _numThreshold.Maximum = 102400;
        UiTheme.StyleInput(_numThreshold);
        var lblExt = UiTheme.CreateFieldLabel("Extensions");
        lblExt.SetBounds(28, 180, 160, 24);
        _txtExtensions.SetBounds(190, 177, 630, 34);
        UiTheme.StyleInput(_txtExtensions);
        card.Controls.AddRange([title, hint, _chkCompress, lblThreshold, _numThreshold, lblExt, _txtExtensions]);
        return card;
    }

    private Control BuildStorageActionCard()
    {
        var card = UiTheme.CreateCard();
        card.Size = new Size(860, 94);
        var btnTest = new Button { Text = "Test File Server", Size = new Size(140, 38), Location = new Point(28, 27) };
        UiTheme.StyleSecondaryButton(btnTest);
        btnTest.Click += async (_, _) => await TestStorageAsync();
        var btnSave = new Button { Text = "Lưu File Server", Size = new Size(140, 38), Location = new Point(178, 27) };
        UiTheme.StylePrimaryButton(btnSave);
        btnSave.Click += async (_, _) => await SaveStorageAsync();
        _lblStatus.SetBounds(334, 25, 490, 44);
        _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        _lblStatus.ForeColor = UiTheme.TextSecondary;
        card.Controls.AddRange([btnTest, btnSave, _lblStatus]);
        return card;
    }

    private async Task LoadAsync()
    {
        try
        {
            if (!AppServices.UseRemoteApi)
            {
                var database = _databaseService.GetCurrent();
                _txtSqlServer.Text = database.ServerAddress;
                _numSqlPort.Value = Math.Clamp(database.Port, (int)_numSqlPort.Minimum, (int)_numSqlPort.Maximum);
                _txtSqlDatabase.Text = database.DatabaseName;
                _cmbSqlAuth.SelectedIndex = database.AuthenticationMode.Equals(DatabaseAuthenticationModes.Windows, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                _txtSqlUsername.Text = database.Username;
                _txtSqlPassword.Text = database.Password;
                _chkSqlEncrypt.Checked = database.Encrypt;
                _chkSqlTrustCertificate.Checked = database.TrustServerCertificate;
                UpdateSqlAuthenticationState();
            }

            var webPortal = await _webPortalService.GetAsync();
            _chkWebPortalEnabled.Checked = webPortal.Enabled;
            _chkWebAllowCreate.Checked = webPortal.AllowCreate;
            _chkWebAllowApproval.Checked = webPortal.AllowApproval;
            _txtWebPortalBaseUrl.Text = webPortal.BaseUrl;
            SetWebPortalStatus(
                webPortal.Enabled
                    ? $"Đang bật: {webPortal.BaseUrl}/"
                    : "DCR Web Portal đang tắt. Production Dashboard vẫn hoạt động bình thường.",
                false,
                success: webPortal.Enabled);

            var storage = await _storageService.GetAsync();
            _chkUseNetwork.Checked = storage.UseNetworkShare;
            _chkFallbackLocal.Checked = storage.FallbackToLocalStorageWhenNetworkShareUnavailable;
            _txtServer.Text = storage.ServerAddress;
            _txtShare.Text = storage.ShareName;
            _txtSubfolder.Text = storage.RootSubfolder;
            _chkCompress.Checked = storage.CompressLargeTechnicalFiles;
            _numThreshold.Value = Math.Clamp(storage.CompressionThresholdMb, (int)_numThreshold.Minimum, (int)_numThreshold.Maximum);
            _txtExtensions.Text = storage.CompressionExtensions;
            UpdateResolvedPath();
        }
        catch (Exception ex)
        {
            SetStorageStatus(ex.Message, true);
        }
    }

    private DatabaseConnectionSettings GatherDatabase() => new()
    {
        ServerAddress = _txtSqlServer.Text.Trim(),
        Port = (int)_numSqlPort.Value,
        DatabaseName = _txtSqlDatabase.Text.Trim(),
        AuthenticationMode = _cmbSqlAuth.SelectedIndex == 1 ? DatabaseAuthenticationModes.Windows : DatabaseAuthenticationModes.SqlServer,
        Username = _txtSqlUsername.Text.Trim(),
        Password = _txtSqlPassword.Text,
        Encrypt = _chkSqlEncrypt.Checked,
        TrustServerCertificate = _chkSqlTrustCertificate.Checked,
        ConnectTimeoutSeconds = 10
    };

    private FileStorageSettings GatherStorage() => new()
    {
        UseNetworkShare = _chkUseNetwork.Checked,
        FallbackToLocalStorageWhenNetworkShareUnavailable = _chkFallbackLocal.Checked,
        ServerAddress = _txtServer.Text.Trim(),
        ShareName = _txtShare.Text.Trim(),
        RootSubfolder = _txtSubfolder.Text.Trim(),
        CompressLargeTechnicalFiles = _chkCompress.Checked,
        CompressionThresholdMb = (int)_numThreshold.Value,
        CompressionExtensions = _txtExtensions.Text.Trim()
    };

    private WebPortalSettings GatherWebPortal() => new()
    {
        Enabled = _chkWebPortalEnabled.Checked,
        AllowCreate = _chkWebAllowCreate.Checked,
        AllowApproval = _chkWebAllowApproval.Checked,
        BaseUrl = _txtWebPortalBaseUrl.Text.Trim()
    };

    private async Task TestWebPortalAsync()
    {
        try
        {
            UseWaitCursor = true;
            var settings = GatherWebPortal();
            settings.Validate();
            SetWebPortalStatus("Đang kiểm tra website...", false);

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            using var response = await http.GetAsync(settings.BaseUrl.TrimEnd('/') + "/");
            SetWebPortalStatus(
                $"Website phản hồi HTTP {(int)response.StatusCode} {response.ReasonPhrase}. URL DCR: {settings.BaseUrl}/",
                error: false,
                success: true);
        }
        catch (Exception ex)
        {
            SetWebPortalStatus("Không truy cập được Web Portal: " + ex.GetBaseException().Message, true);
        }
        finally { UseWaitCursor = false; }
    }

    private async Task SaveWebPortalAsync()
    {
        try
        {
            var settings = GatherWebPortal();
            settings.Validate();
            await _webPortalService.SaveAsync(settings);
            SetWebPortalStatus(
                settings.Enabled
                    ? $"Đã bật DCR Web Portal: {settings.BaseUrl}/"
                    : "Đã tắt DCR Web Portal. Link web sẽ không được thêm vào email phê duyệt.",
                false,
                success: true);
        }
        catch (Exception ex)
        {
            SetWebPortalStatus("Không lưu được Web Portal: " + ex.Message, true);
        }
    }

    private async Task TestDatabaseAsync()
    {
        try
        {
            UseWaitCursor = true;
            SetSqlStatus("Đang kết nối SQL Server...", false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var result = await _databaseService.TestConnectionAsync(GatherDatabase(), timeout.Token);
            SetSqlStatus($"OK: {result.ServerName} / {result.DatabaseName} / {result.LoginName} / SQL {result.ProductVersion}", false, true);
        }
        catch (OperationCanceledException) { SetSqlStatus("Kết nối SQL Server quá thời gian chờ.", true); }
        catch (Exception ex) { SetSqlStatus("Kết nối thất bại: " + ex.Message, true); }
        finally { UseWaitCursor = false; }
    }

    private async Task SaveDatabaseAsync()
    {
        try
        {
            UseWaitCursor = true;
            var settings = GatherDatabase();
            SetSqlStatus("Đang kiểm tra trước khi lưu...", false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var result = await _databaseService.TestConnectionAsync(settings, timeout.Token);
            _databaseService.SaveForNextStartup(settings);
            SetSqlStatus($"Đã lưu SQL Server ({result.DatabaseName}). Hãy khởi động lại ứng dụng để chuyển kết nối.", false, true);
        }
        catch (OperationCanceledException) { SetSqlStatus("Không lưu: kết nối SQL Server quá thời gian chờ.", true); }
        catch (Exception ex) { SetSqlStatus("Không lưu cấu hình SQL: " + ex.Message, true); }
        finally { UseWaitCursor = false; }
    }

    private async Task TestStorageAsync()
    {
        try
        {
            UseWaitCursor = true;
            SetStorageStatus("Đang kiểm tra quyền đọc/ghi...", false);
            var root = await _storageService.TestConnectionAsync(GatherStorage());
            SetStorageStatus($"Kết nối OK: {root}", false, success: true);
        }
        catch (Exception ex) { SetStorageStatus("Kết nối thất bại: " + ex.Message, true); }
        finally { UseWaitCursor = false; }
    }

    private async Task SaveStorageAsync()
    {
        try
        {
            await _storageService.SaveAsync(GatherStorage());
            SetStorageStatus("Đã lưu cấu hình File Server dùng chung vào SystemSettings.", false, success: true);
            UpdateResolvedPath();
        }
        catch (Exception ex) { SetStorageStatus(ex.Message, true); }
    }

    private void UpdateSqlAuthenticationState()
    {
        var sqlAuthentication = _cmbSqlAuth.SelectedIndex != 1;
        _txtSqlUsername.Enabled = sqlAuthentication;
        _txtSqlPassword.Enabled = sqlAuthentication;
        _chkShowSqlPassword.Enabled = sqlAuthentication;
    }

    private void UpdateResolvedPath()
    {
        try { _lblResolvedPath.Text = "Storage Root: " + _storageService.ResolveRoot(GatherStorage()); }
        catch { _lblResolvedPath.Text = "Storage Root: cấu hình chưa hợp lệ"; }
    }

    private void SetSqlStatus(string text, bool error, bool success = false)
    {
        _lblSqlStatus.Text = text;
        _lblSqlStatus.ForeColor = error ? UiTheme.Danger : success ? UiTheme.Success : UiTheme.TextSecondary;
    }

    private void SetWebPortalStatus(string text, bool error, bool success = false)
    {
        _lblWebPortalStatus.Text = text;
        _lblWebPortalStatus.ForeColor = error ? UiTheme.Danger : success ? UiTheme.Success : UiTheme.TextSecondary;
    }

    private void SetStorageStatus(string text, bool error, bool success = false)
    {
        _lblStatus.Text = text;
        _lblStatus.ForeColor = error ? UiTheme.Danger : success ? UiTheme.Success : UiTheme.TextSecondary;
    }

    private static void AddField(Control card, int top, string labelText, Control control, int width, int left = 28)
    {
        var label = UiTheme.CreateFieldLabel(labelText);
        label.SetBounds(left, top, width, 24);
        control.SetBounds(left, top + 27, width, 32);
        UiTheme.StyleInput(control);
        card.Controls.AddRange([label, control]);
    }
}
