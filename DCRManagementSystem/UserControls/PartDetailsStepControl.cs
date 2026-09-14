using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;
using DCRManagementSystem.Services;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.Security;
using System.ComponentModel;

namespace DCRManagementSystem.UserControls;

public sealed class PartDetailsStepControl : DcrWizardStepControl
{
    protected override int StepNumber => 2;

    private readonly BindingList<PartEditItem> _parts = new();
    private readonly DataGridView _gridParts = new();
    private readonly DataGridViewComboBoxColumn _colChangeType = new();
    private readonly Button _btnPasteExcel = new();
    private readonly Button _btnImportExcel = new();
    private readonly List<string> _changeTypes = new();
    private bool _editable = true;

    public PartDetailsStepControl()
    {
        var page = WizardControlUi.CreateFlowPage();
        var card = WizardControlUi.CreateCard(
            "Danh sách linh kiện",
            "Nhập trực tiếp, dán nhiều dòng từ Excel bằng Ctrl+V/Paste hoặc import file .xlsx.", 575);

        WizardControlUi.ConfigureButton(_btnPasteExcel, "Dán từ Excel", 135);
        _btnPasteExcel.Location = new Point(662, 60);
        _btnPasteExcel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _btnPasteExcel.Click += (_, _) => PasteFromClipboard();

        WizardControlUi.ConfigureButton(_btnImportExcel, "Import Excel (.xlsx)", 155, primary: true);
        _btnImportExcel.Location = new Point(807, 60);
        _btnImportExcel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _btnImportExcel.Click += async (_, _) => await ImportExcelAsync();

        UiTheme.ConfigureGrid(_gridParts, readOnly: false);
        _gridParts.Location = new Point(28, 112);
        _gridParts.Size = new Size(924, 420);
        _gridParts.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _gridParts.AutoGenerateColumns = false;
        _gridParts.DataSource = _parts;
        _gridParts.DataError += (_, e) => e.ThrowException = false;

        _colChangeType.HeaderText = "Loại thay đổi";
        _colChangeType.DataPropertyName = nameof(PartEditItem.ChangeType);
        _colChangeType.Width = 140;
        _colChangeType.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        _colChangeType.FlatStyle = FlatStyle.Flat;
        _colChangeType.DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton;

        var colPartNo = WizardControlUi.CreateTextColumn("Mã linh kiện *", nameof(PartEditItem.PartNumber), 150);
        var colPartName = WizardControlUi.CreateTextColumn("Tên linh kiện *", nameof(PartEditItem.PartName), 250);
        var colQty = WizardControlUi.CreateTextColumn("Số lượng *", nameof(PartEditItem.Quantity), 120);
        var colReplaced = WizardControlUi.CreateTextColumn("Thay thế bởi", nameof(PartEditItem.ReplacedBy), 180);
        colPartName.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

        _gridParts.Columns.AddRange([_colChangeType, colPartNo, colPartName, colQty, colReplaced]);
        _gridParts.DefaultValuesNeeded += (_, e) =>
        {
            if (_changeTypes.Count > 0)
                e.Row.Cells[_colChangeType.Index].Value = _changeTypes[0];
        };
        _gridParts.KeyDown += GridPartsOnKeyDown;

        card.Controls.AddRange([_btnPasteExcel, _btnImportExcel, _gridParts]);
        page.Controls.Add(card);
        WizardControlUi.AttachResponsiveCards(page);
        Controls.Add(page);
        _btnPasteExcel.BringToFront();
        _btnImportExcel.BringToFront();

        _gridParts.CellValueChanged += (_, _) => RaiseDataChanged();
        _gridParts.UserAddedRow += (_, _) => RaiseDataChanged();
        _gridParts.UserDeletedRow += (_, _) => RaiseDataChanged();
    }

    public void BindChangeTypes(IReadOnlyCollection<PartChangeTypeDefinition> definitions)
    {
        _changeTypes.Clear();
        _changeTypes.AddRange(definitions
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .Select(x => x.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase));
        RebuildChangeTypeItems();
    }

    protected override void LoadFrom(DcrEditModel model)
    {
        _parts.RaiseListChangedEvents = false;
        _parts.Clear();
        foreach (var item in model.Parts)
            _parts.Add(Clone(item));
        _parts.RaiseListChangedEvents = true;
        _parts.ResetBindings();
        RebuildChangeTypeItems();
    }

    protected override void ApplyTo(DcrEditModel model)
    {
        _gridParts.EndEdit();
        if (BindingContext[_parts] is CurrencyManager manager)
            manager.EndCurrentEdit();

        var order = 1;
        model.Parts = _parts
            .Select(Clone)
            .Select(x =>
            {
                x.SortOrder = order++;
                return x;
            })
            .ToList();
    }

    public override void SetEditable(bool editable)
    {
        _editable = editable;
        _gridParts.ReadOnly = !editable;
        _gridParts.AllowUserToAddRows = editable;
        _gridParts.AllowUserToDeleteRows = editable;
        _btnPasteExcel.Enabled = editable;
        _btnImportExcel.Enabled = editable;
    }

    private void RebuildChangeTypeItems()
    {
        var values = _changeTypes
            .Concat(_parts.Select(x => x.ChangeType))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _colChangeType.Items.Clear();
        foreach (var value in values)
            _colChangeType.Items.Add(value);
    }

    private void GridPartsOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_editable || !e.Control || e.KeyCode != Keys.V)
            return;

        e.SuppressKeyPress = true;
        e.Handled = true;
        PasteFromClipboard();
    }

    private void PasteFromClipboard()
    {
        if (!_editable)
            return;

        try
        {
            if (!Clipboard.ContainsText())
            {
                UiMessageBox.Show(this, "Clipboard không có dữ liệu dạng bảng.", "Paste Part List",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var imported = PartImportService.ParseClipboardText(Clipboard.GetText());
            AppendImported(imported, "clipboard");
        }
        catch (Exception ex)
        {
            UiMessageBox.Show(this, ex.Message, "Paste Part List",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ImportExcelAsync()
    {
        if (!_editable)
            return;

        using var dialog = new OpenFileDialog
        {
            Title = "Import Part List từ Excel",
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            UseWaitCursor = true;
            _btnImportExcel.Enabled = false;
            var imported = await Task.Run(() => PartImportService.ImportXlsx(dialog.FileName));
            AppendImported(imported, Path.GetFileName(dialog.FileName));
        }
        catch (Exception ex)
        {
            UiMessageBox.Show(this, ex.Message, "Import Excel",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
            _btnImportExcel.Enabled = _editable;
        }
    }

    private void AppendImported(IReadOnlyCollection<PartEditItem> imported, string source)
    {
        if (imported.Count == 0)
        {
            UiMessageBox.Show(this,
                $"Không tìm thấy dòng Part hợp lệ trong {source}.",
                "Part List",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var invalidChangeTypes = imported
            .Select(x => (x.ChangeType ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x) && !_changeTypes.Contains(x, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (invalidChangeTypes.Count > 0)
        {
            UiMessageBox.Show(this,
                @"Các Loại thay đổi sau chưa có trong Danh mục DCR hoặc đang bị vô hiệu hóa:
- " +
                string.Join(@"
- ", invalidChangeTypes) +
                @"

Administrator hãy bổ sung / kích hoạt danh mục trước khi import.",
                "Loại thay đổi không hợp lệ",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }
        var defaultChangeType = _changeTypes.FirstOrDefault() ?? string.Empty;
        var nextSortOrder = _parts.Count + 1;
        foreach (var item in imported)
        {
            if (string.IsNullOrWhiteSpace(item.ChangeType))
                item.ChangeType = defaultChangeType;
            item.SortOrder = nextSortOrder++;
            _parts.Add(item);
        }

        RebuildChangeTypeItems();
        RaiseDataChanged();
        UiMessageBox.Show(this,
            $"Đã thêm {imported.Count:N0} dòng Part từ {source}.",
            "Part List",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private static PartEditItem Clone(PartEditItem x) => new()
    {
        Id = x.Id,
        ChangeType = x.ChangeType,
        PartNumber = x.PartNumber,
        PartName = x.PartName,
        KPC = x.KPC, // legacy data is retained in the model/database but no longer edited in the UI.
        Quantity = x.Quantity,
        ReplacedBy = x.ReplacedBy,
        SortOrder = x.SortOrder
    };
}
