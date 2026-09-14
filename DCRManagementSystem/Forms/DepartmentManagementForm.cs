using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;

namespace DCRManagementSystem.Forms;

public sealed class DepartmentManagementForm : Form
{
    private readonly DataGridView _grid = new();
    private readonly TextBox _txtSearch = new();
    private readonly Label _lblCount = new();
    private List<DepartmentGridRow> _rows = new();

    public DepartmentManagementForm()
    {
        if (!CurrentUser.IsAdmin)
            throw new UnauthorizedAccessException("Chỉ Administrator được quản lý Phòng ban.");

        Text = "Department Management";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1080, 680);
        MinimumSize = new Size(940, 580);
        MaximumSize = new Size(1920, 1080);
        UiTheme.ApplyForm(this);
        BuildUi();
        Shown += async (_, _) => await RefreshAsync();
    }

    private void BuildUi()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 102, BackColor = Color.White };
        var title = UiTheme.CreatePageTitle("Quản lý phòng ban"); title.Location = new Point(28, 17);
        var subtitle = UiTheme.CreatePageSubtitle("Mỗi phòng ban thuộc một Đơn vị tổ chức. Head có thể là Manager hoặc chức danh điều hành cao hơn và có thể để trống."); subtitle.Location = new Point(30, 59);
        var btnAdd = new Button { Text = "+  Thêm phòng ban", Size = new Size(150, 38), Anchor = AnchorStyles.Top | AnchorStyles.Right }; UiTheme.StylePrimaryButton(btnAdd); btnAdd.Click += async (_, _) => await EditAsync(null);
        var btnClose = new Button { Text = "Đóng", Size = new Size(86, 38), Anchor = AnchorStyles.Top | AnchorStyles.Right }; UiTheme.StyleSecondaryButton(btnClose); btnClose.Click += (_, _) => Close();
        header.Resize += (_, _) => { btnAdd.Location = new Point(header.ClientSize.Width - btnAdd.Width - 28, 31); btnClose.Location = new Point(btnAdd.Left - btnClose.Width - 10, 31); };
        header.Controls.AddRange([title, subtitle, btnClose, btnAdd]);

        var body = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Background, Padding = new Padding(28, 24, 28, 28) };
        var filterCard = UiTheme.CreateCard(); filterCard.Dock = DockStyle.Top; filterCard.Height = 76;
        var searchLabel = new Label { Text = "Tìm kiếm:", AutoSize = true, Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold), ForeColor = UiTheme.TextSecondary, Location = new Point(18, 22) };
        _txtSearch.SetBounds(103, 20, 360, 34); _txtSearch.PlaceholderText = " Mã, tên phòng, đơn vị, Head..."; UiTheme.StyleInput(_txtSearch); _txtSearch.TextChanged += (_, _) => ApplyFilter();
        _lblCount.AutoSize = false; _lblCount.Size = new Size(180, 34); _lblCount.TextAlign = ContentAlignment.MiddleRight; _lblCount.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold); _lblCount.ForeColor = UiTheme.TextSecondary; _lblCount.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        filterCard.Resize += (_, _) => _lblCount.Location = new Point(filterCard.ClientSize.Width - _lblCount.Width - 18, 20);
        filterCard.Controls.AddRange([searchLabel, _txtSearch, _lblCount]);

        var gridCard = UiTheme.CreateCard(); gridCard.Dock = DockStyle.Fill;
        var gridBar = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Color.White };
        var gridLabel = new Label { Text = "Danh sách phòng ban", AutoSize = true, Location = new Point(18, 18), Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold), ForeColor = UiTheme.TextPrimary };
        var btnDelete = new Button { Text = "Xóa", Size = new Size(86, 34), Anchor = AnchorStyles.Top | AnchorStyles.Right }; UiTheme.StyleDangerButton(btnDelete); btnDelete.Click += async (_, _) => { if (_grid.CurrentRow?.DataBoundItem is DepartmentGridRow row) await DeleteAsync(row); };
        var btnEdit = new Button { Text = "Sửa", Size = new Size(86, 34), Anchor = AnchorStyles.Top | AnchorStyles.Right }; UiTheme.StyleSecondaryButton(btnEdit); btnEdit.Click += async (_, _) => { if (_grid.CurrentRow?.DataBoundItem is DepartmentGridRow row) await EditAsync(row.Source); };
        var btnRefresh = new Button { Text = "Làm mới", Size = new Size(92, 34), Anchor = AnchorStyles.Top | AnchorStyles.Right }; UiTheme.StyleSecondaryButton(btnRefresh); btnRefresh.Click += async (_, _) => await RefreshAsync();
        gridBar.Resize += (_, _) => { btnDelete.Location = new Point(gridBar.ClientSize.Width - btnDelete.Width - 18, 12); btnEdit.Location = new Point(btnDelete.Left - btnEdit.Width - 10, 12); btnRefresh.Location = new Point(btnEdit.Left - btnRefresh.Width - 10, 12); };
        gridBar.Controls.AddRange([gridLabel, btnRefresh, btnEdit, btnDelete]);

        _grid.Dock = DockStyle.Fill; UiTheme.ConfigureGrid(_grid);
        _grid.Columns.Add(Column(nameof(DepartmentGridRow.Code), "Code", 65));
        _grid.Columns.Add(Column(nameof(DepartmentGridRow.Name), "Department Name", 165));
        _grid.Columns.Add(Column(nameof(DepartmentGridRow.BusinessUnit), "Đơn vị tổ chức", 145));
        _grid.Columns.Add(Column(nameof(DepartmentGridRow.Manager), "Manager", 125));
        _grid.Columns.Add(Column(nameof(DepartmentGridRow.Director), "Head cấp trên", 125));
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = nameof(DepartmentGridRow.IsActive), HeaderText = "Active", FillWeight = 48 });
        _grid.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0 && _grid.Rows[e.RowIndex].DataBoundItem is DepartmentGridRow row) await EditAsync(row.Source); };

        gridCard.Controls.Add(_grid); gridCard.Controls.Add(UiTheme.CreateDivider()); gridCard.Controls.Add(gridBar);
        body.Controls.Add(gridCard); body.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 18, BackColor = UiTheme.Background }); body.Controls.Add(filterCard);
        Controls.Add(body); Controls.Add(UiTheme.CreateDivider()); Controls.Add(header);
    }

    private async Task RefreshAsync()
    {
        try
        {
            UseWaitCursor = true;
            var departments = await AppServices.CreateAdminService().GetDepartmentsAsync(false);
            _rows = departments.Select(x => new DepartmentGridRow
            {
                Source = x,
                Code = x.DepartmentCode,
                Name = x.DepartmentName,
                BusinessUnit = x.BusinessUnit?.UnitName ?? string.Empty,
                Manager = x.ManagerUser?.FullName ?? string.Empty,
                Director = x.BusinessUnit?.DirectorUser?.FullName ?? string.Empty,
                IsActive = x.IsActive
            }).ToList();
            ApplyFilter();
        }
        catch (Exception ex) { DCRManagementSystem.Helpers.UiMessageBox.Show(this, ex.Message, "Departments", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { UseWaitCursor = false; }
    }

    private void ApplyFilter()
    {
        var k = _txtSearch.Text.Trim();
        var data = string.IsNullOrWhiteSpace(k) ? _rows : _rows.Where(x => x.Code.Contains(k, StringComparison.OrdinalIgnoreCase) || x.Name.Contains(k, StringComparison.OrdinalIgnoreCase) || x.BusinessUnit.Contains(k, StringComparison.OrdinalIgnoreCase) || x.Manager.Contains(k, StringComparison.OrdinalIgnoreCase) || x.Director.Contains(k, StringComparison.OrdinalIgnoreCase)).ToList();
        _grid.DataSource = data; _lblCount.Text = $"{data.Count:N0} phòng ban";
    }

    private async Task EditAsync(Department? department)
    {
        try
        {
            var service = AppServices.CreateAdminService();
            var departments = await service.GetDepartmentsAsync(false);
            if (department is not null)
            {
                department = departments.SingleOrDefault(x => x.Id == department.Id)
                    ?? throw new InvalidOperationException("Phòng ban không còn tồn tại. Hãy làm mới danh sách.");
            }
            var users = await service.GetUsersAsync();
            var units = await service.GetBusinessUnitsAsync(false);
            var roles = await service.GetRolesAsync(false);
            using var dialog = new DepartmentEditForm(department, departments, units, users, roles);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            await service.SaveDepartmentAsync(department?.Id, dialog.Code, dialog.DepartmentName, dialog.BusinessUnitId, dialog.ManagerUserId, dialog.IsActive);
            await RefreshAsync();
        }
        catch (Exception ex) { DCRManagementSystem.Helpers.UiMessageBox.Show(this, ex.Message, "Save Department", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private async Task DeleteAsync(DepartmentGridRow row)
    {
        var confirm = DCRManagementSystem.Helpers.UiMessageBox.Show(
            this,
            $"Xóa vĩnh viễn phòng ban '{row.Name}' ({row.Code})?\r\n\r\n" +
            "Chỉ phòng ban chưa được người dùng, DCR hoặc ma trận phê duyệt sử dụng mới có thể xóa. " +
            "Nếu cần giữ lịch sử, hãy sửa và bỏ chọn 'Đang hoạt động'.",
            "Xóa phòng ban",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes) return;

        try
        {
            UseWaitCursor = true;
            await AppServices.CreateAdminService().DeleteDepartmentAsync(row.Source.Id);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            DCRManagementSystem.Helpers.UiMessageBox.Show(this, ex.Message, "Xóa phòng ban", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { UseWaitCursor = false; }
    }

    private static DataGridViewTextBoxColumn Column(string property, string header, float weight) => new() { DataPropertyName = property, HeaderText = header, FillWeight = weight };
    private sealed class DepartmentGridRow
    {
        public Department Source { get; set; } = new();
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string BusinessUnit { get; set; } = string.Empty;
        public string Manager { get; set; } = string.Empty;
        public string Director { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }
}

internal sealed class DepartmentEditForm : Form
{
    private readonly TextBox _txtCode = new();
    private readonly TextBox _txtName = new();
    private readonly ComboBox _cmbBusinessUnit = new();
    private readonly ComboBox _cmbManager = new();
    private readonly Label _lblDirector = new();
    private readonly CheckBox _chkActive = new();
    private readonly List<BusinessUnit> _units;

    public string Code => _txtCode.Text.Trim();
    public string DepartmentName => _txtName.Text.Trim();
    public int? BusinessUnitId => _cmbBusinessUnit.SelectedValue is int id && id > 0 ? id : null;
    public int? ManagerUserId => _cmbManager.SelectedValue is int id && id > 0 ? id : null;
    public bool IsActive => _chkActive.Checked;

    public DepartmentEditForm(Department? department, List<Department> departments, List<BusinessUnit> units, List<User> users, List<RoleDefinition> roles)
    {
        _units = units;
        Text = department is null ? "Thêm phòng ban" : "Sửa phòng ban";
        StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ClientSize = new Size(650, 548); UiTheme.ApplyForm(this);

        var header = new Panel { Dock = DockStyle.Top, Height = 90, BackColor = Color.White };
        var title = new Label { Text = Text, AutoSize = true, Font = new Font("Segoe UI Semibold", 17F, FontStyle.Bold), ForeColor = UiTheme.TextPrimary, Location = new Point(28, 16) };
        var subtitle = UiTheme.CreatePageSubtitle("Head có cấp bậc tối thiểu Manager; một người được phép đứng đầu nhiều Phòng ban và có thể để trống khi chưa bổ nhiệm."); subtitle.Location = new Point(30, 54); header.Controls.AddRange([title, subtitle]);

        var card = UiTheme.CreateCard(); card.SetBounds(28, 114, 594, 340);
        var unitItems = new List<BusinessUnit> { new() { Id = 0, UnitName = UiLanguageManager.T("(Chọn Đơn vị tổ chức)", "(Select Organization Unit)") } }; unitItems.AddRange(units.Where(x => x.IsActive || x.Id == department?.BusinessUnitId).OrderBy(x => x.SortOrder).ThenBy(x => x.UnitName));
        _cmbBusinessUnit.DropDownStyle = ComboBoxStyle.DropDownList; _cmbBusinessUnit.DataSource = unitItems; _cmbBusinessUnit.DisplayMember = nameof(BusinessUnit.UnitName); _cmbBusinessUnit.ValueMember = nameof(BusinessUnit.Id); _cmbBusinessUnit.SelectedIndexChanged += (_, _) => UpdateDirectorLabel();

        var managerItems = new List<Item> { new() { Id = 0, Name = "(Chưa gán Manager)" } };
        var managerLevel = roles.FirstOrDefault(x => string.Equals(x.RoleName, RoleNames.Manager, StringComparison.OrdinalIgnoreCase))?.HierarchyLevel ?? 20;
        managerItems.AddRange(users
            .Where(x => x.UserId == department?.ManagerUserId ||
                        (x.IsActive && !x.IsDeleted &&
                         !string.Equals(x.Role, RoleNames.Administrator, StringComparison.OrdinalIgnoreCase) &&
                         (roles.FirstOrDefault(r => r.RoleName == x.Role)?.HierarchyLevel ?? 0) >= managerLevel))
            .OrderBy(x => x.FullName)
            .Select(x =>
            {
                var headCount = departments.Count(d => d.ManagerUserId == x.UserId);
                var concurrent = headCount > 0 ? $" - đang đứng đầu {headCount} phòng ban" : string.Empty;
                return new Item { Id = x.UserId, Name = $"{x.FullName} ({x.Username}) - {x.Role}{concurrent}" };
            }));
        _cmbManager.DropDownStyle = ComboBoxStyle.DropDownList; _cmbManager.DataSource = managerItems; _cmbManager.DisplayMember = nameof(Item.Name); _cmbManager.ValueMember = nameof(Item.Id);

        Add(card, 28, "Code", _txtCode); Add(card, 86, "Name", _txtName); Add(card, 144, "Đơn vị tổ chức", _cmbBusinessUnit); Add(card, 202, "Head Phòng ban", _cmbManager);
        var dLabel = UiTheme.CreateFieldLabel("Head cấp trên"); dLabel.SetBounds(28, 260, 150, 24);
        _lblDirector.SetBounds(190, 258, 372, 32); _lblDirector.ForeColor = UiTheme.TextSecondary; _lblDirector.TextAlign = ContentAlignment.MiddleLeft;
        card.Controls.AddRange([dLabel, _lblDirector]);
        _chkActive.Text = "Phòng ban đang hoạt động"; _chkActive.SetBounds(190, 306, 230, 24); card.Controls.Add(_chkActive);

        var ok = new Button { Text = "Lưu", DialogResult = DialogResult.OK, Size = new Size(104, 38), Location = new Point(402, 478) }; UiTheme.StylePrimaryButton(ok);
        var cancel = new Button { Text = "Hủy", DialogResult = DialogResult.Cancel, Size = new Size(104, 38), Location = new Point(518, 478) }; UiTheme.StyleSecondaryButton(cancel);
        Controls.AddRange([card, ok, cancel, header]); AcceptButton = ok; CancelButton = cancel;
        MaximumSize = new Size(1920, 1080);
        if (department is not null)
        {
            _txtCode.Text = department.DepartmentCode; _txtName.Text = department.DepartmentName; _cmbBusinessUnit.SelectedValue = department.BusinessUnitId ?? 0; _cmbManager.SelectedValue = department.ManagerUserId ?? 0; _chkActive.Checked = department.IsActive;
        }
        else _chkActive.Checked = true;
        UpdateDirectorLabel();
    }

    private void UpdateDirectorLabel()
    {
        var id = _cmbBusinessUnit.SelectedValue is int value ? value : 0;
        var unit = _units.FirstOrDefault(x => x.Id == id);
        _lblDirector.Text = unit?.DirectorUser is null ? UiLanguageManager.T("(Đơn vị này chưa cấu hình Head; hệ thống sẽ tìm ở cấp cha)", "(This unit has no Head; parent units will be checked)") : $"{unit.DirectorUser.FullName} ({unit.DirectorUser.Role})";
    }

    private static void Add(Panel card, int top, string label, Control control)
    {
        var l = UiTheme.CreateFieldLabel(label); l.SetBounds(28, top, 150, 24); control.SetBounds(190, top - 2, 372, 32); UiTheme.StyleInput(control); card.Controls.AddRange([l, control]);
    }
    private sealed class Item { public int Id { get; set; } public string Name { get; set; } = string.Empty; }
}
