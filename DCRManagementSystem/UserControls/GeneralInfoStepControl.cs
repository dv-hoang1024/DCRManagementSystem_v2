using DCRManagementSystem.Models;

namespace DCRManagementSystem.UserControls;

public sealed class GeneralInfoStepControl : DcrWizardStepControl
{
    protected override int StepNumber => 1;

    private readonly TextBox _txtDcrNumber = new();
    private readonly ComboBox _cmbDepartment = new();
    private readonly TextBox _txtModuleGroup = new();
    private readonly TextBox _txtOwner = new();
    private readonly TextBox _txtOwnerEmail = new();
    private readonly TextBox _txtOwnerPhone = new();
    private readonly DateTimePicker _dtCreated = new();
    private readonly TextBox _txtTitle = new();
    private readonly ComboBox _cmbRank = new();
    private readonly ComboBox _cmbProductLine = new();
    private readonly TextBox _txtBuildStage = new();
    private readonly TextBox _txtRelatedEcr = new();
    private readonly TextBox _txtRelatedEcn = new();
    private readonly TextBox _txtRelatedMcn = new();
    private readonly List<string> _productLineNames = new();

    public event EventHandler? RankChanged;
    public string SelectedRank => DcrRanks.Normalize(_cmbRank.SelectedItem?.ToString());

    public GeneralInfoStepControl()
    {
        BuildUi();
        RegisterDirtyTracking();
    }

    public void BindDepartments(IReadOnlyCollection<Department> departments)
    {
        _cmbDepartment.DataSource = departments.ToList();
        _cmbDepartment.DisplayMember = nameof(Department.DepartmentName);
        _cmbDepartment.ValueMember = nameof(Department.Id);
    }

    public void BindProductLines(IReadOnlyCollection<ProductLineDefinition> productLines)
    {
        var selected = _cmbProductLine.SelectedItem?.ToString() ?? string.Empty;
        _productLineNames.Clear();
        _productLineNames.AddRange(productLines
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .Select(x => x.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase));

        _cmbProductLine.BeginUpdate();
        try
        {
            _cmbProductLine.Items.Clear();
            foreach (var name in _productLineNames)
                _cmbProductLine.Items.Add(name);
            if (!string.IsNullOrWhiteSpace(selected))
                SelectProductLine(selected);
        }
        finally { _cmbProductLine.EndUpdate(); }
    }

    private void BuildUi()
    {
        var page = WizardControlUi.CreateFlowPage();
        var card = WizardControlUi.CreateCard("1. Thông tin chung", "Các trường có dấu * phải được hoàn thành trước khi chuyển bước.", 672);

        WizardControlUi.ConfigureReadOnly(_txtDcrNumber);
        WizardControlUi.ConfigureReadOnly(_txtOwner);
        WizardControlUi.ConfigureReadOnly(_txtOwnerEmail);
        WizardControlUi.ConfigureReadOnly(_txtOwnerPhone);
        _cmbDepartment.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbRank.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbRank.Items.AddRange(DcrRanks.All.Cast<object>().ToArray());
        _cmbProductLine.DropDownStyle = ComboBoxStyle.DropDownList;
        _dtCreated.Enabled = false;
        _dtCreated.Format = DateTimePickerFormat.Custom;
        _dtCreated.CustomFormat = "dd/MM/yyyy HH:mm";

        WizardControlUi.AddPair(card, 92, "DCR Number", _txtDcrNumber, "Created Date", _dtCreated);
        WizardControlUi.AddPair(card, 174, "Requesting Department *", _cmbDepartment, "Module Group", _txtModuleGroup);
        WizardControlUi.AddPair(card, 256, "Request Owner", _txtOwner, "Email", _txtOwnerEmail);
        WizardControlUi.AddPair(card, 338, "Cell Phone", _txtOwnerPhone, "Rank *", _cmbRank);
        WizardControlUi.AddPair(card, 420, "Dòng sản phẩm *", _cmbProductLine, "Build Stage *", _txtBuildStage);
        WizardControlUi.AddPair(card, 502, "Related ECR #", _txtRelatedEcr, "Related ECN #", _txtRelatedEcn);
        WizardControlUi.AddSingle(card, 584, "Related MCN #", _txtRelatedMcn, 420);

        var titleCard = WizardControlUi.CreateCard("2. Tiêu đề yêu cầu", "Mô tả ngắn gọn mục đích hoặc vấn đề của DCR.", 175);
        WizardControlUi.AddSingle(titleCard, 92, "Title *", _txtTitle, 900);

        page.Controls.Add(card);
        page.Controls.Add(titleCard);
        WizardControlUi.AttachResponsiveCards(page);
        Controls.Add(page);
    }

    private void RegisterDirtyTracking()
    {
        foreach (var text in new[] { _txtModuleGroup, _txtTitle, _txtBuildStage, _txtRelatedEcr, _txtRelatedEcn, _txtRelatedMcn })
            text.TextChanged += (_, _) => RaiseDataChanged();
        _cmbDepartment.SelectedIndexChanged += (_, _) => RaiseDataChanged();
        _cmbRank.SelectedIndexChanged += (_, _) =>
        {
            RaiseDataChanged();
            RankChanged?.Invoke(this, EventArgs.Empty);
        };
        _cmbProductLine.SelectedIndexChanged += (_, _) => RaiseDataChanged();
    }

    protected override void LoadFrom(DcrEditModel model)
    {
        _txtDcrNumber.Text = model.DCRNumber;
        _cmbDepartment.SelectedValue = model.RequestingDepartmentId;
        _txtModuleGroup.Text = model.ModuleGroup;
        _txtOwner.Text = model.RequestOwnerName;
        _txtOwnerEmail.Text = model.RequestOwnerEmail;
        _txtOwnerPhone.Text = model.RequestOwnerPhone;
        _dtCreated.Value = model.CreatedDate;
        _txtTitle.Text = model.Title;
        _cmbRank.SelectedItem = DcrRanks.Normalize(model.Rank);
        SelectProductLine(model.Program);
        _txtBuildStage.Text = model.BuildStage;
        _txtRelatedEcr.Text = model.RelatedECR;
        _txtRelatedEcn.Text = model.RelatedECN;
        _txtRelatedMcn.Text = model.RelatedMCN;
    }

    protected override void ApplyTo(DcrEditModel model)
    {
        model.DCRNumber = _txtDcrNumber.Text;
        model.RequestingDepartmentId = _cmbDepartment.SelectedValue is int departmentId ? departmentId : 0;
        if (_cmbDepartment.SelectedItem is Department department)
        {
            model.RequestingDepartmentCode = department.DepartmentCode;
            model.RequestingDepartmentName = department.DepartmentName;
        }
        model.ModuleGroup = _txtModuleGroup.Text.Trim();
        model.Title = _txtTitle.Text.Trim();
        model.Rank = DcrRanks.Normalize(_cmbRank.SelectedItem?.ToString());
        model.Program = _cmbProductLine.SelectedItem?.ToString()?.Trim() ?? string.Empty;
        model.BuildStage = _txtBuildStage.Text.Trim();
        model.RelatedECR = _txtRelatedEcr.Text.Trim();
        // RelatedPPS is intentionally retained in the model/database only for historical DCR compatibility.
        model.RelatedECN = _txtRelatedEcn.Text.Trim();
        model.RelatedMCN = _txtRelatedMcn.Text.Trim();
    }

    public override void SetEditable(bool editable)
    {
        foreach (Control control in new Control[] { _cmbDepartment, _txtModuleGroup, _txtTitle, _cmbRank, _cmbProductLine, _txtBuildStage, _txtRelatedEcr, _txtRelatedEcn, _txtRelatedMcn })
            WizardControlUi.SetEditable(control, editable);
    }

    private void SelectProductLine(string? value)
    {
        value = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            _cmbProductLine.SelectedIndex = -1;
            return;
        }

        var existing = _cmbProductLine.Items.Cast<object>()
            .Select((item, index) => new { Text = item?.ToString() ?? string.Empty, Index = index })
            .FirstOrDefault(x => x.Text.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            _cmbProductLine.SelectedIndex = existing.Index;
            return;
        }

        // Preserve display of historical/inactive product lines when an old DCR is opened.
        _cmbProductLine.Items.Add(value);
        _cmbProductLine.SelectedItem = value;
    }
}
