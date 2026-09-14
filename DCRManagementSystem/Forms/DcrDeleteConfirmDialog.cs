using DCRManagementSystem.Helpers;

namespace DCRManagementSystem.Forms;

public sealed class DcrDeleteConfirmDialog : Form
{
    private readonly string _dcrNumber;
    private readonly TextBox _txtConfirmation = new();
    private readonly Button _btnDelete = new();

    public DcrDeleteConfirmDialog(string dcrNumber)
    {
        _dcrNumber = dcrNumber;
        Text = "Xác nhận xóa DCR";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(520, 290);
        MaximumSize = new Size(1920, 1080);
        UiTheme.ApplyForm(this);
        BuildUi();
    }

    private void BuildUi()
    {
        var title = new Label
        {
            Text = "Xóa DCR khỏi hệ thống",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold),
            ForeColor = UiTheme.Danger,
            Location = new Point(28, 24)
        };

        var warning = new Label
        {
            Text = $"DCR {_dcrNumber} sẽ bị xóa khỏi dữ liệu hoạt động. Toàn bộ email đang chờ/đang gửi, thời hạn, luồng phê duyệt, attachment và file vật lý liên quan sẽ được hủy hoặc dọn dẹp. Snapshot xóa vẫn được giữ trong DCRDeletionLogs để audit.",
            AutoSize = false,
            Font = new Font("Segoe UI", 9.5F),
            ForeColor = UiTheme.TextSecondary,
            Location = new Point(28, 66),
            Size = new Size(464, 86)
        };

        var prompt = new Label
        {
            Text = $"Nhập chính xác {_dcrNumber} để xác nhận:",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
            ForeColor = UiTheme.TextPrimary,
            Location = new Point(28, 166)
        };

        _txtConfirmation.Location = new Point(28, 193);
        _txtConfirmation.Size = new Size(464, 30);
        UiTheme.StyleInput(_txtConfirmation);
        _txtConfirmation.TextChanged += (_, _) =>
            _btnDelete.Enabled = string.Equals(_txtConfirmation.Text.Trim(), _dcrNumber, StringComparison.OrdinalIgnoreCase);

        var btnCancel = new Button { Text = "Hủy", Size = new Size(90, 36), Location = new Point(302, 239) };
        UiTheme.StyleSecondaryButton(btnCancel);
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        _btnDelete.Text = "Xóa DCR";
        _btnDelete.Size = new Size(100, 36);
        _btnDelete.Location = new Point(402, 239);
        _btnDelete.Enabled = false;
        UiTheme.StyleDangerButton(_btnDelete);
        _btnDelete.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };

        Controls.AddRange([title, warning, prompt, _txtConfirmation, btnCancel, _btnDelete]);
        AcceptButton = _btnDelete;
        CancelButton = btnCancel;
    }
}
