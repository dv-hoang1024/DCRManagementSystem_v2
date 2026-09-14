using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;

namespace DCRManagementSystem.Forms;

public sealed class BusinessUnitManagementForm : Form
{
    private readonly DataGridView _grid = new();
    private readonly TextBox _txtSearch = new();
    private readonly Label _lblCount = new();
    private List<Row> _rows = new();

    public BusinessUnitManagementForm()
    {
        if (!CurrentUser.IsAdmin)
            throw new UnauthorizedAccessException("Chỉ Administrator được quản lý Đơn vị tổ chức.");

        Text = "Quản lý Đơn vị tổ chức";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1040, 650);
        MinimumSize = new Size(900, 560);
        MaximumSize = new Size(1920, 1080);
        UiTheme.ApplyForm(this);
        BuildUi();
        Shown += async (_, _) => await RefreshAsync();
    }

    private void BuildUi()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 102, BackColor = Color.White };
        var title = UiTheme.CreatePageTitle("Quản lý Đơn vị tổ chức");
        title.Location = new Point(28, 17);
        var subtitle = UiTheme.CreatePageSubtitle("Cây nhiều cấp: Ban điều hành, Khối, Trung tâm, Nhà máy, Khu vực...; mỗi đơn vị có thể có Head hoặc để trống.");
        subtitle.Location = new Point(30, 59);
        var add = new Button { Text = "+  Thêm đơn vị", Size = new Size(140, 38), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        UiTheme.StylePrimaryButton(add);
        add.Click += async (_, _) => await EditAsync(null);
        var close = new Button { Text = "Đóng", Size = new Size(86, 38), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        UiTheme.StyleSecondaryButton(close);
        close.Click += (_, _) => Close();
        header.Resize += (_, _) =>
        {
            add.Location = new Point(header.ClientSize.Width - add.Width - 28, 31);
            close.Location = new Point(add.Left - close.Width - 10, 31);
        };
        header.Controls.AddRange([title, subtitle, close, add]);

        var body = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Background, Padding = new Padding(28, 24, 28, 28) };
        var filter = UiTheme.CreateCard(); filter.Dock = DockStyle.Top; filter.Height = 76;
        var lbl = new Label { Text = "Tìm kiếm:", AutoSize = true, Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold), ForeColor = UiTheme.TextSecondary, Location = new Point(18, 22) };
        _txtSearch.SetBounds(103, 20, 340, 34); _txtSearch.PlaceholderText = " Mã, tên, loại đơn vị, Head..."; UiTheme.StyleInput(_txtSearch);
        _txtSearch.TextChanged += (_, _) => ApplyFilter();
        _lblCount.AutoSize = false; _lblCount.Size = new Size(180, 34); _lblCount.TextAlign = ContentAlignment.MiddleRight; _lblCount.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold); _lblCount.ForeColor = UiTheme.TextSecondary; _lblCount.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        filter.Resize += (_, _) => _lblCount.Location = new Point(filter.ClientSize.Width - _lblCount.Width - 18, 20);
        filter.Controls.AddRange([lbl, _txtSearch, _lblCount]);

        var card = UiTheme.CreateCard(); card.Dock = DockStyle.Fill;
        var bar = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Color.White };
        var barTitle = new Label { Text = "Danh sách Đơn vị tổ chức", AutoSize = true, Location = new Point(18, 18), Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold), ForeColor = UiTheme.TextPrimary };
        var del = new Button { Text = "Xóa", Size = new Size(86, 34), Anchor = AnchorStyles.Top | AnchorStyles.Right }; UiTheme.StyleDangerButton(del);
        del.Click += async (_, _) => { if (_grid.CurrentRow?.DataBoundItem is Row r) await DeleteAsync(r); };
        var edit = new Button { Text = "Sửa", Size = new Size(86, 34), Anchor = AnchorStyles.Top | AnchorStyles.Right }; UiTheme.StyleSecondaryButton(edit);
        edit.Click += async (_, _) => { if (_grid.CurrentRow?.DataBoundItem is Row r) await EditAsync(r.Source); };
        var refresh = new Button { Text = "Làm mới", Size = new Size(92, 34), Anchor = AnchorStyles.Top | AnchorStyles.Right }; UiTheme.StyleSecondaryButton(refresh);
        refresh.Click += async (_, _) => await RefreshAsync();
        bar.Resize += (_, _) =>
        {
            del.Location = new Point(bar.ClientSize.Width - del.Width - 18, 12);
            edit.Location = new Point(del.Left - edit.Width - 10, 12);
            refresh.Location = new Point(edit.Left - refresh.Width - 10, 12);
        };
        bar.Controls.AddRange([barTitle, refresh, edit, del]);

        _grid.Dock = DockStyle.Fill; UiTheme.ConfigureGrid(_grid);
        _grid.Columns.Add(Column(nameof(Row.Code), "Code", 70));
        _grid.Columns.Add(Column(nameof(Row.Name), "Đơn vị tổ chức", 190));
        _grid.Columns.Add(Column(nameof(Row.UnitType), "Loại đơn vị", 90));
        _grid.Columns.Add(Column(nameof(Row.ParentUnit), "Cấp trên", 150));
        _grid.Columns.Add(Column(nameof(Row.Director), "Director / Block Head", 150));
        _grid.Columns.Add(Column(nameof(Row.DirectorRole), "Role", 80));
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = nameof(Row.IsActive), HeaderText = "Active", FillWeight = 48 });
        _grid.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0 && _grid.Rows[e.RowIndex].DataBoundItem is Row r) await EditAsync(r.Source); };

        card.Controls.Add(_grid); card.Controls.Add(UiTheme.CreateDivider()); card.Controls.Add(bar);
        body.Controls.Add(card); body.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 18, BackColor = UiTheme.Background }); body.Controls.Add(filter);
        Controls.Add(body); Controls.Add(UiTheme.CreateDivider()); Controls.Add(header);
    }

    private async Task RefreshAsync()
    {
        try
        {
            UseWaitCursor = true;
            var units = await AppServices.CreateAdminService().GetBusinessUnitsAsync(false);
            _rows = units.Select(x => new Row
            {
                Source = x,
                Code = x.UnitCode,
                Name = x.UnitName,
                UnitType = OrganizationUnitTypes.DisplayName(x.UnitType),
                ParentUnit = x.ParentBusinessUnit?.UnitName ?? string.Empty,
                Director = x.DirectorUser?.FullName ?? string.Empty,
                DirectorRole = x.DirectorUser?.Role ?? string.Empty,
                IsActive = x.IsActive
            }).ToList();
            ApplyFilter();
        }
        catch (Exception ex) { DCRManagementSystem.Helpers.UiMessageBox.Show(this, ex.Message, "Đơn vị tổ chức", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { UseWaitCursor = false; }
    }

    private void ApplyFilter()
    {
        var k = _txtSearch.Text.Trim();
        var data = string.IsNullOrWhiteSpace(k) ? _rows : _rows.Where(x => x.Code.Contains(k, StringComparison.OrdinalIgnoreCase) || x.Name.Contains(k, StringComparison.OrdinalIgnoreCase) || x.UnitType.Contains(k, StringComparison.OrdinalIgnoreCase) || x.ParentUnit.Contains(k, StringComparison.OrdinalIgnoreCase) || x.Director.Contains(k, StringComparison.OrdinalIgnoreCase) || x.DirectorRole.Contains(k, StringComparison.OrdinalIgnoreCase)).ToList();
        _grid.DataSource = data; _lblCount.Text = $"{data.Count:N0} đơn vị";
    }

    private async Task EditAsync(BusinessUnit? unit)
    {
        try
        {
            var service = AppServices.CreateAdminService();
            var units = await service.GetBusinessUnitsAsync(false);
            if (unit is not null)
            {
                unit = units.SingleOrDefault(x => x.Id == unit.Id)
                    ?? throw new InvalidOperationException("Đơn vị tổ chức không còn tồn tại. Hãy làm mới danh sách.");
            }
            var users = await service.GetUsersAsync();
            var roles = await service.GetRolesAsync(false);
            using var dlg = new BusinessUnitEditForm(unit, units, users, roles);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            await service.SaveBusinessUnitAsync(unit?.Id, dlg.Code, dlg.UnitName, dlg.DirectorUserId, dlg.IsActive, dlg.ParentBusinessUnitId, dlg.UnitType, dlg.SortOrder);
            await RefreshAsync();
        }
        catch (Exception ex) { DCRManagementSystem.Helpers.UiMessageBox.Show(this, ex.Message, "Lưu Đơn vị tổ chức", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private async Task DeleteAsync(Row row)
    {
        if (DCRManagementSystem.Helpers.UiMessageBox.Show(this, $"Xóa Đơn vị tổ chức '{row.Name}'?", "Xóa Đơn vị tổ chức", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try { await AppServices.CreateAdminService().DeleteBusinessUnitAsync(row.Source.Id); await RefreshAsync(); }
        catch (Exception ex) { DCRManagementSystem.Helpers.UiMessageBox.Show(this, ex.Message, "Xóa Đơn vị tổ chức", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private static DataGridViewTextBoxColumn Column(string p, string h, float w) => new() { DataPropertyName = p, HeaderText = h, FillWeight = w };
    private sealed class Row
    {
        public BusinessUnit Source { get; set; } = new();
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string UnitType { get; set; } = string.Empty;
        public string ParentUnit { get; set; } = string.Empty;
        public string Director { get; set; } = string.Empty;
        public string DirectorRole { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }
}

internal sealed class BusinessUnitEditForm : Form
{
    private readonly TextBox _txtCode = new();
    private readonly TextBox _txtName = new();
    private readonly ComboBox _cmbDirector = new();
    private readonly ComboBox _cmbParent = new();
    private readonly ComboBox _cmbUnitType = new();
    private readonly NumericUpDown _numSortOrder = new();
    private readonly CheckBox _chkActive = new();

    public string Code => _txtCode.Text.Trim();
    public string UnitName => _txtName.Text.Trim();
    public int? DirectorUserId => _cmbDirector.SelectedValue is int id && id > 0 ? id : null;
    public int? ParentBusinessUnitId => _cmbParent.SelectedValue is int id && id > 0 ? id : null;
    public string UnitType => _cmbUnitType.SelectedValue as string ?? OrganizationUnitTypes.Division;
    public int SortOrder => (int)_numSortOrder.Value;
    public bool IsActive => _chkActive.Checked;

    public BusinessUnitEditForm(BusinessUnit? unit, List<BusinessUnit> units, List<User> users, List<RoleDefinition> roles)
    {
        Text = unit is null ? "Thêm Đơn vị tổ chức" : "Sửa Đơn vị tổ chức";
        StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ClientSize = new Size(650, 620); UiTheme.ApplyForm(this);
        var header = new Panel { Dock = DockStyle.Top, Height = 90, BackColor = Color.White };
        var title = new Label { Text = Text, AutoSize = true, Font = new Font("Segoe UI Semibold", 17F, FontStyle.Bold), ForeColor = UiTheme.TextPrimary, Location = new Point(28, 16) };
        var subtitle = UiTheme.CreatePageSubtitle("Head có cấp bậc tối thiểu Director; một người được phép đứng đầu nhiều Đơn vị tổ chức và có thể để trống."); subtitle.Location = new Point(30, 54);
        header.Controls.AddRange([title, subtitle]);
        MaximumSize = new Size(1920, 1080);
        var card = UiTheme.CreateCard(); card.SetBounds(28, 114, 594, 410);
        var parentItems = new List<BusinessUnit> { new() { Id = 0, UnitName = "(Cấp cao nhất / không có cấp trên)" } };
        parentItems.AddRange(units.Where(x => x.Id != unit?.Id && (x.IsActive || x.Id == unit?.ParentBusinessUnitId)).OrderBy(x => x.SortOrder).ThenBy(x => x.UnitName));
        _cmbParent.DropDownStyle = ComboBoxStyle.DropDownList; _cmbParent.DataSource = parentItems; _cmbParent.DisplayMember = nameof(BusinessUnit.UnitName); _cmbParent.ValueMember = nameof(BusinessUnit.Id);
        _cmbUnitType.DropDownStyle = ComboBoxStyle.DropDownList;
        var unitTypeItems = OrganizationUnitTypes.All
            .Select(x => new UnitTypeItem(x, OrganizationUnitTypes.DisplayName(x)))
            .ToList();
        _cmbUnitType.DataSource = unitTypeItems;
        _cmbUnitType.DisplayMember = nameof(UnitTypeItem.DisplayName);
        _cmbUnitType.ValueMember = nameof(UnitTypeItem.Value);
        _numSortOrder.Minimum = 0; _numSortOrder.Maximum = 9999;
        var directorItems = new List<Item> { new() { Id = 0, Name = "(Chưa gán)" } };
        var directorLevel = roles.FirstOrDefault(x => string.Equals(x.RoleName, RoleNames.Director, StringComparison.OrdinalIgnoreCase))?.HierarchyLevel ?? 30;
        directorItems.AddRange(users
            .Where(x => x.UserId == unit?.DirectorUserId ||
                        (x.IsActive && !x.IsDeleted &&
                         !string.Equals(x.Role, RoleNames.Administrator, StringComparison.OrdinalIgnoreCase) &&
                         (roles.FirstOrDefault(r => r.RoleName == x.Role)?.HierarchyLevel ?? 0) >= directorLevel))
            .OrderBy(x => x.FullName)
            .Select(x =>
            {
                var headCount = units.Count(u => u.DirectorUserId == x.UserId);
                var concurrent = headCount > 0 ? $" - đang đứng đầu {headCount} đơn vị" : string.Empty;
                return new Item { Id = x.UserId, Name = $"{x.FullName} ({x.Username}) - {x.Role}{concurrent}" };
            }));
        _cmbDirector.DropDownStyle = ComboBoxStyle.DropDownList; _cmbDirector.DataSource = directorItems; _cmbDirector.DisplayMember = nameof(Item.Name); _cmbDirector.ValueMember = nameof(Item.Id);
        Add(card, 28, "Code", _txtCode); Add(card, 82, "Tên đơn vị", _txtName); Add(card, 136, "Loại đơn vị", _cmbUnitType); Add(card, 190, "Đơn vị cấp trên", _cmbParent); Add(card, 244, "Director / Head", _cmbDirector); Add(card, 298, "Thứ tự hiển thị", _numSortOrder);
        _chkActive.Text = "Đơn vị đang hoạt động"; _chkActive.SetBounds(190, 354, 240, 24); card.Controls.Add(_chkActive);
        var ok = new Button { Text = "Lưu", DialogResult = DialogResult.OK, Size = new Size(104, 38), Location = new Point(402, 548) }; UiTheme.StylePrimaryButton(ok);
        var cancel = new Button { Text = "Hủy", DialogResult = DialogResult.Cancel, Size = new Size(104, 38), Location = new Point(518, 548) }; UiTheme.StyleSecondaryButton(cancel);
        Controls.AddRange([card, ok, cancel, header]); AcceptButton = ok; CancelButton = cancel;
        if (unit is not null) { _txtCode.Text = unit.UnitCode; _txtName.Text = unit.UnitName; _cmbUnitType.SelectedValue = unit.UnitType; _cmbParent.SelectedValue = unit.ParentBusinessUnitId ?? 0; _cmbDirector.SelectedValue = unit.DirectorUserId ?? 0; _numSortOrder.Value = Math.Min(_numSortOrder.Maximum, Math.Max(_numSortOrder.Minimum, unit.SortOrder)); _chkActive.Checked = unit.IsActive; } else { _cmbUnitType.SelectedValue = OrganizationUnitTypes.Division; _chkActive.Checked = true; }
    }

    private static void Add(Panel card, int top, string label, Control control)
    {
        var l = UiTheme.CreateFieldLabel(label); l.SetBounds(28, top, 150, 24); control.SetBounds(190, top - 2, 372, 32); UiTheme.StyleInput(control); card.Controls.AddRange([l, control]);
    }
    private sealed record UnitTypeItem(string Value, string DisplayName);
    private sealed class Item { public int Id { get; set; } public string Name { get; set; } = string.Empty; }
}
