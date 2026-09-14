using DCRManagementSystem.Helpers;

namespace DCRManagementSystem.UserControls;

internal static class WizardControlUi
{
    public static FlowLayoutPanel CreateFlowPage()
    {
        return new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = UiTheme.Background,
            Padding = new Padding(24, 24, 24, 40)
        };
    }

    public static Panel CreateCard(string title, string subtitle, int height)
    {
        var card = UiTheme.CreateCard();
        card.Size = new Size(980, height);
        card.Margin = new Padding(0, 0, 0, 16);

        var lblTitle = new Label
        {
            Text = title,
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold),
            ForeColor = UiTheme.TextPrimary,
            Location = new Point(28, 22)
        };
        var lblSubtitle = new Label
        {
            Text = subtitle,
            AutoSize = false,
            Font = new Font("Segoe UI", 9.2F),
            ForeColor = UiTheme.TextSecondary,
            Location = new Point(28, 53),
            Size = new Size(900, 36),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        card.Controls.Add(lblTitle);
        card.Controls.Add(lblSubtitle);
        return card;
    }

    public static void AttachResponsiveCards(FlowLayoutPanel page)
    {
        void ResizeCards()
        {
            var available = Math.Max(760, page.ClientSize.Width - 68);
            var width = Math.Min(1020, available);
            var left = Math.Max(0, (available - width) / 2);
            foreach (Control control in page.Controls)
            {
                control.Width = width;
                control.Margin = new Padding(left, 0, 0, 16);
            }
        }

        page.Resize += (_, _) => ResizeCards();
        ResizeCards();
    }

    public static void AddPair(Control card, int y, string leftLabel, Control leftControl, string rightLabel, Control rightControl, int leftHeight = 30, int rightHeight = 30)
    {
        AddFieldAt(card, 28, y, leftLabel, leftControl, 410, leftHeight);
        AddFieldAt(card, 510, y, rightLabel, rightControl, 410, rightHeight);
    }

    public static void AddSingle(Control card, int y, string label, Control control, int width, int height = 30)
        => AddFieldAt(card, 28, y, label, control, width, height);

    public static void AddFieldAt(Control card, int x, int y, string label, Control control, int width, int height = 30)
    {
        var lbl = UiTheme.CreateFieldLabel(label);
        lbl.Location = new Point(x, y);
        lbl.Size = new Size(width, 24);
        control.Location = new Point(x, y + 28);
        control.Size = new Size(width, height);
        UiTheme.StyleInput(control);
        card.Controls.Add(lbl);
        card.Controls.Add(control);
    }

    public static DataGridViewTextBoxColumn CreateTextColumn(string header, string property, int width)
    {
        return new DataGridViewTextBoxColumn
        {
            HeaderText = header,
            DataPropertyName = property,
            Width = width,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None
        };
    }

    public static void ConfigureButton(Button button, string text, int width, bool primary = false, bool danger = false)
    {
        button.Text = text;
        button.Size = new Size(width, 38);
        if (danger)
            UiTheme.StyleDangerButton(button);
        else if (primary)
            UiTheme.StylePrimaryButton(button);
        else
            UiTheme.StyleSecondaryButton(button);
    }

    public static void ConfigureReadOnly(TextBox textBox)
    {
        textBox.ReadOnly = true;
        textBox.BackColor = Color.FromArgb(248, 249, 250);
        UiTheme.StyleInput(textBox);
    }

    public static void ConfigureRich(RichTextBox rich)
    {
        rich.DetectUrls = true;
        UiTheme.StyleInput(rich);
    }

    public static void ConfigureCheck(CheckBox checkBox, string text)
    {
        checkBox.Text = text;
        checkBox.AutoSize = true;
        checkBox.Font = new Font("Segoe UI", 10F);
        checkBox.ForeColor = UiTheme.TextPrimary;
    }

    public static void ConfigureNullableDate(DateTimePicker picker)
    {
        picker.Format = DateTimePickerFormat.Custom;
        picker.CustomFormat = "dd/MM/yyyy";
        picker.ShowCheckBox = true;
        picker.Checked = false;
        UiTheme.StyleInput(picker);
    }

    public static void SetNullableDate(DateTimePicker picker, DateTime? value)
    {
        picker.Checked = value.HasValue;
        if (value.HasValue)
            picker.Value = value.Value;
    }

    public static DateTime? GetNullableDate(DateTimePicker picker) => picker.Checked ? picker.Value.Date : null;

    public static void SetEditable(Control control, bool editable)
    {
        switch (control)
        {
            case TextBox text:
                text.ReadOnly = !editable;
                text.BackColor = editable ? Color.White : Color.FromArgb(248, 249, 250);
                break;
            case RichTextBox rich:
                rich.ReadOnly = !editable;
                rich.BackColor = editable ? Color.White : Color.FromArgb(248, 249, 250);
                break;
            default:
                control.Enabled = editable;
                break;
        }
    }
}

public abstract class DcrWizardStepControl : UserControl, IWizardStepControl
{
    private DCRManagementSystem.Models.DcrEditModel? _boundModel;

    public event EventHandler? DataChanged;

    public DCRManagementSystem.Models.ValidationResultModel LastValidationResult { get; private set; } = new();

    protected abstract int StepNumber { get; }

    protected DcrWizardStepControl()
    {
        Dock = DockStyle.Fill;
        BackColor = UiTheme.Background;
    }

    protected void RaiseDataChanged() => DataChanged?.Invoke(this, EventArgs.Empty);

    public void BindFromModel(DCRManagementSystem.Models.DcrEditModel model)
    {
        _boundModel = model;
        LoadFrom(model);
    }

    public void SyncToModel(DCRManagementSystem.Models.DcrEditModel model)
    {
        ApplyTo(model);
        _boundModel = model;
    }

    public Task<bool> ValidateStepAsync()
    {
        LastValidationResult = _boundModel is null
            ? CreateMissingModelValidation()
            : DCRManagementSystem.Services.DcrValidationService.ValidateStep(_boundModel, StepNumber);

        return Task.FromResult(LastValidationResult.IsValid);
    }

    private static DCRManagementSystem.Models.ValidationResultModel CreateMissingModelValidation()
    {
        var result = new DCRManagementSystem.Models.ValidationResultModel();
        result.Errors.Add("Wizard step chưa được bind với DCR model.");
        return result;
    }

    protected abstract void LoadFrom(DCRManagementSystem.Models.DcrEditModel model);
    protected abstract void ApplyTo(DCRManagementSystem.Models.DcrEditModel model);
    public abstract void SetEditable(bool editable);
}
