using System.ComponentModel;
using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;

namespace DCRManagementSystem.Forms;

public sealed class ApprovalMatrixForm : Form
{
    private sealed class NullableDepartmentItem
    {
        public int? Id { get; init; }
        public string Name { get; init; } = string.Empty;
    }

    private sealed class NullableUserItem
    {
        public int? Id { get; init; }
        public string Name { get; init; } = string.Empty;
    }

    private readonly BindingList<ApprovalMatrixRule> _rules = new();
    private readonly DataGridView _grid = new();
    private readonly Label _lblState = new();
    private List<Department> _departments = new();
    private List<User> _users = new();
    private List<RoleDefinition> _roles = new();

    public ApprovalMatrixForm()
    {
        if (!CurrentUser.IsAdmin)
            throw new UnauthorizedAccessException("Chỉ Administrator được cấu hình Approval Matrix.");

        Text = "Approval Matrix";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1480, 760);
        MinimumSize = new Size(1180, 640);
        MaximumSize = new Size(1920, 1080);
        UiTheme.ApplyForm(this);
        BuildUi();
        Shown += async (_, _) => await LoadAsync();
    }

    private void BuildUi()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 104, BackColor = Color.White };
        var title = UiTheme.CreatePageTitle("Ma trận phê duyệt động");
        title.Location = new Point(28, 16);
        var subtitle = UiTheme.CreatePageSubtitle(
            "Định tuyến approver theo Requesting Department, Impacted Department, Target Department, role hoặc user cụ thể.");
        subtitle.Location = new Point(30, 58);

        var btnSave = new Button { Text = "Lưu ma trận", Size = new Size(120, 38), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        UiTheme.StylePrimaryButton(btnSave);
        btnSave.Click += async (_, _) => await SaveAsync();
        var btnClose = new Button { Text = "Đóng", Size = new Size(86, 38), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        UiTheme.StyleSecondaryButton(btnClose);
        btnClose.Click += (_, _) => Close();
        header.Resize += (_, _) =>
        {
            btnSave.Location = new Point(header.ClientSize.Width - btnSave.Width - 28, 32);
            btnClose.Location = new Point(btnSave.Left - btnClose.Width - 10, 32);
        };
        header.Controls.AddRange([title, subtitle, btnClose, btnSave]);

        var body = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Background, Padding = new Padding(28, 22, 28, 28) };
        var info = UiTheme.CreateCard();
        info.Dock = DockStyle.Top;
        info.Height = 90;
        info.BackColor = UiTheme.PrimarySoft;
        var infoText = new Label
        {
            Text = "Ưu tiên nhỏ hơn được xét trước. Requesting Department để trống = áp dụng chung. " +
                   "DESIGN_MANAGER thường dùng RequestingDepartmentManager; IMPACTED_DEPARTMENT dùng ImpactedDepartmentManager; " +
                   "ME/Chief có thể dùng TargetDepartmentManager.",
            AutoSize = false,
            Font = new Font("Segoe UI", 9.5F),
            ForeColor = UiTheme.TextSecondary,
            Location = new Point(18, 17),
            Size = new Size(1200, 56),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        info.Controls.Add(infoText);

        var card = UiTheme.CreateCard();
        card.Dock = DockStyle.Fill;
        var bar = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Color.White };
        var gridTitle = new Label
        {
            Text = "Routing rules",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
            ForeColor = UiTheme.TextPrimary,
            Location = new Point(18, 18)
        };
        _lblState.AutoSize = false;
        _lblState.Size = new Size(250, 34);
        _lblState.TextAlign = ContentAlignment.MiddleRight;
        _lblState.ForeColor = UiTheme.TextSecondary;
        _lblState.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        bar.Resize += (_, _) => _lblState.Location = new Point(bar.ClientSize.Width - 268, 12);
        bar.Controls.AddRange([gridTitle, _lblState]);

        card.Controls.Add(_grid);
        card.Controls.Add(UiTheme.CreateDivider());
        card.Controls.Add(bar);
        body.Controls.Add(card);
        body.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 16, BackColor = UiTheme.Background });
        body.Controls.Add(info);

        Controls.Add(body);
        Controls.Add(UiTheme.CreateDivider());
        Controls.Add(header);
    }

    private void ConfigureGrid()
    {
        _grid.Columns.Clear();
        _grid.AutoGenerateColumns = false;
        _grid.Dock = DockStyle.Fill;
        UiTheme.ConfigureGrid(_grid, readOnly: false);
        _grid.AllowUserToAddRows = true;
        _grid.AllowUserToDeleteRows = true;
        _grid.DataSource = _rules;
        _grid.DataError += (_, e) => e.ThrowException = false;

        var stage = new DataGridViewComboBoxColumn
        {
            DataPropertyName = nameof(ApprovalMatrixRule.StageCode),
            HeaderText = "Stage",
            FlatStyle = FlatStyle.Flat,
            FillWeight = 110
        };
        stage.Items.AddRange(
            WorkflowStageCodes.DesignManager,
            WorkflowStageCodes.ImpactedDepartment,
            WorkflowStageCodes.MEManager,
            WorkflowStageCodes.ChiefEngineer);
        _grid.Columns.Add(stage);

        var depData = new List<NullableDepartmentItem> { new() { Id = null, Name = "(Any / None)" } };
        depData.AddRange(_departments.Select(x => new NullableDepartmentItem
        {
            Id = x.Id,
            Name = $"{x.DepartmentCode} - {x.DepartmentName}"
        }));

        _grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            DataPropertyName = nameof(ApprovalMatrixRule.RequestingDepartmentId),
            HeaderText = "Requesting Dept",
            DataSource = depData.ToList(),
            DisplayMember = nameof(NullableDepartmentItem.Name),
            ValueMember = nameof(NullableDepartmentItem.Id),
            FlatStyle = FlatStyle.Flat,
            FillWeight = 120
        });

        _grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            DataPropertyName = nameof(ApprovalMatrixRule.TargetDepartmentId),
            HeaderText = "Target Dept",
            DataSource = depData.ToList(),
            DisplayMember = nameof(NullableDepartmentItem.Name),
            ValueMember = nameof(NullableDepartmentItem.Id),
            FlatStyle = FlatStyle.Flat,
            FillWeight = 115
        });

        var source = new DataGridViewComboBoxColumn
        {
            DataPropertyName = nameof(ApprovalMatrixRule.ApproverSource),
            HeaderText = "Approver Source",
            FlatStyle = FlatStyle.Flat,
            FillWeight = 140
        };
        source.Items.AddRange(ApproverSources.All);
        _grid.Columns.Add(source);

        var role = new DataGridViewComboBoxColumn
        {
            DataPropertyName = nameof(ApprovalMatrixRule.ApproverRole),
            HeaderText = "Role",
            FlatStyle = FlatStyle.Flat,
            FillWeight = 95
        };
        role.Items.Add(string.Empty);
        // Keep inactive values visible while editing an existing rule. The service still
        // validates what can be activated when the matrix is saved.
        role.Items.AddRange(_roles.OrderByDescending(x => x.HierarchyLevel).Select(x => (object)x.RoleName).ToArray());
        _grid.Columns.Add(role);

        var userData = new List<NullableUserItem> { new() { Id = null, Name = "(None)" } };
        userData.AddRange(_users.Select(x => new NullableUserItem
        {
            Id = x.UserId,
            Name = $"{x.Username} - {x.FullName}"
        }));
        _grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            DataPropertyName = nameof(ApprovalMatrixRule.ApproverUserId),
            HeaderText = "Specific User",
            DataSource = userData,
            DisplayMember = nameof(NullableUserItem.Name),
            ValueMember = nameof(NullableUserItem.Id),
            FlatStyle = FlatStyle.Flat,
            FillWeight = 120
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(ApprovalMatrixRule.Priority),
            HeaderText = "Priority",
            FillWeight = 55
        });
        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            DataPropertyName = nameof(ApprovalMatrixRule.IsActive),
            HeaderText = "Active",
            FillWeight = 48
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(ApprovalMatrixRule.Description),
            HeaderText = "Description",
            FillWeight = 180
        });
    }

    private async Task LoadAsync()
    {
        try
        {
            UseWaitCursor = true;
            _lblState.Text = "Đang tải...";
            var service = AppServices.CreateAdminService();
            // Existing rules may reference a department that has since been disabled.
            // Loading all rows prevents the grid ComboBox from silently displaying blank/default.
            _departments = await service.GetDepartmentsAsync(activeOnly: false);
            _users = await service.GetUsersAsync();
            _roles = await service.GetRolesAsync(activeOnly: false);
            var data = await service.GetApprovalMatrixRulesAsync();

            _rules.Clear();
            foreach (var x in data)
            {
                _rules.Add(new ApprovalMatrixRule
                {
                    Id = x.Id,
                    StageCode = x.StageCode,
                    RequestingDepartmentId = x.RequestingDepartmentId,
                    TargetDepartmentId = x.TargetDepartmentId,
                    ApproverSource = x.ApproverSource,
                    ApproverRole = x.ApproverRole,
                    ApproverUserId = x.ApproverUserId,
                    Priority = x.Priority,
                    IsActive = x.IsActive,
                    Description = x.Description
                });
            }

            ConfigureGrid();
            _lblState.Text = $"{_rules.Count:N0} rule";
        }
        catch (Exception ex)
        {
            DCRManagementSystem.Helpers.UiMessageBox.Show(this, ex.Message, "Approval Matrix", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            _grid.EndEdit();
            _lblState.Text = "Đang lưu...";
            var service = AppServices.CreateAdminService();
            await service.ReplaceApprovalMatrixRulesAsync(_rules.Where(x => !string.IsNullOrWhiteSpace(x.StageCode)));
            _lblState.ForeColor = UiTheme.Success;
            _lblState.Text = "Đã lưu";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _lblState.ForeColor = UiTheme.Danger;
            _lblState.Text = "Lưu thất bại";
            DCRManagementSystem.Helpers.UiMessageBox.Show(this, ex.Message, "Approval Matrix", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
