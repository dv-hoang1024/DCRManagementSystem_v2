using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;

namespace DCRManagementSystem.Forms;

/// <summary>
/// Small authenticated launcher used by GGP Control Main Screen. It reuses the
/// existing DCR UserManagementForm; no separate user-management implementation is created.
/// </summary>
public sealed class UserManagementLauncherForm : Form
{
    private readonly TextBox _username = new();
    private readonly TextBox _password = new();
    private readonly Button _login = new();
    private readonly Label _status = new();

    public UserManagementLauncherForm()
    {
        Text = "DCR - Quản lý người dùng";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 330);
        MinimumSize = MaximumSize = new Size(536, 369);
        MaximizeBox = false;
        UiTheme.ApplyForm(this);
        BuildUi();
    }

    private void BuildUi()
    {
        var title = new Label
        {
            Text = "Quản lý người dùng DCR",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold),
            ForeColor = UiTheme.TextPrimary,
            Location = new Point(38, 28)
        };
        var sub = new Label
        {
            Text = "Production / Warehouse / DCR sử dụng chung danh sách tài khoản này.",
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5F),
            ForeColor = UiTheme.TextSecondary,
            Location = new Point(40, 69)
        };

        Controls.Add(title); Controls.Add(sub);
        AddLabel("Username", 40, 112);
        _username.SetBounds(40, 136, 440, 34); UiTheme.StyleInput(_username);
        AddLabel("Mật khẩu DCR", 40, 180);
        _password.SetBounds(40, 204, 440, 34); _password.UseSystemPasswordChar = true; UiTheme.StyleInput(_password);
        _password.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await LoginAsync(); } };

        _status.SetBounds(40, 246, 270, 40);
        _status.ForeColor = UiTheme.TextSecondary;
        _status.Font = new Font("Segoe UI", 9F);

        _login.Text = "Đăng nhập Administrator";
        _login.SetBounds(310, 250, 170, 38);
        UiTheme.StylePrimaryButton(_login);
        _login.Click += async (_, _) => await LoginAsync();

        Controls.AddRange([_username, _password, _status, _login]);
        Shown += (_, _) => _username.Focus();
    }

    private void AddLabel(string text, int x, int y)
    {
        Controls.Add(new Label
        {
            Text = text,
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
            ForeColor = UiTheme.TextSecondary,
            Location = new Point(x, y)
        });
    }

    private async Task LoginAsync()
    {
        try
        {
            _login.Enabled = false;
            _status.Text = "Đang xác thực...";
            var result = await AppServices.CreateAuthService().LoginAsync(_username.Text.Trim(), _password.Text);
            if (result is null)
            {
                _status.ForeColor = UiTheme.Danger;
                _status.Text = "Sai tài khoản hoặc mật khẩu DCR.";
                return;
            }
            if (!string.Equals(result.User.Role, RoleNames.Administrator, StringComparison.OrdinalIgnoreCase))
            {
                _status.ForeColor = UiTheme.Danger;
                _status.Text = "Chỉ DCR Administrator được quản lý người dùng.";
                return;
            }

            CurrentUser.Set(result.User, result.AuthMethod, result.WindowsIdentity);
            Hide();
            try
            {
                using var manager = new UserManagementForm();
                manager.ShowDialog();
            }
            finally
            {
                CurrentUser.Clear();
                Close();
            }
        }
        catch (Exception ex)
        {
            _status.ForeColor = UiTheme.Danger;
            _status.Text = ex.Message;
        }
        finally
        {
            if (!IsDisposed) _login.Enabled = true;
        }
    }
}
