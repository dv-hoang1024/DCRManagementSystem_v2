using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;

namespace DCRManagementSystem.Forms;

public sealed class MasterDataManagementForm : Form
{
    private readonly DataGridView _gridProductLines = new();
    private readonly DataGridView _gridChangeTypes = new();

    public MasterDataManagementForm()
    {
        if (!CurrentUser.IsAdmin)
            throw new UnauthorizedAccessException("Chỉ Administrator được quản lý Danh mục DCR.");

        Text = "Danh mục DCR";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1040, 760);
        MinimumSize = new Size(900, 650);
        MaximumSize = new Size(1920, 1080);
        UiTheme.ApplyForm(this);
        BuildUi();
        Shown += async (_, _) => await RefreshAllAsync();
    }

    private void BuildUi()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 96, BackColor = Color.White };
        var title = UiTheme.CreatePageTitle("Danh mục DCR");
        title.Location = new Point(28, 15);
        var subtitle = UiTheme.CreatePageSubtitle("Quản lý Dòng sản phẩm và Loại thay đổi dùng khi tạo DCR.");
        subtitle.Location = new Point(30, 56);
        var close = new Button { Text = "Đóng", Size = new Size(90, 36), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        UiTheme.StyleSecondaryButton(close);
        close.Click += (_, _) => Close();
        header.Resize += (_, _) => close.Location = new Point(header.ClientSize.Width - close.Width - 28, 29);
        header.Controls.AddRange([title, subtitle, close]);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 300,
            SplitterWidth = 10,
            BackColor = UiTheme.Background,
            Padding = new Padding(28, 18, 28, 24),
            IsSplitterFixed = false
        };
        split.Panel1.Padding = new Padding(0, 0, 0, 6);
        split.Panel2.Padding = new Padding(0, 6, 0, 0);

        split.Panel1.Controls.Add(BuildProductLineCard());
        split.Panel2.Controls.Add(BuildChangeTypeCard());

        Controls.Add(split);
        Controls.Add(UiTheme.CreateDivider());
        Controls.Add(header);
    }

    private Control BuildProductLineCard()
    {
        var card = UiTheme.CreateCard();
        card.Dock = DockStyle.Fill;
        var bar = BuildBar("Dòng sản phẩm", out var add, out var edit, out var delete, out var refresh);
        add.Click += async (_, _) => await EditProductLineAsync(null);
        edit.Click += async (_, _) =>
        {
            if (_gridProductLines.CurrentRow?.DataBoundItem is ProductLineDefinition item)
                await EditProductLineAsync(item);
        };
        delete.Click += async (_, _) =>
        {
            if (_gridProductLines.CurrentRow?.DataBoundItem is ProductLineDefinition item)
                await DeleteProductLineAsync(item);
        };
        refresh.Click += async (_, _) => await RefreshProductLinesAsync();

        UiTheme.ConfigureGrid(_gridProductLines);
        _gridProductLines.Dock = DockStyle.Fill;
        _gridProductLines.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ProductLineDefinition.Name), HeaderText = "Dòng sản phẩm", FillWeight = 220 });
        _gridProductLines.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ProductLineDefinition.SortOrder), HeaderText = "Thứ tự", FillWeight = 70 });
        _gridProductLines.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = nameof(ProductLineDefinition.IsActive), HeaderText = "Hoạt động", FillWeight = 70 });
        _gridProductLines.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex >= 0 && _gridProductLines.Rows[e.RowIndex].DataBoundItem is ProductLineDefinition item)
                await EditProductLineAsync(item);
        };

        card.Controls.Add(_gridProductLines);
        card.Controls.Add(UiTheme.CreateDivider());
        card.Controls.Add(bar);
        return card;
    }

    private Control BuildChangeTypeCard()
    {
        var card = UiTheme.CreateCard();
        card.Dock = DockStyle.Fill;
        var bar = BuildBar("Loại thay đổi linh kiện", out var add, out var edit, out var delete, out var refresh);
        add.Click += async (_, _) => await EditChangeTypeAsync(null);
        edit.Click += async (_, _) =>
        {
            if (_gridChangeTypes.CurrentRow?.DataBoundItem is PartChangeTypeDefinition item)
                await EditChangeTypeAsync(item);
        };
        delete.Click += async (_, _) =>
        {
            if (_gridChangeTypes.CurrentRow?.DataBoundItem is PartChangeTypeDefinition item)
                await DeleteChangeTypeAsync(item);
        };
        refresh.Click += async (_, _) => await RefreshChangeTypesAsync();

        UiTheme.ConfigureGrid(_gridChangeTypes);
        _gridChangeTypes.Dock = DockStyle.Fill;
        _gridChangeTypes.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(PartChangeTypeDefinition.Name), HeaderText = "Loại thay đổi", FillWeight = 220 });
        _gridChangeTypes.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(PartChangeTypeDefinition.SortOrder), HeaderText = "Thứ tự", FillWeight = 70 });
        _gridChangeTypes.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = nameof(PartChangeTypeDefinition.IsActive), HeaderText = "Hoạt động", FillWeight = 70 });
        _gridChangeTypes.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex >= 0 && _gridChangeTypes.Rows[e.RowIndex].DataBoundItem is PartChangeTypeDefinition item)
                await EditChangeTypeAsync(item);
        };

        card.Controls.Add(_gridChangeTypes);
        card.Controls.Add(UiTheme.CreateDivider());
        card.Controls.Add(bar);
        return card;
    }

    private static Panel BuildBar(string text, out Button add, out Button edit, out Button delete, out Button refresh)
    {
        var bar = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Color.White };
        var title = new Label
        {
            Text = text,
            AutoSize = true,
            Location = new Point(18, 18),
            Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
            ForeColor = UiTheme.TextPrimary
        };
        add = new Button { Text = "+ Thêm", Size = new Size(90, 34), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        edit = new Button { Text = "Sửa", Size = new Size(80, 34), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        delete = new Button { Text = "Xóa", Size = new Size(80, 34), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        refresh = new Button { Text = "Làm mới", Size = new Size(90, 34), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        UiTheme.StylePrimaryButton(add);
        UiTheme.StyleSecondaryButton(edit);
        UiTheme.StyleDangerButton(delete);
        UiTheme.StyleSecondaryButton(refresh);
        var addButton = add;
        var editButton = edit;
        var deleteButton = delete;
        var refreshButton = refresh;
        bar.Resize += (_, _) =>
        {
            deleteButton.Location = new Point(bar.ClientSize.Width - deleteButton.Width - 18, 12);
            editButton.Location = new Point(deleteButton.Left - editButton.Width - 8, 12);
            addButton.Location = new Point(editButton.Left - addButton.Width - 8, 12);
            refreshButton.Location = new Point(addButton.Left - refreshButton.Width - 8, 12);
        };
        bar.Controls.AddRange([title, refresh, add, edit, delete]);
        return bar;
    }

    private async Task RefreshAllAsync()
    {
        await RefreshProductLinesAsync();
        await RefreshChangeTypesAsync();
    }

    private async Task RefreshProductLinesAsync()
    {
        try
        {
            UseWaitCursor = true;
            _gridProductLines.DataSource = await AppServices.CreateAdminService().GetProductLinesAsync();
        }
        catch (Exception ex)
        {
            UiMessageBox.Show(this, ex.Message, "Dòng sản phẩm", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { UseWaitCursor = false; }
    }

    private async Task RefreshChangeTypesAsync()
    {
        try
        {
            UseWaitCursor = true;
            _gridChangeTypes.DataSource = await AppServices.CreateAdminService().GetPartChangeTypesAsync();
        }
        catch (Exception ex)
        {
            UiMessageBox.Show(this, ex.Message, "Loại thay đổi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { UseWaitCursor = false; }
    }

    private async Task EditProductLineAsync(ProductLineDefinition? item)
    {
        try
        {
            var service = AppServices.CreateAdminService();
            if (item is not null)
            {
                item = (await service.GetProductLinesAsync(false)).SingleOrDefault(x => x.Id == item.Id)
                    ?? throw new InvalidOperationException("Dòng sản phẩm không còn tồn tại. Hãy làm mới danh sách.");
            }
            using var dialog = new MasterDataEditForm("Dòng sản phẩm", item?.Name ?? string.Empty, item?.SortOrder ?? 10, item?.IsActive ?? true);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            await service.SaveProductLineAsync(item?.Id, dialog.ItemName, dialog.SortOrder, dialog.IsActive);
            await RefreshProductLinesAsync();
        }
        catch (Exception ex)
        {
            UiMessageBox.Show(this, ex.Message, "Lưu Dòng sản phẩm", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task EditChangeTypeAsync(PartChangeTypeDefinition? item)
    {
        try
        {
            var service = AppServices.CreateAdminService();
            if (item is not null)
            {
                item = (await service.GetPartChangeTypesAsync(false)).SingleOrDefault(x => x.Id == item.Id)
                    ?? throw new InvalidOperationException("Loại thay đổi không còn tồn tại. Hãy làm mới danh sách.");
            }
            using var dialog = new MasterDataEditForm("Loại thay đổi", item?.Name ?? string.Empty, item?.SortOrder ?? 10, item?.IsActive ?? true);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            await service.SavePartChangeTypeAsync(item?.Id, dialog.ItemName, dialog.SortOrder, dialog.IsActive);
            await RefreshChangeTypesAsync();
        }
        catch (Exception ex)
        {
            UiMessageBox.Show(this, ex.Message, "Lưu Loại thay đổi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task DeleteProductLineAsync(ProductLineDefinition item)
    {
        var confirm = UiMessageBox.Show(this,
            $"Xóa Dòng sản phẩm '{item.Name}'? DCR lịch sử vẫn giữ nguyên giá trị đã lưu.",
            "Xóa Dòng sản phẩm",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes) return;
        try
        {
            await AppServices.CreateAdminService().DeleteProductLineAsync(item.Id);
            await RefreshProductLinesAsync();
        }
        catch (Exception ex)
        {
            UiMessageBox.Show(this, ex.Message, "Xóa Dòng sản phẩm", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task DeleteChangeTypeAsync(PartChangeTypeDefinition item)
    {
        var confirm = UiMessageBox.Show(this,
            $"Xóa Loại thay đổi '{item.Name}'? DCR lịch sử vẫn giữ nguyên giá trị đã lưu.",
            "Xóa Loại thay đổi",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes) return;
        try
        {
            await AppServices.CreateAdminService().DeletePartChangeTypeAsync(item.Id);
            await RefreshChangeTypesAsync();
        }
        catch (Exception ex)
        {
            UiMessageBox.Show(this, ex.Message, "Xóa Loại thay đổi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

internal sealed class MasterDataEditForm : Form
{
    private readonly TextBox _txtName = new();
    private readonly NumericUpDown _numSortOrder = new();
    private readonly CheckBox _chkActive = new();

    public string ItemName => _txtName.Text.Trim();
    public int SortOrder => (int)_numSortOrder.Value;
    public bool IsActive => _chkActive.Checked;

    public MasterDataEditForm(string itemLabel, string name, int sortOrder, bool isActive)
    {
        Text = string.IsNullOrWhiteSpace(name) ? $"Thêm {itemLabel}" : $"Sửa {itemLabel}";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(520, 310);
        MaximumSize = new Size(1920, 1080);
        UiTheme.ApplyForm(this);

        var title = UiTheme.CreatePageTitle(Text);
        title.Location = new Point(28, 20);
        var card = UiTheme.CreateCard();
        card.SetBounds(28, 72, 464, 150);

        var lblName = UiTheme.CreateFieldLabel(itemLabel);
        lblName.SetBounds(24, 22, 130, 24);
        _txtName.SetBounds(160, 20, 278, 32);
        UiTheme.StyleInput(_txtName);

        var lblSort = UiTheme.CreateFieldLabel("Thứ tự");
        lblSort.SetBounds(24, 70, 130, 24);
        _numSortOrder.SetBounds(160, 68, 120, 32);
        _numSortOrder.Minimum = 0;
        _numSortOrder.Maximum = 9999;

        _chkActive.Text = "Đang hoạt động";
        _chkActive.SetBounds(160, 112, 200, 24);
        _chkActive.ForeColor = UiTheme.TextPrimary;

        card.Controls.AddRange([lblName, _txtName, lblSort, _numSortOrder, _chkActive]);

        var save = new Button { Text = "Lưu", DialogResult = DialogResult.OK, Size = new Size(104, 38), Location = new Point(272, 244) };
        var cancel = new Button { Text = "Hủy", DialogResult = DialogResult.Cancel, Size = new Size(104, 38), Location = new Point(388, 244) };
        UiTheme.StylePrimaryButton(save);
        UiTheme.StyleSecondaryButton(cancel);
        Controls.AddRange([title, card, save, cancel]);
        AcceptButton = save;
        CancelButton = cancel;

        _txtName.Text = name;
        _numSortOrder.Value = Math.Clamp(sortOrder, 0, 9999);
        _chkActive.Checked = isActive;
    }
}
