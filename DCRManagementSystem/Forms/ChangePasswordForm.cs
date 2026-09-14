using DCRManagementSystem.Helpers;

namespace DCRManagementSystem.Forms;

internal sealed class ChangePasswordForm : Form
{
    private readonly TextBox _txtCurrentPassword = new();
    private readonly TextBox _txtNewPassword = new();
    private readonly TextBox _txtConfirmPassword = new();
    private readonly CheckBox _chkShowPassword = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnCancel = new();
    private bool _saving;

    public ChangePasswordForm()
    {
        Text = UiLanguageManager.T("Đổi mật khẩu DCR", "Change DCR Password");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(520, 455);
        MaximumSize = new Size(1920, 1080);
        UiTheme.ApplyForm(this);
        BuildUi();
        AcceptButton = _btnSave;
        CancelButton = _btnCancel;
    }

    private void BuildUi()
    {
        var card = UiTheme.CreateCard();
        card.SetBounds(24, 22, 472, 346);

        var title = UiTheme.CreatePageTitle(UiLanguageManager.T("Đổi mật khẩu DCR", "Change DCR Password"));
        title.SetBounds(24, 20, 420, 38);
        title.Font = new Font("Segoe UI Semibold", 17F, FontStyle.Bold);

        var description = new Label
        {
            Text = UiLanguageManager.T(
                "Thay đổi mật khẩu DCR cục bộ của tài khoản đang đăng nhập. Thao tác này không thay đổi mật khẩu Windows / Active Directory / LDAP.",
                "Change the local DCR password for the signed-in account. This does not change the Windows / Active Directory / LDAP password."),
            AutoSize = false,
            Location = new Point(24, 62),
            Size = new Size(420, 46),
            ForeColor = UiTheme.TextSecondary,
            Font = new Font("Segoe UI", 9F)
        };

        var lblCurrent = UiTheme.CreateFieldLabel(UiLanguageManager.T("Mật khẩu hiện tại *", "Current password *"));
        lblCurrent.SetBounds(24, 116, 420, 22);
        _txtCurrentPassword.SetBounds(24, 140, 420, 32);
        _txtCurrentPassword.UseSystemPasswordChar = true;
        UiTheme.StyleInput(_txtCurrentPassword);

        var lblNew = UiTheme.CreateFieldLabel(UiLanguageManager.T("Mật khẩu mới *", "New password *"));
        lblNew.SetBounds(24, 184, 420, 22);
        _txtNewPassword.SetBounds(24, 208, 420, 32);
        _txtNewPassword.UseSystemPasswordChar = true;
        UiTheme.StyleInput(_txtNewPassword);

        var lblConfirm = UiTheme.CreateFieldLabel(UiLanguageManager.T("Nhập lại mật khẩu mới *", "Confirm new password *"));
        lblConfirm.SetBounds(24, 252, 420, 22);
        _txtConfirmPassword.SetBounds(24, 276, 420, 32);
        _txtConfirmPassword.UseSystemPasswordChar = true;
        UiTheme.StyleInput(_txtConfirmPassword);

        _chkShowPassword.Text = UiLanguageManager.T("Hiện mật khẩu", "Show passwords");
        _chkShowPassword.SetBounds(24, 316, 200, 24);
        _chkShowPassword.ForeColor = UiTheme.TextSecondary;
        _chkShowPassword.CheckedChanged += (_, _) =>
        {
            var useMask = !_chkShowPassword.Checked;
            _txtCurrentPassword.UseSystemPasswordChar = useMask;
            _txtNewPassword.UseSystemPasswordChar = useMask;
            _txtConfirmPassword.UseSystemPasswordChar = useMask;
        };

        card.Controls.AddRange([
            title, description,
            lblCurrent, _txtCurrentPassword,
            lblNew, _txtNewPassword,
            lblConfirm, _txtConfirmPassword,
            _chkShowPassword
        ]);

        _btnCancel.Text = UiLanguageManager.T("Hủy", "Cancel");
        _btnCancel.SetBounds(280, 390, 100, 38);
        _btnCancel.DialogResult = DialogResult.Cancel;
        UiTheme.StyleSecondaryButton(_btnCancel);

        _btnSave.Text = UiLanguageManager.T("Đổi mật khẩu", "Change password");
        _btnSave.SetBounds(390, 390, 106, 38);
        UiTheme.StylePrimaryButton(_btnSave);
        _btnSave.Click += async (_, _) => await SaveAsync();

        Controls.AddRange([card, _btnCancel, _btnSave]);
    }

    private async Task SaveAsync()
    {
        if (_saving) return;
        var user = CurrentUser.User;
        if (user is null)
        {
            UiMessageBox.Show(this, "Phiên đăng nhập không còn hợp lệ.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var currentPassword = _txtCurrentPassword.Text;
        var newPassword = _txtNewPassword.Text;
        var confirmPassword = _txtConfirmPassword.Text;

        if (string.IsNullOrWhiteSpace(currentPassword))
        {
            UiMessageBox.Show(this, "Vui lòng nhập mật khẩu DCR hiện tại.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtCurrentPassword.Focus();
            return;
        }
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
        {
            UiMessageBox.Show(this, "Mật khẩu mới phải có ít nhất 8 ký tự.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtNewPassword.Focus();
            return;
        }
        if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
        {
            UiMessageBox.Show(this, "Mật khẩu xác nhận không khớp.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtConfirmPassword.SelectAll();
            _txtConfirmPassword.Focus();
            return;
        }
        if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
        {
            UiMessageBox.Show(this, "Mật khẩu mới phải khác mật khẩu hiện tại.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtNewPassword.Focus();
            return;
        }

        try
        {
            _saving = true;
            UseWaitCursor = true;
            _btnSave.Enabled = false;
            _btnCancel.Enabled = false;

            await AppServices.CreateAuthService().ChangePasswordAsync(user.UserId, currentPassword, newPassword);

            UiMessageBox.Show(
                this,
                UiLanguageManager.T(
                    "Đổi mật khẩu DCR thành công. Mật khẩu Windows / AD / LDAP không bị thay đổi.",
                    "DCR password changed successfully. Your Windows / AD / LDAP password was not changed."),
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            UiMessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _saving = false;
            UseWaitCursor = false;
            _btnSave.Enabled = true;
            _btnCancel.Enabled = true;
        }
    }
}
