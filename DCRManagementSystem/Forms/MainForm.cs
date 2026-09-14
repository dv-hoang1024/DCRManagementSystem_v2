using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;

namespace DCRManagementSystem.Forms;

public sealed class MainForm : Form
{
    private readonly TextBox _txtSearch = new();
    private readonly DataGridView _grid = new();
    private readonly Label _lblScopeTitle = new();
    private readonly Label _lblScopeDescription = new();
    private readonly Label _lblCount = new();
    private readonly Label _lblEmpty = new();
    private readonly Label _lblEmptyIcon = new();
    private readonly Panel _emptyStatePanel = new();
    private readonly Button _btnNew = new();
    private readonly Button _btnOpen = new();
    private readonly Button _btnRefresh = new();
    private readonly Button _btnDelete = new();
    private readonly Button _btnClearSearch = new();
    private readonly Button _btnLanguage = new();
    private readonly Button _btnChangePassword = new();
    private readonly Button _btnLogout = new();
    private readonly Dictionary<string, NavigationBadgeButton> _scopeButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _refreshGridSync = new(1, 1);
    private string _currentScope = DcrListScopes.PendingMyApproval;
    private readonly int? _startupRequestId;
    public bool LogoutRequested { get; private set; }
    
    public MainForm(int? startupRequestId = null)
    {
        _startupRequestId = startupRequestId;
        var user = CurrentUser.User
            ?? throw new InvalidOperationException("Chưa đăng nhập.");

        Text = $"DCR Management System - {user.FullName}";
        WindowState = FormWindowState.Maximized;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1920, 1080);
        MaximumSize = new Size(1920, 1080);
        UiTheme.ApplyForm(this);
        BuildUi();
        ApplyScopeVisuals();
        UiLanguageManager.LanguageChanged += OnLanguageChanged;
        FormClosed += (_, _) => UiLanguageManager.LanguageChanged -= OnLanguageChanged;

        Shown += async (_, _) =>
        {
            await RefreshGridAsync();
            if (_startupRequestId.HasValue)
            {
                using var form = new DCRDetailForm(_startupRequestId.Value);
                form.ShowDialog(this);
                await RefreshGridAsync();
            }
        };
    }


    private void BuildUi()
    {
        SuspendLayout();

        var sidebar = BuildSidebar();
        var content = BuildContent();

        Controls.Add(content);
        Controls.Add(sidebar);

        ResumeLayout(true);
    }

    private void ShowSingleDialog<T>(Func<T> factory) where T : Form
    {
        var existing = Application.OpenForms.OfType<T>().FirstOrDefault(x => !x.IsDisposed);
        if (existing is not null)
        {
            if (existing.WindowState == FormWindowState.Minimized) existing.WindowState = FormWindowState.Normal;
            existing.BringToFront();
            existing.Activate();
            return;
        }

        using var form = factory();
        form.ShowDialog(this);
    }

    private Panel BuildSidebar()
    {
        var sidebar = new Panel
        {
            Dock = DockStyle.Left,
            Width = 252,
            BackColor = UiTheme.Primary
        };

        var brand = new Panel
        {
            Dock = DockStyle.Top,
            Height = 82, // Giảm chiều cao từ 116 xuống 82 cho gọn
            BackColor = UiTheme.Primary,
            Padding = new Padding(22, 14, 18, 10)
        };

        // Tiêu đề DCR căn lề trái X = 20
        var brandTitle = new Label
        {
            Text = "DCR",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 22F, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(20, 05)
        };

        // Phụ đề nằm ngay dưới chữ DCR
        var brandSubtitle = new Label
        {
            Text = "Management System",
            AutoSize = true,
            Font = new Font("Segoe UI", 12F),
            ForeColor = Color.FromArgb(229, 246, 235),
            Location = new Point(27, 55)
        };

        // Chỉ thêm 2 nhãn chữ vào panel, không dùng brandLogo nữa
        brand.Controls.AddRange([brandTitle, brandSubtitle]);

        var navigation = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = UiTheme.Primary,
            Padding = new Padding(12, 6, 12, 12)
        };

        var requestLabel = CreateSidebarSectionLabel("DANH MỤC DCR");
        requestLabel.Margin = new Padding(4, 18, 4, 4);
        navigation.Controls.Add(requestLabel);
        AddScopeButton(navigation, DcrListScopes.PendingMyApproval, "  Cần phê duyệt");
        AddScopeButton(navigation, DcrListScopes.RelatedToMe, "  DCR liên quan");
        AddScopeButton(navigation, DcrListScopes.MyRequests, "  DCR của tôi");
        AddScopeButton(navigation, DcrListScopes.Approved, "  Đã duyệt");
        AddScopeButton(navigation, DcrListScopes.Rejected, "  Bị từ chối");

        if (CurrentUser.IsAdmin)
        {
            AddScopeButton(navigation, DcrListScopes.All, "  Tất cả DCR");

            var adminLabel = CreateSidebarSectionLabel("QUẢN TRỊ");
            adminLabel.Margin = new Padding(4, 18, 4, 4);
            navigation.Controls.Add(adminLabel);

            navigation.Controls.Add(CreateAdminButton("  Người dùng", () => ShowSingleDialog(() => new UserManagementForm())));

            navigation.Controls.Add(CreateAdminButton("  Đơn vị tổ chức", () => ShowSingleDialog(() => new BusinessUnitManagementForm())));

            navigation.Controls.Add(CreateAdminButton("  Phòng ban", () => ShowSingleDialog(() => new DepartmentManagementForm())));

            navigation.Controls.Add(CreateAdminButton("  Role", () => ShowSingleDialog(() => new RoleManagementForm())));

            navigation.Controls.Add(CreateAdminButton("  Danh mục DCR", () => ShowSingleDialog(() => new MasterDataManagementForm())));

            navigation.Controls.Add(CreateAdminButton("  Luồng phê duyệt", () => ShowSingleDialog(() => new WorkflowConfigForm())));

            navigation.Controls.Add(CreateAdminButton("  Ma trận phê duyệt", () => ShowSingleDialog(() => new ApprovalMatrixForm())));

            navigation.Controls.Add(CreateAdminButton("  Thiết lập hệ thống", () => ShowSingleDialog(() => new SystemSettingsForm())));
        }

        navigation.Resize += (_, _) => ResizeSidebarButtons(navigation);

        var userPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 116,
            BackColor = Color.FromArgb(13, 102, 56),
            Padding = new Padding(22, 16, 18, 12)
        };

        var userName = new Label
        {
            Text = CurrentUser.User?.FullName ?? string.Empty,
            AutoEllipsis = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
            Location = new Point(22, 17),
            Size = new Size(208, 24)
        };

        var userRole = new Label
        {
            Text = AppServices.UseRemoteApi
                ? $"{CurrentUser.User?.Role ?? string.Empty} • {CurrentUser.AuthMethod} • {AppServices.ApiConnectionLabel}"
                : $"{CurrentUser.User?.Role ?? string.Empty} • {CurrentUser.AuthMethod}",
            AutoEllipsis = true,
            ForeColor = Color.FromArgb(218, 240, 226),
            Font = new Font("Segoe UI", 9F),
            Location = new Point(22, 43),
            Size = new Size(208, 21)
        };

        _btnChangePassword.Text = UiLanguageManager.T("Đổi mật khẩu", "Password");
        _btnChangePassword.Location = new Point(22, 73);
        _btnChangePassword.Size = new Size(100, 32);
        UiTheme.StyleSecondaryButton(_btnChangePassword);
        _btnChangePassword.BackColor = Color.FromArgb(13, 102, 56);
        _btnChangePassword.ForeColor = Color.White;
        _btnChangePassword.FlatAppearance.BorderColor = Color.FromArgb(151, 216, 178);
        _btnChangePassword.FlatAppearance.MouseOverBackColor = UiTheme.PrimaryHover;
        _btnChangePassword.Click += (_, _) => OpenChangePassword();

        _btnLogout.Text = UiLanguageManager.T("Đăng xuất", "Sign Out");
        _btnLogout.Location = new Point(130, 73);
        _btnLogout.Size = new Size(100, 32);
        UiTheme.StyleSecondaryButton(_btnLogout);
        _btnLogout.BackColor = Color.FromArgb(13, 102, 56);
        _btnLogout.ForeColor = Color.White;
        _btnLogout.FlatAppearance.BorderColor = Color.FromArgb(246, 162, 94);
        _btnLogout.FlatAppearance.MouseOverBackColor = UiTheme.PrimaryHover;
        _btnLogout.Click += (_, _) => RequestLogout();

        userPanel.Controls.AddRange([userName, userRole, _btnChangePassword, _btnLogout]);

        sidebar.Controls.Add(navigation);
        sidebar.Controls.Add(userPanel);
        sidebar.Controls.Add(brand);

        return sidebar;
    }


    private void OpenChangePassword()
    {
        using var form = new ChangePasswordForm();
        form.ShowDialog(this);
    }

    private void RequestLogout()
    {
        var answer = DCRManagementSystem.Helpers.UiMessageBox.Show(
            "Bạn muốn đăng xuất khỏi DCR Management System để đăng nhập bằng tài khoản khác?",
            "Đăng xuất",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (answer != DialogResult.Yes)
            return;

        LogoutRequested = true;
        CurrentUser.Clear();
        Close();
    }

    private Control BuildContent()
    {
        var content = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Background
        };

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 105,
            BackColor = Color.White,
            Padding = new Padding(30, 18, 30, 14)
        };

        _lblScopeTitle.AutoSize = true;
        _lblScopeTitle.Font = new Font("Segoe UI Semibold", 19F, FontStyle.Bold);
        _lblScopeTitle.ForeColor = UiTheme.TextPrimary;
        _lblScopeTitle.Location = new Point(30, 17);

        _lblScopeDescription.AutoSize = true;
        _lblScopeDescription.Font = new Font("Segoe UI", 9.5F);
        _lblScopeDescription.ForeColor = UiTheme.TextSecondary;
        _lblScopeDescription.Location = new Point(32, 60);

        _btnLanguage.Text = UiLanguageManager.ToggleButtonText;
        _btnLanguage.Size = new Size(48, 34);
        _btnLanguage.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        UiTheme.StyleSecondaryButton(_btnLanguage);
        _btnLanguage.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
        _btnLanguage.Click += (_, _) => UiLanguageManager.Toggle();

        _btnNew.Text = "+  Tạo DCR";
        _btnNew.Size = new Size(142, 38);
        _btnNew.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        UiTheme.StylePrimaryButton(_btnNew);
        _btnNew.Click += (_, _) => OpenNewDcr();

        header.Resize += (_, _) =>
        {
            _btnNew.Location = new Point(
                Math.Max(600, header.ClientSize.Width - _btnNew.Width - 30),
                28);
            _btnLanguage.Location = new Point(_btnNew.Left - _btnLanguage.Width - 10, 30);
        };

        header.Controls.AddRange([_lblScopeTitle, _lblScopeDescription, _btnLanguage, _btnNew]);

        var workspace = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(28, 24, 28, 28),
            BackColor = UiTheme.Background
        };

        var toolbarCard = UiTheme.CreateCard();
        toolbarCard.Dock = DockStyle.Top;
        toolbarCard.Height = 76;
        toolbarCard.Padding = new Padding(18, 17, 18, 14);

        var searchLabel = new Label
        {
            Text = "Tìm kiếm:",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
            ForeColor = UiTheme.TextSecondary,
            Location = new Point(18, 22)
        };

        _txtSearch.Location = new Point(98, 20);
        _txtSearch.Size = new Size(330, 32);
        _txtSearch.PlaceholderText = " Mã DCR, tiêu đề, dòng sản phẩm, người tạo...";
        UiTheme.StyleInput(_txtSearch);
        _txtSearch.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                await RefreshGridAsync();
                e.SuppressKeyPress = true;
            }
        };

        var btnSearch = new Button
        {
            Text = "Tìm",
            Size = new Size(78, 34),
            Location = new Point(436, 19)
        };
        UiTheme.StylePrimaryButton(btnSearch);
        btnSearch.Click += async (_, _) => await RefreshGridAsync();

        _btnClearSearch.Text = "Xóa lọc";
        _btnClearSearch.Size = new Size(88, 34);
        _btnClearSearch.Location = new Point(518, 19);
        _btnClearSearch.Visible = false;
        UiTheme.StyleSecondaryButton(_btnClearSearch);
        _btnClearSearch.Click += async (_, _) =>
        {
            _txtSearch.Clear();
            await RefreshGridAsync();
        };
        _txtSearch.TextChanged += (_, _) => _btnClearSearch.Visible = !string.IsNullOrWhiteSpace(_txtSearch.Text);

        _lblCount.AutoSize = false;
        _lblCount.TextAlign = ContentAlignment.MiddleRight;
        _lblCount.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);
        _lblCount.ForeColor = UiTheme.TextSecondary;
        _lblCount.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _lblCount.Size = new Size(180, 32);

        toolbarCard.Resize += (_, _) =>
        {
            _lblCount.Location = new Point(
                Math.Max(620, toolbarCard.ClientSize.Width - _lblCount.Width - 18),
                20);
        };

        toolbarCard.Controls.AddRange([searchLabel, _txtSearch, btnSearch, _btnClearSearch, _lblCount]);

        var gridCard = UiTheme.CreateCard();
        gridCard.Dock = DockStyle.Fill;
        gridCard.Padding = new Padding(0);
        gridCard.Margin = new Padding(0, 18, 0, 0);

        var gridTitleBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 62,
            BackColor = Color.White,
            Padding = new Padding(18, 14, 18, 12)
        };

        var gridTitle = new Label
        {
            Text = "Danh sách DCR",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
            ForeColor = UiTheme.TextPrimary,
            Location = new Point(18, 18)
        };

        _btnOpen.Text = "Mở DCR";
        _btnOpen.Size = new Size(96, 34);
        _btnOpen.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        UiTheme.StylePrimaryButton(_btnOpen);
        _btnOpen.Click += (_, _) => OpenSelected();

        _btnRefresh.Text = "Làm mới";
        _btnRefresh.Size = new Size(92, 34);
        _btnRefresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        UiTheme.StyleSecondaryButton(_btnRefresh);
        _btnRefresh.Click += async (_, _) => await RefreshGridAsync();

        _btnDelete.Text = "Xóa DCR";
        _btnDelete.Size = new Size(92, 34);
        _btnDelete.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _btnDelete.Visible = CurrentUser.IsAdmin;
        UiTheme.StyleDangerButton(_btnDelete);
        _btnDelete.Click += async (_, _) => await DeleteSelectedAsync();

        gridTitleBar.Resize += (_, _) =>
        {
            _btnOpen.Location = new Point(
                Math.Max(520, gridTitleBar.ClientSize.Width - _btnOpen.Width - 18),
                13);
            _btnRefresh.Location = new Point(_btnOpen.Left - _btnRefresh.Width - 10, 13);
            _btnDelete.Location = new Point(_btnRefresh.Left - _btnDelete.Width - 10, 13);
        };

        gridTitleBar.Controls.AddRange([gridTitle, _btnDelete, _btnRefresh, _btnOpen]);

        ConfigureGrid();

        var gridHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(0, 1, 0, 0)
        };

        _emptyStatePanel.Dock = DockStyle.Fill;
        _emptyStatePanel.BackColor = Color.White;
        _emptyStatePanel.Visible = false;

        _lblEmptyIcon.Text = "\uE8A5";
        _lblEmptyIcon.Font = new Font("Segoe MDL2 Assets", 38F, FontStyle.Regular);
        _lblEmptyIcon.ForeColor = Color.FromArgb(185, 188, 193);
        _lblEmptyIcon.TextAlign = ContentAlignment.MiddleCenter;
        _lblEmptyIcon.AutoSize = false;
        _lblEmptyIcon.Size = new Size(90, 70);

        _lblEmpty.Text = "Không có DCR phù hợp với bộ lọc hiện tại.";
        _lblEmpty.TextAlign = ContentAlignment.MiddleCenter;
        _lblEmpty.Font = new Font("Segoe UI", 10.5F);
        _lblEmpty.ForeColor = UiTheme.TextSecondary;
        _lblEmpty.AutoSize = false;
        _lblEmpty.Size = new Size(520, 36);

        _emptyStatePanel.Controls.AddRange([_lblEmptyIcon, _lblEmpty]);
        _emptyStatePanel.Resize += (_, _) =>
        {
            _lblEmptyIcon.Location = new Point(Math.Max(0, (_emptyStatePanel.ClientSize.Width - _lblEmptyIcon.Width) / 2), Math.Max(40, (_emptyStatePanel.ClientSize.Height - 120) / 2));
            _lblEmpty.Location = new Point(Math.Max(0, (_emptyStatePanel.ClientSize.Width - _lblEmpty.Width) / 2), _lblEmptyIcon.Bottom + 4);
        };

        gridHost.Controls.Add(_grid);
        gridHost.Controls.Add(_emptyStatePanel);

        gridCard.Controls.Add(gridHost);
        gridCard.Controls.Add(UiTheme.CreateDivider());
        gridCard.Controls.Add(gridTitleBar);

        workspace.Controls.Add(gridCard);
        workspace.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 18, BackColor = UiTheme.Background });
        workspace.Controls.Add(toolbarCard);

        content.Controls.Add(workspace);
        content.Controls.Add(UiTheme.CreateDivider());
        content.Controls.Add(header);

        return content;
    }

    private void AddScopeButton(FlowLayoutPanel navigation, string scope, string text)
    {
        var button = new NavigationBadgeButton
        {
            Text = text,
            Size = new Size(216, 44),
            Margin = new Padding(0, 2, 0, 2)
        };

        button.Click += async (_, _) =>
        {
            if (_currentScope == scope)
            {
                return;
            }

            _currentScope = scope;
            ApplyScopeVisuals();
            await RefreshGridAsync();
        };

        _scopeButtons[scope] = button;
        navigation.Controls.Add(button);
    }

    private Button CreateAdminButton(string text, Action action)
    {
        var button = new Button
        {
            Text = text,
            Size = new Size(216, 42),
            Margin = new Padding(0, 2, 0, 2)
        };
        UiTheme.StyleNavigationButton(button, false);
        button.Click += (_, _) => action();
        return button;
    }

    private static Label CreateSidebarSectionLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = false,
            Size = new Size(216, 26),
            Margin = new Padding(4, 4, 4, 4),
            ForeColor = Color.FromArgb(205, 236, 216),
            Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
        };
    }

    private static void ResizeSidebarButtons(FlowLayoutPanel navigation)
    {
        var width = Math.Max(160, navigation.ClientSize.Width - navigation.Padding.Horizontal - 4);
        foreach (Control control in navigation.Controls)
        {
            if (control is Button or Label)
            {
                control.Width = width;
            }
        }
    }

    private void ApplyScopeVisuals()
    {
        foreach (var pair in _scopeButtons)
        {
            UiTheme.StyleNavigationButton(pair.Value, pair.Key == _currentScope);
        }

        (_lblScopeTitle.Text, _lblScopeDescription.Text) = _currentScope switch
        {
            DcrListScopes.PendingMyApproval => (
                UiLanguageManager.T("Cần phê duyệt", "Pending Approval"),
                UiLanguageManager.T("Danh sách DCR đang chờ bạn xử lý.", "DCRs awaiting your action.")),
            DcrListScopes.RelatedToMe => (
                UiLanguageManager.T("DCR liên quan", "Involved DCRs"),
                UiLanguageManager.T("Các DCR có liên quan đến bạn.", "DCRs associated with you.")),
            DcrListScopes.MyRequests => (
                UiLanguageManager.T("DCR của tôi", "My Requests"),
                UiLanguageManager.T("Các DCR do bạn khởi tạo.", "DCRs created by you.")),
            DcrListScopes.Approved => (
                UiLanguageManager.T("Đã duyệt", "Approved"),
                UiLanguageManager.T("Các DCR đã hoàn tất toàn bộ luồng phê duyệt.", "DCRs with a completed approval workflow.")),
            DcrListScopes.Rejected => (
                UiLanguageManager.T("Bị từ chối", "Rejected"),
                UiLanguageManager.T("Các DCR bị từ chối trong quá trình phê duyệt.", "DCRs rejected during approval.")),
            DcrListScopes.All => (
                UiLanguageManager.T("Tất cả DCR", "All DCRs"),
                UiLanguageManager.T("Toàn bộ DCR trong hệ thống.", "All DCRs in the system.")),
            _ => ("DCR", UiLanguageManager.T("Danh sách yêu cầu thay đổi độ lệch tạm thời.", "Temporary deviation change requests."))
        };

        UpdateScopeButtonTexts();
        UpdateEmptyStateText();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        _btnLanguage.Text = UiLanguageManager.ToggleButtonText;
        _btnChangePassword.Text = UiLanguageManager.T("Đổi mật khẩu", "Password");
        _btnLogout.Text = UiLanguageManager.T("Đăng xuất", "Sign Out");
        ApplyScopeVisuals();
        UpdateEmptyStateText();
        if (_grid.DataSource is System.Collections.ICollection collection)
            _lblCount.Text = UiLanguageManager.IsEnglish ? $"{collection.Count:N0} requests" : $"{collection.Count:N0} yêu cầu";
        UiLanguageManager.Apply(this);
    }

    private void UpdateScopeButtonTexts()
    {
        if (_scopeButtons.TryGetValue(DcrListScopes.PendingMyApproval, out var pending)) pending.Text = "  " + UiLanguageManager.T("Cần phê duyệt", "Pending Approval");
        if (_scopeButtons.TryGetValue(DcrListScopes.RelatedToMe, out var related)) related.Text = "  " + UiLanguageManager.T("DCR liên quan", "Involved DCRs");
        if (_scopeButtons.TryGetValue(DcrListScopes.MyRequests, out var mine)) mine.Text = "  " + UiLanguageManager.T("DCR của tôi", "My Requests");
        if (_scopeButtons.TryGetValue(DcrListScopes.Approved, out var approved)) approved.Text = "  " + UiLanguageManager.T("Đã duyệt", "Approved");
        if (_scopeButtons.TryGetValue(DcrListScopes.Rejected, out var rejected)) rejected.Text = "  " + UiLanguageManager.T("Bị từ chối", "Rejected");
        if (_scopeButtons.TryGetValue(DcrListScopes.All, out var all)) all.Text = "  " + UiLanguageManager.T("Tất cả DCR", "All DCRs");
    }

    private void UpdateEmptyStateText()
    {
        _lblEmpty.Text = _currentScope == DcrListScopes.PendingMyApproval
            ? UiLanguageManager.T("Không có DCR nào cần xử lý tại thời điểm này.", "No DCRs require action at this time.")
            : UiLanguageManager.T("Không có DCR phù hợp với bộ lọc hiện tại.", "No DCRs match the current filter.");
    }

    private async Task UpdatePendingApprovalBadgeAsync(IReadOnlyCollection<DcrListItem> currentData)
    {
        if (!_scopeButtons.TryGetValue(DcrListScopes.PendingMyApproval, out var button) || CurrentUser.User is null)
            return;
        try
        {
            int count;
            if (_currentScope == DcrListScopes.PendingMyApproval && string.IsNullOrWhiteSpace(_txtSearch.Text))
            {
                count = currentData.Count;
            }
            else
            {
                count = await AppServices.CreateDcrService().GetPendingApprovalCountAsync(CurrentUser.User.UserId);
            }
            button.BadgeCount = count;
        }
        catch
        {
            // Badge is informational; a transient count failure must not block the workspace.
        }
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        UiTheme.ConfigureGrid(_grid);

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(DcrListItem.DCRNumber),
            HeaderText = "DCR No.",
            FillWeight = 82
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(DcrListItem.Title),
            HeaderText = "Title",
            FillWeight = 210
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(DcrListItem.Program),
            HeaderText = "Dòng sản phẩm",
            FillWeight = 62
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(DcrListItem.BuildStage),
            HeaderText = "Build Stage",
            FillWeight = 70
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(DcrListItem.Department),
            HeaderText = "Department",
            FillWeight = 90
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(DcrListItem.Owner),
            HeaderText = "Owner",
            FillWeight = 110
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(DcrListItem.RevisionNo),
            HeaderText = "Rev",
            FillWeight = 42,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.MiddleCenter
            }
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(DcrListItem.CurrentStage),
            HeaderText = "Stage",
            FillWeight = 46,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.MiddleCenter
            }
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(DcrListItem.Status),
            HeaderText = "Status",
            FillWeight = 75
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(DcrListItem.CreatedDate),
            HeaderText = "Created",
            DefaultCellStyle = new DataGridViewCellStyle { Format = "dd/MM/yyyy HH:mm" },
            FillWeight = 96
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(DcrListItem.LastSavedAt),
            HeaderText = "Last Saved",
            DefaultCellStyle = new DataGridViewCellStyle { Format = "dd/MM/yyyy HH:mm" },
            FillWeight = 96
        });

        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0)
            {
                OpenSelected();
            }
        };

        _grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 ||
                _grid.Columns[e.ColumnIndex].DataPropertyName != nameof(DcrListItem.Status) ||
                e.Value is not string status)
            {
                return;
            }

            if (status.Equals(RequestStatuses.Approved, StringComparison.OrdinalIgnoreCase))
            {
                e.CellStyle.ForeColor = UiTheme.Success;
                e.CellStyle.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);
            }
            else if (status.Equals(RequestStatuses.Rejected, StringComparison.OrdinalIgnoreCase))
            {
                e.CellStyle.ForeColor = UiTheme.Danger;
                e.CellStyle.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);
            }
            else if (RequestStatuses.IsEditable(status))
            {
                e.CellStyle.ForeColor = UiTheme.TextSecondary;
            }
            else
            {
                e.CellStyle.ForeColor = UiTheme.Primary;
                e.CellStyle.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);
            }
        };
    }

    private async Task RefreshGridAsync()
    {
        if (!IsHandleCreated || CurrentUser.User is null)
        {
            return;
        }

        if (!await _refreshGridSync.WaitAsync(0))
            return;

        try
        {
            UseWaitCursor = true;
            _btnRefresh.Enabled = false;

            var service = AppServices.CreateDcrService();
            var data = await service.GetListAsync(
                _currentScope,
                CurrentUser.User.UserId,
                _txtSearch.Text,
                CurrentUser.IsAdmin);

            _grid.DataSource = data;
            _lblCount.Text = UiLanguageManager.IsEnglish ? $"{data.Count:N0} requests" : $"{data.Count:N0} yêu cầu";
            _emptyStatePanel.Visible = data.Count == 0;
            _grid.Visible = data.Count > 0;
            UpdateEmptyStateText();
            await UpdatePendingApprovalBadgeAsync(data);
        }
        catch (Exception ex)
        {
            DCRManagementSystem.Helpers.UiMessageBox.Show(
                this,
                ex.Message,
                "Không thể tải danh sách DCR",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            _btnRefresh.Enabled = true;
            UseWaitCursor = false;
            _refreshGridSync.Release();
        }
    }


    private async Task DeleteSelectedAsync()
    {
        if (!CurrentUser.IsAdmin || CurrentUser.User is null)
        {
            DCRManagementSystem.Helpers.UiMessageBox.Show(this, "Chỉ Administrator/Server mode mới được xóa DCR.", "Xóa DCR",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_grid.CurrentRow?.DataBoundItem is not DcrListItem item)
        {
            DCRManagementSystem.Helpers.UiMessageBox.Show(this, "Vui lòng chọn một DCR cần xóa.", "Xóa DCR",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var confirm = new DcrDeleteConfirmDialog(item.DCRNumber);
        if (confirm.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            UseWaitCursor = true;
            _btnDelete.Enabled = false;
            await AppServices.CreateAdminService().DeleteDcrAsync(
                item.Id,
                CurrentUser.User.UserId,
                CurrentUser.IsAdmin);

            DCRManagementSystem.Helpers.UiMessageBox.Show(this,
                $"Đã xóa {item.DCRNumber} và hủy các tác vụ email, thời hạn, phê duyệt cùng file lưu trữ liên quan. Snapshot xóa được giữ trong DCRDeletionLogs để audit.",
                "Xóa DCR",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            await RefreshGridAsync();
        }
        catch (Exception ex)
        {
            DCRManagementSystem.Helpers.UiMessageBox.Show(this, ex.Message, "Không thể xóa DCR",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnDelete.Enabled = true;
            UseWaitCursor = false;
        }
    }

    private void OpenNewDcr()
    {
        using var form = new DCRDetailForm(null);
        form.ShowDialog(this);
        _ = RefreshGridAsync();
    }

    private void OpenSelected()
    {
        if (_grid.CurrentRow?.DataBoundItem is not DcrListItem item)
        {
            DCRManagementSystem.Helpers.UiMessageBox.Show(this, "Vui lòng chọn một DCR.", "DCR",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var form = new DCRDetailForm(item.Id);
        form.ShowDialog(this);
        _ = RefreshGridAsync();
    }
}
