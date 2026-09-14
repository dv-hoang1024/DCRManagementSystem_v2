using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;
using DCRManagementSystem.Services.Remote;

namespace DCRManagementSystem.Forms;

public sealed class LoginForm : Form
{
    private readonly TextBox _txtUsername = new();
    private readonly TextBox _txtPassword = new();
    private readonly Button _btnLogin = new();
    private readonly Button _btnWindows = new();
    private readonly Label _lblWindowsIdentity = new();
    private readonly Label _lblStatus = new();
    private readonly Label _lblConnectionStatus = new();
    private readonly LinkLabel _lnkConnectionSettings = new();
    private readonly LinkLabel _lnkRetryConnection = new();
    private readonly Button _btnLanguage = new();
    private readonly CheckBox _chkRememberWindows = new();
    private Label? _lblVersion;
    private int? _startupRequestId;
    private bool _autoLoginAttempted;
    private bool _connectionAvailable;
    private string _connectionError = string.Empty;

    public LoginForm(int? startupRequestId = null, string? startupConnectionError = null)
    {
        _startupRequestId = startupRequestId;
        _connectionError = startupConnectionError ?? string.Empty;
        _connectionAvailable = !AppServices.UseRemoteApi || AppServices.Settings.Api.IsEndpointResolved;
        Text = "DCR Management System - Đăng nhập";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ClientSize = new Size(940, 640);
        MinimumSize = new Size(940, 640);
        MaximumSize = new Size(1920, 1080);
        UiTheme.ApplyForm(this);
        BuildUi();
        UiLanguageManager.LanguageChanged += OnLanguageChanged;
        FormClosed += (_, _) => UiLanguageManager.LanguageChanged -= OnLanguageChanged;
        AcceptButton = _btnLogin;
        Shown += async (_, _) =>
        {
            RefreshConnectionUi();
            if (_connectionAvailable)
                await TryAutoWindowsLoginAsync();
        };
    }

    private void BuildUi()
    {
        // 1. Panel thương hiệu bên trái (Đã bỏ logo, căn chỉnh lại chữ cân đối)
        var brandPanel = new Panel
        {
            Dock = DockStyle.Left,
            Width = 360,
            BackColor = UiTheme.Primary
        };

        var brandMark = new Label { Text = "DCR", AutoSize = true, Font = new Font("Segoe UI Semibold", 34F, FontStyle.Bold), ForeColor = Color.White, Location = new Point(40, 50) };
        var brandTitle = new Label { Text = "Management System", AutoSize = true, Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold), ForeColor = Color.White, Location = new Point(49, 120) };

        var brandDescription = new Label
        {
            Text = "Temporary Deviation Change\r\nRequest / Order",
            AutoSize = false,
            Size = new Size(275, 150),
            Font = new Font("Segoe UI", 10F),
            ForeColor = Color.FromArgb(235, 248, 240),
            Location = new Point(52, 172)
        };

        _lblVersion = new Label
        {
            Text = GetVersionText(),
            AutoSize = true,
            Font = new Font("Segoe UI", 8.5F),
            ForeColor = Color.FromArgb(206, 236, 218),
            Location = new Point(47, 580)
        };

        brandPanel.Controls.AddRange([brandMark, brandTitle, brandDescription, _lblVersion]);

        // 2. Panel Form đăng nhập bên phải
        var rightPanel = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Background };
        var card = UiTheme.CreateCard();
        card.Size = new Size(440, 594);

        var title = new Label { Text = "Đăng nhập", AutoSize = true, Font = new Font("Segoe UI Semibold", 21F, FontStyle.Bold), ForeColor = UiTheme.TextPrimary, Location = new Point(36, 28) };
        _btnLanguage.Text = UiLanguageManager.ToggleButtonText;
        _btnLanguage.SetBounds(354, 26, 48, 30);
        UiTheme.StyleSecondaryButton(_btnLanguage);
        _btnLanguage.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
        _btnLanguage.Click += (_, _) => UiLanguageManager.Toggle();
        var subtitle = new Label { Text = "Lần đầu đăng nhập bằng tài khoản/mật khẩu. Có thể ghi nhớ Windows/AD cho lần sau.", AutoSize = false, Size = new Size(365, 40), Font = new Font("Segoe UI", 9.5F), ForeColor = UiTheme.TextSecondary, Location = new Point(38, 72) };

        _lblWindowsIdentity.Text = $"Windows: {AuditEnvironment.WindowsIdentityName}";
        _lblWindowsIdentity.SetBounds(38, 120, 364, 25);
        _lblWindowsIdentity.ForeColor = UiTheme.TextSecondary;
        _lblWindowsIdentity.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);

        _btnWindows.Text = "Đăng nhập nhanh bằng Windows / AD";
        _btnWindows.SetBounds(38, 150, 364, 40);
        UiTheme.StylePrimaryButton(_btnWindows);
        _btnWindows.Click += async (_, _) => await WindowsLoginAsync(showMappingError: true);

        var divider = new Label { Text = "────────────  hoặc tài khoản local / LDAP  ────────────", AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.TextSecondary, Location = new Point(38, 202), Size = new Size(364, 24) };
        var lblUsername = UiTheme.CreateFieldLabel("Username");
        lblUsername.SetBounds(38, 238, 364, 22);
        _txtUsername.SetBounds(38, 264, 364, 34);
        _txtUsername.PlaceholderText = "Nhập username";
        UiTheme.StyleInput(_txtUsername);
        var lblPassword = UiTheme.CreateFieldLabel("Password");
        lblPassword.SetBounds(38, 316, 364, 22);
        _txtPassword.SetBounds(38, 342, 364, 34);
        _txtPassword.PlaceholderText = "Nhập password";
        _txtPassword.UseSystemPasswordChar = true;
        UiTheme.StyleInput(_txtPassword);

        _chkRememberWindows.Text = "Nhớ Windows/AD và tự động đăng nhập lần sau";
        _chkRememberWindows.SetBounds(38, 386, 364, 26);
        _chkRememberWindows.Checked = true;
        _chkRememberWindows.ForeColor = UiTheme.TextSecondary;
        _chkRememberWindows.Font = new Font("Segoe UI", 9F);

        _btnLogin.Text = "Đăng nhập tài khoản";
        _btnLogin.SetBounds(38, 422, 364, 40);
        UiTheme.StyleSecondaryButton(_btnLogin);
        _btnLogin.Click += async (_, _) => await CredentialLoginAsync();

        _lblConnectionStatus.SetBounds(38, 470, 364, 22);
        _lblConnectionStatus.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
        _lblConnectionStatus.TextAlign = ContentAlignment.MiddleLeft;

        _lnkConnectionSettings.Text = "⚙ Cấu hình kết nối";
        _lnkConnectionSettings.SetBounds(38, 494, 170, 24);
        _lnkConnectionSettings.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
        _lnkConnectionSettings.LinkColor = UiTheme.Primary;
        _lnkConnectionSettings.ActiveLinkColor = UiTheme.PrimaryHover;
        _lnkConnectionSettings.VisitedLinkColor = UiTheme.Primary;
        _lnkConnectionSettings.LinkClicked += async (_, _) => await OpenConnectionSettingsAsync();

        _lnkRetryConnection.Text = "Thử kết nối lại";
        _lnkRetryConnection.SetBounds(250, 494, 152, 24);
        _lnkRetryConnection.TextAlign = ContentAlignment.MiddleRight;
        _lnkRetryConnection.Font = new Font("Segoe UI", 9F);
        _lnkRetryConnection.LinkColor = UiTheme.Primary;
        _lnkRetryConnection.ActiveLinkColor = UiTheme.PrimaryHover;
        _lnkRetryConnection.VisitedLinkColor = UiTheme.Primary;
        _lnkRetryConnection.LinkClicked += async (_, _) => await RetryConnectionAsync(showSuccessMessage: true);

        _lblStatus.SetBounds(38, 522, 364, 60);
        _lblStatus.ForeColor = UiTheme.Danger;
        _lblStatus.Font = new Font("Segoe UI", 8.8F);
        _lblStatus.TextAlign = ContentAlignment.TopLeft;

        var mode = AppServices.Settings.Authentication.Mode;
        if (mode.Equals(AuthenticationModes.WindowsOnly, StringComparison.OrdinalIgnoreCase))
        {
            _txtUsername.Enabled = false;
            _txtPassword.Enabled = false;
            _btnLogin.Enabled = false;
            divider.Text = "Windows Authentication only";
        }

        card.Controls.AddRange([
            title,
            _btnLanguage,
            subtitle,
            _lblWindowsIdentity,
            _btnWindows,
            divider,
            lblUsername,
            _txtUsername,
            lblPassword,
            _txtPassword,
            _chkRememberWindows,
            _btnLogin,
            _lblConnectionStatus,
            _lnkConnectionSettings,
            _lnkRetryConnection,
            _lblStatus
        ]);
        rightPanel.Controls.Add(card);
        rightPanel.Resize += (_, _) =>
        {
            card.Left = Math.Max(30, (rightPanel.ClientSize.Width - card.Width) / 2);
            card.Top = Math.Max(30, (rightPanel.ClientSize.Height - card.Height) / 2);
        };
        card.Left = 70;
        card.Top = 60;
        Controls.Add(rightPanel);
        Controls.Add(brandPanel);
    }

    private string GetVersionText()
    {
        if (!AppServices.UseRemoteApi)
            return ".NET 8 • WinForms • SQL Server";

        return _connectionAvailable
            ? $".NET 8 • WinForms • {AppServices.ApiConnectionLabel}"
            : ".NET 8 • WinForms • Chưa kết nối";
    }

    private void RefreshConnectionUi()
    {
        if (!AppServices.UseRemoteApi)
        {
            _connectionAvailable = true;
            _lblConnectionStatus.Text = "● SQL Server";
            _lblConnectionStatus.ForeColor = UiTheme.Primary;
            _lnkConnectionSettings.Visible = false;
            _lnkRetryConnection.Visible = false;
            if (_lblVersion is not null) _lblVersion.Text = GetVersionText();
            ApplyLoginAvailability();
            return;
        }

        _connectionAvailable = AppServices.Settings.Api.IsEndpointResolved;

        if (_connectionAvailable)
        {
            _connectionError = string.Empty;
            _lblConnectionStatus.Text = $"● Đã kết nối: {AppServices.ApiConnectionLabel}";
            _lblConnectionStatus.ForeColor = UiTheme.Primary;
            if (_lblStatus.Text.StartsWith("Không kết nối", StringComparison.OrdinalIgnoreCase) ||
                _lblStatus.Text.StartsWith("Không thể kết nối", StringComparison.OrdinalIgnoreCase))
            {
                _lblStatus.Text = string.Empty;
            }
        }
        else
        {
            _lblConnectionStatus.Text = "● Chưa kết nối hệ thống";
            _lblConnectionStatus.ForeColor = UiTheme.Danger;
            if (string.IsNullOrWhiteSpace(_connectionError))
            {
                _connectionError =
                    "Không kết nối được DCR API. Hãy kiểm tra mạng hoặc mở 'Cấu hình kết nối' để nhập địa chỉ máy chủ.";
            }

            SetStatus(_connectionError, true);
        }

        _lnkConnectionSettings.Visible = true;
        _lnkRetryConnection.Visible = true;
        if (_lblVersion is not null) _lblVersion.Text = GetVersionText();
        ApplyLoginAvailability();
    }

    private void ApplyLoginAvailability()
    {
        var windowsOnly = AppServices.Settings.Authentication.Mode.Equals(
            AuthenticationModes.WindowsOnly,
            StringComparison.OrdinalIgnoreCase);

        _btnWindows.Enabled = _connectionAvailable;
        _btnLogin.Enabled = _connectionAvailable && !windowsOnly;
        _txtUsername.Enabled = _connectionAvailable && !windowsOnly;
        _txtPassword.Enabled = _connectionAvailable && !windowsOnly;
        _chkRememberWindows.Enabled = _connectionAvailable;
    }

    private async Task OpenConnectionSettingsAsync()
    {
        using var dialog = new ApiConnectionSettingsForm(AppServices.Settings);
        var result = dialog.ShowDialog(this);

        if (result == DialogResult.OK && dialog.Connected)
        {
            _connectionAvailable = true;
            _connectionError = string.Empty;
            RefreshConnectionUi();
            SetStatus($"Kết nối thành công qua {AppServices.ApiConnectionLabel}.", false);
            _autoLoginAttempted = false;
            return;
        }

        if (AppServices.Settings.Api.IsEndpointResolved)
        {
            _connectionAvailable = true;
            _connectionError = string.Empty;
            RefreshConnectionUi();
            return;
        }

        _connectionAvailable = false;
        _connectionError = string.IsNullOrWhiteSpace(dialog.LastError)
            ? "Chưa kết nối được DCR API. Kiểm tra cấu hình hoặc thử kết nối lại."
            : dialog.LastError;
        RefreshConnectionUi();

        await Task.CompletedTask;
    }

    private async Task RetryConnectionAsync(bool showSuccessMessage)
    {
        if (!AppServices.UseRemoteApi)
            return;

        try
        {
            SetConnectionBusy(true, "Đang kiểm tra Local API và Cloudflare API...");
            AppServices.ClearRemoteSession();
            await ApiEndpointResolver.ResolveAsync(AppServices.Settings);

            _connectionAvailable = true;
            _connectionError = string.Empty;
            RefreshConnectionUi();

            if (showSuccessMessage)
                SetStatus($"Kết nối thành công qua {AppServices.ApiConnectionLabel}.", false);
        }
        catch (Exception ex)
        {
            AppServices.Settings.Api.MarkDisconnected();
            _connectionAvailable = false;
            _connectionError = ex.Message;
            RefreshConnectionUi();
        }
        finally
        {
            SetConnectionBusy(false, string.Empty);
        }
    }

    private void SetConnectionBusy(bool busy, string message)
    {
        _lnkConnectionSettings.Enabled = !busy;
        _lnkRetryConnection.Enabled = !busy;
        UseWaitCursor = busy;

        if (busy)
        {
            _btnLogin.Enabled = false;
            _btnWindows.Enabled = false;
            _txtUsername.Enabled = false;
            _txtPassword.Enabled = false;
            _chkRememberWindows.Enabled = false;
            if (!string.IsNullOrWhiteSpace(message))
                SetStatus(message, false);
            return;
        }

        ApplyLoginAvailability();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        _btnLanguage.Text = UiLanguageManager.ToggleButtonText;
        UiLanguageManager.Apply(this);
    }

    private async Task TryAutoWindowsLoginAsync()
    {
        if (!_connectionAvailable) return;
        if (_autoLoginAttempted || !AppServices.Settings.Authentication.AutoLoginWindows) return;
        if (AppServices.Settings.Authentication.Mode.Equals(AuthenticationModes.LocalOnly, StringComparison.OrdinalIgnoreCase)) return;
        _autoLoginAttempted = true;

        try
        {
            var result = await AppServices.CreateAuthService().LoginWithRememberedWindowsAsync();
            if (result is not null)
                CompleteLogin(result);
        }
        catch
        {
            // Auto-login is only a convenience. Never block the credential screen.
        }
    }

    private async Task WindowsLoginAsync(bool showMappingError)
    {
        try
        {
            SetBusy(true, "Đang xác thực phiên Windows...");
            var auth = AppServices.CreateAuthService();
            var result = await auth.LoginWithRememberedWindowsAsync();

            if (result is null)
            {
                if (showMappingError)
                    SetStatus("Windows/AD chưa được ghi nhớ cho user DCR trên máy này. Hãy đăng nhập bằng tài khoản/mật khẩu và giữ chọn 'Nhớ Windows/AD'.", false);
                return;
            }
            CompleteLogin(result);
        }
        catch (Exception ex) { SetStatus(ex.Message, true); }
        finally { SetBusy(false, string.Empty); }
    }

    private async Task CredentialLoginAsync()
    {
        try
        {
            SetBusy(true, "Đang xác thực...");
            var auth = AppServices.CreateAuthService();
            var result = await auth.LoginAsync(_txtUsername.Text, _txtPassword.Text);
            if (result is null)
            {
                SetStatus("Không xác thực được tài khoản. Kiểm tra username/password, LDAP hoặc local fallback.", true);
                _txtPassword.SelectAll();
                _txtPassword.Focus();
                return;
            }

            if (_chkRememberWindows.Checked)
            {
                try
                {
                    auth.RememberCurrentWindowsLogin(result.User);
                }
                catch (Exception rememberEx)
                {
                    UiMessageBox.Show(this,
                        "Đăng nhập thành công nhưng không thể ghi nhớ Windows/AD cho lần sau.\r\n\r\n" + rememberEx.Message,
                        "Windows / AD",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
            else
            {
                // The checkbox is the user's explicit choice for this Windows profile.
                // If it is cleared, remove any older remembered DCR account.
                auth.ForgetRememberedWindowsLogin();
            }

            CompleteLogin(result);
        }
        catch (Exception ex) { SetStatus(ex.Message, true); }
        finally { SetBusy(false, string.Empty); }
    }

    private void CompleteLogin(AuthenticationResult result)
    {
        CurrentUser.Set(result.User, result.AuthMethod, result.WindowsIdentity);
        _lblStatus.Text = string.Empty;

        var requestId = _startupRequestId;
        _startupRequestId = null;
        Hide();

        using var main = new MainForm(requestId);
        main.ShowDialog();

        if (main.LogoutRequested)
        {
            AppServices.ClearRemoteSession();
            CurrentUser.Clear();
            _autoLoginAttempted = true;
            _txtUsername.Clear();
            _txtPassword.Clear();
            _lblStatus.Text = "Đã đăng xuất. Hãy đăng nhập bằng tài khoản bạn muốn sử dụng.";
            _lblStatus.ForeColor = UiTheme.TextSecondary;
            Show();
            Activate();
            _txtUsername.Focus();
            return;
        }

        AppServices.ClearRemoteSession();
        CurrentUser.Clear();
        Close();
    }

    private void SetBusy(bool busy, string message)
    {
        if (busy)
        {
            _btnLogin.Enabled = false;
            _btnWindows.Enabled = false;
        }
        else
        {
            ApplyLoginAvailability();
        }

        UseWaitCursor = busy;
        if (!string.IsNullOrWhiteSpace(message)) SetStatus(message, false);
    }

    private void SetStatus(string text, bool error)
    {
        _lblStatus.ForeColor = error ? UiTheme.Danger : UiTheme.TextSecondary;
        _lblStatus.Text = text;
    }
}