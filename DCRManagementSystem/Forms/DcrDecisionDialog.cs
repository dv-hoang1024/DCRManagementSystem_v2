using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;

namespace DCRManagementSystem.Forms;

internal sealed class DcrDecisionDialog : Form
{
    private readonly RichTextBox _comment = new();

    public string Comment => _comment.Text.Trim();

    public DcrDecisionDialog(string decision, string username)
    {
        Text = decision switch
        {
            ApprovalDecisions.Approved => "Approve DCR",
            ApprovalDecisions.Rejected => "Reject DCR",
            ApprovalDecisions.Returned => "Request Information",
            _ => "DCR Decision"
        };
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(570, 370);
        MaximumSize = new Size(1920, 1080);
        UiTheme.ApplyForm(this);

        var card = UiTheme.CreateCard();
        card.Location = new Point(24, 24);
        card.Size = new Size(522, 280);

        var title = UiTheme.CreatePageTitle(Text);
        title.Location = new Point(24, 20);
        title.Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold);

        var description = new Label
        {
            Text = $"Xác nhận với tài khoản đang đăng nhập: {username}. " +
                (decision == ApprovalDecisions.Approved ? "Comment có thể để trống." : "Vui lòng nhập lý do."),
            AutoSize = false,
            Location = new Point(24, 58),
            Size = new Size(470, 44),
            ForeColor = UiTheme.TextSecondary
        };

        var lblComment = UiTheme.CreateFieldLabel(decision == ApprovalDecisions.Approved ? "Comment" : "Reason *");
        lblComment.Location = new Point(24, 108);
        lblComment.Size = new Size(470, 24);
        _comment.Location = new Point(24, 134);
        _comment.Size = new Size(470, 92);
        UiTheme.StyleInput(_comment);
        card.Controls.AddRange([title, description, lblComment, _comment]);

        var buttonY = 318;
        var cancel = new Button
        {
            Text = "Cancel",
            Size = new Size(95, 38),
            Location = new Point(342, buttonY),
            DialogResult = DialogResult.Cancel
        };
        UiTheme.StyleSecondaryButton(cancel);

        var confirm = new Button
        {
            Text = decision switch
            {
                ApprovalDecisions.Approved => "Approve",
                ApprovalDecisions.Rejected => "Reject",
                ApprovalDecisions.Returned => "Request Info",
                _ => decision
            },
            Size = new Size(110, 38),
            Location = new Point(444, buttonY)
        };
        if (decision == ApprovalDecisions.Rejected)
            UiTheme.StyleDangerButton(confirm);
        else
            UiTheme.StylePrimaryButton(confirm);

        confirm.Click += (_, _) =>
        {
            if ((decision == ApprovalDecisions.Rejected || decision == ApprovalDecisions.Returned) &&
                string.IsNullOrWhiteSpace(_comment.Text))
            {
                DCRManagementSystem.Helpers.UiMessageBox.Show("Vui lòng nhập lý do.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        };

        Controls.AddRange([card, cancel, confirm]);
        AcceptButton = confirm;
        CancelButton = cancel;
    }
}
