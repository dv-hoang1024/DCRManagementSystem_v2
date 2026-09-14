using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;

namespace DCRManagementSystem.Forms;

public sealed class UserManagementForm : Form
{
    private readonly DataGridView _grid = new();
    private readonly TextBox _txtSearch = new();
    private readonly Label _lblCount = new();
    private List<UserGridRow> _rows = new();

    public UserManagementForm()
    {
        if (!CurrentUser.IsAdmin) throw new UnauthorizedAccessException("Chỉ Administrator được quản lý Users.");
        Text = "Quản lý người dùng"; StartPosition = FormStartPosition.CenterParent; Size = new Size(1420, 760); MinimumSize = new Size(1180, 640); UiTheme.ApplyForm(this); BuildUi(); Shown += async (_, _) => await RefreshAsync();
    }

    private void BuildUi()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 102, BackColor = Color.White };
        var title = UiTheme.CreatePageTitle("Quản lý người dùng"); title.Location = new Point(28, 17);
        var subtitle = UiTheme.CreatePageSubtitle("Quyền Production Tracking / Warehouse Management được cấp riêng cho từng user, không phụ thuộc Phòng ban hoặc Role."); subtitle.Location = new Point(30, 59);
        var add = new Button { Text = "+  Thêm người dùng", Size = new Size(154, 38), Anchor = AnchorStyles.Top | AnchorStyles.Right }; UiTheme.StylePrimaryButton(add); add.Click += async (_, _) => await EditUserAsync(null);
        var close = new Button { Text = "Đóng", Size = new Size(86, 38), Anchor = AnchorStyles.Top | AnchorStyles.Right }; UiTheme.StyleSecondaryButton(close); close.Click += (_, _) => Close();
        header.Resize += (_, _) => { add.Location = new Point(header.ClientSize.Width - add.Width - 28, 31); close.Location = new Point(add.Left - close.Width - 10, 31); }; header.Controls.AddRange([title, subtitle, close, add]);

        var body = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Background, Padding = new Padding(28, 24, 28, 28) };
        var filter = UiTheme.CreateCard(); filter.Dock = DockStyle.Top; filter.Height = 76;
        var sl = new Label { Text = "Tìm kiếm:", AutoSize = true, Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold), ForeColor = UiTheme.TextSecondary, Location = new Point(18, 22) };
        _txtSearch.SetBounds(103, 20, 390, 34); _txtSearch.PlaceholderText = " Username, tên, email, Khối, phòng ban, role..."; UiTheme.StyleInput(_txtSearch); _txtSearch.TextChanged += (_, _) => ApplyFilter();
        _lblCount.AutoSize = false; _lblCount.Size = new Size(180, 34); _lblCount.TextAlign = ContentAlignment.MiddleRight; _lblCount.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold); _lblCount.ForeColor = UiTheme.TextSecondary; _lblCount.Anchor = AnchorStyles.Top | AnchorStyles.Right; filter.Resize += (_, _) => _lblCount.Location = new Point(filter.ClientSize.Width - _lblCount.Width - 18, 20);
        filter.Controls.AddRange([sl, _txtSearch, _lblCount]);

        var card = UiTheme.CreateCard(); card.Dock = DockStyle.Fill;
        var bar = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Color.White };
        var bt = new Label { Text = "Danh sách người dùng", AutoSize = true, Location = new Point(18, 18), Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold), ForeColor = UiTheme.TextPrimary };
        var del = new Button { Text = "Xóa", Size = new Size(86, 34), Anchor = AnchorStyles.Top | AnchorStyles.Right }; UiTheme.StyleDangerButton(del); del.Click += async (_, _) => { if (_grid.CurrentRow?.DataBoundItem is UserGridRow r) await DeleteUserAsync(r); };
        var edit = new Button { Text = "Sửa", Size = new Size(86, 34), Anchor = AnchorStyles.Top | AnchorStyles.Right }; UiTheme.StyleSecondaryButton(edit); edit.Click += async (_, _) => { if (_grid.CurrentRow?.DataBoundItem is UserGridRow r) await EditUserAsync(r.Source); };
        var savePermissions = new Button { Text = "Lưu quyền", Size = new Size(104, 34), Anchor = AnchorStyles.Top | AnchorStyles.Right }; UiTheme.StylePrimaryButton(savePermissions); savePermissions.Click += async (_, _) => await SaveModulePermissionsAsync();
        var refresh = new Button { Text = "Làm mới", Size = new Size(92, 34), Anchor = AnchorStyles.Top | AnchorStyles.Right }; UiTheme.StyleSecondaryButton(refresh); refresh.Click += async (_, _) => await RefreshAsync();
        bar.Resize += (_, _) =>
        {
            del.Location = new Point(bar.ClientSize.Width - del.Width - 18, 12);
            edit.Location = new Point(del.Left - edit.Width - 10, 12);
            savePermissions.Location = new Point(edit.Left - savePermissions.Width - 10, 12);
            refresh.Location = new Point(savePermissions.Left - refresh.Width - 10, 12);
        };
        bar.Controls.AddRange([bt, refresh, savePermissions, edit, del]);

        _grid.Dock = DockStyle.Fill; UiTheme.ConfigureGrid(_grid, false);
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _grid.Columns.Add(FixedColumn(nameof(UserGridRow.Username), "Username", 105));
        _grid.Columns.Add(FixedColumn(nameof(UserGridRow.FullName), "Họ và tên", 165));
        _grid.Columns.Add(FixedColumn(nameof(UserGridRow.Email), "Email", 180));

        // Đặt quyền module ở giữa grid, luôn nhìn thấy ở độ rộng form mặc định.
        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            DataPropertyName = nameof(UserGridRow.CanUseProductionTracking),
            HeaderText = "Production",
            ToolTipText = "Cho phép sử dụng Production Tracking",
            Width = 105,
            MinimumWidth = 90,
            ReadOnly = false,
            SortMode = DataGridViewColumnSortMode.Automatic
        });
        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            DataPropertyName = nameof(UserGridRow.CanUseWarehouseManagement),
            HeaderText = "Warehouse",
            ToolTipText = "Cho phép sử dụng Warehouse Management",
            Width = 105,
            MinimumWidth = 90,
            ReadOnly = false,
            SortMode = DataGridViewColumnSortMode.Automatic
        });

        _grid.Columns.Add(FixedColumn(nameof(UserGridRow.BusinessUnit), "Khối", 130));
        _grid.Columns.Add(FixedColumn(nameof(UserGridRow.Department), "Phòng ban", 145));
        _grid.Columns.Add(FixedColumn(nameof(UserGridRow.DirectManager), "Quản lý trực tiếp", 155));
        _grid.Columns.Add(FixedColumn(nameof(UserGridRow.Role), "Role", 105));
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = nameof(UserGridRow.IsActive), HeaderText = "Hoạt động", Width = 75, MinimumWidth = 70, ReadOnly = true });
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0 && _grid.Rows[e.RowIndex].DataBoundItem is UserGridRow r) await EditUserAsync(r.Source); };
        MaximumSize = new Size(1920, 1080);
        card.Controls.Add(_grid); card.Controls.Add(UiTheme.CreateDivider()); card.Controls.Add(bar); body.Controls.Add(card); body.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 18, BackColor = UiTheme.Background }); body.Controls.Add(filter); Controls.Add(body); Controls.Add(UiTheme.CreateDivider()); Controls.Add(header);
    }

    private async Task RefreshAsync()
    {
        try
        {
            UseWaitCursor = true;
            var users = await AppServices.CreateAdminService().GetUsersAsync();
            _rows = users.Select(x => new UserGridRow
            {
                Source = x,
                UserId = x.UserId,
                Username = x.Username,
                FullName = x.FullName,
                Email = x.Email,
                BusinessUnit = JoinNames(
                    x.BusinessUnitAssignments.Select(a => a.BusinessUnit?.UnitName),
                    x.BusinessUnit?.UnitName ?? x.Department?.BusinessUnit?.UnitName),
                Department = JoinNames(
                    x.DepartmentAssignments.Select(a => a.Department?.DepartmentName),
                    x.Department?.DepartmentName),
                DirectManager = x.DirectManager?.FullName ?? string.Empty,
                Role = x.Role,
                CanUseProductionTracking = x.CanUseProductionTracking,
                CanUseWarehouseManagement = x.CanUseWarehouseManagement,
                OriginalCanUseProductionTracking = x.CanUseProductionTracking,
                OriginalCanUseWarehouseManagement = x.CanUseWarehouseManagement,
                IsActive = x.IsActive
            }).ToList();
            ApplyFilter();
        }
        catch (Exception ex) { DCRManagementSystem.Helpers.UiMessageBox.Show(this, ex.Message, "User Management", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { UseWaitCursor = false; }
    }

    private void ApplyFilter()
    {
        var k = _txtSearch.Text.Trim();
        var data = string.IsNullOrWhiteSpace(k) ? _rows : _rows.Where(x => x.Username.Contains(k, StringComparison.OrdinalIgnoreCase) || x.FullName.Contains(k, StringComparison.OrdinalIgnoreCase) || x.Email.Contains(k, StringComparison.OrdinalIgnoreCase) || x.BusinessUnit.Contains(k, StringComparison.OrdinalIgnoreCase) || x.Department.Contains(k, StringComparison.OrdinalIgnoreCase) || x.DirectManager.Contains(k, StringComparison.OrdinalIgnoreCase) || x.Role.Contains(k, StringComparison.OrdinalIgnoreCase)).ToList();
        _grid.DataSource = data; _lblCount.Text = $"{data.Count:N0} người dùng";
    }

    private async Task SaveModulePermissionsAsync()
    {
        try
        {
            if (_grid.IsCurrentCellDirty)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            _grid.EndEdit();
            Validate();
            if (_grid.DataSource is not null)
                BindingContext[_grid.DataSource]?.EndCurrentEdit();
            UseWaitCursor = true;
            var service = AppServices.CreateAdminService();

            // Lưu toàn bộ trạng thái đang hiển thị thay vì chỉ dựa vào so sánh Original.
            // Cách này tránh mất lần click checkbox cuối cùng khi người dùng bấm Lưu quyền ngay.
            foreach (var row in _rows)
                await service.SaveUserModulePermissionsAsync(row.UserId, row.CanUseProductionTracking, row.CanUseWarehouseManagement);

            DCRManagementSystem.Helpers.UiMessageBox.Show(
                this,
                $"Đã xác nhận và lưu quyền module cho {_rows.Count:N0} người dùng.",
                "Quyền GGP Control",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            DCRManagementSystem.Helpers.UiMessageBox.Show(this, ex.Message, "Lưu quyền module", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { UseWaitCursor = false; }
    }

    private async Task EditUserAsync(User? user)
    {
        try
        {
            var service = AppServices.CreateAdminService();
            if (user is not null)
            {
                user = (await service.GetUsersAsync()).SingleOrDefault(x => x.UserId == user.UserId)
                    ?? throw new InvalidOperationException("Người dùng không còn tồn tại. Hãy làm mới danh sách.");
            }
            var departments = await service.GetDepartmentsAsync(false);
            var units = await service.GetBusinessUnitsAsync(false);
            var roles = await service.GetRolesAsync(false);
            var users = await service.GetUsersAsync();
            using var dialog = new UserEditForm(user, units, departments, roles, users);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            await service.SaveUserAsync(
                user?.UserId,
                dialog.Username,
                dialog.FullName,
                dialog.Email,
                dialog.Phone,
                dialog.BusinessUnitId,
                dialog.DepartmentId,
                dialog.BusinessUnitIds,
                dialog.DepartmentIds,
                dialog.DirectManagerUserId,
                dialog.Role,
                dialog.IsActive,
                dialog.NewPassword);
            await RefreshAsync();
        }
        catch (Exception ex) { DCRManagementSystem.Helpers.UiMessageBox.Show(this, ex.Message, "Save User", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private async Task DeleteUserAsync(UserGridRow row)
    {
        if (CurrentUser.User is null) return;
        if (DCRManagementSystem.Helpers.UiMessageBox.Show(this, $"Xóa người dùng '{row.FullName}' ({row.Username})?\r\n\r\nTài khoản sẽ bị loại khỏi hệ thống nhưng lịch sử DCR/Audit vẫn được giữ.", "Xóa người dùng", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try { await AppServices.CreateAdminService().DeleteUserAsync(row.UserId, CurrentUser.User.UserId); await RefreshAsync(); }
        catch (Exception ex) { DCRManagementSystem.Helpers.UiMessageBox.Show(this, ex.Message, "Xóa người dùng", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private static DataGridViewTextBoxColumn Column(string p, string h, float w) => new() { DataPropertyName = p, HeaderText = h, FillWeight = w, ReadOnly = true };
    private static DataGridViewTextBoxColumn FixedColumn(string p, string h, int width) => new() { DataPropertyName = p, HeaderText = h, Width = width, MinimumWidth = Math.Min(width, 70), ReadOnly = true };
    private static string JoinNames(IEnumerable<string?> assignedNames, string? legacyName)
    {
        var names = assignedNames
            .Append(legacyName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();
        return string.Join(", ", names);
    }
    private sealed class UserGridRow
    {
        public User Source { get; set; } = new(); public int UserId { get; set; } public string Username { get; set; } = string.Empty; public string FullName { get; set; } = string.Empty; public string Email { get; set; } = string.Empty; public string BusinessUnit { get; set; } = string.Empty; public string Department { get; set; } = string.Empty; public string DirectManager { get; set; } = string.Empty; public string Role { get; set; } = string.Empty; public bool CanUseProductionTracking { get; set; } public bool CanUseWarehouseManagement { get; set; } public bool OriginalCanUseProductionTracking { get; set; } public bool OriginalCanUseWarehouseManagement { get; set; } public bool IsActive { get; set; }
    }
}

internal sealed class UserEditForm : Form
{
    private readonly TextBox _txtUsername = new(); private readonly TextBox _txtFullName = new(); private readonly TextBox _txtEmail = new(); private readonly TextBox _txtPhone = new(); private readonly ComboBox _cmbBusinessUnit = new(); private readonly ComboBox _cmbDepartment = new(); private readonly ComboBox _cmbDirectManager = new(); private readonly ComboBox _cmbRole = new(); private readonly CheckBox _chkActive = new(); private readonly TextBox _txtPassword = new(); private readonly Label _lblOrgHint = new();
    private readonly User? _editingUser; private readonly List<BusinessUnit> _units; private readonly List<Department> _departments; private readonly List<RoleDefinition> _roles; private readonly List<User> _users; private bool _initializing;

    public string Username => _txtUsername.Text.Trim(); public string FullName => _txtFullName.Text.Trim(); public string Email => _txtEmail.Text.Trim(); public string Phone => _txtPhone.Text.Trim();
    public int? BusinessUnitId => _cmbBusinessUnit.SelectedValue is int id && id > 0 ? id : null;
    public int? DepartmentId => _cmbDepartment.SelectedValue is int id && id > 0 ? id : null;
    // The concurrent-assignment controls are intentionally hidden. Preserve existing
    // active assignments while editing so this simplified form never deletes old data.
    public IReadOnlyCollection<int> BusinessUnitIds => PreservedBusinessUnitIds();
    public IReadOnlyCollection<int> DepartmentIds => PreservedDepartmentIds();
    public int? DirectManagerUserId => _cmbDirectManager.SelectedValue is int id && id > 0 ? id : null;
    public string Role => _cmbRole.SelectedItem?.ToString() ?? RoleNames.Staff; public bool IsActive => _chkActive.Checked; public string? NewPassword => string.IsNullOrWhiteSpace(_txtPassword.Text) ? null : _txtPassword.Text;

    public UserEditForm(User? user, List<BusinessUnit> units, List<Department> departments, List<RoleDefinition> roles, List<User> users)
    {
        _editingUser = user; _units = units; _departments = departments; _roles = roles; _users = users;
        Text = user is null ? "Thêm người dùng" : "Sửa người dùng"; StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ClientSize = new Size(740, 724); UiTheme.ApplyForm(this); BuildUi();
    }

    private void BuildUi()
    {
        _initializing = true;
        var header = new Panel { Dock = DockStyle.Top, Height = 90, BackColor = Color.White };
        var title = new Label { Text = Text, AutoSize = true, Font = new Font("Segoe UI Semibold", 17F, FontStyle.Bold), ForeColor = UiTheme.TextPrimary, Location = new Point(28, 16) };
        var subtitle = UiTheme.CreatePageSubtitle("Thiết lập tài khoản, Role, đơn vị chính, phòng ban chính và quản lý trực tiếp."); subtitle.Location = new Point(30, 54); header.Controls.AddRange([title, subtitle]);
        var card = UiTheme.CreateCard(); card.SetBounds(28, 102, 684, 510);

        var currentUnitId = _editingUser?.BusinessUnitId ?? _editingUser?.Department?.BusinessUnitId;
        var unitItems = new List<BusinessUnit> { new() { Id = 0, UnitName = UiLanguageManager.T("(Không)", "(None)") } }; unitItems.AddRange(_units.Where(x => x.IsActive || x.Id == currentUnitId).OrderBy(x => x.UnitName)); _cmbBusinessUnit.DropDownStyle = ComboBoxStyle.DropDownList; _cmbBusinessUnit.DataSource = unitItems; _cmbBusinessUnit.DisplayMember = nameof(BusinessUnit.UnitName); _cmbBusinessUnit.ValueMember = nameof(BusinessUnit.Id);
        var deptItems = new List<Department> { new() { Id = 0, DepartmentName = UiLanguageManager.T("(Không)", "(None)") } }; deptItems.AddRange(_departments.Where(x => x.IsActive || x.Id == _editingUser?.DepartmentId).OrderBy(x => x.DepartmentName)); _cmbDepartment.DropDownStyle = ComboBoxStyle.DropDownList; _cmbDepartment.DataSource = deptItems; _cmbDepartment.DisplayMember = nameof(Department.DepartmentName); _cmbDepartment.ValueMember = nameof(Department.Id);
        _cmbRole.DropDownStyle = ComboBoxStyle.DropDownList; _cmbRole.Items.AddRange(_roles.Where(x => x.IsActive || x.RoleName == _editingUser?.Role).OrderByDescending(x => x.HierarchyLevel).Select(x => (object)x.RoleName).ToArray());
        _cmbDirectManager.DropDownStyle = ComboBoxStyle.DropDownList;
        _txtPassword.UseSystemPasswordChar = true; _chkActive.Text = "Cho phép đăng nhập";

        Add(card, 14, "Username", _txtUsername); Add(card, 58, "Full Name", _txtFullName); Add(card, 102, "Email", _txtEmail); Add(card, 146, "Phone", _txtPhone); Add(card, 190, "Role", _cmbRole); Add(card, 234, "Khối chính", _cmbBusinessUnit);
        Add(card, 278, "Phòng ban chính", _cmbDepartment);
        Add(card, 322, "Direct Manager", _cmbDirectManager); Add(card, 366, "New Password", _txtPassword);
        _lblOrgHint.SetBounds(190, 404, 455, 52); _lblOrgHint.ForeColor = UiTheme.TextSecondary; _lblOrgHint.Font = new Font("Segoe UI", 8.7F); card.Controls.Add(_lblOrgHint);
        _chkActive.SetBounds(190, 466, 210, 24); card.Controls.Add(_chkActive);

        var hint = new Label { Text = "Staff bắt buộc có ít nhất một Phòng ban và một quản lý trực tiếp. Manager/Director của đơn vị có thể để trống khi vị trí chưa được bổ nhiệm.", AutoSize = false, Size = new Size(675, 34), ForeColor = UiTheme.TextSecondary, Font = new Font("Segoe UI", 8.8F), Location = new Point(30, 622) };
        var ok = new Button { Text = "Lưu", DialogResult = DialogResult.OK, Size = new Size(104, 38), Location = new Point(492, 670) }; UiTheme.StylePrimaryButton(ok);
        var cancel = new Button { Text = "Hủy", DialogResult = DialogResult.Cancel, Size = new Size(104, 38), Location = new Point(608, 670) }; UiTheme.StyleSecondaryButton(cancel);
        Controls.AddRange([card, hint, ok, cancel, header]); AcceptButton = ok; CancelButton = cancel;

        if (_editingUser is not null)
        {
            _txtUsername.Text = _editingUser.Username; _txtFullName.Text = _editingUser.FullName; _txtEmail.Text = _editingUser.Email; _txtPhone.Text = _editingUser.Phone; _cmbRole.SelectedItem = _editingUser.Role; _cmbBusinessUnit.SelectedValue = _editingUser.BusinessUnitId ?? _editingUser.Department?.BusinessUnitId ?? 0; _cmbDepartment.SelectedValue = _editingUser.DepartmentId ?? 0; _chkActive.Checked = _editingUser.IsActive;
        }
        else { _cmbRole.SelectedItem = RoleNames.Staff; _chkActive.Checked = true; }
        MaximumSize = new Size(1920, 1080);
        _cmbRole.SelectedIndexChanged += (_, _) => { if (!_initializing) UpdateOrganizationUi(); };
        _cmbDepartment.SelectedIndexChanged += (_, _) => { if (!_initializing) { SyncBusinessUnitFromDepartment(); UpdateOrganizationUi(); } };
        _cmbBusinessUnit.SelectedIndexChanged += (_, _) => { if (!_initializing) UpdateOrganizationUi(); };
        _initializing = false; UpdateOrganizationUi(_editingUser?.DirectManagerUserId);
    }

    private void SyncBusinessUnitFromDepartment()
    {
        if (_cmbDepartment.SelectedValue is not int deptId || deptId <= 0) return;
        var dept = _departments.FirstOrDefault(x => x.Id == deptId); if (dept?.BusinessUnitId is int unitId) _cmbBusinessUnit.SelectedValue = unitId;
    }

    private void UpdateOrganizationUi(int? preferredManagerId = null)
    {
        if (_initializing) return;
        _initializing = true;
        try
        {
        var roleName = Role; var role = _roles.FirstOrDefault(x => x.RoleName == roleName); var managerLevel = _roles.FirstOrDefault(x => x.RoleName == RoleNames.Manager)?.HierarchyLevel ?? 20; var directorLevel = _roles.FirstOrDefault(x => x.RoleName == RoleNames.Director)?.HierarchyLevel ?? 30; var level = role?.HierarchyLevel ?? 0;
        var isAdmin = string.Equals(roleName, RoleNames.Administrator, StringComparison.OrdinalIgnoreCase); var isStaff = string.Equals(roleName, RoleNames.Staff, StringComparison.OrdinalIgnoreCase); var isDepartmentLeadership = !isAdmin && level >= managerLevel && level < directorLevel; var isBlockLeadership = !isAdmin && level >= directorLevel;

        if (isStaff || isDepartmentLeadership)
        {
            _cmbDepartment.Enabled = true; SyncBusinessUnitFromDepartment(); _cmbBusinessUnit.Enabled = false; _cmbDirectManager.Enabled = true;
            _lblOrgHint.Text = isStaff ? "Staff phải chọn Direct Manager cấp cao hơn và cùng ít nhất một Phòng ban; hệ thống ưu tiên Head của Phòng ban chính." : "Khi tạo tài khoản mới, nếu Phòng ban chưa có Head thì người này sẽ tự động được bổ nhiệm.";
        }
        else if (isBlockLeadership)
        {
            _cmbDepartment.SelectedValue = 0; _cmbDepartment.Enabled = false; _cmbBusinessUnit.Enabled = true; _cmbDirectManager.Enabled = true; _lblOrgHint.Text = "Khi tạo tài khoản mới, nếu Đơn vị/Khối chưa có Head thì người này sẽ tự động được bổ nhiệm.";
        }
        else { _cmbDepartment.Enabled = true; _cmbBusinessUnit.Enabled = true; _cmbDirectManager.Enabled = true; _lblOrgHint.Text = "Role tùy chỉnh: chọn phạm vi tổ chức và Direct Manager phù hợp với hierarchy level."; }

        var unitId = _cmbBusinessUnit.SelectedValue is int uid && uid > 0 ? uid : (int?)null; var deptId = _cmbDepartment.SelectedValue is int did && did > 0 ? did : (int?)null;
        int? automaticManagerId = null;
        if (isStaff && deptId.HasValue)
            automaticManagerId = _departments.FirstOrDefault(x => x.Id == deptId.Value)?.ManagerUserId;
        else if (isDepartmentLeadership && unitId.HasValue)
            automaticManagerId = FindNearestUnitHeadId(unitId.Value);

        var items = new List<ManagerItem> { new() { Id = 0, Name = isStaff ? UiLanguageManager.T("(Phòng ban chưa gán Head)", "(Department Head not assigned)") : isDepartmentLeadership ? UiLanguageManager.T("(Đơn vị chưa gán Head - vẫn được phép lưu)", "(Organization Head not assigned - saving is allowed)") : UiLanguageManager.T("(Không gán)", "(None)") } };
        var candidates = _users
            .Where(x => x.IsActive && !x.IsDeleted && (_editingUser is null || x.UserId != _editingUser.UserId))
            .Where(x => (_roles.FirstOrDefault(r => r.RoleName == x.Role)?.HierarchyLevel ?? 0) > level);
        // Director and higher roles may report to any higher executive, including one in another Khối.
        items.AddRange(candidates.OrderBy(x => _roles.FirstOrDefault(r => r.RoleName == x.Role)?.HierarchyLevel ?? 0).ThenBy(x => x.FullName).Select(x => new ManagerItem { Id = x.UserId, Name = $"{x.FullName} ({x.Username}) - {x.Role}" }));
        if (_editingUser?.DirectManagerUserId is int currentManagerId && items.All(x => x.Id != currentManagerId))
        {
            var currentManager = _users.FirstOrDefault(x => x.UserId == currentManagerId);
            if (currentManager is not null)
                items.Add(new ManagerItem { Id = currentManager.UserId, Name = $"{currentManager.FullName} ({currentManager.Username}) - {currentManager.Role}" });
        }
        _cmbDirectManager.DataSource = items; _cmbDirectManager.DisplayMember = nameof(ManagerItem.Name); _cmbDirectManager.ValueMember = nameof(ManagerItem.Id);
        var target = preferredManagerId ?? _editingUser?.DirectManagerUserId ?? automaticManagerId;
        if (target.HasValue && items.Any(x => x.Id == target.Value)) _cmbDirectManager.SelectedValue = target.Value; else _cmbDirectManager.SelectedValue = 0;
        }
        finally
        {
            _initializing = false;
        }
    }

    private int? FindNearestUnitHeadId(int businessUnitId)
    {
        var visited = new HashSet<int>();
        int? currentId = businessUnitId;
        while (currentId.HasValue && visited.Add(currentId.Value))
        {
            var unit = _units.FirstOrDefault(x => x.Id == currentId.Value && x.IsActive);
            if (unit is null) return null;
            if (unit.DirectorUserId.HasValue) return unit.DirectorUserId;
            currentId = unit.ParentBusinessUnitId;
        }

        return null;
    }

    private static void Add(Panel card, int top, string label, Control control) { var l = UiTheme.CreateFieldLabel(label); l.SetBounds(28, top, 150, 24); control.SetBounds(190, top - 2, 420, 32); UiTheme.StyleInput(control); card.Controls.AddRange([l, control]); }

    private IReadOnlyCollection<int> PreservedBusinessUnitIds()
    {
        var activeIds = _units.Where(x => x.IsActive).Select(x => x.Id).ToHashSet();
        var ids = _editingUser?.BusinessUnitAssignments
            .Select(x => x.BusinessUnitId)
            .Where(activeIds.Contains)
            .ToHashSet() ?? new HashSet<int>();
        if (BusinessUnitId.HasValue) ids.Add(BusinessUnitId.Value);
        return ids.ToArray();
    }

    private IReadOnlyCollection<int> PreservedDepartmentIds()
    {
        var activeIds = _departments.Where(x => x.IsActive).Select(x => x.Id).ToHashSet();
        var ids = _editingUser?.DepartmentAssignments
            .Select(x => x.DepartmentId)
            .Where(activeIds.Contains)
            .ToHashSet() ?? new HashSet<int>();
        if (DepartmentId.HasValue) ids.Add(DepartmentId.Value);
        return ids.ToArray();
    }
    private sealed class ManagerItem { public int Id { get; set; } public string Name { get; set; } = string.Empty; }
}
