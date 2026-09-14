namespace DCRManagementSystem.Helpers;

public static class UiTheme
{
    public static readonly Color Background = Color.FromArgb(247, 249, 247);
    public static readonly Color Surface = Color.White;
    // Brand palette derived from the DCR logo: deep green + warm orange.
    public static readonly Color Primary = Color.FromArgb(20, 132, 73);
    public static readonly Color PrimaryHover = Color.FromArgb(15, 108, 60);
    public static readonly Color PrimarySoft = Color.FromArgb(232, 247, 238);
    public static readonly Color Accent = Color.FromArgb(247, 126, 31);
    public static readonly Color AccentHover = Color.FromArgb(220, 103, 16);
    public static readonly Color AccentSoft = Color.FromArgb(255, 244, 232);
    public static readonly Color TextPrimary = Color.FromArgb(32, 33, 36);
    public static readonly Color TextSecondary = Color.FromArgb(95, 99, 104);
    public static readonly Color Border = Color.FromArgb(224, 224, 228);
    public static readonly Color GridHeader = Color.FromArgb(242, 248, 244);
    public static readonly Color Success = Color.FromArgb(32, 132, 76);
    public static readonly Color SuccessSoft = Color.FromArgb(232, 247, 238);
    public static readonly Color Danger = Color.FromArgb(190, 54, 54);
    public static readonly Color DangerSoft = Color.FromArgb(252, 237, 237);
    public static readonly Color Warning = AccentHover;
    public static readonly Color WarningSoft = AccentSoft;

    public static void ApplyForm(Form form)
    {
        form.BackColor = Background;
        form.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
        form.Icon = (Icon)AppBranding.AppIcon.Clone();
        ModernWindowChrome.Prepare(form);
        UiLanguageManager.Attach(form);
    }

    public static Panel CreateCard()
    {
        return new Panel
        {
            BackColor = Surface,
            BorderStyle = BorderStyle.FixedSingle
        };
    }

    public static Label CreatePageTitle(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 19F, FontStyle.Bold),
            ForeColor = TextPrimary
        };
    }

    public static Label CreatePageSubtitle(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5F),
            ForeColor = TextSecondary
        };
    }

    public static Label CreateFieldLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = false,
            Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(60, 64, 67),
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    public static void StyleInput(Control control)
    {
        control.Font = new Font("Segoe UI", 9.5F);
        control.BackColor = Surface;
        control.ForeColor = TextPrimary;

        if (control is TextBox textBox)
        {
            textBox.BorderStyle = BorderStyle.FixedSingle;
        }
        else if (control is RichTextBox richTextBox)
        {
            richTextBox.BorderStyle = BorderStyle.FixedSingle;
        }
        else if (control is ComboBox comboBox)
        {
            comboBox.FlatStyle = FlatStyle.Flat;
        }
    }

    public static void StylePrimaryButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = PrimaryHover;
        button.BackColor = Primary;
        button.ForeColor = Color.White;
        button.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
    }

    public static void StyleSecondaryButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = Color.FromArgb(198, 220, 205);
        button.FlatAppearance.MouseOverBackColor = PrimarySoft;
        button.BackColor = Surface;
        button.ForeColor = Color.FromArgb(43, 76, 55);
        button.Font = new Font("Segoe UI", 9.5F);
        button.Cursor = Cursors.Hand;
    }

    public static void StyleDangerButton(Button button)
    {
        StyleSecondaryButton(button);
        button.ForeColor = Danger;
        button.FlatAppearance.BorderColor = Color.FromArgb(231, 188, 188);
        button.FlatAppearance.MouseOverBackColor = DangerSoft;
    }

    public static void StyleNavigationButton(Button button, bool active)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.TextAlign = ContentAlignment.MiddleLeft;
        button.Padding = button is NavigationBadgeButton ? new Padding(18, 0, 55, 0) : new Padding(18, 0, 8, 0);
        button.Cursor = Cursors.Hand;
        button.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);
        button.BackColor = active ? Accent : Primary;
        button.ForeColor = Color.White;
        button.FlatAppearance.MouseOverBackColor = active ? AccentHover : PrimaryHover;
    }

    public static void ConfigureGrid(DataGridView grid, bool readOnly = true)
    {
        grid.ReadOnly = readOnly;
        grid.AllowUserToAddRows = !readOnly;
        grid.AllowUserToDeleteRows = !readOnly;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.AutoGenerateColumns = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.BackgroundColor = Surface;
        grid.BorderStyle = BorderStyle.None;
        grid.GridColor = Color.FromArgb(235, 235, 238);
        grid.RowTemplate.Height = 38;
        grid.ColumnHeadersHeight = 42;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = GridHeader,
            ForeColor = Color.FromArgb(60, 64, 67),
            Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
            SelectionBackColor = GridHeader,
            SelectionForeColor = Color.FromArgb(60, 64, 67),
            Padding = new Padding(6, 0, 6, 0)
        };
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Surface,
            ForeColor = TextPrimary,
            SelectionBackColor = PrimarySoft,
            SelectionForeColor = TextPrimary,
            Font = new Font("Segoe UI", 9.5F),
            Padding = new Padding(6, 2, 6, 2)
        };
        grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(252, 252, 253),
            ForeColor = TextPrimary,
            SelectionBackColor = PrimarySoft,
            SelectionForeColor = TextPrimary,
            Font = new Font("Segoe UI", 9.5F),
            Padding = new Padding(6, 2, 6, 2)
        };
    }

    public static Panel CreateDivider(DockStyle dock = DockStyle.Top)
    {
        return new Panel
        {
            Dock = dock,
            Height = dock is DockStyle.Top or DockStyle.Bottom ? 1 : 0,
            Width = dock is DockStyle.Left or DockStyle.Right ? 1 : 0,
            BackColor = Border
        };
    }
}
