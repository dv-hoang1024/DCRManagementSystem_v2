using System.ComponentModel;
using System.Diagnostics;
using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;
using Microsoft.Web.WebView2.WinForms;

namespace DCRManagementSystem.UserControls;

public enum ApprovalPlanDisplaySource
{
    DefaultMatrix,
    ExistingDcr,
    OrganizationSuggestion,
    OrganizationFallback,
    GlobalTemplate,
    PersonalTemplate,
    Custom
}

public sealed class ReviewSubmitStepControl : DcrWizardStepControl
{
    protected override int StepNumber => 5;

    private readonly WebView2 _pdfViewer = new();
    private readonly RichTextBox _txtFallback = new();
    private readonly Label _lblPdfState = new();
    private readonly Button _btnOpenPdf = new();
    private readonly Button _btnRefreshPdf = new();

    private readonly DataGridView _gridAttachments = new();
    private readonly Button _btnOpenAttachment = new();

    private readonly TextBox _txtApproverSearch = new();
    private readonly Button _btnSearchApprover = new();
    private readonly ComboBox _cmbApproverResults = new();
    private readonly NumericUpDown _numApprovalLevel = new();
    private readonly TextBox _txtLevelName = new();
    private readonly Button _btnAddApprover = new();
    private readonly Button _btnNextLevel = new();
    private readonly Button _btnRemoveApprover = new();
    private readonly Button _btnSuggestApproval = new();
    private readonly Button _btnLoadApprovalTemplate = new();
    private readonly Button _btnSaveApprovalTemplate = new();
    private readonly ComboBox _cmbGlobalTemplates = new();
    private readonly Button _btnLoadGlobalTemplate = new();
    private readonly Label _lblPlanMode = new();
    private readonly DataGridView _gridApprovalPlan = new();
    private readonly BindingList<ApprovalPlanEditItem> _approvalPlan = new();
    private readonly BindingList<ApproverSearchItem> _approverResults = new();
    private readonly BindingList<ApprovalPlanTemplateEditModel> _globalTemplates = new();

    private readonly DataGridView _gridApproval = new();
    private readonly DataGridView _gridAudit = new();
    private string _currentPdfPath = string.Empty;
    private string _fallbackText = string.Empty;
    private bool _editable;
    private ApprovalPlanDisplaySource _planSource = ApprovalPlanDisplaySource.DefaultMatrix;
    private string _globalTemplateName = string.Empty;

    public event EventHandler? RefreshPdfRequested;
    public event EventHandler? OpenAttachmentRequested;
    public event EventHandler? SuggestedApprovalRequested;
    public event EventHandler? SavedApprovalLoadRequested;
    public event EventHandler? SavedApprovalSaveRequested;

    public Func<string, Task<List<ApproverSearchItem>>>? SearchApproversAsync { get; set; }
    public AttachmentListItem? SelectedAttachment => _gridAttachments.CurrentRow?.DataBoundItem as AttachmentListItem;

    public ReviewSubmitStepControl()
    {
        var page = WizardControlUi.CreateFlowPage();

        var review = WizardControlUi.CreateCard(
            "Review tổng quan - PDF",
            "PDF được sinh từ chính dữ liệu DCR hiện tại. Đây cũng là định dạng được đính kèm email gửi cho approver.",
            700);

        _lblPdfState.AutoSize = false;
        _lblPdfState.Location = new Point(28, 86);
        _lblPdfState.Size = new Size(560, 30);
        _lblPdfState.Font = new Font("Segoe UI", 9.5F);
        _lblPdfState.ForeColor = UiTheme.TextSecondary;
        _lblPdfState.TextAlign = ContentAlignment.MiddleLeft;
        _lblPdfState.Text = "PDF review sẽ được tạo khi DCR đã được lưu.";

        ConfigureSmallButton(_btnRefreshPdf, "Làm mới PDF", 120);
        _btnRefreshPdf.Location = new Point(702, 86);
        _btnRefreshPdf.Click += (_, _) => RefreshPdfRequested?.Invoke(this, EventArgs.Empty);

        ConfigureSmallButton(_btnOpenPdf, "Mở PDF", 100);
        _btnOpenPdf.Location = new Point(832, 86);
        _btnOpenPdf.Enabled = false;
        _btnOpenPdf.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_currentPdfPath) || !File.Exists(_currentPdfPath))
                return;
            Process.Start(new ProcessStartInfo { FileName = _currentPdfPath, UseShellExecute = true });
        };

        _pdfViewer.Location = new Point(28, 126);
        _pdfViewer.Size = new Size(924, 520);
        _pdfViewer.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _pdfViewer.DefaultBackgroundColor = Color.White;

        _txtFallback.ReadOnly = true;
        _txtFallback.BorderStyle = BorderStyle.FixedSingle;
        _txtFallback.BackColor = Color.White;
        _txtFallback.ForeColor = UiTheme.TextPrimary;
        _txtFallback.Font = new Font("Consolas", 9.5F);
        _txtFallback.Location = _pdfViewer.Location;
        _txtFallback.Size = _pdfViewer.Size;
        _txtFallback.Anchor = _pdfViewer.Anchor;
        _txtFallback.Visible = false;

        review.Controls.AddRange([_lblPdfState, _btnRefreshPdf, _btnOpenPdf, _pdfViewer, _txtFallback]);
        review.Resize += (_, _) =>
        {
            _btnOpenPdf.Location = new Point(Math.Max(28, review.ClientSize.Width - _btnOpenPdf.Width - 28), 86);
            _btnRefreshPdf.Location = new Point(Math.Max(28, _btnOpenPdf.Left - _btnRefreshPdf.Width - 10), 86);
            _lblPdfState.Width = Math.Max(260, _btnRefreshPdf.Left - 40);
        };
        _btnRefreshPdf.BringToFront();
        _btnOpenPdf.BringToFront();

        var attachments = WizardControlUi.CreateCard(
            "Tệp đính kèm đã Submit",
            "Người tạo và tất cả approver có liên quan có thể mở lại toàn bộ tài liệu kỹ thuật đã đính kèm cùng DCR.",
            330);
        ConfigureSmallButton(_btnOpenAttachment, "Mở tệp", 100);
        _btnOpenAttachment.Location = new Point(852, 82);
        _btnOpenAttachment.Click += (_, _) => OpenAttachmentRequested?.Invoke(this, EventArgs.Empty);
        UiTheme.ConfigureGrid(_gridAttachments);
        _gridAttachments.AutoGenerateColumns = false;
        _gridAttachments.Location = new Point(28, 122);
        _gridAttachments.Size = new Size(924, 165);
        _gridAttachments.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _gridAttachments.Columns.Add(WizardControlUi.CreateTextColumn("Type", nameof(AttachmentListItem.AttachmentType), 110));
        _gridAttachments.Columns.Add(WizardControlUi.CreateTextColumn("File Name", nameof(AttachmentListItem.FileName), 300));
        _gridAttachments.Columns.Add(WizardControlUi.CreateTextColumn("Size", nameof(AttachmentListItem.FileSize), 90));
        _gridAttachments.Columns.Add(WizardControlUi.CreateTextColumn("ZIP", nameof(AttachmentListItem.IsCompressed), 55));
        _gridAttachments.Columns.Add(WizardControlUi.CreateTextColumn("Uploaded By", nameof(AttachmentListItem.UploadedBy), 150));
        _gridAttachments.Columns.Add(WizardControlUi.CreateTextColumn("Uploaded At", nameof(AttachmentListItem.UploadedAt), 160));
        attachments.Controls.AddRange([_btnOpenAttachment, _gridAttachments]);
        attachments.Resize += (_, _) => _btnOpenAttachment.Left = Math.Max(28, attachments.ClientSize.Width - _btnOpenAttachment.Width - 28);
        _btnOpenAttachment.BringToFront();

        var routing = WizardControlUi.CreateCard(
            "Luồng phê duyệt cho DCR này",
            "Chọn đúng luồng phê duyệt",
            602);

        _lblPlanMode.AutoSize = false;
        _lblPlanMode.Location = new Point(28, 88);
        _lblPlanMode.Size = new Size(924, 28);
        _lblPlanMode.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);
        _lblPlanMode.ForeColor = UiTheme.Primary;

        var lblSearch = UiTheme.CreateFieldLabel("Tìm người phê duyệt (tên / email / username / phòng ban)");
        lblSearch.Location = new Point(28, 122);
        lblSearch.Size = new Size(410, 22);
        _txtApproverSearch.Location = new Point(28, 148);
        _txtApproverSearch.Size = new Size(330, 30);
        UiTheme.StyleInput(_txtApproverSearch);
        ConfigureSmallButton(_btnSearchApprover, "Tìm", 72);
        _btnSearchApprover.Location = new Point(368, 146);
        _btnSearchApprover.Click += async (_, _) => await SearchApproverAsync();
        _txtApproverSearch.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            await SearchApproverAsync();
        };

        var lblResult = UiTheme.CreateFieldLabel("Kết quả");
        lblResult.Location = new Point(456, 122);
        lblResult.Size = new Size(496, 22);
        _cmbApproverResults.Location = new Point(456, 148);
        _cmbApproverResults.Size = new Size(496, 30);
        _cmbApproverResults.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbApproverResults.DataSource = _approverResults;
        _cmbApproverResults.DisplayMember = nameof(ApproverSearchItem.DisplayText);
        UiTheme.StyleInput(_cmbApproverResults);

        var lblLevel = UiTheme.CreateFieldLabel("Cấp");
        lblLevel.Location = new Point(28, 190);
        lblLevel.Size = new Size(80, 22);
        _numApprovalLevel.Location = new Point(28, 216);
        _numApprovalLevel.Size = new Size(80, 30);
        _numApprovalLevel.Minimum = 1;
        _numApprovalLevel.Maximum = 99;
        _numApprovalLevel.Value = 1;
        UiTheme.StyleInput(_numApprovalLevel);
        _numApprovalLevel.ValueChanged += (_, _) => UpdateSuggestedLevelName();

        var lblLevelName = UiTheme.CreateFieldLabel("Tên cấp");
        lblLevelName.Location = new Point(124, 190);
        lblLevelName.Size = new Size(280, 22);
        _txtLevelName.Location = new Point(124, 216);
        _txtLevelName.Size = new Size(280, 30);
        _txtLevelName.Text = "Cấp phê duyệt 1";
        UiTheme.StyleInput(_txtLevelName);

        ConfigureSmallButton(_btnAddApprover, "Thêm approver", 130);
        _btnAddApprover.Location = new Point(420, 214);
        _btnAddApprover.Click += (_, _) => AddSelectedApprover();
        ConfigureSmallButton(_btnNextLevel, "+ Cấp mới", 100);
        _btnNextLevel.Location = new Point(560, 214);
        _btnNextLevel.Click += (_, _) => AddNextLevel();
        ConfigureSmallButton(_btnRemoveApprover, "Xóa dòng", 100);
        _btnRemoveApprover.Location = new Point(670, 214);
        _btnRemoveApprover.Click += (_, _) => RemoveSelectedApprover();
        ConfigureSmallButton(_btnSuggestApproval, "Gợi ý theo tổ chức", 172);
        _btnSuggestApproval.Location = new Point(780, 214);
        _btnSuggestApproval.Click += (_, _) => SuggestedApprovalRequested?.Invoke(this, EventArgs.Empty);

        var lblGlobalTemplate = UiTheme.CreateFieldLabel("Cấu hình theo Rank");
        lblGlobalTemplate.Location = new Point(28, 268);
        lblGlobalTemplate.Size = new Size(150, 22);
        _cmbGlobalTemplates.Location = new Point(188, 263);
        _cmbGlobalTemplates.Size = new Size(360, 30);
        _cmbGlobalTemplates.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbGlobalTemplates.DataSource = _globalTemplates;
        _cmbGlobalTemplates.DisplayMember = nameof(ApprovalPlanTemplateEditModel.DisplayName);
        UiTheme.StyleInput(_cmbGlobalTemplates);
        ConfigureSmallButton(_btnLoadGlobalTemplate, "Nạp cấu hình", 130);
        _btnLoadGlobalTemplate.Location = new Point(558, 262);
        _btnLoadGlobalTemplate.Click += (_, _) => LoadSelectedGlobalTemplate();

        var lblSavedTemplate = UiTheme.CreateFieldLabel("Mẫu line cá nhân");
        lblSavedTemplate.Location = new Point(28, 307);
        lblSavedTemplate.Size = new Size(150, 22);
        ConfigureSmallButton(_btnLoadApprovalTemplate, "Nạp line đã lưu", 150);
        _btnLoadApprovalTemplate.Location = new Point(188, 301);
        _btnLoadApprovalTemplate.Click += (_, _) => SavedApprovalLoadRequested?.Invoke(this, EventArgs.Empty);
        ConfigureSmallButton(_btnSaveApprovalTemplate, "Lưu line hiện tại", 165);
        _btnSaveApprovalTemplate.Location = new Point(348, 301);
        _btnSaveApprovalTemplate.Click += (_, _) => SavedApprovalSaveRequested?.Invoke(this, EventArgs.Empty);

        UiTheme.ConfigureGrid(_gridApprovalPlan);
        _gridApprovalPlan.AutoGenerateColumns = false;
        _gridApprovalPlan.Location = new Point(28, 346);
        _gridApprovalPlan.Size = new Size(924, 208);
        _gridApprovalPlan.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _gridApprovalPlan.DataSource = _approvalPlan;
        _gridApprovalPlan.Columns.Add(WizardControlUi.CreateTextColumn("Cấp", nameof(ApprovalPlanEditItem.LevelNumber), 60));
        _gridApprovalPlan.Columns.Add(WizardControlUi.CreateTextColumn("Tên cấp", nameof(ApprovalPlanEditItem.LevelName), 190));
        _gridApprovalPlan.Columns.Add(WizardControlUi.CreateTextColumn("Approver", nameof(ApprovalPlanEditItem.ApproverName), 180));
        _gridApprovalPlan.Columns.Add(WizardControlUi.CreateTextColumn("Email", nameof(ApprovalPlanEditItem.ApproverEmail), 210));
        _gridApprovalPlan.Columns.Add(WizardControlUi.CreateTextColumn("Role", nameof(ApprovalPlanEditItem.ApproverRole), 120));
        _gridApprovalPlan.Columns.Add(WizardControlUi.CreateTextColumn("Khối", nameof(ApprovalPlanEditItem.BusinessUnitName), 145));
        _gridApprovalPlan.Columns.Add(WizardControlUi.CreateTextColumn("Phòng ban", nameof(ApprovalPlanEditItem.DepartmentName), 155));
        routing.Controls.AddRange([_lblPlanMode, lblSearch, _txtApproverSearch, _btnSearchApprover, lblResult, _cmbApproverResults,
            lblLevel, _numApprovalLevel, lblLevelName, _txtLevelName, _btnAddApprover, _btnNextLevel, _btnRemoveApprover, _btnSuggestApproval,
            lblGlobalTemplate, _cmbGlobalTemplates, _btnLoadGlobalTemplate,
            lblSavedTemplate, _btnLoadApprovalTemplate, _btnSaveApprovalTemplate, _gridApprovalPlan]);

        var approval = WizardControlUi.CreateCard("Approval History", "Mỗi quyết định lưu approver, timestamp, phương thức xác thực và signature hash.", 390);
        UiTheme.ConfigureGrid(_gridApproval);
        _gridApproval.AutoGenerateColumns = false;
        _gridApproval.Location = new Point(28, 95);
        _gridApproval.Size = new Size(924, 255);
        _gridApproval.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _gridApproval.Columns.Add(WizardControlUi.CreateTextColumn("Rev", nameof(ApprovalHistoryItem.RevisionNo), 55));
        _gridApproval.Columns.Add(WizardControlUi.CreateTextColumn("Stage", nameof(ApprovalHistoryItem.StageName), 230));
        _gridApproval.Columns.Add(WizardControlUi.CreateTextColumn("Dept", nameof(ApprovalHistoryItem.Department), 85));
        _gridApproval.Columns.Add(WizardControlUi.CreateTextColumn("Approver", nameof(ApprovalHistoryItem.Approver), 150));
        _gridApproval.Columns.Add(WizardControlUi.CreateTextColumn("Decision", nameof(ApprovalHistoryItem.Decision), 100));
        _gridApproval.Columns.Add(WizardControlUi.CreateTextColumn("Date", nameof(ApprovalHistoryItem.DecisionDate), 145));
        _gridApproval.Columns.Add(WizardControlUi.CreateTextColumn("Comments", nameof(ApprovalHistoryItem.Comments), 220));
        _gridApproval.Columns.Add(WizardControlUi.CreateTextColumn("Auth", nameof(ApprovalHistoryItem.AuthMethod), 120));
        _gridApproval.Columns.Add(WizardControlUi.CreateTextColumn("Verify", nameof(ApprovalHistoryItem.SignatureStatus), 80));
        _gridApproval.Columns.Add(WizardControlUi.CreateTextColumn("Signature", nameof(ApprovalHistoryItem.SignatureHash), 220));
        approval.Controls.Add(_gridApproval);

        var audit = WizardControlUi.CreateCard("Audit Trail", "Tự động ghi nhận thay đổi Entity + audit nghiệp vụ: ai, làm gì, khi nào, old/new, máy tính/IP/Windows identity.", 410);
        UiTheme.ConfigureGrid(_gridAudit);
        _gridAudit.AutoGenerateColumns = false;
        _gridAudit.Location = new Point(28, 95);
        _gridAudit.Size = new Size(924, 275);
        _gridAudit.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _gridAudit.Columns.Add(WizardControlUi.CreateTextColumn("Time", nameof(AuditListItem.CreatedAt), 145));
        _gridAudit.Columns.Add(WizardControlUi.CreateTextColumn("User", nameof(AuditListItem.User), 130));
        _gridAudit.Columns.Add(WizardControlUi.CreateTextColumn("Action", nameof(AuditListItem.Action), 170));
        _gridAudit.Columns.Add(WizardControlUi.CreateTextColumn("Old Value", nameof(AuditListItem.OldValue), 260));
        _gridAudit.Columns.Add(WizardControlUi.CreateTextColumn("New Value", nameof(AuditListItem.NewValue), 260));
        _gridAudit.Columns.Add(WizardControlUi.CreateTextColumn("Computer", nameof(AuditListItem.ComputerName), 120));
        _gridAudit.Columns.Add(WizardControlUi.CreateTextColumn("IP", nameof(AuditListItem.IpAddress), 110));
        _gridAudit.Columns.Add(WizardControlUi.CreateTextColumn("Windows Identity", nameof(AuditListItem.WindowsIdentity), 170));
        audit.Controls.Add(_gridAudit);

        page.Controls.Add(review);
        page.Controls.Add(attachments);
        page.Controls.Add(routing);
        page.Controls.Add(approval);
        page.Controls.Add(audit);
        WizardControlUi.AttachResponsiveCards(page);
        Controls.Add(page);
        UpdatePlanModeLabel();
    }

    public void SetReviewText(string text)
    {
        _fallbackText = text ?? string.Empty;
        _txtFallback.Text = _fallbackText;
    }

    public void SetPdfBusy(string message = "Đang tạo PDF review...")
    {
        _lblPdfState.Text = message;
        _lblPdfState.ForeColor = UiTheme.Primary;
        _btnOpenPdf.Enabled = false;
    }

    public async Task ShowPdfAsync(string pdfPath)
    {
        if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
        {
            ClearPdf("Không tìm thấy PDF review.");
            return;
        }

        _currentPdfPath = Path.GetFullPath(pdfPath);
        _btnOpenPdf.Enabled = true;
        _lblPdfState.Text = $"PDF review: {Path.GetFileName(_currentPdfPath)}";
        _lblPdfState.ForeColor = UiTheme.Success;

        try
        {
            await _pdfViewer.EnsureCoreWebView2Async();
            _pdfViewer.Visible = true;
            _txtFallback.Visible = false;
            _pdfViewer.Source = new Uri(_currentPdfPath);
        }
        catch (Exception ex)
        {
            _pdfViewer.Visible = false;
            _txtFallback.Visible = true;
            _txtFallback.Text =
                $"Không thể hiển thị PDF trực tiếp trong cửa sổ. Bạn vẫn có thể bấm 'Mở PDF'.\r\n\r\n{ex.Message}\r\n\r\n" +
                _fallbackText;
            _lblPdfState.Text = "PDF đã tạo nhưng WebView2 không hiển thị được. Bấm 'Mở PDF' để xem ngoài ứng dụng.";
            _lblPdfState.ForeColor = UiTheme.Warning;
        }
    }

    public void ClearPdf(string message = "PDF review sẽ được tạo khi DCR đã được lưu.")
    {
        _currentPdfPath = string.Empty;
        _btnOpenPdf.Enabled = false;
        _lblPdfState.Text = message;
        _lblPdfState.ForeColor = UiTheme.TextSecondary;
        _pdfViewer.Visible = false;
        _txtFallback.Visible = true;
        _txtFallback.Text = _fallbackText;
    }

    public void SetAttachments(IEnumerable<AttachmentListItem> items)
    {
        _gridAttachments.DataSource = items.ToList();
        _btnOpenAttachment.Enabled = _gridAttachments.Rows.Count > 0;
    }

    public void SetApprovalHistory(IEnumerable<ApprovalHistoryItem> items) => _gridApproval.DataSource = items.ToList();
    public void SetAuditLogs(IEnumerable<AuditListItem> items) => _gridAudit.DataSource = items.ToList();

    protected override void LoadFrom(DcrEditModel model)
    {
        _approvalPlan.RaiseListChangedEvents = false;
        _approvalPlan.Clear();
        foreach (var item in model.ApprovalPlan.OrderBy(x => x.LevelNumber).ThenBy(x => x.Sequence))
        {
            _approvalPlan.Add(new ApprovalPlanEditItem
            {
                Id = item.Id,
                LevelNumber = item.LevelNumber,
                LevelName = item.LevelName,
                ApproverId = item.ApproverId,
                ApproverName = item.ApproverName,
                ApproverEmail = item.ApproverEmail,
                ApproverRole = item.ApproverRole,
                BusinessUnitId = item.BusinessUnitId,
                BusinessUnitCode = item.BusinessUnitCode,
                BusinessUnitName = item.BusinessUnitName,
                DepartmentId = item.DepartmentId,
                DepartmentCode = item.DepartmentCode,
                DepartmentName = item.DepartmentName,
                Sequence = item.Sequence
            });
        }
        _approvalPlan.RaiseListChangedEvents = true;
        _approvalPlan.ResetBindings();
        _planSource = _approvalPlan.Count == 0
            ? ApprovalPlanDisplaySource.DefaultMatrix
            : ApprovalPlanDisplaySource.ExistingDcr;
        var next = Math.Max(1, _approvalPlan.Select(x => x.LevelNumber).DefaultIfEmpty(1).Max());
        _numApprovalLevel.Value = Math.Min(_numApprovalLevel.Maximum, next);
        UpdateSuggestedLevelName(forceWhenEmpty: true);
        UpdatePlanModeLabel();
    }

    protected override void ApplyTo(DcrEditModel model)
    {
        model.ApprovalPlan = _approvalPlan
            .Where(x => x.ApproverId > 0 && x.LevelNumber > 0)
            .OrderBy(x => x.LevelNumber)
            .ThenBy(x => x.Sequence)
            .Select(x => new ApprovalPlanEditItem
            {
                Id = x.Id,
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
            })
            .ToList();
    }

    public void SetApprovalPlan(
        IEnumerable<ApprovalPlanEditItem> items,
        ApprovalPlanDisplaySource source = ApprovalPlanDisplaySource.Custom)
    {
        _approvalPlan.RaiseListChangedEvents = false;
        _approvalPlan.Clear();
        foreach (var item in items.OrderBy(x => x.LevelNumber).ThenBy(x => x.Sequence))
        {
            _approvalPlan.Add(new ApprovalPlanEditItem
            {
                Id = item.Id,
                LevelNumber = item.LevelNumber,
                LevelName = item.LevelName,
                ApproverId = item.ApproverId,
                ApproverName = item.ApproverName,
                ApproverEmail = item.ApproverEmail,
                ApproverRole = item.ApproverRole,
                BusinessUnitId = item.BusinessUnitId,
                BusinessUnitCode = item.BusinessUnitCode,
                BusinessUnitName = item.BusinessUnitName,
                DepartmentId = item.DepartmentId,
                DepartmentCode = item.DepartmentCode,
                DepartmentName = item.DepartmentName,
                Sequence = item.Sequence
            });
        }
        _approvalPlan.RaiseListChangedEvents = true;
        _approvalPlan.ResetBindings();
        _planSource = _approvalPlan.Count == 0 ? ApprovalPlanDisplaySource.DefaultMatrix : source;
        _numApprovalLevel.Value = Math.Min(_numApprovalLevel.Maximum, Math.Max(1, _approvalPlan.Select(x => x.LevelNumber).DefaultIfEmpty(1).Max()));
        UpdateSuggestedLevelName(forceWhenEmpty: true);
        UpdatePlanModeLabel();
        RaiseDataChanged();
    }

    public void SetApprovalTemplates(IEnumerable<ApprovalPlanTemplateEditModel> templates)
    {
        var selectedId = (_cmbGlobalTemplates.SelectedItem as ApprovalPlanTemplateEditModel)?.Id;
        _globalTemplates.RaiseListChangedEvents = false;
        _globalTemplates.Clear();
        foreach (var template in templates.OrderByDescending(x => x.IsDefault).ThenBy(x => x.Name))
            _globalTemplates.Add(template);
        _globalTemplates.RaiseListChangedEvents = true;
        _globalTemplates.ResetBindings();
        if (_globalTemplates.Count > 0)
            _cmbGlobalTemplates.SelectedItem = _globalTemplates.FirstOrDefault(x => x.Id == selectedId)
                                                 ?? _globalTemplates.FirstOrDefault(x => x.IsDefault)
                                                 ?? _globalTemplates[0];
        _btnLoadGlobalTemplate.Enabled = _editable && _globalTemplates.Count > 0;
    }

    public void SetApprovalPlanFromTemplate(ApprovalPlanTemplateEditModel template)
    {
        _globalTemplateName = template.Name;
        SetApprovalPlan(template.ApprovalPlan, ApprovalPlanDisplaySource.GlobalTemplate);
    }

    private void LoadSelectedGlobalTemplate()
    {
        if (!_editable || _cmbGlobalTemplates.SelectedItem is not ApprovalPlanTemplateEditModel selected)
            return;
        if (_approvalPlan.Count > 0 && UiMessageBox.Show(
                this,
                $"Thay line hiện tại bằng cấu hình '{selected.Name}' của Rank {selected.Rank}?",
                "Nạp cấu hình theo Rank",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        SetApprovalPlanFromTemplate(selected);
    }

    public void MarkApprovalPlanAsPersonalTemplate()
    {
        if (_approvalPlan.Count == 0) return;
        _planSource = ApprovalPlanDisplaySource.PersonalTemplate;
        UpdatePlanModeLabel();
    }

    public void RefreshLocalizedText() => UpdatePlanModeLabel();

    public override void SetEditable(bool editable)
    {
        _editable = editable;
        _txtApproverSearch.Enabled = editable;
        _btnSearchApprover.Enabled = editable;
        _cmbApproverResults.Enabled = editable;
        _numApprovalLevel.Enabled = editable;
        _txtLevelName.Enabled = editable;
        _btnAddApprover.Enabled = editable;
        _btnNextLevel.Enabled = editable;
        _btnRemoveApprover.Enabled = editable;
        _btnSuggestApproval.Enabled = editable;
        _btnLoadApprovalTemplate.Enabled = editable;
        _btnSaveApprovalTemplate.Enabled = editable && _approvalPlan.Count > 0;
        _cmbGlobalTemplates.Enabled = editable;
        _btnLoadGlobalTemplate.Enabled = editable && _globalTemplates.Count > 0;
        _gridApprovalPlan.ReadOnly = true;
        _btnOpenAttachment.Enabled = _gridAttachments.Rows.Count > 0;
        UpdatePlanModeLabel();
    }

    private async Task SearchApproverAsync()
    {
        if (!_editable || SearchApproversAsync is null)
            return;
        try
        {
            _btnSearchApprover.Enabled = false;
            var rows = await SearchApproversAsync(_txtApproverSearch.Text);
            _approverResults.RaiseListChangedEvents = false;
            _approverResults.Clear();
            foreach (var row in rows)
                _approverResults.Add(row);
            _approverResults.RaiseListChangedEvents = true;
            _approverResults.ResetBindings();
            if (_approverResults.Count > 0)
                _cmbApproverResults.SelectedIndex = 0;
            else
                DCRManagementSystem.Helpers.UiMessageBox.Show("Không tìm thấy user hoạt động phù hợp.", "Tìm approver", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            DCRManagementSystem.Helpers.UiMessageBox.Show(ex.Message, "Tìm approver", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnSearchApprover.Enabled = _editable;
        }
    }

    private void AddSelectedApprover()
    {
        if (!_editable || _cmbApproverResults.SelectedItem is not ApproverSearchItem user)
        {
            DCRManagementSystem.Helpers.UiMessageBox.Show("Hãy tìm và chọn người phê duyệt trước.", "Luồng phê duyệt", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var level = (int)_numApprovalLevel.Value;
        if (_approvalPlan.Any(x => x.ApproverId == user.UserId))
        {
            DCRManagementSystem.Helpers.UiMessageBox.Show("Người này đã có trong luồng phê duyệt của DCR. Mỗi người chỉ được chọn một lần để sau khi đã xử lý DCR sẽ chỉ còn quyền xem.", "Luồng phê duyệt", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var levelName = string.IsNullOrWhiteSpace(_txtLevelName.Text)
            ? $"Cấp phê duyệt {level}"
            : _txtLevelName.Text.Trim();
        var sequence = _approvalPlan.Where(x => x.LevelNumber == level).Select(x => x.Sequence).DefaultIfEmpty(0).Max() + 1;
        _approvalPlan.Add(new ApprovalPlanEditItem
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
            Sequence = sequence
        });
        _planSource = ApprovalPlanDisplaySource.Custom;
        NormalizeLevelNames(level, levelName);
        UpdatePlanModeLabel();
        RaiseDataChanged();
    }

    private void AddNextLevel()
    {
        if (!_editable) return;
        var max = _approvalPlan.Select(x => x.LevelNumber).DefaultIfEmpty(0).Max();
        var next = Math.Clamp(max + 1, 1, (int)_numApprovalLevel.Maximum);
        _numApprovalLevel.Value = next;
        _txtLevelName.Text = $"Cấp phê duyệt {next}";
        _txtApproverSearch.Focus();
    }

    private void RemoveSelectedApprover()
    {
        if (!_editable || _gridApprovalPlan.CurrentRow?.DataBoundItem is not ApprovalPlanEditItem selected)
            return;
        _approvalPlan.Remove(selected);
        _planSource = _approvalPlan.Count == 0
            ? ApprovalPlanDisplaySource.DefaultMatrix
            : ApprovalPlanDisplaySource.Custom;
        RenumberSequences();
        UpdatePlanModeLabel();
        RaiseDataChanged();
    }

    private void NormalizeLevelNames(int level, string levelName)
    {
        foreach (var item in _approvalPlan.Where(x => x.LevelNumber == level))
            item.LevelName = levelName;
        _approvalPlan.ResetBindings();
    }

    private void RenumberSequences()
    {
        foreach (var group in _approvalPlan.GroupBy(x => x.LevelNumber))
        {
            var sequence = 1;
            foreach (var item in group.OrderBy(x => x.Sequence).ThenBy(x => x.ApproverId))
                item.Sequence = sequence++;
        }
        _approvalPlan.ResetBindings();
    }

    private void UpdateSuggestedLevelName(bool forceWhenEmpty = false)
    {
        var level = (int)_numApprovalLevel.Value;
        var existing = _approvalPlan.FirstOrDefault(x => x.LevelNumber == level)?.LevelName;
        if (!string.IsNullOrWhiteSpace(existing))
        {
            _txtLevelName.Text = existing;
            return;
        }
        if (forceWhenEmpty || string.IsNullOrWhiteSpace(_txtLevelName.Text) || _txtLevelName.Text.StartsWith("Cấp phê duyệt ", StringComparison.OrdinalIgnoreCase))
            _txtLevelName.Text = $"Cấp phê duyệt {level}";
    }

    private void UpdatePlanModeLabel()
    {
        if (_approvalPlan.Count == 0)
        {
            _lblPlanMode.Text = UiLanguageManager.T(
                "Routing: Approval Matrix mặc định (chưa chọn approver tùy chỉnh).",
                "Routing: Default Approval Matrix (no custom approver selected).");
            _lblPlanMode.ForeColor = UiTheme.TextSecondary;
            _btnSaveApprovalTemplate.Enabled = false;
            return;
        }

        var levels = _approvalPlan.Select(x => x.LevelNumber).Distinct().Count();
        var parallelLevels = _approvalPlan.GroupBy(x => x.LevelNumber).Count(x => x.Count() > 1);
        var source = _planSource switch
        {
            ApprovalPlanDisplaySource.PersonalTemplate => UiLanguageManager.T("Mẫu cá nhân đã lưu", "Saved personal template"),
            ApprovalPlanDisplaySource.GlobalTemplate => UiLanguageManager.T(
                $"Cấu hình theo Rank: {_globalTemplateName}",
                $"Rank template: {_globalTemplateName}"),
            ApprovalPlanDisplaySource.OrganizationSuggestion => UiLanguageManager.T("Gợi ý theo tổ chức", "Organization suggestion"),
            ApprovalPlanDisplaySource.OrganizationFallback => UiLanguageManager.T(
                "Gợi ý tổ chức (mẫu cá nhân không khả dụng)",
                "Organization fallback (personal template unavailable)"),
            ApprovalPlanDisplaySource.ExistingDcr => UiLanguageManager.T("Line đã lưu trong DCR", "Line saved in this DCR"),
            _ => UiLanguageManager.T("Tùy chỉnh cho DCR này", "Customized for this DCR")
        };
        _lblPlanMode.Text = UiLanguageManager.IsEnglish
            ? $"Routing — {source}: {levels} level(s), {_approvalPlan.Count} approver(s)" +
              (parallelLevels > 0 ? $", {parallelLevels} parallel level(s)." : ".")
            : $"Routing — {source}: {levels} cấp, {_approvalPlan.Count} approver" +
              (parallelLevels > 0 ? $", {parallelLevels} cấp có đồng phê duyệt." : ".");
        _lblPlanMode.ForeColor = UiTheme.Primary;
        _btnSaveApprovalTemplate.Enabled = _editable;
    }

    private static void ConfigureSmallButton(Button button, string text, int width)
    {
        button.Text = text;
        button.Width = width;
        button.Height = 30;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = UiTheme.Border;
        button.BackColor = Color.White;
        button.ForeColor = UiTheme.TextPrimary;
        button.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
    }
}
