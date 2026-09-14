using System.Diagnostics;
using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;
using DCRManagementSystem.Services;

namespace DCRManagementSystem.UserControls;

public sealed class MailServerSettingsControl : UserControl
{
    private readonly IEmailConfigurationService _emailConfig = AppServices.CreateEmailConfigurationService();
    private readonly IEmailOutboxService _outbox = AppServices.CreateEmailOutboxService();
    private readonly IMailWorkerStateService _state = AppServices.CreateMailWorkerStateService();
    private readonly IMailWorkerService _worker = AppServices.CreateMailWorkerService();

    private readonly CheckBox _chkEnabled = new();
    private readonly TextBox _txtTenant = new();
    private readonly TextBox _txtClientId = new();
    private readonly TextBox _txtAccount = new();
    private readonly TextBox _txtTestRecipient = new();
    private readonly Label _lblSignedAccount = new();
    private readonly Label _lblServerIdentity = new();
    private readonly Label _lblQueue = new();
    private readonly Label _lblWorker = new();
    private readonly Label _lblStatus = new();
    private bool _loaded;

    public MailServerSettingsControl(bool requireAdministrator = true)
    {
        if (requireAdministrator && !CurrentUser.IsAdmin)
            throw new UnauthorizedAccessException("Chỉ System Administrator được cấu hình Mail Server.");

        BackColor = UiTheme.Surface;
        BorderStyle = BorderStyle.FixedSingle;
        Height = 735;
        MinimumSize = new Size(780, 735);
        Margin = new Padding(0, 0, 0, 16);
        BuildUi();
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_loaded)
            return;
        _loaded = true;
        await ReloadAsync();
    }

    public async Task ReloadAsync()
    {
        try
        {
            var email = await _emailConfig.GetAsync();
            _chkEnabled.Checked = email.Enabled;
            _txtTenant.Text = string.IsNullOrWhiteSpace(email.TenantId) ? "organizations" : email.TenantId;
            _txtClientId.Text = email.ClientId;
            _txtAccount.Text = email.AccountHint;
            await RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            SetStatus("Không tải được cấu hình Mail Server: " + ex.GetBaseException().Message, true);
        }
    }

    private void BuildUi()
    {
        var title = new Label
        {
            Text = "Email Notification / Microsoft Graph Mail Server",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold),
            ForeColor = UiTheme.TextPrimary,
            Location = new Point(28, 20)
        };

        var hint = new Label
        {
            Text = "Chỉ cần cấu hình Microsoft 365 trên một máy chủ. Tất cả máy client chỉ ghi EmailOutbox vào SQL; người dùng DCR không cần đăng nhập Outlook/Microsoft.",
            AutoSize = false,
            Size = new Size(790, 42),
            ForeColor = UiTheme.TextSecondary,
            Location = new Point(28, 52)
        };

        var setupTitle = new Label
        {
            Text = "A. App Registration - chỉ làm một lần",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold),
            ForeColor = UiTheme.Primary,
            Location = new Point(28, 104)
        };

        var setupHint = new Label
        {
            Text = "Trong Microsoft Entra tạo Public/Desktop App: Redirect URI http://localhost; Microsoft Graph Delegated permission Mail.Send. Sau đó dán Tenant ID và Client ID vào bên dưới. Khi đổi tài khoản gửi sau này KHÔNG cần tạo App Registration mới.",
            AutoSize = false,
            Size = new Size(790, 55),
            ForeColor = UiTheme.TextSecondary,
            Location = new Point(28, 130)
        };

        var btnOpenEntra = new Button { Text = "Mở Microsoft Entra", Size = new Size(155, 36), Location = new Point(28, 188) };
        UiTheme.StyleSecondaryButton(btnOpenEntra);
        btnOpenEntra.Click += (_, _) => OpenEntraPortal();

        var btnCopyChecklist = new Button { Text = "Copy hướng dẫn App", Size = new Size(155, 36), Location = new Point(194, 188) };
        UiTheme.StyleSecondaryButton(btnCopyChecklist);
        btnCopyChecklist.Click += (_, _) => CopyAppRegistrationChecklist();

        _chkEnabled.Text = "Bật Email Notification qua Server Mail Worker";
        _chkEnabled.AutoSize = true;
        _chkEnabled.Location = new Point(380, 196);

        AddField(this, 240, "Tenant ID / Domain", _txtTenant, 350, 28);
        AddField(this, 240, "Application (Client) ID", _txtClientId, 380, 420);
        AddField(this, 308, "Tài khoản gửi / Gợi ý đăng nhập", _txtAccount, 350, 28);
        AddField(this, 308, "Email nhận thử", _txtTestRecipient, 380, 420);

        var btnSave = new Button { Text = "Lưu cấu hình", Size = new Size(125, 38), Location = new Point(28, 382) };
        UiTheme.StylePrimaryButton(btnSave);
        btnSave.Click += async (_, _) => await SaveAsync();

        var btnSignIn = new Button { Text = "Đăng nhập / Đổi tài khoản", Size = new Size(190, 38), Location = new Point(165, 382) };
        UiTheme.StylePrimaryButton(btnSignIn);
        btnSignIn.Click += async (_, _) => await SignInOrSwitchAsync();

        var btnSignOut = new Button { Text = "Đăng xuất Microsoft", Size = new Size(155, 38), Location = new Point(367, 382) };
        UiTheme.StyleSecondaryButton(btnSignOut);
        btnSignOut.Click += async (_, _) => await SignOutAsync();

        var btnTest = new Button { Text = "Gửi email test", Size = new Size(135, 38), Location = new Point(534, 382) };
        UiTheme.StyleSecondaryButton(btnTest);
        btnTest.Click += async (_, _) => await TestProductionPathAsync();

        var btnProcess = new Button { Text = "Xử lý Queue", Size = new Size(120, 38), Location = new Point(681, 382) };
        UiTheme.StyleSecondaryButton(btnProcess);
        btnProcess.Click += async (_, _) => await ProcessQueueAsync();

        var statusTitle = new Label
        {
            Text = "B. Trạng thái Mail Server",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold),
            ForeColor = UiTheme.Primary,
            Location = new Point(28, 440)
        };

        _lblSignedAccount.SetBounds(28, 470, 790, 24);
        _lblServerIdentity.SetBounds(28, 497, 790, 24);
        _lblQueue.SetBounds(28, 524, 790, 24);
        _lblWorker.SetBounds(28, 551, 790, 44);
        foreach (var label in new[] { _lblSignedAccount, _lblServerIdentity, _lblQueue, _lblWorker })
            label.ForeColor = UiTheme.TextSecondary;

        var btnRefresh = new Button { Text = "Làm mới trạng thái", Size = new Size(145, 36), Location = new Point(28, 605) };
        UiTheme.StyleSecondaryButton(btnRefresh);
        btnRefresh.Click += async (_, _) => await RefreshStatusAsync();

        var machineNote = new Label
        {
            Text = AppServices.UseRemoteApi
                ? "Remote API mode: Microsoft Graph token và Mail Worker chạy trên DCR API server. Đăng nhập/đổi tài khoản Microsoft phải thực hiện trực tiếp trên máy server."
                : $"Máy hiện tại: {Environment.MachineName} | Windows: {MailWorkerStateService.GetCurrentWindowsIdentity()}. Hãy thực hiện đăng nhập Microsoft trên đúng máy sẽ chạy Mail Worker/Scheduled Task.",
            AutoSize = false,
            Size = new Size(620, 42),
            ForeColor = UiTheme.TextSecondary,
            Location = new Point(185, 602)
        };

        if (AppServices.UseRemoteApi)
        {
            btnSignIn.Enabled = false;
            btnSignOut.Enabled = false;
            btnSignIn.Text = "Đăng nhập trên Server";
            btnSignOut.Text = "Đăng xuất trên Server";
        }

        _lblStatus.SetBounds(28, 655, 790, 58);
        _lblStatus.ForeColor = UiTheme.TextSecondary;

        Controls.AddRange([
            title, hint, setupTitle, setupHint, btnOpenEntra, btnCopyChecklist, _chkEnabled,
            btnSave, btnSignIn, btnSignOut, btnTest, btnProcess, statusTitle,
            _lblSignedAccount, _lblServerIdentity, _lblQueue, _lblWorker, btnRefresh, machineNote, _lblStatus
        ]);
    }

    private EmailSettings Gather() => new()
    {
        Enabled = _chkEnabled.Checked,
        AuthenticationMode = EmailAuthenticationModes.MicrosoftGraphDelegated,
        TenantId = string.IsNullOrWhiteSpace(_txtTenant.Text) ? "organizations" : _txtTenant.Text.Trim(),
        ClientId = _txtClientId.Text.Trim(),
        AccountHint = _txtAccount.Text.Trim(),
        SmtpHost = "smtp.office365.com",
        SmtpPort = 587,
        EnableSsl = true,
        FromName = "DCR Management System"
    };

    private async Task SaveAsync()
    {
        try
        {
            UseWaitCursor = true;
            await _emailConfig.SaveAsync(Gather());
            SetStatus("Đã lưu cấu hình Mail Server vào SQL SystemSettings.", false, true);
            await RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            SetStatus("Lưu cấu hình thất bại: " + ex.GetBaseException().Message, true);
        }
        finally { UseWaitCursor = false; }
    }

    private async Task SignInOrSwitchAsync()
    {
        try
        {
            UseWaitCursor = true;
            var settings = Gather();
            settings.Enabled = true;
            await _emailConfig.SaveAsync(settings);

            var current = await _emailConfig.GetSignedInGraphAccountAsync(settings);
            if (!string.IsNullOrWhiteSpace(current))
            {
                var answer = DCRManagementSystem.Helpers.UiMessageBox.Show(
                    $"Hiện đang có phiên Microsoft: {current}.\r\n\r\nBạn có muốn đăng xuất phiên này và chọn tài khoản gửi khác không?",
                    "Đổi tài khoản gửi mail",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (answer != DialogResult.Yes)
                {
                    SetStatus("Giữ nguyên tài khoản Microsoft hiện tại.", false);
                    return;
                }

                await _emailConfig.SignOutGraphAsync(settings);
            }

            var machineConfirm = DCRManagementSystem.Helpers.UiMessageBox.Show(
                $"Token Microsoft sẽ được lưu trên máy {Environment.MachineName} và gắn với Windows account:\r\n{MailWorkerStateService.GetCurrentWindowsIdentity()}\r\n\r\nChỉ tiếp tục nếu đây là máy sẽ chạy DCR Mail Worker.",
                "Xác nhận máy Mail Server",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information);
            if (machineConfirm != DialogResult.OK)
                return;

            SetStatus("Đang mở trình duyệt Microsoft. Hãy chọn tài khoản gửi mail trung tâm...", false);
            var account = await _emailConfig.SignInGraphWithAccountSelectionAsync(settings);
            if (!string.IsNullOrWhiteSpace(account))
            {
                _txtAccount.Text = account;
                settings.AccountHint = account;
                await _emailConfig.SaveAsync(settings);
            }

            await _state.RecordSignInAsync(account);
            var requeued = await _outbox.RequeueAuthenticationBlockedAsync();
            SetStatus($"Đăng nhập thành công: {account}. Đã mở lại {requeued} email chờ xác thực.", false, true);
            await RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            SetStatus("Đăng nhập/đổi tài khoản thất bại: " + ex.GetBaseException().Message, true);
        }
        finally { UseWaitCursor = false; }
    }

    private async Task SignOutAsync()
    {
        try
        {
            UseWaitCursor = true;
            var settings = Gather();
            await _emailConfig.SignOutGraphAsync(settings);
            await _state.RecordSignOutAsync();
            SetStatus("Đã đăng xuất Microsoft trên máy Mail Server này. Email mới vẫn nằm trong Outbox cho tới khi đăng nhập lại.", false, true);
            await RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            SetStatus("Đăng xuất Microsoft thất bại: " + ex.GetBaseException().Message, true);
        }
        finally { UseWaitCursor = false; }
    }

    private async Task TestProductionPathAsync()
    {
        try
        {
            UseWaitCursor = true;
            var recipient = _txtTestRecipient.Text.Trim();
            if (string.IsNullOrWhiteSpace(recipient))
                throw new InvalidOperationException("Hãy nhập Email nhận thử.");

            var settings = Gather();
            settings.Enabled = true;
            await _emailConfig.SaveAsync(settings);

            var outboxId = await _outbox.EnqueueTestAsync(recipient);
            var result = await _worker.RunOnceAsync();
            SetStatus($"Đã test theo đúng đường production. Outbox #{outboxId}; {result}. Hãy kiểm tra Inbox/Junk và trạng thái Outbox.", result.Failed > 0 || result.RequiresSignIn, result.Sent > 0);
            await RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            SetStatus("Gửi email test thất bại: " + ex.GetBaseException().Message, true);
        }
        finally { UseWaitCursor = false; }
    }

    private async Task ProcessQueueAsync()
    {
        try
        {
            UseWaitCursor = true;
            var result = await _worker.RunOnceAsync();
            SetStatus("Mail Worker: " + result, result.Failed > 0 || result.RequiresSignIn, result.Sent > 0);
            await RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            SetStatus("Mail Worker lỗi: " + ex.GetBaseException().Message, true);
        }
        finally { UseWaitCursor = false; }
    }

    private async Task RefreshStatusAsync()
    {
        try
        {
            var settings = Gather();
            string graphAccount = string.Empty;
            if (!string.IsNullOrWhiteSpace(settings.ClientId))
            {
                try { graphAccount = await _emailConfig.GetSignedInGraphAccountAsync(settings); }
                catch { }
            }

            var state = await _state.GetAsync();
            var queue = await _outbox.GetSnapshotAsync();
            _lblSignedAccount.Text = "Microsoft account: " + (string.IsNullOrWhiteSpace(graphAccount) ? "chưa đăng nhập trên Windows user hiện tại" : graphAccount);
            _lblServerIdentity.Text = $"Mail Server đăng ký: {(string.IsNullOrWhiteSpace(state.MachineName) ? "chưa đăng ký" : state.MachineName)} | Windows: {(string.IsNullOrWhiteSpace(state.WindowsIdentity) ? "-" : state.WindowsIdentity)}";
            _lblQueue.Text = $"Outbox: Pending={queue.Pending} | RequiresSignIn={queue.RequiresSignIn} | Failed={queue.Failed} | SentToday={queue.SentToday}";
            _lblWorker.Text = $"Last worker: {(state.LastHeartbeat.HasValue ? state.LastHeartbeat.Value.ToString("dd/MM/yyyy HH:mm:ss") : "-")} | {state.LastResult}";
        }
        catch (Exception ex)
        {
            SetStatus("Không làm mới được trạng thái Mail Server: " + ex.GetBaseException().Message, true);
        }
    }

    private void OpenEntraPortal()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://entra.microsoft.com/",
                UseShellExecute = true
            });
            SetStatus("Đã mở Microsoft Entra. Tạo App Registration theo checklist hiển thị trong màn hình này.", false);
        }
        catch (Exception ex)
        {
            SetStatus("Không mở được Microsoft Entra: " + ex.Message, true);
        }
    }

    private void CopyAppRegistrationChecklist()
    {
        const string text =
            "DCR Management System Mail Worker\r\n" +
            "1. Microsoft Entra ID > App registrations > New registration\r\n" +
            "2. Name: DCR Management System Mail Worker\r\n" +
            "3. Account type: Accounts in this organizational directory only\r\n" +
            "4. Authentication > Add platform > Mobile and desktop applications\r\n" +
            "5. Redirect URI: http://localhost\r\n" +
            "6. API permissions > Microsoft Graph > Delegated permissions > Mail.Send\r\n" +
            "7. Copy Directory (tenant) ID và Application (client) ID vào System Settings của DCR.\r\n" +
            "Không tạo Client Secret cho luồng desktop delegated.";
        try
        {
            Clipboard.SetText(text);
            SetStatus("Đã copy checklist App Registration vào clipboard.", false, true);
        }
        catch (Exception ex)
        {
            SetStatus("Không copy được checklist: " + ex.Message, true);
        }
    }

    private void SetStatus(string text, bool error, bool success = false)
    {
        _lblStatus.Text = text;
        _lblStatus.ForeColor = error ? UiTheme.Danger : success ? UiTheme.Success : UiTheme.TextSecondary;
    }

    private static void AddField(Control parent, int top, string labelText, Control control, int width, int left)
    {
        var label = UiTheme.CreateFieldLabel(labelText);
        label.SetBounds(left, top, width, 24);
        control.SetBounds(left, top + 27, width, 32);
        UiTheme.StyleInput(control);
        parent.Controls.AddRange([label, control]);
    }
}
