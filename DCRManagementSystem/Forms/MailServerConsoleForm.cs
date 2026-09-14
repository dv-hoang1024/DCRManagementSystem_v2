using DCRManagementSystem.Helpers;
using DCRManagementSystem.UserControls;

namespace DCRManagementSystem.Forms;

public sealed class MailServerConsoleForm : Form
{
    public MailServerConsoleForm()
    {
        Text = "DCR Mail Server Console";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(940, 860);
        MinimumSize = new Size(860, 760);
        MaximumSize = new Size(1920, 1080);
        UiTheme.ApplyForm(this);
        BuildUi();
    }

    private void BuildUi()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 96, BackColor = Color.White };
        var title = UiTheme.CreatePageTitle("DCR Mail Server Console");
        title.Location = new Point(28, 16);
        var subtitle = UiTheme.CreatePageSubtitle("Cùng cấu hình với Quản trị > Thiết lập hệ thống. Console này chỉ giữ lại để thao tác trực tiếp trên máy chủ khi cần.");
        subtitle.Location = new Point(30, 57);
        header.Controls.AddRange([title, subtitle]);

        var body = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = UiTheme.Background,
            Padding = new Padding(28, 24, 28, 28)
        };

        var mailSettings = new MailServerSettingsControl(requireAdministrator: false)
        {
            Width = 850,
            Height = 735
        };
        body.Controls.Add(mailSettings);
        body.Resize += (_, _) => mailSettings.Width = Math.Max(780, body.ClientSize.Width - 56);

        Controls.Add(body);
        Controls.Add(header);
    }
}
