using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;

namespace DCRManagementSystem.Forms;

public sealed class RoleManagementForm : Form
{
    private readonly DataGridView _grid = new();
    private readonly TextBox _txtSearch = new();
    private readonly Label _lblCount = new();
    private List<RoleGridRow> _rows = new();

    public RoleManagementForm()
    {
        if (!CurrentUser.IsAdmin)
            throw new UnauthorizedAccessException("Chỉ Administrator được quản lý Role.");

        Text = "Role Management";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(980, 650);
        MinimumSize = new Size(860, 560);
        MaximumSize = new Size(1920, 1080);
        UiTheme.ApplyForm(this);
        BuildUi();
        Shown += async (_, _) => await RefreshAsync();
    }

    private void BuildUi()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 102, BackColor = Color.White };
        var title = UiTheme.CreatePageTitle("Quản lý Role");
        title.Location = new Point(28, 17);
        var subtitle = UiTheme.CreatePageSubtitle("Thêm, sửa, xóa Role và thiết lập cấp bậc dùng cho gợi ý luồng phê duyệt.");
        subtitle.Location = new Point(30, 59);

        var btnAdd = new Button { Text = "+  Thêm Role", Size = new Size(132, 38), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        UiTheme.StylePrimaryButton(btnAdd);
        btnAdd.Click += async (_, _) => await EditAsync(null);
        var btnClose = new Button { Text = "Đóng", Size = new Size(86, 38), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        UiTheme.StyleSecondaryButton(btnClose);
        btnClose.Click += (_, _) => Close();
        header.Resize += (_, _) =>
        {
            btnAdd.Location = new Point(header.ClientSize.Width - btnAdd.Width - 28, 31);
            btnClose.Location = new Point(btnAdd.Left - btnClose.Width - 10, 31);
        };
        header.Controls.AddRange([title, subtitle, btnClose, btnAdd]);

        var body = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Background, Padding = new Padding(28, 24, 28, 28) };
        var filter = UiTheme.CreateCard();
        filter.Dock = DockStyle.Top;
        filter.Height = 76;
        var lbl = new Label { Text = " Tìm kiếm", AutoSize = true, Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold), ForeColor = UiTheme.TextSecondary, Location = new Point(18, 22) };
        _txtSearch.SetBounds(103, 20, 340, 34);
        _txtSearch.PlaceholderText = " Tên role, mô tả...";
        UiTheme.StyleInput(_txtSearch);
        _txtSearch.TextChanged += (_, _) => ApplyFilter();
        _lblCount.Size = new Size(160, 34);
        _lblCount.TextAlign = ContentAlignment.MiddleRight;
        _lblCount.ForeColor = UiTheme.TextSecondary;
        _lblCount.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);
        filter.Resize += (_, _) => _lblCount.Location = new Point(filter.ClientSize.Width - _lblCount.Width - 18, 20);
        filter.Controls.AddRange([lbl, _txtSearch, _lblCount]);

        var card = UiTheme.CreateCard();
        card.Dock = DockStyle.Fill;
        var bar = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Color.White };
        var barTitle = new Label { Text = "Danh sách Role", AutoSize = true, Location = new Point(18, 18), Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold), ForeColor = UiTheme.TextPrimary };
        var btnDelete = new Button { Text = "Xóa", Size = new Size(86, 34), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        UiTheme.StyleDangerButton(btnDelete);
        btnDelete.Click += async (_, _) =>
        {
            if (_grid.CurrentRow?.DataBoundItem is RoleGridRow row) await DeleteAsync(row);
        };
        var btnEdit = new Button { Text = "Sửa", Size = new Size(86, 34), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        UiTheme.StyleSecondaryButton(btnEdit);
        btnEdit.Click += async (_, _) =>
        {
            if (_grid.CurrentRow?.DataBoundItem is RoleGridRow row) await EditAsync(row.Source);
        };
        var btnRefresh = new Button { Text = "Làm mới", Size = new Size(92, 34), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        UiTheme.StyleSecondaryButton(btnRefresh);
        btnRefresh.Click += async (_, _) => await RefreshAsync();
        bar.Resize += (_, _) =>
        {
            btnDelete.Location = new Point(bar.ClientSize.Width - btnDelete.Width - 18, 12);
            btnEdit.Location = new Point(btnDelete.Left - btnEdit.Width - 10, 12);
            btnRefresh.Location = new Point(btnEdit.Left - btnRefresh.Width - 10, 12);
        };
        bar.Controls.AddRange([barTitle, btnRefresh, btnEdit, btnDelete]);

        _grid.Dock = DockStyle.Fill;
        UiTheme.ConfigureGrid(_grid);
        _grid.Columns.Add(CreateColumn(nameof(RoleGridRow.RoleName), "Role", 110));
        _grid.Columns.Add(CreateColumn(nameof(RoleGridRow.Description), "Description", 200));
        _grid.Columns.Add(CreateColumn(nameof(RoleGridRow.Scope), "Phạm vi", 85));
        _grid.Columns.Add(CreateColumn(nameof(RoleGridRow.HierarchyLevel), "Level", 55));
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = nameof(RoleGridRow.IsActive), HeaderText = "Active", FillWeight = 48 });
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = nameof(RoleGridRow.IsProtected), HeaderText = "Protected", FillWeight = 58 });
        _grid.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex >= 0 && _grid.Rows[e.RowIndex].DataBoundItem is RoleGridRow row) await EditAsync(row.Source);
        };

        card.Controls.Add(_grid);
        card.Controls.Add(UiTheme.CreateDivider());
        card.Controls.Add(bar);
        body.Controls.Add(card);
        body.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 18, BackColor = UiTheme.Background });
        body.Controls.Add(filter);
        Controls.Add(body);
        Controls.Add(UiTheme.CreateDivider());
        Controls.Add(header);
    }

    private async Task RefreshAsync()
    {
        try
        {
            UseWaitCursor = true;
            var roles = await AppServices.CreateAdminService().GetRolesAsync();
            var directorLevel = roles.FirstOrDefault(x => string.Equals(x.RoleName, RoleNames.Director, StringComparison.OrdinalIgnoreCase))?.HierarchyLevel ?? 30;
            _rows = roles.Select(x => new RoleGridRow
            {
                Source = x,
                RoleName = x.RoleName,
                Description = x.Description,
                Scope = string.Equals(x.RoleName, RoleNames.Administrator, StringComparison.OrdinalIgnoreCase)
                    ? "Hệ thống"
                    : string.Equals(x.RoleName, RoleNames.Staff, StringComparison.OrdinalIgnoreCase) || string.Equals(x.RoleName, RoleNames.Manager, StringComparison.OrdinalIgnoreCase)
                        ? "Phòng ban"
                        : x.HierarchyLevel >= directorLevel ? "Khối / Phòng ban" : "Tùy chỉnh",
                HierarchyLevel = x.HierarchyLevel,
                IsActive = x.IsActive,
                IsProtected = x.IsSystemProtected
            }).ToList();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            DCRManagementSystem.Helpers.UiMessageBox.Show(this, ex.Message, "Role Management", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { UseWaitCursor = false; }
    }

    private void ApplyFilter()
    {
        var keyword = _txtSearch.Text.Trim();
        var data = string.IsNullOrWhiteSpace(keyword)
            ? _rows
            : _rows.Where(x => x.RoleName.Contains(keyword, StringComparison.OrdinalIgnoreCase) || x.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();
        _grid.DataSource = data;
        _lblCount.Text = $"{data.Count:N0} role";
    }

    private async Task EditAsync(RoleDefinition? role)
    {
        try
        {
            var service = AppServices.CreateAdminService();
            if (role is not null)
            {
                role = (await service.GetRolesAsync(false)).SingleOrDefault(x => x.Id == role.Id)
                    ?? throw new InvalidOperationException("Role không còn tồn tại. Hãy làm mới danh sách.");
            }
            using var dialog = new RoleEditForm(role);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            await service.SaveRoleAsync(role?.Id, dialog.RoleName, dialog.Description, dialog.HierarchyLevel, dialog.IsActive);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            DCRManagementSystem.Helpers.UiMessageBox.Show(this, ex.Message, "Lưu Role", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task DeleteAsync(RoleGridRow row)
    {
        var confirm = DCRManagementSystem.Helpers.UiMessageBox.Show(this, $"Xóa Role '{row.RoleName}'?", "Xóa Role", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes) return;
        try
        {
            await AppServices.CreateAdminService().DeleteRoleAsync(row.Source.Id);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            DCRManagementSystem.Helpers.UiMessageBox.Show(this, ex.Message, "Xóa Role", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static DataGridViewTextBoxColumn CreateColumn(string property, string header, float weight) => new()
    {
        DataPropertyName = property,
        HeaderText = header,
        FillWeight = weight
    };

    private sealed class RoleGridRow
    {
        public RoleDefinition Source { get; set; } = new();
        public string RoleName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public int HierarchyLevel { get; set; }
        public bool IsActive { get; set; }
        public bool IsProtected { get; set; }
    }
}

internal sealed class RoleEditForm : Form
{
    private readonly TextBox _txtName = new();
    private readonly TextBox _txtDescription = new();
    private readonly NumericUpDown _numLevel = new();
    private readonly CheckBox _chkActive = new();

    public string RoleName => _txtName.Text.Trim();
    public string Description => _txtDescription.Text.Trim();
    public int HierarchyLevel => (int)_numLevel.Value;
    public bool IsActive => _chkActive.Checked;

    public RoleEditForm(RoleDefinition? role)
    {
        Text = role is null ? "Thêm Role" : "Sửa Role";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(650, 430);
        MaximumSize = new Size(1920, 1080);
        UiTheme.ApplyForm(this);

        var header = new Panel { Dock = DockStyle.Top, Height = 90, BackColor = Color.White };
        var title = new Label { Text = Text, AutoSize = true, Font = new Font("Segoe UI Semibold", 17F, FontStyle.Bold), ForeColor = UiTheme.TextPrimary, Location = new Point(28, 16) };
        var subtitle = UiTheme.CreatePageSubtitle("Số càng lớn thì cấp càng cao. Staff/Manager thuộc Phòng ban; Role có level từ Director trở lên thuộc Khối.");
        subtitle.Location = new Point(30, 54);
        header.Controls.AddRange([title, subtitle]);

        var card = UiTheme.CreateCard();
        card.SetBounds(28, 114, 594, 230);
        AddField(card, 28, "Role Name", _txtName);
        AddField(card, 86, "Description", _txtDescription);
        _numLevel.Minimum = 0;
        _numLevel.Maximum = 1000;
        AddField(card, 144, "Hierarchy Level", _numLevel);
        _chkActive.Text = "Role đang hoạt động";
        _chkActive.SetBounds(190, 198, 220, 24);
        card.Controls.Add(_chkActive);

        var ok = new Button { Text = "Lưu", DialogResult = DialogResult.OK, Size = new Size(104, 38), Location = new Point(402, 366) };
        UiTheme.StylePrimaryButton(ok);
        var cancel = new Button { Text = "Hủy", DialogResult = DialogResult.Cancel, Size = new Size(104, 38), Location = new Point(518, 366) };
        UiTheme.StyleSecondaryButton(cancel);
        Controls.AddRange([card, ok, cancel, header]);
        AcceptButton = ok;
        CancelButton = cancel;

        if (role is not null)
        {
            _txtName.Text = role.RoleName;
            _txtDescription.Text = role.Description;
            _numLevel.Value = Math.Clamp(role.HierarchyLevel, (int)_numLevel.Minimum, (int)_numLevel.Maximum);
            _chkActive.Checked = role.IsActive;
            if (role.IsSystemProtected)
            {
                _txtName.ReadOnly = true;
                _chkActive.Enabled = false;
            }
        }
        else
        {
            _numLevel.Value = 10;
            _chkActive.Checked = true;
        }
    }

    private static void AddField(Panel card, int top, string labelText, Control control)
    {
        var label = UiTheme.CreateFieldLabel(labelText);
        label.SetBounds(28, top, 150, 24);
        control.SetBounds(190, top - 2, 372, 32);
        UiTheme.StyleInput(control);
        card.Controls.AddRange([label, control]);
    }
}
