using System.ComponentModel;
using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;

namespace DCRManagementSystem.UserControls;

public sealed class DefectDescriptionStepControl : DcrWizardStepControl
{
    protected override int StepNumber => 3;

    private readonly RichTextBox _txtProblem = new();
    private readonly RichTextBox _txtSolution = new();
    private readonly RichTextBox _txtMaterialChange = new();
    private readonly RichTextBox _txtFormFit = new();
    private readonly TextBox _txtRetrofitVolume = new();
    private readonly RichTextBox _txtRetrofitInstruction = new();
    private readonly BindingList<ImpactDepartmentEditItem> _impacts = new();
    private readonly DataGridView _gridImpacts = new();
    private readonly DataGridViewComboBoxColumn _impactDepartmentColumn = new();
    private readonly ComboBox _cmbAttachmentType = new();
    private readonly DataGridView _gridAttachments = new();
    private readonly Button _btnUploadAttachment = new();
    private readonly Button _btnOpenAttachment = new();
    private readonly Button _btnDeleteAttachment = new();

    public event EventHandler? UploadAttachmentRequested;
    public event EventHandler? OpenAttachmentRequested;
    public event EventHandler? DeleteAttachmentRequested;

    public string SelectedAttachmentType => _cmbAttachmentType.SelectedItem?.ToString() ?? AttachmentTypes.General;
    public AttachmentListItem? SelectedAttachment => _gridAttachments.CurrentRow?.DataBoundItem as AttachmentListItem;

    public DefectDescriptionStepControl()
    {
        BuildUi();
        RegisterDirtyTracking();
    }

    public void BindDepartments(IReadOnlyCollection<Department> departments)
    {
        var list = new List<Department>
        {
            new() { Id = 0, DepartmentCode = string.Empty, DepartmentName = DCRManagementSystem.Helpers.UiLanguageManager.T("-- Chọn phòng ban --", "-- Select Department --") }
        };
        list.AddRange(departments);
        _impactDepartmentColumn.DataSource = list;
        _impactDepartmentColumn.DisplayMember = nameof(Department.DepartmentName);
        _impactDepartmentColumn.ValueMember = nameof(Department.Id);
    }

    public void SetAttachments(IEnumerable<AttachmentListItem> items) => _gridAttachments.DataSource = items.ToList();

    private void BuildUi()
    {
        var page = WizardControlUi.CreateFlowPage();
        var deviation = WizardControlUi.CreateCard("3. Deviation description", "Mô tả rõ vấn đề, giải pháp và thay đổi vật liệu/thiết kế nếu có.", 805);
        foreach (var rich in new[] { _txtProblem, _txtSolution, _txtMaterialChange, _txtFormFit, _txtRetrofitInstruction })
            WizardControlUi.ConfigureRich(rich);

        WizardControlUi.AddSingle(deviation, 92, "Problem Description *", _txtProblem, 900, 100);
        WizardControlUi.AddSingle(deviation, 226, "Solution *", _txtSolution, 900, 100);
        WizardControlUi.AddSingle(deviation, 360, "Material Change", _txtMaterialChange, 900, 85);
        WizardControlUi.AddSingle(deviation, 480, "Form / Fit / Function detail", _txtFormFit, 900, 85);
        WizardControlUi.AddPair(deviation, 600, "Retrofit Volume", _txtRetrofitVolume, "Retrofit Instruction", _txtRetrofitInstruction, rightHeight: 108);

        var impacts = WizardControlUi.CreateCard("4. Impacted department(s)", "Các phòng ban được chọn sẽ được đưa vào ma trận routing ở stage Impacted Department.", 365);
        UiTheme.ConfigureGrid(_gridImpacts, readOnly: false);
        _gridImpacts.AutoGenerateColumns = false;
        _gridImpacts.Location = new Point(28, 100);
        _gridImpacts.Size = new Size(924, 220);
        _gridImpacts.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _gridImpacts.DataSource = _impacts;
        _gridImpacts.DataError += (_, e) => e.ThrowException = false;
        _impactDepartmentColumn.HeaderText = "Department *";
        _impactDepartmentColumn.DataPropertyName = nameof(ImpactDepartmentEditItem.DepartmentId);
        _impactDepartmentColumn.DisplayMember = nameof(Department.DepartmentName);
        _impactDepartmentColumn.ValueMember = nameof(Department.Id);
        _impactDepartmentColumn.FlatStyle = FlatStyle.Flat;
        _impactDepartmentColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _gridImpacts.Columns.Add(_impactDepartmentColumn);
        _gridImpacts.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Estimated Cost",
            DataPropertyName = nameof(ImpactDepartmentEditItem.EstimatedCost),
            Width = 220,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None
        });
        impacts.Controls.Add(_gridImpacts);

        var attachments = WizardControlUi.CreateCard("5. Tài liệu đính kèm","", 430);
        var lblType = UiTheme.CreateFieldLabel("Attachment Type");
        lblType.Location = new Point(28, 85);
        lblType.Size = new Size(150, 28);
        _cmbAttachmentType.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbAttachmentType.Items.AddRange(AttachmentTypes.All);
        _cmbAttachmentType.SelectedIndex = 0;
        _cmbAttachmentType.Location = new Point(180, 85);
        _cmbAttachmentType.Size = new Size(210, 30);
        UiTheme.StyleInput(_cmbAttachmentType);

        WizardControlUi.ConfigureButton(_btnUploadAttachment, "Upload File", 110, primary: true);
        WizardControlUi.ConfigureButton(_btnOpenAttachment, "Open", 85);
        WizardControlUi.ConfigureButton(_btnDeleteAttachment, "Delete", 85, danger: true);
        _btnUploadAttachment.Location = new Point(410, 83);
        _btnOpenAttachment.Location = new Point(530, 83);
        _btnDeleteAttachment.Location = new Point(625, 83);
        _btnUploadAttachment.Click += (_, _) => UploadAttachmentRequested?.Invoke(this, EventArgs.Empty);
        _btnOpenAttachment.Click += (_, _) => OpenAttachmentRequested?.Invoke(this, EventArgs.Empty);
        _btnDeleteAttachment.Click += (_, _) => DeleteAttachmentRequested?.Invoke(this, EventArgs.Empty);

        UiTheme.ConfigureGrid(_gridAttachments);
        _gridAttachments.AutoGenerateColumns = false;
        _gridAttachments.Location = new Point(28, 132);
        _gridAttachments.Size = new Size(924, 250);
        _gridAttachments.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _gridAttachments.Columns.Add(WizardControlUi.CreateTextColumn("Type", nameof(AttachmentListItem.AttachmentType), 120));
        _gridAttachments.Columns.Add(WizardControlUi.CreateTextColumn("File Name", nameof(AttachmentListItem.FileName), 260));
        _gridAttachments.Columns.Add(WizardControlUi.CreateTextColumn("Original", nameof(AttachmentListItem.FileSize), 85));
        _gridAttachments.Columns.Add(WizardControlUi.CreateTextColumn("Stored", nameof(AttachmentListItem.StoredFileSize), 85));
        _gridAttachments.Columns.Add(WizardControlUi.CreateTextColumn("ZIP", nameof(AttachmentListItem.IsCompressed), 55));
        _gridAttachments.Columns.Add(WizardControlUi.CreateTextColumn("SHA-256", nameof(AttachmentListItem.Sha256Hash), 250));
        _gridAttachments.Columns.Add(WizardControlUi.CreateTextColumn("Uploaded By", nameof(AttachmentListItem.UploadedBy), 130));
        _gridAttachments.Columns.Add(WizardControlUi.CreateTextColumn("Uploaded At", nameof(AttachmentListItem.UploadedAt), 150));
        attachments.Controls.AddRange(new Control[] { lblType, _cmbAttachmentType, _btnUploadAttachment, _btnOpenAttachment, _btnDeleteAttachment, _gridAttachments });

        page.Controls.Add(deviation);
        page.Controls.Add(impacts);
        page.Controls.Add(attachments);
        WizardControlUi.AttachResponsiveCards(page);
        Controls.Add(page);
    }

    private void RegisterDirtyTracking()
    {
        foreach (var rich in new[] { _txtProblem, _txtSolution, _txtMaterialChange, _txtFormFit, _txtRetrofitInstruction })
            rich.TextChanged += (_, _) => RaiseDataChanged();
        _txtRetrofitVolume.TextChanged += (_, _) => RaiseDataChanged();
        _gridImpacts.CellValueChanged += (_, _) => RaiseDataChanged();
        _gridImpacts.UserAddedRow += (_, _) => RaiseDataChanged();
        _gridImpacts.UserDeletedRow += (_, _) => RaiseDataChanged();
    }

    protected override void LoadFrom(DcrEditModel model)
    {
        _txtProblem.Text = model.ProblemDescription;
        _txtSolution.Text = model.Solution;
        _txtMaterialChange.Text = model.MaterialChangeDescription;
        _txtFormFit.Text = model.FormFitFunctionDetail;
        _txtRetrofitVolume.Text = model.RetrofitVolume;
        _txtRetrofitInstruction.Text = model.RetrofitInstruction;
        _impacts.RaiseListChangedEvents = false;
        _impacts.Clear();
        foreach (var item in model.ImpactedDepartments)
            _impacts.Add(new ImpactDepartmentEditItem
            {
                Id = item.Id,
                DepartmentId = item.DepartmentId,
                DepartmentCode = item.DepartmentCode,
                DepartmentName = item.DepartmentName,
                EstimatedCost = item.EstimatedCost
            });
        _impacts.RaiseListChangedEvents = true;
        _impacts.ResetBindings();
    }

    protected override void ApplyTo(DcrEditModel model)
    {
        _gridImpacts.EndEdit();
        if (BindingContext[_impacts] is CurrencyManager manager)
            manager.EndCurrentEdit();
        model.ProblemDescription = _txtProblem.Text.Trim();
        model.Solution = _txtSolution.Text.Trim();
        model.MaterialChangeDescription = _txtMaterialChange.Text.Trim();
        model.FormFitFunctionDetail = _txtFormFit.Text.Trim();
        model.RetrofitVolume = _txtRetrofitVolume.Text.Trim();
        model.RetrofitInstruction = _txtRetrofitInstruction.Text.Trim();
        model.ImpactedDepartments = _impacts.Select(x => new ImpactDepartmentEditItem
        {
            Id = x.Id,
            DepartmentId = x.DepartmentId,
            DepartmentCode = x.DepartmentCode,
            DepartmentName = x.DepartmentName,
            EstimatedCost = x.EstimatedCost
        }).ToList();
    }

    public override void SetEditable(bool editable)
    {
        foreach (Control control in new Control[] { _txtProblem, _txtSolution, _txtMaterialChange, _txtFormFit, _txtRetrofitVolume, _txtRetrofitInstruction })
            WizardControlUi.SetEditable(control, editable);
        _gridImpacts.ReadOnly = !editable;
        _gridImpacts.AllowUserToAddRows = editable;
        _gridImpacts.AllowUserToDeleteRows = editable;
        _cmbAttachmentType.Enabled = editable;
        _btnUploadAttachment.Enabled = editable;
        _btnDeleteAttachment.Enabled = editable;
        _btnOpenAttachment.Enabled = true;
    }
}
