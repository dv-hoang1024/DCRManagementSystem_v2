using System.Diagnostics;
using System.Text;
using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;
using DCRManagementSystem.Services;
using DCRManagementSystem.UserControls;

namespace DCRManagementSystem.Forms;

public sealed class DCRDetailForm : Form
{
    private readonly int? _initialRequestId;
    private int? _requestId;
    private DcrEditModel _model = new();
    private List<Department> _departments = new();
    private List<ProductLineDefinition> _productLines = new();
    private List<PartChangeTypeDefinition> _partChangeTypes = new();

    private readonly IDcrService _dcrService = AppServices.CreateDcrService();
    private readonly IAttachmentService _attachmentService = AppServices.CreateAttachmentService();
    private readonly IAuthService _authService = AppServices.CreateAuthService();
    private readonly IPdfService _pdfService = AppServices.CreatePdfService();
    private readonly DraftRecoveryService _recoveryService = AppServices.CreateDraftRecoveryService();

    private readonly List<DcrWizardStepControl> _steps = new();
    private readonly GeneralInfoStepControl _generalStep = new();
    private readonly PartDetailsStepControl _partsStep = new();
    private readonly DefectDescriptionStepControl _defectStep = new();
    private readonly PlanTrackingStepControl _planStep = new();
    private readonly ReviewSubmitStepControl _reviewStep = new();

    private int _currentStepIndex;
    private bool _suppressDirty;
    private bool _dirty;
    private bool _saving;
    private bool _submitting;
    private bool _loaded;
    private bool _editable;
    private long _changeVersion;
    private string _approvalPlanRank = DcrRanks.C;

    private readonly System.Windows.Forms.Timer _autoSaveTimer = new() { Interval = 30000 };
    private readonly Panel _stepHost = new();
    private readonly Label _lblDocumentTitle = new();
    private readonly Label _lblStepCounter = new();
    private readonly Label _lblStepTitle = new();
    private readonly Label _lblStepDescription = new();
    private readonly Label _lblSaveState = new();
    private readonly Label _lblReturnReason = new();
    private readonly Panel _progressTrack = new();
    private readonly Panel _progressValue = new();
    private readonly Button _btnBack = new();
    private readonly Button _btnNext = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnSubmit = new();
    private readonly Button _btnApprove = new();
    private readonly Button _btnReject = new();
    private readonly Button _btnReturn = new();
    private readonly Button _btnOpenFinalPdf = new();
    private readonly Button _btnExportPdf = new();
    private readonly Button _btnPrint = new();
    private readonly Button _btnLanguage = new();

    private static readonly string[] StepTitles =
    {
        "Thông tin chung & Dòng sản phẩm",
        "Danh sách linh kiện",
        "Vấn đề, giải pháp & Tài liệu",
        "Kế hoạch & Tracking",
        "Review & Submit"
    };

    private static readonly string[] StepDescriptions =
    {
        "Khai báo thông tin người tạo, dòng sản phẩm, build stage và các mã ECR/ECN/MCN.",
        "Thêm các linh kiện chịu ảnh hưởng. Part Number, Part Name và Quantity là thông tin bắt buộc.",
        "Mô tả vấn đề, giải pháp, phòng ban bị ảnh hưởng và tải tài liệu kỹ thuật liên quan.",
        "Xác nhận nhận diện vật liệu, MRD timing, rework và khoảng thời gian áp dụng DCR.",
        "Kiểm tra toàn bộ nội dung trước khi Submit và theo dõi lịch sử phê duyệt/audit."
    };

    public DCRDetailForm(int? requestId)
    {
        _initialRequestId = requestId;
        _requestId = requestId;
        Text = requestId.HasValue ? "DCR" : "New DCR";
        StartPosition = FormStartPosition.CenterParent;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1120, 760);
        MaximumSize = new Size(1920, 1080);

        BuildUi();
        UiLanguageManager.LanguageChanged += OnLanguageChanged;
        FormClosed += (_, _) => UiLanguageManager.LanguageChanged -= OnLanguageChanged;
        RegisterStepEvents();
        Shown += async (_, _) => await LoadDataAsync();
        FormClosing += OnFormClosing;
        _autoSaveTimer.Tick += async (_, _) => await AutoSaveAsync();
    }

    private void BuildUi()
    {
        UiTheme.ApplyForm(this);
        SuspendLayout();
        var header = BuildHeader();
        var footer = BuildFooter();
        _stepHost.Dock = DockStyle.Fill;
        _stepHost.BackColor = UiTheme.Background;

        _steps.AddRange([_generalStep, _partsStep, _defectStep, _planStep, _reviewStep]);
        foreach (var step in _steps)
        {
            step.Dock = DockStyle.Fill;
            step.Visible = false;
            _stepHost.Controls.Add(step);
        }

        Controls.Add(_stepHost);
        Controls.Add(footer);
        Controls.Add(header);
        ShowStep(0);
        ResumeLayout(true);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        _btnLanguage.Text = UiLanguageManager.ToggleButtonText;
        ShowStep(_currentStepIndex);
        RefreshReviewText();
        _reviewStep.RefreshLocalizedText();
        UiLanguageManager.Apply(this);
    }

    private void RegisterStepEvents()
    {
        foreach (var step in _steps)
            step.DataChanged += (_, _) => MarkDirty();
        _generalStep.RankChanged += (_, _) => OnRankChanged();
        _defectStep.UploadAttachmentRequested += async (_, _) => await UploadAttachmentAsync();
        _defectStep.OpenAttachmentRequested += async (_, _) => await OpenAttachmentAsync();
        _defectStep.DeleteAttachmentRequested += async (_, _) => await DeleteAttachmentAsync();
        _reviewStep.RefreshPdfRequested += async (_, _) => await RefreshReviewPdfSafeAsync();
        _reviewStep.OpenAttachmentRequested += async (_, _) => await OpenReviewAttachmentAsync();
        _reviewStep.SearchApproversAsync = query => _dcrService.SearchApproversAsync(query);
        _reviewStep.SuggestedApprovalRequested += async (_, _) => await ApplySuggestedApprovalAsync(true);
        _reviewStep.SavedApprovalLoadRequested += async (_, _) => await LoadSavedApprovalPlanAsync();
        _reviewStep.SavedApprovalSaveRequested += async (_, _) => await SaveCurrentApprovalPlanAsync();
    }

    private Panel BuildHeader()
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 165, BackColor = Color.White, Padding = new Padding(28, 14, 28, 12) };
        var appTitle = new Label
        {
            Text = "Temporary Deviation Change Request / Order",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(55, 55, 60),
            Location = new Point(28, 14)
        };
        _lblDocumentTitle.AutoSize = true;
        _lblDocumentTitle.Font = new Font("Segoe UI", 9.5F);
        _lblDocumentTitle.ForeColor = UiTheme.TextSecondary;
        _lblDocumentTitle.Location = new Point(28, 42);
        _lblDocumentTitle.Text = "DCR mới • Bản nháp";

        _lblSaveState.AutoSize = true;
        _lblSaveState.Font = new Font("Segoe UI", 9F);
        _lblSaveState.ForeColor = UiTheme.TextSecondary;
        _lblSaveState.Text = "Chưa lưu";

        _btnLanguage.Text = UiLanguageManager.ToggleButtonText;
        _btnLanguage.Size = new Size(48, 30);
        UiTheme.StyleSecondaryButton(_btnLanguage);
        _btnLanguage.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
        _btnLanguage.Click += (_, _) => UiLanguageManager.Toggle();

        _lblStepTitle.AutoSize = true;
        _lblStepTitle.Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold);
        _lblStepTitle.ForeColor = UiTheme.TextPrimary;
        _lblStepTitle.Location = new Point(28, 76);
        _lblStepDescription.AutoSize = false;
        _lblStepDescription.Font = new Font("Segoe UI", 9.5F);
        _lblStepDescription.ForeColor = UiTheme.TextSecondary;
        _lblStepDescription.Location = new Point(28, 114);
        _lblStepDescription.Height = 22;
        _lblStepCounter.AutoSize = true;
        _lblStepCounter.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);
        _lblStepCounter.ForeColor = UiTheme.Primary;
        _lblReturnReason.AutoSize = false;
        _lblReturnReason.Visible = false;
        _lblReturnReason.BackColor = UiTheme.WarningSoft;
        _lblReturnReason.ForeColor = UiTheme.Warning;
        _lblReturnReason.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
        _lblReturnReason.TextAlign = ContentAlignment.MiddleLeft;
        _lblReturnReason.Padding = new Padding(10, 0, 10, 0);
        _lblReturnReason.Height = 28;
        _progressTrack.Height = 5;
        _progressTrack.BackColor = Color.FromArgb(230, 230, 234);
        _progressTrack.Location = new Point(28, 150);
        _progressTrack.Controls.Add(_progressValue);
        _progressValue.Dock = DockStyle.Left;
        _progressValue.BackColor = UiTheme.Accent;
        panel.Controls.AddRange([appTitle, _lblDocumentTitle, _lblSaveState, _btnLanguage, _lblStepCounter, _lblStepTitle, _lblStepDescription, _lblReturnReason, _progressTrack, UiTheme.CreateDivider(DockStyle.Bottom)]);
        panel.Resize += (_, _) =>
        {
            _btnLanguage.Location = new Point(Math.Max(28, panel.ClientSize.Width - _btnLanguage.Width - 28), 10);
            _lblSaveState.Location = new Point(Math.Max(28, _btnLanguage.Left - _lblSaveState.Width - 12), 16);
            _lblStepCounter.Location = new Point(Math.Max(28, panel.ClientSize.Width - _lblStepCounter.Width - 28), 82);
            _lblStepDescription.Width = Math.Max(420, panel.ClientSize.Width - 56);
            _progressTrack.Width = Math.Max(100, panel.ClientSize.Width - 56);
            _lblReturnReason.Width = Math.Max(100, panel.ClientSize.Width - 56);
            UpdateHeaderLayoutForReturnReason();
            UpdateProgressBar();
        };
        return panel;
    }

    private Panel BuildFooter()
    {
        var panel = new Panel { Dock = DockStyle.Bottom, Height = 76, BackColor = Color.White, Padding = new Padding(24, 14, 24, 14) };
        panel.Controls.Add(UiTheme.CreateDivider(DockStyle.Top));
        ConfigureButton(_btnBack, "←  Back", 105);
        ConfigureButton(_btnNext, "Next  →", 105, primary: true);
        ConfigureButton(_btnSave, "Save Draft", 110);
        ConfigureButton(_btnSubmit, "Submit", 105, primary: true);
        ConfigureButton(_btnApprove, "Approve", 105, primary: true);
        ConfigureButton(_btnReject, "Reject", 95, danger: true);
        ConfigureButton(_btnReturn, "Request Info", 120);
        ConfigureButton(_btnOpenFinalPdf, "Final PDF", 105);
        ConfigureButton(_btnExportPdf, "Export PDF", 110);
        ConfigureButton(_btnPrint, "Print", 85);
        _btnSave.Visible = _btnSubmit.Visible = _btnApprove.Visible = _btnReject.Visible = _btnReturn.Visible = _btnOpenFinalPdf.Visible = false;
        _btnExportPdf.Enabled = _btnPrint.Enabled = false;
        _btnBack.Click += (_, _) => { if (_currentStepIndex > 0) ShowStep(_currentStepIndex - 1); };
        _btnNext.Click += async (_, _) => await NextAsync();
        _btnSave.Click += async (_, _) => await SaveDraftInteractiveAsync();
        _btnSubmit.Click += async (_, _) => await SubmitAsync();
        _btnApprove.Click += async (_, _) => await DecideAsync(ApprovalDecisions.Approved);
        _btnReject.Click += async (_, _) => await DecideAsync(ApprovalDecisions.Rejected);
        _btnReturn.Click += async (_, _) => await DecideAsync(ApprovalDecisions.Returned);
        _btnOpenFinalPdf.Click += async (_, _) => await OpenFinalPdfAsync();
        _btnExportPdf.Click += async (_, _) => await ExportPdfAsync(false);
        _btnPrint.Click += async (_, _) => await ExportPdfAsync(true);
        panel.Controls.AddRange([_btnBack, _btnNext, _btnSave, _btnSubmit, _btnApprove, _btnReject, _btnReturn, _btnOpenFinalPdf, _btnExportPdf, _btnPrint]);
        panel.Resize += (_, _) => LayoutFooterButtons(panel);
        LayoutFooterButtons(panel);
        return panel;
    }

    private async Task LoadDataAsync()
    {
        if (CurrentUser.User is null) { Close(); return; }
        try
        {
            _suppressDirty = true;
            if (_requestId.HasValue && !await _dcrService.CanViewAsync(_requestId.Value, CurrentUser.User.UserId, CurrentUser.IsAdmin))
                throw new UnauthorizedAccessException("Bạn không có quyền xem DCR này.");
            _departments = await _dcrService.GetActiveDepartmentsAsync();
            _productLines = await _dcrService.GetActiveProductLinesAsync();
            _partChangeTypes = await _dcrService.GetActivePartChangeTypesAsync();
            _model = _requestId.HasValue ? await _dcrService.LoadEditDataAsync(_requestId.Value) : await _dcrService.CreateNewModelAsync(CurrentUser.User.UserId);
            BindReferenceSources();
            await ApplyRecoveryIfNeededAsync();
            _approvalPlanRank = DcrRanks.Normalize(_model.Rank);
            PopulateControls();
            await RefreshAuxiliaryAsync();
            _editable = IsEditableByCurrentUser();
            ApplyPermissions();
            UpdateDocumentState();
            var targetStep = RequestStatuses.IsEditable(_model.Status) ? Math.Clamp(_model.DraftStep <= 0 ? 0 : _model.DraftStep - 1, 0, 4) : 4;
            var autoSuggested = false;
            if (targetStep == _steps.Count - 1 && _editable)
                autoSuggested = await ApplyPreferredApprovalAsync();
            ShowStep(targetStep);
            _dirty = autoSuggested;
            _loaded = true;
            _autoSaveTimer.Start();
        }
        catch (Exception ex)
        {
            DCRManagementSystem.Helpers.UiMessageBox.Show(ex.Message, "Không thể mở DCR", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
        finally { _suppressDirty = false; }
    }

    private async Task ApplyRecoveryIfNeededAsync()
    {
        if (CurrentUser.User is null || !RequestStatuses.IsEditable(_model.Status)) return;
        var recovery = _recoveryService.TryLoad(CurrentUser.User.UserId, _requestId);
        if (recovery is null) return;
        var databaseTime = _model.Id.HasValue ? _model.LastSavedAt ?? _model.CreatedDate : DateTime.MinValue;
        if (recovery.SavedAt <= databaseTime.AddSeconds(2)) return;
        var result = DCRManagementSystem.Helpers.UiMessageBox.Show($"Có bản phục hồi cục bộ mới hơn dữ liệu trên server ({recovery.SavedAt:dd/MM/yyyy HH:mm:ss}).\r\n\r\nBạn có muốn khôi phục nội dung này không?", "Draft Recovery", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (result == DialogResult.Yes)
        {
            var recovered = recovery.Model;
            recovered.Id = _model.Id;
            recovered.RowVersion = _model.RowVersion;
            recovered.DCRNumber = _model.DCRNumber;
            recovered.RequestOwnerId = _model.RequestOwnerId;
            recovered.RequestOwnerName = _model.RequestOwnerName;
            recovered.RequestOwnerEmail = _model.RequestOwnerEmail;
            recovered.RequestOwnerPhone = _model.RequestOwnerPhone;
            recovered.CreatedBy = _model.CreatedBy;
            recovered.CreatedDate = _model.CreatedDate;
            recovered.Status = _model.Status;
            recovered.CurrentStage = _model.CurrentStage;
            recovered.ReturnedDate = _model.ReturnedDate;
            recovered.LastReturnReason = _model.LastReturnReason;
            recovered.RevisionNo = _model.RevisionNo;
            recovered.LastSavedAt = _model.LastSavedAt;
            _model = recovered;
            _dirty = true;
        }
        else _recoveryService.Delete(CurrentUser.User.UserId, _requestId);
        await Task.CompletedTask;
    }

    private void BindReferenceSources()
    {
        // Active reference lists are correct for new choices, but an existing DCR must
        // continue to display its currently stored departments even after one is disabled.
        var currentDepartments = _departments.ToList();
        AddHistoricalDepartment(currentDepartments, _model.RequestingDepartmentId,
            _model.RequestingDepartmentCode, _model.RequestingDepartmentName);
        foreach (var impact in _model.ImpactedDepartments)
            AddHistoricalDepartment(currentDepartments, impact.DepartmentId, impact.DepartmentCode, impact.DepartmentName);

        _departments = currentDepartments.OrderBy(x => x.DepartmentName).ToList();
        _generalStep.BindDepartments(_departments);
        _generalStep.BindProductLines(_productLines);
        _partsStep.BindChangeTypes(_partChangeTypes);
        _defectStep.BindDepartments(_departments);
    }

    private static void AddHistoricalDepartment(List<Department> target, int id, string code, string name)
    {
        if (id <= 0 || target.Any(x => x.Id == id)) return;
        target.Add(new Department
        {
            Id = id,
            DepartmentCode = code ?? string.Empty,
            DepartmentName = string.IsNullOrWhiteSpace(name) ? $"Phòng ban #{id} (không hoạt động)" : name,
            IsActive = false
        });
    }

    private void PopulateControls()
    {
        foreach (var step in _steps) step.BindFromModel(_model);
        RefreshReviewText();
    }

    private DcrEditModel GatherModel()
    {
        foreach (var step in _steps) step.SyncToModel(_model);
        return _model;
    }

    private async Task NextAsync()
    {
        if (_currentStepIndex >= _steps.Count - 1) return;
        try
        {
            _steps[_currentStepIndex].SyncToModel(_model);
            if (!await _steps[_currentStepIndex].ValidateStepAsync())
            {
                ShowValidation(_steps[_currentStepIndex].LastValidationResult);
                return;
            }

            var next = _currentStepIndex + 1;
            if (next == _steps.Count - 1)
                await ReconcileApprovalPlanRankAsync();
            if (_editable && !await SaveDraftCoreAsync(_currentStepIndex + 2, false, false)) return;
            if (next == _steps.Count - 1)
            {
                await ApplyPreferredApprovalAsync();
                await RefreshAuxiliaryAsync();
            }
            ShowStep(next);
        }
        catch (Exception ex)
        {
            DCRManagementSystem.Helpers.UiMessageBox.Show(ex.Message, "Không thể chuyển bước", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ApplySuggestedApprovalAsync(bool force)
    {
        if (!_editable || !RequestStatuses.IsEditable(_model.Status) || _model.RequestOwnerId <= 0) return;

        _reviewStep.SyncToModel(_model);
        if (_model.ApprovalPlan.Count > 0 && !force) return;
        if (_model.ApprovalPlan.Count > 0 && force)
        {
            var replace = DCRManagementSystem.Helpers.UiMessageBox.Show(
                this,
                $"Line hiện tại sẽ được thay bằng line chính thức của Rank {_model.Rank}, sử dụng thông tin người dùng và tổ chức mới nhất. Tiếp tục?",
                "Gợi ý line theo Rank",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (replace != DialogResult.Yes) return;
        }

        var suggestion = await _dcrService.GetSuggestedApprovalPlanAsync(_model.RequestOwnerId, _model.Rank);
        if (suggestion.ApprovalPlan.Count == 0)
        {
            if (force)
                DCRManagementSystem.Helpers.UiMessageBox.Show(this, BuildMissingApproverMessage(suggestion), "Gợi ý line theo Rank", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (force && !suggestion.IsComplete)
        {
            var usePartial = DCRManagementSystem.Helpers.UiMessageBox.Show(
                this,
                BuildMissingApproverMessage(suggestion) + "\r\n\r\nNạp phần đã tìm thấy để bạn bổ sung thủ công?",
                "Line Rank chưa đầy đủ",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (usePartial != DialogResult.Yes) return;
        }
        else if (!force && !suggestion.IsComplete)
        {
            return;
        }

        _model.ApprovalPlan = suggestion.ApprovalPlan;
        _approvalPlanRank = suggestion.Rank;
        _reviewStep.SetApprovalPlan(suggestion.ApprovalPlan, ApprovalPlanDisplaySource.OrganizationSuggestion);
        MarkDirty();
    }

    private void OnRankChanged()
    {
        if (!_loaded || _suppressDirty) return;
        var selectedRank = _generalStep.SelectedRank;
        if (string.Equals(selectedRank, _approvalPlanRank, StringComparison.Ordinal)) return;

        // A line belongs to the rank for which it was generated/saved. Do not let
        // AutoSave persist an old-rank line under a newly selected rank.
        _model.Rank = selectedRank;
        _model.ApprovalPlan = [];
        _approvalPlanRank = selectedRank;
        _reviewStep.SetApprovalPlan([], ApprovalPlanDisplaySource.DefaultMatrix);
        MarkDirty();
    }

    private async Task<bool> ApplyPreferredApprovalAsync()
    {
        if (!_editable || !RequestStatuses.IsEditable(_model.Status) ||
            _model.RequestOwnerId <= 0 || CurrentUser.User is null)
            return false;

        _reviewStep.SyncToModel(_model);
        List<ApprovalPlanTemplateEditModel> globalTemplates;
        try
        {
            globalTemplates = await _dcrService.GetApprovalPlanTemplatesAsync(_model.Rank);
        }
        catch
        {
            // A template-table/API rollout mismatch must not block opening old DCRs.
            globalTemplates = [];
        }
        _reviewStep.SetApprovalTemplates(globalTemplates);
        if (_model.ApprovalPlan.Count > 0)
            return false;

        var defaultTemplate = globalTemplates.FirstOrDefault(x => x.IsDefault) ?? globalTemplates.FirstOrDefault();
        if (defaultTemplate is not null && defaultTemplate.ApprovalPlan.Count > 0)
        {
            _model.ApprovalPlan = defaultTemplate.ApprovalPlan;
            _approvalPlanRank = DcrRanks.Normalize(_model.Rank);
            _reviewStep.SetApprovalPlanFromTemplate(defaultTemplate);
            MarkDirty();
            return true;
        }

        var personalTemplateUnavailable = false;
        List<ApprovalPlanEditItem> saved;
        try
        {
            saved = await _dcrService.GetSavedApprovalPlanAsync(CurrentUser.User.UserId, _model.Rank);
        }
        catch
        {
            // Auto-fill must never prevent a DCR from opening or advancing. A stale
            // personal template is surfaced when the user explicitly clicks Load.
            saved = [];
            personalTemplateUnavailable = true;
        }
        if (saved.Count > 0)
        {
            _model.ApprovalPlan = saved;
            _approvalPlanRank = DcrRanks.Normalize(_model.Rank);
            _reviewStep.SetApprovalPlan(saved, ApprovalPlanDisplaySource.PersonalTemplate);
            MarkDirty();
            return true;
        }

        var suggestion = await _dcrService.GetSuggestedApprovalPlanAsync(_model.RequestOwnerId, _model.Rank);
        if (!suggestion.IsComplete)
            return false;

        _model.ApprovalPlan = suggestion.ApprovalPlan;
        _approvalPlanRank = suggestion.Rank;
        _reviewStep.SetApprovalPlan(
            suggestion.ApprovalPlan,
            personalTemplateUnavailable
                ? ApprovalPlanDisplaySource.OrganizationFallback
                : ApprovalPlanDisplaySource.OrganizationSuggestion);
        MarkDirty();
        return true;
    }

    private async Task LoadSavedApprovalPlanAsync()
    {
        if (!_editable || !RequestStatuses.IsEditable(_model.Status) || CurrentUser.User is null)
            return;

        try
        {
            _reviewStep.SyncToModel(_model);
            if (_model.ApprovalPlan.Count > 0)
            {
                var replace = DCRManagementSystem.Helpers.UiMessageBox.Show(
                    this,
                    "Line phê duyệt hiện tại của DCR sẽ được thay bằng mẫu cá nhân đã lưu. Tiếp tục?",
                    "Nạp mẫu line cá nhân",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);
                if (replace != DialogResult.Yes) return;
            }

            var saved = await _dcrService.GetSavedApprovalPlanAsync(CurrentUser.User.UserId, _model.Rank);
            if (saved.Count == 0)
            {
                DCRManagementSystem.Helpers.UiMessageBox.Show(
                    this,
                    $"Bạn chưa lưu mẫu line phê duyệt cá nhân cho Rank {_model.Rank}. Hãy tạo line hiện tại rồi bấm 'Lưu line hiện tại'.",
                    "Nạp mẫu line cá nhân",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            _model.ApprovalPlan = saved;
            _approvalPlanRank = DcrRanks.Normalize(_model.Rank);
            _reviewStep.SetApprovalPlan(saved, ApprovalPlanDisplaySource.PersonalTemplate);
            MarkDirty();
        }
        catch (Exception ex)
        {
            DCRManagementSystem.Helpers.UiMessageBox.Show(
                this,
                ex.Message,
                "Nạp mẫu line cá nhân",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async Task SaveCurrentApprovalPlanAsync()
    {
        if (!_editable || !RequestStatuses.IsEditable(_model.Status) || CurrentUser.User is null)
            return;

        try
        {
            _reviewStep.SyncToModel(_model);
            if (_model.ApprovalPlan.Count == 0)
            {
                DCRManagementSystem.Helpers.UiMessageBox.Show(
                    this,
                    "Line phê duyệt hiện tại đang trống. Hãy thêm approver trước khi lưu mẫu cá nhân.",
                    "Lưu mẫu line cá nhân",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var confirm = DCRManagementSystem.Helpers.UiMessageBox.Show(
                this,
                $"Lưu line hiện tại làm mẫu cá nhân cho Rank {_model.Rank}? Mẫu Rank {_model.Rank} đã lưu trước đó sẽ được thay thế; các Rank và DCR khác không bị thay đổi.",
                "Lưu mẫu line cá nhân",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes) return;

            await _dcrService.SaveUserApprovalPlanAsync(CurrentUser.User.UserId, _model.Rank, _model.ApprovalPlan);
            _reviewStep.MarkApprovalPlanAsPersonalTemplate();
            DCRManagementSystem.Helpers.UiMessageBox.Show(
                this,
                $"Đã lưu mẫu line cá nhân Rank {_model.Rank}, gồm phòng ban, chức danh, tên và email người phê duyệt. Khi nạp lại, hệ thống sẽ làm mới các thông tin này theo UserId.",
                "Lưu mẫu line cá nhân",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            DCRManagementSystem.Helpers.UiMessageBox.Show(
                this,
                ex.Message,
                "Lưu mẫu line cá nhân",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static string BuildMissingApproverMessage(ApprovalPlanSuggestionResult suggestion)
    {
        var header = $"Chưa thể tạo đầy đủ line chính thức cho Rank {suggestion.Rank} từ dữ liệu đang active.";
        if (suggestion.MissingApprovers.Count == 0) return header;
        return header + "\r\n\r\nThiếu cấu hình:\r\n- " + string.Join("\r\n- ", suggestion.MissingApprovers);
    }

    private async Task ReconcileApprovalPlanRankAsync()
    {
        var currentRank = DcrRanks.Normalize(_model.Rank);
        if (string.Equals(currentRank, _approvalPlanRank, StringComparison.Ordinal)) return;

        _reviewStep.SyncToModel(_model);
        if (_model.ApprovalPlan.Count > 0)
        {
            var replace = DCRManagementSystem.Helpers.UiMessageBox.Show(
                this,
                $"Rank đã đổi từ {_approvalPlanRank} sang {currentRank}. Thay line hiện tại bằng mẫu/gợi ý đúng Rank {currentRank}?\r\n\r\nChọn No nếu bạn muốn giữ line hiện tại như một line tùy biến.",
                "Rank và line phê duyệt",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1);
            if (replace == DialogResult.Yes)
            {
                _model.ApprovalPlan = [];
                _reviewStep.SetApprovalPlan([], ApprovalPlanDisplaySource.DefaultMatrix);
            }
        }

        _approvalPlanRank = currentRank;
        await Task.CompletedTask;
    }

    private async Task SaveDraftInteractiveAsync()
    {
        if (!_editable || _submitting) return;
        GatherModel();
        await SaveDraftCoreAsync(_currentStepIndex + 1, false, true);
    }

    private async Task AutoSaveAsync()
    {
        if (!_loaded || !_editable || !_dirty || _saving || _submitting || CurrentUser.User is null) return;
        GatherModel();
        SaveRecovery();
        await SaveDraftCoreAsync(_currentStepIndex + 1, true, false);
    }

    private async Task<bool> SaveDraftCoreAsync(int draftStep, bool isAutoSave, bool showSuccess)
    {
        if (_saving || CurrentUser.User is null) return false;
        _saving = true;
        var oldRequestId = _requestId;
        var savedChangeVersion = _changeVersion;
        SetSaveState("Đang lưu...", UiTheme.Primary);
        SaveRecovery();
        try
        {
            var id = await _dcrService.SaveDraftAsync(_model, CurrentUser.User.UserId, CurrentUser.IsAdmin, draftStep, isAutoSave);
            _requestId = id;
            if (!oldRequestId.HasValue) _recoveryService.MoveNewDraftToRequest(CurrentUser.User.UserId, id);

            // IMPORTANT: Auto-save must be non-destructive. Do not reload the request and
            // do not call BindFromModel/PopulateControls here. Rebinding WinForms controls
            // steals focus/caret from the TextBox that the operator is currently editing.
            // SaveDraftAsync updates only server-generated metadata on the existing model
            // (Id, RowVersion, DCR number, LastSavedAt and child row identities).
            UpdateDocumentState();
            _recoveryService.Delete(CurrentUser.User.UserId, _requestId);

            if (_changeVersion == savedChangeVersion)
            {
                _dirty = false;
                SetSaveState($"Đã lưu {_model.LastSavedAt:HH:mm:ss}", UiTheme.Success);
            }
            else
            {
                // The user continued typing while the asynchronous SQL save was running.
                // Never mark those newer edits as saved. Capture them in local recovery and
                // let the next AutoSave persist them without changing focus or the current step.
                GatherModel();
                _dirty = true;
                SaveRecovery();
                SetSaveState("Có thay đổi mới • sẽ tự lưu", UiTheme.Warning);
            }

            if (showSuccess) DCRManagementSystem.Helpers.UiMessageBox.Show("Đã lưu DCR.", "DCR", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }
        catch (DcrAlreadyCreatedException ex)
        {
            _requestId = ex.RequestId;
            _model.Id = ex.RequestId;
            await ReloadLatestFromServerAsync();
            if (!isAutoSave)
                DCRManagementSystem.Helpers.UiMessageBox.Show(ex.Message, "DCR đã được lưu", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }
        catch (DcrConcurrencyException ex)
        {
            SetSaveState("Xung đột dữ liệu • cần tải lại", UiTheme.Warning);
            if (!isAutoSave)
                await HandleConcurrencyConflictAsync(ex, "Lưu Draft");
            return false;
        }
        catch (Exception ex)
        {
            SetSaveState("Server chưa lưu • đã có recovery local", UiTheme.Warning);
            if (!isAutoSave)
                DCRManagementSystem.Helpers.UiMessageBox.Show($"Không thể lưu vào server. Bản hiện tại đã được lưu vào vùng Draft Recovery trên máy này.\r\n\r\n{ex.Message}", "Lưu Draft", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        finally { _saving = false; }
    }

    private async Task SubmitAsync()
    {
        if (_submitting || _saving || !_editable || CurrentUser.User is null) return;
        _submitting = true;
        _btnSubmit.Enabled = _btnSave.Enabled = _btnBack.Enabled = _btnNext.Enabled = _stepHost.Enabled = false;
        try
        {
            await SubmitCoreAsync();
        }
        finally
        {
            _submitting = false;
            if (!IsDisposed)
            {
                _btnSubmit.Enabled = _btnSave.Enabled = _btnNext.Enabled = _stepHost.Enabled = true;
                _btnBack.Enabled = _currentStepIndex > 0;
                ApplyPermissions();
            }
        }
    }

    private async Task SubmitCoreAsync()
    {
        if (!_editable || CurrentUser.User is null) return;

        GatherModel();
        _reviewStep.SyncToModel(_model);
        if (!await _reviewStep.ValidateStepAsync())
        {
            ShowValidation(_reviewStep.LastValidationResult);
            return;
        }

        if (!await SaveDraftCoreAsync(5, false, false) || !_requestId.HasValue) return;

        var confirm = DCRManagementSystem.Helpers.UiMessageBox.Show(
            "Submit DCR vào luồng phê duyệt?\r\n\r\nSau khi Submit, nội dung sẽ chuyển sang Read-only cho người tạo. Khi approver chọn Request Info, DCR chuyển sang trạng thái Trả về để bổ sung thông tin.",
            "Submit DCR",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        try
        {
            var submitResult = await _dcrService.SubmitAsync(
                _requestId.Value,
                CurrentUser.User.UserId,
                CurrentUser.IsAdmin,
                _model.RowVersion);

            _recoveryService.Delete(CurrentUser.User.UserId, _requestId);
            _dirty = false;
            _autoSaveTimer.Stop();

            var notificationText = submitResult.Notification.ToUserMessage();
            DCRManagementSystem.Helpers.UiMessageBox.Show(
                "DCR đã được Submit và chuyển tới approver đầu tiên. Cửa sổ DCR sẽ tự động đóng.\r\n\r\n" + notificationText,
                "DCR",
                MessageBoxButtons.OK,
                submitResult.Notification.HasProblems ? MessageBoxIcon.Warning : MessageBoxIcon.Information);

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (DcrConcurrencyException ex)
        {
            await HandleConcurrencyConflictAsync(ex, "Submit thất bại");
        }
        catch (Exception ex)
        {
            DCRManagementSystem.Helpers.UiMessageBox.Show(ex.Message, "Submit thất bại", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task DecideAsync(string decision)
    {
        if (!_requestId.HasValue || CurrentUser.User is null) return;
        using var dialog = new DcrDecisionDialog(decision, CurrentUser.User.Username);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var authentication = await _authService.AuthenticateDecisionAsync(CurrentUser.User.UserId);
            var decisionResult = await _dcrService.ProcessDecisionAsync(
                _requestId.Value,
                CurrentUser.User.UserId,
                decision,
                dialog.Comment,
                authentication,
                _model.RowVersion);

            _autoSaveTimer.Stop();
            _dirty = false;

            var message = decision switch
            {
                ApprovalDecisions.Approved when decisionResult.WorkflowCompleted => "Đã phê duyệt cấp cuối. Toàn bộ workflow đã hoàn tất.",
                ApprovalDecisions.Approved when decisionResult.StageAdvanced => $"Đã Approve. DCR đã chuyển sang cấp phê duyệt tiếp theo (Stage {decisionResult.CurrentStage}).",
                ApprovalDecisions.Approved => "Đã Approve. DCR đang chờ các đồng phê duyệt còn lại trong cùng cấp.",
                ApprovalDecisions.Rejected => "DCR đã bị Reject.",
                ApprovalDecisions.Returned => "DCR đã được trả về Initiator để bổ sung thông tin.",
                _ => "Đã xử lý quyết định."
            };

            var notificationText = decisionResult.Notification.ToUserMessage();
            DCRManagementSystem.Helpers.UiMessageBox.Show(
                message + "\r\n\r\n" + notificationText + "\r\n\r\nCửa sổ DCR sẽ tự động đóng. Khi mở lại, bạn chỉ có thể xem nếu quyết định của bạn đã được ghi nhận.",
                "DCR",
                MessageBoxButtons.OK,
                decisionResult.Notification.HasProblems ? MessageBoxIcon.Warning : MessageBoxIcon.Information);

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (DcrConcurrencyException ex)
        {
            await HandleConcurrencyConflictAsync(ex, "Không thể xử lý quyết định");
        }
        catch (Exception ex)
        {
            DCRManagementSystem.Helpers.UiMessageBox.Show(ex.Message, "Không thể xử lý quyết định", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task HandleConcurrencyConflictAsync(DcrConcurrencyException exception, string title)
    {
        var reload = DCRManagementSystem.Helpers.UiMessageBox.Show(
            exception.Message +
            "\r\n\r\nCác thay đổi chưa lưu của bạn đã được giữ trong Draft Recovery cục bộ. " +
            "Bạn có muốn tải lại phiên bản mới nhất từ server ngay bây giờ không?",
            title,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (reload == DialogResult.Yes && _requestId.HasValue)
            await ReloadLatestFromServerAsync();
    }

    private async Task ReloadLatestFromServerAsync()
    {
        if (!_requestId.HasValue) return;

        _suppressDirty = true;
        try
        {
            _model = await _dcrService.LoadEditDataAsync(_requestId.Value);
            PopulateControls();
            await RefreshAuxiliaryAsync();
            _editable = IsEditableByCurrentUser();
            ApplyPermissions();
            UpdateDocumentState();
            ShowStep(RequestStatuses.IsEditable(_model.Status)
                ? Math.Clamp(_model.DraftStep <= 0 ? 0 : _model.DraftStep - 1, 0, _steps.Count - 1)
                : _steps.Count - 1);
            _dirty = false;
        }
        finally
        {
            _suppressDirty = false;
        }
    }

    private async Task ReloadAfterActionAsync()
    {
        if (!_requestId.HasValue) return;
        _suppressDirty = true;
        _model = await _dcrService.LoadEditDataAsync(_requestId.Value);
        PopulateControls();
        await RefreshAuxiliaryAsync();
        _editable = IsEditableByCurrentUser();
        ApplyPermissions();
        UpdateDocumentState();
        ShowStep(RequestStatuses.IsEditable(_model.Status) ? 0 : 4);
        _dirty = false;
        _suppressDirty = false;
    }

    private async Task UploadAttachmentAsync()
    {
        if (!_editable || CurrentUser.User is null) return;
        GatherModel();
        if (!await SaveDraftCoreAsync(Math.Max(3, _currentStepIndex + 1), false, false) || !_requestId.HasValue) return;
        using var dialog = new OpenFileDialog { Title = "Chọn tài liệu đính kèm", Filter = "All files (*.*)|*.*", Multiselect = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            foreach (var file in dialog.FileNames)
                await _attachmentService.UploadAsync(_requestId.Value, file, _defectStep.SelectedAttachmentType, CurrentUser.User.UserId, CurrentUser.IsAdmin);
            await RefreshAttachmentsAsync();
        }
        catch (Exception ex) { DCRManagementSystem.Helpers.UiMessageBox.Show(ex.Message, "Upload file", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private async Task OpenAttachmentAsync()
        => await OpenAttachmentAsync(_defectStep.SelectedAttachment);

    private async Task OpenReviewAttachmentAsync()
        => await OpenAttachmentAsync(_reviewStep.SelectedAttachment);

    private async Task OpenAttachmentAsync(AttachmentListItem? item)
    {
        if (item is null || CurrentUser.User is null) return;
        try { await _attachmentService.OpenAsync(item.Id, CurrentUser.User.UserId, CurrentUser.IsAdmin); }
        catch (Exception ex) { DCRManagementSystem.Helpers.UiMessageBox.Show(ex.Message, "Open attachment", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private async Task DeleteAttachmentAsync()
    {
        var item = _defectStep.SelectedAttachment;
        if (!_editable || CurrentUser.User is null || item is null) return;
        if (DCRManagementSystem.Helpers.UiMessageBox.Show($"Xóa attachment '{item.FileName}'?", "Attachment", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try { await _attachmentService.DeleteAsync(item.Id, CurrentUser.User.UserId, CurrentUser.IsAdmin); await RefreshAttachmentsAsync(); }
        catch (Exception ex) { DCRManagementSystem.Helpers.UiMessageBox.Show(ex.Message, "Delete attachment", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private async Task OpenFinalPdfAsync()
    {
        if (!_requestId.HasValue) return;
        try
        {
            if (string.IsNullOrWhiteSpace(_model.FinalPdfPath))
            {
                if (!CurrentUser.IsAdmin || _model.Status != RequestStatuses.Approved) throw new InvalidOperationException("Final Approved PDF chưa được tạo.");
                await _pdfService.GenerateFinalApprovedPdfAsync(_requestId.Value, CurrentUser.User?.UserId);
                _model = await _dcrService.LoadEditDataAsync(_requestId.Value);
                UpdateDocumentState(); ApplyPermissions(); RefreshReviewText();
            }
            var path = await _pdfService.GetVerifiedFinalApprovedPdfPathAsync(_requestId.Value);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex) { DCRManagementSystem.Helpers.UiMessageBox.Show(ex.Message, "Final PDF", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private async Task ExportPdfAsync(bool printAfter)
    {
        if (!_requestId.HasValue) { DCRManagementSystem.Helpers.UiMessageBox.Show("Hãy lưu DCR trước khi xuất PDF.", "PDF", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        using var dialog = new SaveFileDialog { Title = "Export DCR PDF", Filter = "PDF file (*.pdf)|*.pdf", FileName = $"{(_model.DCRNumber.Length > 0 ? _model.DCRNumber : "DCR")}_R{_model.RevisionNo}.pdf" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            await _pdfService.ExportAsync(_requestId.Value, dialog.FileName);
            if (_currentStepIndex == 4)
                await _reviewStep.ShowPdfAsync(dialog.FileName);
            Process.Start(new ProcessStartInfo { FileName = dialog.FileName, Verb = printAfter ? "print" : string.Empty, UseShellExecute = true });
        }
        catch (Exception ex) { DCRManagementSystem.Helpers.UiMessageBox.Show(ex.Message, "PDF", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private async Task RefreshAuxiliaryAsync()
    {
        if (!_requestId.HasValue)
        {
            _defectStep.SetAttachments([]);
            _reviewStep.SetAttachments([]);
            _reviewStep.SetApprovalHistory([]);
            _reviewStep.SetAuditLogs([]);
            _reviewStep.ClearPdf();
            RefreshReviewText();
            return;
        }
        await RefreshAttachmentsAsync();
        _reviewStep.SetApprovalHistory(await _dcrService.GetApprovalHistoryAsync(_requestId.Value));
        _reviewStep.SetAuditLogs(await _dcrService.GetAuditLogsAsync(_requestId.Value));
        RefreshReviewText();
    }

    private async Task RefreshAttachmentsAsync()
    {
        var items = _requestId.HasValue ? await _dcrService.GetAttachmentsAsync(_requestId.Value) : [];
        _defectStep.SetAttachments(items);
        _reviewStep.SetAttachments(items);
    }

    private void RefreshReviewText()
    {
        GatherModelSafeForReview();
        var department = _departments.FirstOrDefault(x => x.Id == _model.RequestingDepartmentId);
        var impactNames = _model.ImpactedDepartments.Where(x => x.DepartmentId > 0)
            .Select(x => _departments.FirstOrDefault(d => d.Id == x.DepartmentId)?.DepartmentName ?? $"Department #{x.DepartmentId}")
            .Distinct().ToList();
        string Tr(string vi, string en) => UiLanguageManager.T(vi, en);
        string YesNo(bool value) => value ? Tr("Có", "Yes") : Tr("Không", "No");
        var sb = new StringBuilder();
        sb.AppendLine($"DCR          : {(_model.DCRNumber.Length == 0 ? Tr("(sẽ tạo khi lưu)", "(created on save)") : _model.DCRNumber)}");
        sb.AppendLine($"{Tr("Phiên bản", "Revision"),-13}: R{_model.RevisionNo}");
        sb.AppendLine($"{Tr("Trạng thái", "Status"),-13}: {UiLanguageManager.TranslateStatus(_model.Status)}    {Tr("Cấp hiện tại", "Current Stage")}: {_model.CurrentStage}");
        sb.AppendLine($"{Tr("Người tạo", "Owner"),-13}: {_model.RequestOwnerName}");
        sb.AppendLine($"{Tr("Phòng ban", "Department"),-13}: {department?.DepartmentName ?? "-"} / {_model.ModuleGroup}");
        sb.AppendLine($"{Tr("Tiêu đề", "Title"),-13}: {_model.Title}");
        sb.AppendLine($"{Tr("Dòng sản phẩm", "Product Line"),-13}: {_model.Program}    Build Stage: {_model.BuildStage}");
        sb.AppendLine($"{Tr("Tham chiếu", "References"),-13}: ECR={_model.RelatedECR} | ECN={_model.RelatedECN} | MCN={_model.RelatedMCN}");
        sb.AppendLine();
        sb.AppendLine($"{Tr("Linh kiện", "Parts"),-13}: {_model.Parts.Count(IsMeaningfulPart)} {Tr("dòng", "item(s)")}");
        foreach (var part in _model.Parts.Where(IsMeaningfulPart))
            sb.AppendLine($"  - {part.ChangeType} | {part.PartNumber} | {part.PartName} | Qty: {part.Quantity} | {Tr("Thay thế bởi", "Replaced by")}: {part.ReplacedBy}");
        sb.AppendLine();
        sb.AppendLine($"{Tr("Vấn đề", "Problem"),-13}: {_model.ProblemDescription}");
        sb.AppendLine($"{Tr("Giải pháp", "Solution"),-13}: {_model.Solution}");
        sb.AppendLine($"{Tr("Đổi vật liệu", "Material Chg"),-13}: {_model.MaterialChangeDescription}");
        sb.AppendLine($"{Tr("Phòng ảnh hưởng", "Impacted Dept"),-13}: {(impactNames.Count == 0 ? "-" : string.Join(", ", impactNames))}");
        sb.AppendLine();
        sb.AppendLine($"{Tr("Nhận diện VL", "Material ID"),-13}: {YesNo(_model.MaterialIdentificationRequired)}    Station: {_model.MaterialUsageStation}");
        sb.AppendLine($"Supplier MRD  : {YesNo(_model.SupplierSupportsMRD)}    Arrival: {FormatDate(_model.ExpectedArrivalDate)}");
        sb.AppendLine($"{Tr("Quy trình tạm", "Temp Process"),-13}: {YesNo(_model.TemporaryProcessRequired)}    Rework: {YesNo(_model.ReworkRequired)}");
        sb.AppendLine($"{Tr("Kế hoạch", "Plan"),-13}: {FormatDate(_model.PlannedStartDate)} → {FormatDate(_model.PlannedEndDate)}    PO: {_model.ProductionOrderNumber}");
        if (_model.ApprovalPlan.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"{Tr("Luồng phê duyệt", "Approval Plan")}: {_model.ApprovalPlan.Select(x => x.LevelNumber).Distinct().Count()} {Tr("cấp", "level(s)")} / {_model.ApprovalPlan.Count} approver");
            foreach (var level in _model.ApprovalPlan.GroupBy(x => x.LevelNumber).OrderBy(x => x.Key))
                sb.AppendLine($"  - {Tr("Cấp", "Level")} {level.Key}: {string.Join(", ", level.Select(x => x.ApproverName))}");
        }
        if (!string.IsNullOrWhiteSpace(_model.FinalPdfPath)) sb.AppendLine($"Final PDF    : {_model.FinalPdfPath} | {FormatDateTime(_model.FinalPdfGeneratedAt)} | SHA256={_model.FinalPdfSha256}");
        if (!string.IsNullOrWhiteSpace(_model.LastReturnReason)) { sb.AppendLine(); sb.AppendLine($"{Tr("Lần trả gần nhất", "Last Return")}: {_model.LastReturnReason}"); }
        _reviewStep.SetReviewText(sb.ToString());
    }

    private void GatherModelSafeForReview() { if (!_loaded || _suppressDirty) return; try { GatherModel(); } catch { } }

    private void ShowStep(int index)
    {
        index = Math.Clamp(index, 0, _steps.Count - 1);
        _currentStepIndex = index;
        for (var i = 0; i < _steps.Count; i++) _steps[i].Visible = i == index;
        _lblStepTitle.Text = StepTitles[index];
        _lblStepDescription.Text = StepDescriptions[index];
        _lblStepCounter.Text = $"Bước {index + 1} / {_steps.Count}";
        _btnBack.Enabled = index > 0;
        _btnNext.Visible = index < _steps.Count - 1;
        _btnSubmit.Visible = index == 4 && _editable;
        _btnSave.Visible = _editable;
        UpdateProgressBar();
        LayoutFooterButtons(_btnBack.Parent as Panel);
        if (index == 4) { RefreshReviewText(); _ = RefreshReviewPdfSafeAsync(); }
    }

    private async Task RefreshReviewPdfSafeAsync()
    {
        try
        {
            await RefreshReviewPdfAsync();
        }
        catch (Exception ex)
        {
            _reviewStep.ClearPdf($"Không thể tạo PDF review: {ex.GetBaseException().Message}");
        }
    }

    private async Task RefreshReviewPdfAsync()
    {
        if (!_requestId.HasValue)
        {
            _reviewStep.ClearPdf();
            return;
        }

        _reviewStep.SetPdfBusy();
        var path = await _pdfService.GeneratePreviewPdfAsync(_requestId.Value);
        if (IsDisposed)
            return;
        await _reviewStep.ShowPdfAsync(path);
    }

    private void ApplyPermissions()
    {
        _editable = IsEditableByCurrentUser();
        foreach (var step in _steps) step.SetEditable(_editable);
        _btnSave.Visible = _editable;
        _btnSubmit.Visible = _editable && _currentStepIndex == 4;
        _btnApprove.Visible = _btnReject.Visible = _btnReturn.Visible = false;
        if (_requestId.HasValue && CurrentUser.User is not null) _ = UpdateDecisionButtonsAsync();
        _btnOpenFinalPdf.Visible = _requestId.HasValue && (_model.Status == RequestStatuses.Approved && (!string.IsNullOrWhiteSpace(_model.FinalPdfPath) || CurrentUser.IsAdmin));
        _btnOpenFinalPdf.Text = string.IsNullOrWhiteSpace(_model.FinalPdfPath) ? "Generate PDF" : "Final PDF";
        _btnExportPdf.Enabled = _requestId.HasValue;
        _btnPrint.Enabled = _requestId.HasValue;
    }

    private async Task UpdateDecisionButtonsAsync()
    {
        if (!_requestId.HasValue || CurrentUser.User is null) return;
        try
        {
            var canApprove = await _dcrService.CanApproveAsync(_requestId.Value, CurrentUser.User.UserId);
            if (IsDisposed) return;
            _btnApprove.Visible = _btnReject.Visible = _btnReturn.Visible = canApprove;
            LayoutFooterButtons(_btnBack.Parent as Panel);
        }
        catch { _btnApprove.Visible = _btnReject.Visible = _btnReturn.Visible = false; }
    }

    private bool IsEditableByCurrentUser()
        => CurrentUser.User is not null && RequestStatuses.IsEditable(_model.Status) && (CurrentUser.IsAdmin || _model.CreatedBy == CurrentUser.User.UserId);

    private void UpdateDocumentState()
    {
        _btnSave.Text = _model.Status == RequestStatuses.Returned
            ? UiLanguageManager.T("Lưu thay đổi", "Save Changes")
            : UiLanguageManager.T("Lưu nháp", "Save Draft");
        var number = string.IsNullOrWhiteSpace(_model.DCRNumber) ? "DCR mới" : _model.DCRNumber;
        _lblDocumentTitle.Text = $"{number} • R{_model.RevisionNo} • {_model.Status}";
        Text = $"{number} - {_model.Status}";
        _lblReturnReason.Visible = _model.Status == RequestStatuses.Returned && !string.IsNullOrWhiteSpace(_model.LastReturnReason);
        if (_lblReturnReason.Visible) _lblReturnReason.Text = $"↩ Request Info: {_model.LastReturnReason}";
        UpdateHeaderLayoutForReturnReason();
        if (_model.LastSavedAt.HasValue) SetSaveState($"Đã lưu {_model.LastSavedAt:dd/MM HH:mm:ss}", UiTheme.Success);
    }

    private void UpdateHeaderLayoutForReturnReason()
    {
        if (_lblReturnReason.Parent is not Panel panel) return;
        if (_lblReturnReason.Visible)
        {
            panel.Height = 197;
            _lblStepDescription.Location = new Point(28, 143);
            _progressTrack.Location = new Point(28, 181);
            _lblReturnReason.Location = new Point(28, 111);
        }
        else
        {
            panel.Height = 165;
            _lblStepDescription.Location = new Point(28, 114);
            _progressTrack.Location = new Point(28, 150);
        }
    }

    private void MarkDirty()
    {
        if (_suppressDirty || !_loaded || !_editable) return;
        _changeVersion++;
        _dirty = true;
        SetSaveState("Chưa lưu", UiTheme.Warning);
    }

    private void SaveRecovery()
    {
        if (CurrentUser.User is null || !_editable) return;
        try { _recoveryService.Save(CurrentUser.User.UserId, _requestId, _model); } catch { }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if ((_submitting || _saving) && DialogResult != DialogResult.OK)
        {
            e.Cancel = true;
            return;
        }
        _autoSaveTimer.Stop();
        if (!_loaded || !_dirty || !_editable || CurrentUser.User is null) return;
        try { GatherModel(); SaveRecovery(); } catch { }
    }

    private void ShowValidation(ValidationResultModel validation)
        => DCRManagementSystem.Helpers.UiMessageBox.Show("Vui lòng hoàn thành các thông tin sau:\r\n\r\n• " + string.Join("\r\n• ", validation.Errors), "Kiểm tra dữ liệu", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private void UpdateProgressBar()
    {
        if (_progressTrack.Width > 0) _progressValue.Width = (int)Math.Round(_progressTrack.Width * ((_currentStepIndex + 1d) / _steps.Count));
    }

    private void SetSaveState(string text, Color color)
    {
        _lblSaveState.Text = text;
        _lblSaveState.ForeColor = color;
        if (_lblSaveState.Parent is Panel panel) _lblSaveState.Location = new Point(Math.Max(28, panel.ClientSize.Width - _lblSaveState.Width - 28), 16);
    }

    private void LayoutFooterButtons(Panel? panel)
    {
        if (panel is null) return;
        var left = 24;
        _btnBack.Location = new Point(left, 20); left += _btnBack.Width + 10;
        _btnNext.Location = new Point(left, 20); left += _btnNext.Visible ? _btnNext.Width + 10 : 0;
        _btnSave.Location = new Point(left, 20); left += _btnSave.Visible ? _btnSave.Width + 10 : 0;
        _btnSubmit.Location = new Point(left, 20);
        var right = panel.ClientSize.Width - 24;
        foreach (var button in new[] { _btnPrint, _btnExportPdf, _btnOpenFinalPdf, _btnReturn, _btnReject, _btnApprove })
        {
            if (!button.Visible) continue;
            right -= button.Width; button.Location = new Point(right, 20); right -= 10;
        }
    }

    private static void ConfigureButton(Button button, string text, int width, bool primary = false, bool danger = false)
    {
        button.Text = text;
        button.Size = new Size(width, 38);
        if (danger) UiTheme.StyleDangerButton(button); else if (primary) UiTheme.StylePrimaryButton(button); else UiTheme.StyleSecondaryButton(button);
    }

    private static bool IsMeaningfulPart(PartEditItem x)
        => !string.IsNullOrWhiteSpace(x.PartNumber) || !string.IsNullOrWhiteSpace(x.PartName) || !string.IsNullOrWhiteSpace(x.Quantity) || !string.IsNullOrWhiteSpace(x.ReplacedBy);
    private static string FormatDate(DateTime? value) => value.HasValue ? value.Value.ToString("dd/MM/yyyy") : "-";
    private static string FormatDateTime(DateTime? value) => value.HasValue ? value.Value.ToString("dd/MM/yyyy HH:mm:ss") : "-";

}
