using System.ComponentModel;
using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;
using DCRManagementSystem.Services;

namespace DCRManagementSystem.Forms;

/// <summary>
/// Manages named, rank-specific approval-plan templates. Legacy workflow stages
/// and Approval Matrix remain the fallback for DCRs without a custom plan.
/// </summary>
public sealed class WorkflowConfigForm : Form
{
    private readonly ComboBox _cmbTemplates = new();
    private readonly TextBox _txtName = new();
    private readonly ComboBox _cmbRank = new();
    private readonly CheckBox _chkDefault = new();
    private readonly CheckBox _chkActive = new();
    private readonly TextBox _txtSearch = new();
    private readonly ComboBox _cmbApprovers = new();
    private readonly NumericUpDown _numLevel = new();
    private readonly TextBox _txtLevelName = new();
    private readonly DataGridView _grid = new();
    private readonly Label _lblState = new();
    private readonly BindingList<ApprovalPlanTemplateEditModel> _templates = new();
    private readonly BindingList<ApprovalPlanEditItem> _entries = new();
    private readonly BindingList<ApproverSearchItem> _approvers = new();
    private readonly IAdminService _adminService = AppServices.CreateAdminService();
    private readonly IDcrService _dcrService = AppServices.CreateDcrService();
    private int _templateId;

    public WorkflowConfigForm()
    {
        if (!CurrentUser.IsAdmin)
            throw new UnauthorizedAccessException("Chỉ Administrator được cấu hình Workflow.");

        Text = "Cấu hình luồng phê duyệt";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1180, 760);
        MinimumSize = new Size(1040, 650);
        MaximumSize = new Size(1920, 1080);
        UiTheme.ApplyForm(this);
        BuildUi();
        Shown += async (_, _) => await LoadAsync();
    }

    private void BuildUi()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 96, BackColor = Color.White };
        var title = UiTheme.CreatePageTitle("Cấu hình luồng phê duyệt theo Rank");
        title.Location = new Point(28, 15);
        var subtitle = UiTheme.CreatePageSubtitle("Tạo nhiều mẫu Rank A/B/C/S. Mẫu mặc định được tự nạp khi tạo DCR và người dùng vẫn có thể tùy biến.");
        subtitle.Location = new Point(30, 57);
        var btnClose = CreateButton("Đóng", 84, false);
        btnClose.Click += (_, _) => Close();
        header.Resize += (_, _) => btnClose.Location = new Point(header.ClientSize.Width - btnClose.Width - 28, 29);
        header.Controls.AddRange([title, subtitle, btnClose]);

        var editor = new Panel { Dock = DockStyle.Top, Height = 158, BackColor = Color.White, Padding = new Padding(28, 16, 28, 14) };
        AddLabel(editor, "Cấu hình", 28, 14, 130);
        _cmbTemplates.Location = new Point(28, 40);
        _cmbTemplates.Size = new Size(320, 30);
        _cmbTemplates.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbTemplates.DataSource = _templates;
        _cmbTemplates.DisplayMember = nameof(ApprovalPlanTemplateEditModel.DisplayName);
        UiTheme.StyleInput(_cmbTemplates);
        _cmbTemplates.SelectedIndexChanged += (_, _) => LoadSelectedTemplate();

        AddLabel(editor, "Tên cấu hình", 370, 14, 180);
        _txtName.Location = new Point(370, 40);
        _txtName.Size = new Size(310, 30);
        UiTheme.StyleInput(_txtName);

        AddLabel(editor, "Rank", 700, 14, 80);
        _cmbRank.Location = new Point(700, 40);
        _cmbRank.Size = new Size(90, 30);
        _cmbRank.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbRank.Items.AddRange(DcrRanks.All.Cast<object>().ToArray());
        _cmbRank.SelectedItem = DcrRanks.C;
        UiTheme.StyleInput(_cmbRank);

        _chkDefault.Text = "Mặc định của Rank";
        _chkDefault.AutoSize = true;
        _chkDefault.Location = new Point(815, 44);
        _chkActive.Text = "Hoạt động";
        _chkActive.AutoSize = true;
        _chkActive.Checked = true;
        _chkActive.Location = new Point(960, 44);

        var btnNew = CreateButton("+ Tạo mới", 105, false);
        btnNew.Location = new Point(28, 96);
        btnNew.Click += (_, _) => NewTemplate();
        var btnSave = CreateButton("Lưu cấu hình", 125, true);
        btnSave.Location = new Point(143, 96);
        btnSave.Click += async (_, _) => await SaveAsync();
        var btnDelete = CreateButton("Xóa cấu hình", 125, false);
        btnDelete.Location = new Point(278, 96);
        btnDelete.ForeColor = UiTheme.Danger;
        btnDelete.Click += async (_, _) => await DeleteAsync();
        _lblState.AutoSize = false;
        _lblState.Location = new Point(430, 96);
        _lblState.Size = new Size(620, 34);
        _lblState.TextAlign = ContentAlignment.MiddleLeft;
        _lblState.ForeColor = UiTheme.TextSecondary;
        editor.Controls.AddRange([_cmbTemplates, _txtName, _cmbRank, _chkDefault, _chkActive, btnNew, btnSave, btnDelete, _lblState]);

        var body = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Background, Padding = new Padding(28, 18, 28, 28) };
        var card = UiTheme.CreateCard();
        card.Dock = DockStyle.Fill;
        var tools = new Panel { Dock = DockStyle.Top, Height = 125, BackColor = Color.White };
        AddLabel(tools, "Tìm người duyệt (tên / email / username / phòng ban)", 18, 12, 380);
        _txtSearch.Location = new Point(18, 39);
        _txtSearch.Size = new Size(300, 30);
        UiTheme.StyleInput(_txtSearch);
        var btnSearch = CreateButton("Tìm", 70, false);
        btnSearch.Location = new Point(328, 38);
        btnSearch.Click += async (_, _) => await SearchAsync();
        _txtSearch.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            await SearchAsync();
        };
        AddLabel(tools, "Kết quả", 415, 12, 100);
        _cmbApprovers.Location = new Point(415, 39);
        _cmbApprovers.Size = new Size(520, 30);
        _cmbApprovers.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbApprovers.DataSource = _approvers;
        _cmbApprovers.DisplayMember = nameof(ApproverSearchItem.DisplayText);
        UiTheme.StyleInput(_cmbApprovers);

        AddLabel(tools, "Cấp", 18, 77, 65);
        _numLevel.Location = new Point(70, 82);
        _numLevel.Size = new Size(70, 30);
        _numLevel.Minimum = 1;
        _numLevel.Maximum = 99;
        AddLabel(tools, "Tên cấp", 160, 77, 80);
        _txtLevelName.Location = new Point(235, 82);
        _txtLevelName.Size = new Size(270, 30);
        _txtLevelName.Text = "Cấp phê duyệt 1";
        UiTheme.StyleInput(_txtLevelName);
        _numLevel.ValueChanged += (_, _) =>
        {
            var level = (int)_numLevel.Value;
            _txtLevelName.Text = _entries.FirstOrDefault(x => x.LevelNumber == level)?.LevelName ?? $"Cấp phê duyệt {level}";
        };
        var btnAdd = CreateButton("Thêm người duyệt", 145, true);
        btnAdd.Location = new Point(525, 80);
        btnAdd.Click += (_, _) => AddApprover();
        var btnNext = CreateButton("+ Cấp mới", 105, false);
        btnNext.Location = new Point(680, 80);
        btnNext.Click += (_, _) => _numLevel.Value = Math.Min(_numLevel.Maximum, _entries.Select(x => x.LevelNumber).DefaultIfEmpty(0).Max() + 1);
        var btnRemove = CreateButton("Xóa dòng", 105, false);
        btnRemove.Location = new Point(795, 80);
        btnRemove.ForeColor = UiTheme.Danger;
        btnRemove.Click += (_, _) => RemoveSelected();
        tools.Controls.AddRange([_txtSearch, btnSearch, _cmbApprovers, _numLevel, _txtLevelName, btnAdd, btnNext, btnRemove]);

        ConfigureGrid();
        card.Controls.Add(_grid);
        card.Controls.Add(UiTheme.CreateDivider());
        card.Controls.Add(tools);
        body.Controls.Add(card);

        Controls.Add(body);
        Controls.Add(UiTheme.CreateDivider());
        Controls.Add(editor);
        Controls.Add(UiTheme.CreateDivider());
        Controls.Add(header);
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        UiTheme.ConfigureGrid(_grid);
        _grid.AutoGenerateColumns = false;
        _grid.DataSource = _entries;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Cấp", DataPropertyName = nameof(ApprovalPlanEditItem.LevelNumber), Width = 65 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tên cấp", DataPropertyName = nameof(ApprovalPlanEditItem.LevelName), Width = 210 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Người phê duyệt", DataPropertyName = nameof(ApprovalPlanEditItem.ApproverName), Width = 190 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Email", DataPropertyName = nameof(ApprovalPlanEditItem.ApproverEmail), Width = 220 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Role", DataPropertyName = nameof(ApprovalPlanEditItem.ApproverRole), Width = 120 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Khối", DataPropertyName = nameof(ApprovalPlanEditItem.BusinessUnitName), Width = 160 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Phòng ban", DataPropertyName = nameof(ApprovalPlanEditItem.DepartmentName), Width = 170 });
    }

    private async Task LoadAsync(int? selectId = null)
    {
        try
        {
            UseWaitCursor = true;
            _lblState.Text = "Đang tải cấu hình...";
            var rows = await _adminService.GetApprovalPlanTemplatesAsync();
            _templates.RaiseListChangedEvents = false;
            _templates.Clear();
            foreach (var row in rows) _templates.Add(row);
            _templates.RaiseListChangedEvents = true;
            _templates.ResetBindings();
            if (_templates.Count == 0)
                NewTemplate();
            else
                _cmbTemplates.SelectedItem = _templates.FirstOrDefault(x => x.Id == selectId) ?? _templates[0];
            _lblState.Text = $"{rows.Count:N0} cấu hình";
        }
        catch (Exception ex) { ShowError(ex, "Tải cấu hình"); }
        finally { UseWaitCursor = false; }
    }

    private void LoadSelectedTemplate()
    {
        if (_cmbTemplates.SelectedItem is not ApprovalPlanTemplateEditModel selected) return;
        _templateId = selected.Id;
        _txtName.Text = selected.Name;
        _cmbRank.SelectedItem = DcrRanks.Normalize(selected.Rank);
        _chkDefault.Checked = selected.IsDefault;
        _chkActive.Checked = selected.IsActive;
        SetEntries(selected.ApprovalPlan);
    }

    private void NewTemplate()
    {
        _cmbTemplates.SelectedIndex = -1;
        _templateId = 0;
        _txtName.Clear();
        _cmbRank.SelectedItem = DcrRanks.C;
        _chkDefault.Checked = !_templates.Any(x => x.Rank == DcrRanks.C && x.IsActive);
        _chkActive.Checked = true;
        SetEntries([]);
        _txtName.Focus();
        _lblState.Text = "Cấu hình mới - chưa lưu";
    }

    private void SetEntries(IEnumerable<ApprovalPlanEditItem> rows)
    {
        _entries.RaiseListChangedEvents = false;
        _entries.Clear();
        foreach (var row in rows.OrderBy(x => x.LevelNumber).ThenBy(x => x.Sequence)) _entries.Add(Clone(row));
        _entries.RaiseListChangedEvents = true;
        _entries.ResetBindings();
        _numLevel.Value = Math.Min(_numLevel.Maximum, Math.Max(1, _entries.Select(x => x.LevelNumber).DefaultIfEmpty(1).Max()));
    }

    private async Task SearchAsync()
    {
        try
        {
            var rows = await _dcrService.SearchApproversAsync(_txtSearch.Text, 100);
            _approvers.RaiseListChangedEvents = false;
            _approvers.Clear();
            foreach (var row in rows) _approvers.Add(row);
            _approvers.RaiseListChangedEvents = true;
            _approvers.ResetBindings();
            if (_approvers.Count > 0) _cmbApprovers.SelectedIndex = 0;
            _lblState.Text = $"Tìm thấy {_approvers.Count:N0} người dùng";
        }
        catch (Exception ex) { ShowError(ex, "Tìm người duyệt"); }
    }

    private void AddApprover()
    {
        if (_cmbApprovers.SelectedItem is not ApproverSearchItem user)
        {
            UiMessageBox.Show(this, "Hãy tìm và chọn người phê duyệt.", "Luồng phê duyệt", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_entries.Any(x => x.ApproverId == user.UserId))
        {
            UiMessageBox.Show(this, "Người này đã có trong cấu hình.", "Luồng phê duyệt", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var level = (int)_numLevel.Value;
        var levelName = string.IsNullOrWhiteSpace(_txtLevelName.Text) ? $"Cấp phê duyệt {level}" : _txtLevelName.Text.Trim();
        foreach (var row in _entries.Where(x => x.LevelNumber == level)) row.LevelName = levelName;
        _entries.Add(new ApprovalPlanEditItem
        {
            LevelNumber = level,
            LevelName = levelName,
            ApproverId = user.UserId,
            ApproverName = user.FullName,
            ApproverEmail = user.Email,
            ApproverRole = user.Role,
            BusinessUnitId = user.BusinessUnitId,
            BusinessUnitCode = user.BusinessUnitCode,
            BusinessUnitName = user.BusinessUnit,
            DepartmentId = user.DepartmentId,
            DepartmentCode = user.DepartmentCode,
            DepartmentName = user.Department,
            Sequence = _entries.Count(x => x.LevelNumber == level) + 1
        });
        _entries.ResetBindings();
    }

    private void RemoveSelected()
    {
        if (_grid.CurrentRow?.DataBoundItem is not ApprovalPlanEditItem selected) return;
        _entries.Remove(selected);
        Renumber();
    }

    private async Task SaveAsync()
    {
        try
        {
            UseWaitCursor = true;
            _lblState.Text = "Đang lưu...";
            var saved = await _adminService.SaveApprovalPlanTemplateAsync(new ApprovalPlanTemplateEditModel
            {
                Id = _templateId,
                Name = _txtName.Text,
                Rank = _cmbRank.SelectedItem?.ToString() ?? DcrRanks.C,
                IsDefault = _chkDefault.Checked,
                IsActive = _chkActive.Checked,
                ApprovalPlan = _entries.Select(Clone).ToList()
            });
            _lblState.ForeColor = UiTheme.Success;
            _lblState.Text = $"Đã lưu '{saved.Name}' cho Rank {saved.Rank}.";
            await LoadAsync(saved.Id);
        }
        catch (Exception ex)
        {
            _lblState.ForeColor = UiTheme.Danger;
            _lblState.Text = "Lưu thất bại";
            ShowError(ex, "Lưu cấu hình");
        }
        finally { UseWaitCursor = false; }
    }

    private async Task DeleteAsync()
    {
        if (_templateId <= 0) return;
        if (UiMessageBox.Show(this, $"Xóa cấu hình '{_txtName.Text}'? Các DCR đã lưu/submit không bị thay đổi.", "Xóa cấu hình", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        try
        {
            await _adminService.DeleteApprovalPlanTemplateAsync(_templateId);
            await LoadAsync();
        }
        catch (Exception ex) { ShowError(ex, "Xóa cấu hình"); }
    }

    private void Renumber()
    {
        foreach (var group in _entries.GroupBy(x => x.LevelNumber))
        {
            var sequence = 1;
            foreach (var row in group.OrderBy(x => x.Sequence)) row.Sequence = sequence++;
        }
        _entries.ResetBindings();
    }

    private static ApprovalPlanEditItem Clone(ApprovalPlanEditItem x) => new()
    {
        LevelNumber = x.LevelNumber,
        LevelName = x.LevelName,
        ApproverId = x.ApproverId,
        ApproverName = x.ApproverName,
        ApproverEmail = x.ApproverEmail,
        ApproverRole = x.ApproverRole,
        BusinessUnitId = x.BusinessUnitId,
        BusinessUnitCode = x.BusinessUnitCode,
        BusinessUnitName = x.BusinessUnitName,
        DepartmentId = x.DepartmentId,
        DepartmentCode = x.DepartmentCode,
        DepartmentName = x.DepartmentName,
        Sequence = x.Sequence
    };

    private static void AddLabel(Control parent, string text, int x, int y, int width) =>
        parent.Controls.Add(new Label { Text = text, Location = new Point(x, y), Size = new Size(width, 22), Font = new Font("Segoe UI Semibold", 9F), ForeColor = UiTheme.TextSecondary });

    private static Button CreateButton(string text, int width, bool primary)
    {
        var button = new Button { Text = text, Size = new Size(width, 34), Cursor = Cursors.Hand };
        if (primary) UiTheme.StylePrimaryButton(button); else UiTheme.StyleSecondaryButton(button);
        return button;
    }

    private void ShowError(Exception ex, string title) =>
        UiMessageBox.Show(this, ex.GetBaseException().Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
}
