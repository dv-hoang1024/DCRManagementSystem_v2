using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;

namespace DCRManagementSystem.UserControls;

public sealed class PlanTrackingStepControl : DcrWizardStepControl
{
    protected override int StepNumber => 4;

    private readonly CheckBox _chkMaterialIdentification = new();
    private readonly TextBox _txtMaterialUsageStation = new();
    private readonly CheckBox _chkSupplierMrd = new();
    private readonly DateTimePicker _dtExpectedArrival = new();
    private readonly CheckBox _chkTemporaryProcess = new();
    private readonly CheckBox _chkRework = new();
    private readonly DateTimePicker _dtPlannedStart = new();
    private readonly DateTimePicker _dtPlannedEnd = new();
    private readonly TextBox _txtProductionOrder = new();

    public PlanTrackingStepControl()
    {
        BuildUi();
        RegisterDirtyTracking();
    }

    private void BuildUi()
    {
        var page = WizardControlUi.CreateFlowPage();
        var card = WizardControlUi.CreateCard("Kế hoạch, MRD & Tracking", "Thông tin kiểm soát thời điểm áp dụng, nhận diện vật liệu và rework.", 650);

        WizardControlUi.ConfigureCheck(_chkMaterialIdentification, "DCR material must be identified with DCO material labels");
        WizardControlUi.ConfigureCheck(_chkSupplierMrd, "Supplier can support MRD timing");
        WizardControlUi.ConfigureCheck(_chkTemporaryProcess, "Temporary process needed");
        WizardControlUi.ConfigureCheck(_chkRework, "Rework on the part needed");
        _chkMaterialIdentification.Location = new Point(28, 94);
        _chkSupplierMrd.Location = new Point(28, 195);
        _chkTemporaryProcess.Location = new Point(28, 325);
        _chkRework.Location = new Point(500, 325);

        var lblStation = UiTheme.CreateFieldLabel("Station for Material Usage");
        lblStation.Location = new Point(28, 132);
        lblStation.Size = new Size(220, 28);
        _txtMaterialUsageStation.Location = new Point(250, 132);
        _txtMaterialUsageStation.Size = new Size(340, 30);
        UiTheme.StyleInput(_txtMaterialUsageStation);

        WizardControlUi.ConfigureNullableDate(_dtExpectedArrival);
        WizardControlUi.ConfigureNullableDate(_dtPlannedStart);
        WizardControlUi.ConfigureNullableDate(_dtPlannedEnd);
        WizardControlUi.AddFieldAt(card, 28, 238, "Expected Arrival Date", _dtExpectedArrival, 280);
        WizardControlUi.AddFieldAt(card, 500, 238, "Production Order Number", _txtProductionOrder, 360);
        WizardControlUi.AddFieldAt(card, 28, 390, "Planned Start Date", _dtPlannedStart, 280);
        WizardControlUi.AddFieldAt(card, 500, 390, "Planned End Date", _dtPlannedEnd, 280);

        var note = new Label
        {
            Text = "Lưu ý: Planned Start Date không được sau Planned End Date. Nếu yêu cầu nhận diện vật liệu, Station for Material Usage là bắt buộc.",
            AutoSize = false,
            ForeColor = UiTheme.TextSecondary,
            Font = new Font("Segoe UI", 9.5F),
            Location = new Point(28, 505),
            Size = new Size(900, 55)
        };
        card.Controls.AddRange(new Control[] { _chkMaterialIdentification, _chkSupplierMrd, _chkTemporaryProcess, _chkRework, lblStation, _txtMaterialUsageStation, note });
        page.Controls.Add(card);
        WizardControlUi.AttachResponsiveCards(page);
        Controls.Add(page);
    }

    private void RegisterDirtyTracking()
    {
        _txtMaterialUsageStation.TextChanged += (_, _) => RaiseDataChanged();
        _txtProductionOrder.TextChanged += (_, _) => RaiseDataChanged();
        _chkMaterialIdentification.CheckedChanged += (_, _) => RaiseDataChanged();
        _chkSupplierMrd.CheckedChanged += (_, _) => RaiseDataChanged();
        _chkTemporaryProcess.CheckedChanged += (_, _) => RaiseDataChanged();
        _chkRework.CheckedChanged += (_, _) => RaiseDataChanged();
        _dtExpectedArrival.ValueChanged += (_, _) => RaiseDataChanged();
        _dtPlannedStart.ValueChanged += (_, _) => RaiseDataChanged();
        _dtPlannedEnd.ValueChanged += (_, _) => RaiseDataChanged();
    }

    protected override void LoadFrom(DcrEditModel model)
    {
        _chkMaterialIdentification.Checked = model.MaterialIdentificationRequired;
        _txtMaterialUsageStation.Text = model.MaterialUsageStation;
        _chkSupplierMrd.Checked = model.SupplierSupportsMRD;
        WizardControlUi.SetNullableDate(_dtExpectedArrival, model.ExpectedArrivalDate);
        _chkTemporaryProcess.Checked = model.TemporaryProcessRequired;
        _chkRework.Checked = model.ReworkRequired;
        WizardControlUi.SetNullableDate(_dtPlannedStart, model.PlannedStartDate);
        WizardControlUi.SetNullableDate(_dtPlannedEnd, model.PlannedEndDate);
        _txtProductionOrder.Text = model.ProductionOrderNumber;
    }

    protected override void ApplyTo(DcrEditModel model)
    {
        model.MaterialIdentificationRequired = _chkMaterialIdentification.Checked;
        model.MaterialUsageStation = _txtMaterialUsageStation.Text.Trim();
        model.SupplierSupportsMRD = _chkSupplierMrd.Checked;
        model.ExpectedArrivalDate = WizardControlUi.GetNullableDate(_dtExpectedArrival);
        model.TemporaryProcessRequired = _chkTemporaryProcess.Checked;
        model.ReworkRequired = _chkRework.Checked;
        model.PlannedStartDate = WizardControlUi.GetNullableDate(_dtPlannedStart);
        model.PlannedEndDate = WizardControlUi.GetNullableDate(_dtPlannedEnd);
        model.ProductionOrderNumber = _txtProductionOrder.Text.Trim();
    }

    public override void SetEditable(bool editable)
    {
        foreach (Control control in new Control[] { _chkMaterialIdentification, _txtMaterialUsageStation, _chkSupplierMrd, _dtExpectedArrival, _chkTemporaryProcess, _chkRework, _dtPlannedStart, _dtPlannedEnd, _txtProductionOrder })
            WizardControlUi.SetEditable(control, editable);
    }
}
