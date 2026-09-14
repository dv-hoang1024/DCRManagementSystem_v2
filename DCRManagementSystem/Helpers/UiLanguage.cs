using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace DCRManagementSystem.Helpers;

public enum UiLanguage
{
    Vietnamese,
    English
}

/// <summary>
/// Runtime UI localization for the WinForms client. The selected language is kept per Windows profile
/// and is intentionally independent from business data stored in SQL Server.
/// </summary>
public static class UiLanguageManager
{
    private sealed class HookMarker { }
    private sealed class ControlTextState
    {
        public string OriginalText { get; set; } = string.Empty;
        public string OriginalPlaceholder { get; set; } = string.Empty;
    }
    private sealed class ColumnTextState
    {
        public string OriginalHeader { get; set; } = string.Empty;
    }

    private static readonly ConditionalWeakTable<Control, HookMarker> HookedControls = new();
    private static readonly ConditionalWeakTable<Control, ControlTextState> TextStates = new();
    private static readonly ConditionalWeakTable<DataGridView, HookMarker> HookedGrids = new();
    private static readonly ConditionalWeakTable<DataGridViewColumn, ColumnTextState> ColumnStates = new();
    private static readonly string LanguageFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DCRManagementSystem", "ui-language.txt");

    private static bool _applying;
    private static UiLanguage _current = LoadLanguage();

    public static UiLanguage Current => _current;
    public static bool IsEnglish => _current == UiLanguage.English;
    public static event EventHandler? LanguageChanged;

    private static readonly Dictionary<string, (string Vi, string En)> Lookup = BuildLookup();

    public static string T(string vi, string en) => IsEnglish ? en : vi;

    public static void Toggle() => SetLanguage(IsEnglish ? UiLanguage.Vietnamese : UiLanguage.English);

    public static void SetLanguage(UiLanguage language)
    {
        if (_current == language)
            return;

        _current = language;
        SaveLanguage(language);
        ApplyToOpenForms();
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string ToggleButtonText => IsEnglish ? "VI" : "EN";
    public static string ToggleToolTip => T("Chuyển sang tiếng Anh", "Switch to Vietnamese");

    public static void Attach(Form form)
    {
        HookControl(form);
        form.Shown += (_, _) => Apply(form);
        form.KeyPreview = true;
        form.KeyDown -= FormLanguageShortcut;
        form.KeyDown += FormLanguageShortcut;
    }

    private static void FormLanguageShortcut(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.Shift && e.KeyCode == Keys.L)
        {
            Toggle();
            e.SuppressKeyPress = true;
        }
    }

    public static void ApplyToOpenForms()
    {
        foreach (Form form in Application.OpenForms)
            Apply(form);
    }

    public static void Apply(Control root)
    {
        var previous = _applying;
        try
        {
            _applying = true;
            ApplyControl(root);
        }
        finally
        {
            _applying = previous;
        }
    }

    private static void ApplyControl(Control control)
    {
        HookControl(control);
        var state = TextStates.GetValue(control, c => new ControlTextState
        {
            OriginalText = c.Text,
            OriginalPlaceholder = c is TextBox tb ? tb.PlaceholderText : string.Empty
        });

        if (!string.IsNullOrWhiteSpace(state.OriginalText))
            control.Text = TranslateText(state.OriginalText);

        if (control is TextBox textBox && !string.IsNullOrWhiteSpace(state.OriginalPlaceholder))
            textBox.PlaceholderText = TranslateText(state.OriginalPlaceholder);

        if (control is DataGridView grid)
        {
            foreach (DataGridViewColumn column in grid.Columns)
            {
                var columnState = ColumnStates.GetValue(column, c => new ColumnTextState { OriginalHeader = c.HeaderText });
                if (!string.IsNullOrWhiteSpace(columnState.OriginalHeader))
                    column.HeaderText = TranslateText(columnState.OriginalHeader);
            }
            HookGrid(grid);
            grid.Invalidate();
        }

        foreach (Control child in control.Controls)
            ApplyControl(child);
    }

    private static void HookControl(Control control)
    {
        if (HookedControls.TryGetValue(control, out _))
            return;

        HookedControls.Add(control, new HookMarker());
        TextStates.Add(control, new ControlTextState
        {
            OriginalText = control.Text,
            OriginalPlaceholder = control is TextBox tb ? tb.PlaceholderText : string.Empty
        });

        control.TextChanged += (_, _) =>
        {
            if (_applying) return;
            var state = TextStates.GetValue(control, _ => new ControlTextState());
            state.OriginalText = control.Text;
            if (string.IsNullOrWhiteSpace(control.Text)) return;
            var translated = TranslateText(state.OriginalText);
            if (translated == control.Text) return;
            try
            {
                _applying = true;
                control.Text = translated;
            }
            finally { _applying = false; }
        };
        control.ControlAdded += (_, e) => Apply(e.Control);
    }

    private static void HookGrid(DataGridView grid)
    {
        if (HookedGrids.TryGetValue(grid, out _))
            return;
        HookedGrids.Add(grid, new HookMarker());
        grid.CellFormatting += (_, e) =>
        {
            if (e.Value is not string text || string.IsNullOrWhiteSpace(text))
                return;
            var translated = TranslateText(text);
            if (!string.Equals(translated, text, StringComparison.Ordinal))
                e.Value = translated;
        };
    }

    public static string TranslateText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        if (Lookup.TryGetValue(text.Trim(), out var pair))
        {
            var translated = IsEnglish ? pair.En : pair.Vi;
            if (text.StartsWith(' ') || text.EndsWith(' '))
                return PreserveOuterWhitespace(text, translated);
            return translated;
        }

        return TranslateDynamic(text);
    }

    public static string TranslateStatus(string status)
    {
        return status switch
        {
            "Draft" => T("Bản nháp", "Draft"),
            "Submitted" => T("Đã gửi", "Submitted"),
            "InApproval" => T("Đang phê duyệt", "In Approval"),
            "Pending" => T("Chờ xử lý", "Pending"),
            "Waiting" => T("Chờ đến lượt", "Waiting"),
            "Approved" => T("Đã duyệt", "Approved"),
            "Completed" => T("Hoàn tất", "Completed"),
            "Rejected" => T("Bị từ chối", "Rejected"),
            "Returned" => T("Trả về", "Returned"),
            "Skipped" => T("Bỏ qua", "Skipped"),
            "Cancelled" => T("Đã hủy", "Cancelled"),
            "Processing" => T("Đang xử lý", "Processing"),
            "Sent" => T("Đã gửi", "Sent"),
            "Failed" => T("Thất bại", "Failed"),
            "RequiresSignIn" => T("Cần đăng nhập", "Sign-in Required"),
            _ => TranslateText(status)
        };
    }

    private static string TranslateDynamic(string text)
    {
        var s = text;

        if (!IsEnglish)
        {
            foreach (var status in new[] { "Draft", "Submitted", "InApproval", "Pending", "Waiting", "Approved", "Completed", "Rejected", "Returned", "Skipped", "Cancelled", "Processing", "Sent", "Failed", "RequiresSignIn" })
            {
                s = s.Replace($"• {status} •", $"• {TranslateStatus(status)} •", StringComparison.OrdinalIgnoreCase);
                if (s.EndsWith("• " + status, StringComparison.OrdinalIgnoreCase))
                    s = s[..^(status.Length)] + TranslateStatus(status);
            }
        }

        // Business status values are kept in English in SQL but rendered according to UI language.
        if (new[] { "Draft", "Submitted", "InApproval", "Pending", "Waiting", "Approved", "Completed", "Rejected", "Returned", "Skipped", "Cancelled", "Processing", "Sent", "Failed", "RequiresSignIn" }.Contains(s, StringComparer.OrdinalIgnoreCase))
            return TranslateStatus(CanonicalStatus(s));

        if (IsEnglish)
        {
            s = Regex.Replace(s, @"^Bước\s+(\d+)\s*/\s*(\d+)$", "Step $1 / $2", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"^([\d.,]+)\s+yêu cầu$", "$1 requests", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"^([\d.,]+)\s+phòng ban$", "$1 departments", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"^([\d.,]+)\s+Khối$", "$1 business units", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"^([\d.,]+)\s+người dùng$", "$1 users", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"^Đã lưu\s+(.+)$", "Saved $1", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"^DCR mới\s*•\s*(.+)$", "New DCR • $1", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"^Microsoft account:\s*", "Microsoft account: ", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"^Storage Root:\s*", "Storage Root: ", RegexOptions.IgnoreCase);
            s = s.Replace(" chưa đăng nhập trên Windows user hiện tại", " not signed in for the current Windows user", StringComparison.OrdinalIgnoreCase);
            s = s.Replace(" cấp / ", " levels / ", StringComparison.OrdinalIgnoreCase);
            s = s.Replace(" approver", " approver", StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            s = Regex.Replace(s, @"^Step\s+(\d+)\s*/\s*(\d+)$", "Bước $1 / $2", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"^([\d.,]+)\s+requests$", "$1 yêu cầu", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"^([\d.,]+)\s+departments$", "$1 phòng ban", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"^([\d.,]+)\s+business units$", "$1 Khối", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"^([\d.,]+)\s+users$", "$1 người dùng", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"^Saved\s+(.+)$", "Đã lưu $1", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"^New DCR\s*•\s*(.+)$", "DCR mới • $1", RegexOptions.IgnoreCase);
            s = s.Replace(" not signed in for the current Windows user", " chưa đăng nhập trên Windows user hiện tại", StringComparison.OrdinalIgnoreCase);
        }

        // Translate common fragments in multi-line status/help messages without changing identifiers and user data.
        var fragments = IsEnglish ? ViToEnFragments : EnToViFragments;
        foreach (var (from, to) in fragments)
            s = s.Replace(from, to, StringComparison.OrdinalIgnoreCase);

        return s;
    }

    private static string CanonicalStatus(string value)
    {
        string[] statuses = ["Draft", "Submitted", "InApproval", "Pending", "Waiting", "Approved", "Completed", "Rejected", "Returned", "Skipped", "Cancelled", "Processing", "Sent", "Failed", "RequiresSignIn"];
        var exact = statuses.FirstOrDefault(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;
        if (value.Equals("in approval", StringComparison.OrdinalIgnoreCase)) return "InApproval";
        if (value.Equals("sign-in required", StringComparison.OrdinalIgnoreCase)) return "RequiresSignIn";
        return value;
    }

    private static string PreserveOuterWhitespace(string original, string translated)
    {
        var left = original.Length - original.TrimStart().Length;
        var right = original.Length - original.TrimEnd().Length;
        return new string(' ', left) + translated + new string(' ', right);
    }

    private static UiLanguage LoadLanguage()
    {
        try
        {
            if (!File.Exists(LanguageFile)) return UiLanguage.Vietnamese;
            var text = File.ReadAllText(LanguageFile).Trim();
            return text.Equals("en", StringComparison.OrdinalIgnoreCase) ? UiLanguage.English : UiLanguage.Vietnamese;
        }
        catch { return UiLanguage.Vietnamese; }
    }

    private static void SaveLanguage(UiLanguage language)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LanguageFile)!);
            File.WriteAllText(LanguageFile, language == UiLanguage.English ? "en" : "vi");
        }
        catch { }
    }

    private static Dictionary<string, (string Vi, string En)> BuildLookup()
    {
        var result = new Dictionary<string, (string Vi, string En)>(StringComparer.OrdinalIgnoreCase);
        void Add(string vi, string en)
        {
            var pair = (vi, en);
            result[vi] = pair;
            result[en] = pair;
        }

        // Main workspace and standardized PLM/ERP terminology.
        Add("DANH MỤC DCR", "DCR WORKSPACE");
        Add("QUẢN TRỊ", "ADMINISTRATION");
        Add("Cần phê duyệt", "Pending Approval");
        Add("Chờ tôi duyệt", "Pending Approval");
        Add("DCR liên quan", "Involved DCRs");
        Add("DCR liên quan đến tôi", "Involved DCRs");
        Add("DCR của tôi", "My Requests");
        Add("Tôi đã tạo", "My Requests");
        Add("DCR tôi đã tạo", "My Requests");
        Add("Đã duyệt / Hoàn tất", "Approved / Completed");
        Add("Đã hoàn thành", "Approved / Completed");
        Add("Bị từ chối", "Rejected");
        Add("Đã từ chối", "Rejected");
        Add("Tất cả DCR", "All DCRs");
        Add("Danh sách DCR đang chờ bạn xử lý", "DCRs awaiting your action");
        Add("Danh sách DCR đang chờ bạn xử lý.", "DCRs awaiting your action.");
        Add("Các DCR đang ở đúng stage và đang chờ quyết định của bạn.", "DCRs awaiting your action.");
        Add("Các DCR có liên quan đến bạn trong vòng đời workflow.", "DCRs associated with you in the workflow lifecycle.");
        Add("Các DCR có liên quan đến bạn.", "DCRs associated with you.");
        Add("Toàn bộ DCR trong hệ thống.", "All DCRs in the system.");
        Add("Toàn bộ DCR bạn đã tạo, đang/đã từng được phân công phê duyệt hoặc có liên quan trong lịch sử workflow.", "DCRs you created, approved, or were assigned to in workflow history.");
        Add("Các DCR do bạn khởi tạo.", "DCRs created by you.");
        Add("Theo dõi draft, DCR đang duyệt và kết quả của các yêu cầu do bạn khởi tạo.", "Draft, in-progress, and completed requests created by you.");
        Add("Các DCR đã hoàn tất toàn bộ luồng phê duyệt.", "DCRs with a completed approval workflow.");
        Add("Các DCR bị từ chối trong quá trình phê duyệt.", "DCRs rejected during approval.");
        Add("Góc nhìn quản trị trên toàn bộ yêu cầu trong hệ thống.", "Administrative view of all requests.");
        Add("Không có DCR nào cần xử lý tại thời điểm này", "No DCRs require action at this time");
        Add("Không có DCR nào cần xử lý tại thời điểm này.", "No DCRs require action at this time.");
        Add("Không có DCR phù hợp với bộ lọc hiện tại.", "No DCRs match the current filter.");
        Add("Danh sách yêu cầu", "Request List");
        Add("Tìm kiếm", "Search"); Add("Danh sách DCR", "DCR List");
        Add("Tìm", "Search");
        Add("Xóa lọc", "Clear Filter");
        Add("+  Tạo DCR mới", "+  New DCR"); Add("+  Tạo DCR", "+  New DCR");
        Add("Mở DCR", "Open DCR");
        Add("Làm mới", "Refresh");
        Add("Xóa DCR", "Delete DCR");
        Add("Đăng xuất", "Sign Out");
        Add("Người dùng", "Users");
        Add("Khối", "Business Unit");
        Add("Phòng ban", "Department");
        Add("Role", "Roles");
        Add("Luồng phê duyệt", "Approval Workflow");
        Add("Ma trận phê duyệt", "Approval Matrix");
        Add("Thiết lập hệ thống", "System Settings");
        Add("Danh mục cấu hình", "Master Data");
        Add("Mã DCR, tiêu đề, dòng sản phẩm, người tạo...", "DCR number, title, product line, owner...");
        Add("DANH MỤC DCR", "DCR MASTER DATA");
        Add("Quản lý Dòng sản phẩm và Loại thay đổi dùng khi tạo DCR.", "Manage Product Lines and Change Types used when creating DCRs.");
        Add("Dòng sản phẩm", "Product Line"); Add("Dòng sản phẩm *", "Product Line *");
        Add("Loại thay đổi linh kiện", "Part Change Types"); Add("Loại thay đổi", "Change Type");
        Add("Thứ tự", "Sort Order"); Add("Đang hoạt động", "Active");
        Add("Thêm Dòng sản phẩm", "Add Product Line"); Add("Sửa Dòng sản phẩm", "Edit Product Line");
        Add("Thêm Loại thay đổi", "Add Change Type"); Add("Sửa Loại thay đổi", "Edit Change Type");

        Add("Thêm người dùng", "Add User"); Add("Sửa người dùng", "Edit User"); Add("Xóa người dùng", "Delete User");
        Add("Thêm Khối", "Add Business Unit"); Add("Sửa Khối", "Edit Business Unit"); Add("Xóa Khối", "Delete Business Unit"); Add("Lưu Khối", "Save Business Unit");
        Add("Thêm phòng ban", "Add Department"); Add("Sửa phòng ban", "Edit Department"); Add("Lưu phòng ban", "Save Department");
        Add("Thêm Role", "Add Role"); Add("Sửa Role", "Edit Role"); Add("Xóa Role", "Delete Role"); Add("Lưu Role", "Save Role");
        Add("Lỗi khởi động", "Startup Error"); Add("Không thể khởi động DCR Management System.", "Unable to start DCR Management System.");
        Add("Không thể tải danh sách DCR", "Unable to Load DCR List"); Add("Không thể xóa DCR", "Unable to Delete DCR");
        Add("Không thể mở DCR", "Unable to Open DCR"); Add("Không thể chuyển bước", "Unable to Continue");
        Add("Kiểm tra dữ liệu", "Data Validation"); Add("Lưu Draft", "Save Draft"); Add("Submit thất bại", "Submission Failed");

        // Common actions and states.
        Add("Đóng", "Close"); Add("Hủy", "Cancel"); Add("Hủy", "Cancel");
        Add("Lưu", "Save"); Add("Sửa", "Edit"); Add("Xóa", "Delete");
        Add("Thêm", "Add"); Add("Đang tải...", "Loading..."); Add("Đang lưu...", "Saving...");
        Add("Đã lưu", "Saved"); Add("Đã lưu cấu hình", "Configuration saved"); Add("Lưu thất bại", "Save failed");
        Add("Hoạt động", "Active"); Add("Bảo vệ", "Protected"); Add("Mô tả", "Description");
        Add("Ưu tiên", "Priority"); Add("Trạng thái", "Status"); Add("Ngày tạo", "Created"); Add("Lưu gần nhất", "Last Saved");
        Add("Người tạo", "Owner"); Add("Tiêu đề", "Title"); Add("Chương trình", "Program"); Add("Dòng sản phẩm", "Product Line"); Add("Giai đoạn Build", "Build Stage");
        Add("Phòng ban", "Department"); Add("Phòng ban *", "Department *"); Add("Cấp", "Stage"); Add("Phiên bản", "Rev");
        Add("DCR No.", "DCR No."); Add("Mã DCR, tiêu đề, chương trình, người tạo...", "DCR number, title, program, owner...");

        // Login.
        Add("DCR Management System - Đăng nhập", "DCR Management System - Sign In");
        Add("Đăng nhập", "Sign In"); Add("Đăng nhập tài khoản", "Sign in with account");
        Add("Đăng nhập bằng Windows / AD", "Sign in with Windows / AD"); Add("Đăng nhập nhanh bằng Windows / AD", "Quick sign-in with Windows / AD");
        Add("Ưu tiên phiên Windows/AD hiện tại. Tài khoản local là fallback.", "Windows/AD is preferred. Local account is available as fallback."); Add("Lần đầu đăng nhập bằng tài khoản/mật khẩu. Có thể ghi nhớ Windows/AD cho lần sau.", "First sign in with your account/password. You can remember Windows/AD for future sign-ins."); Add("Nhớ Windows/AD và tự động đăng nhập lần sau", "Remember Windows/AD and sign in automatically next time");
        Add("────────────  hoặc tài khoản local / LDAP  ────────────", "────────────  or local / LDAP account  ────────────");
        Add("Nhập username", "Enter username"); Add("Nhập password", "Enter password");
        Add("Username", "Username"); Add("Password", "Password"); Add("Chỉ xác thực Windows", "Windows Authentication only");
        Add("Đã đăng xuất. Hãy đăng nhập bằng tài khoản bạn muốn sử dụng.", "Signed out. Sign in with the account you want to use.");

        // DCR wizard.
        Add("Temporary Deviation Change Request / Order", "Temporary Deviation Change Request / Order");
        Add("DCR mới", "New DCR"); Add("DCR mới • Draft", "New DCR • Draft"); Add("Chưa lưu", "Not saved");
        Add("Thông tin chung & Chương trình", "General Information & Program"); Add("Thông tin chung & Dòng sản phẩm", "General Information & Product Line");
        Add("Danh sách linh kiện", "Part List");
        Add("Vấn đề, giải pháp & Tài liệu", "Issue, Solution & Documents");
        Add("Kế hoạch & Tracking", "Plan & Tracking");
        Add("Review & Submit", "Review & Submit");
        Add("Khai báo thông tin người tạo DCR, chương trình, build stage và các mã ECR/PPS/ECN/MCN.", "Requestor details, vehicle program, build stage, and ECR/PPS/ECN/MCN references."); Add("Khai báo thông tin người tạo, dòng sản phẩm, build stage và các mã ECR/ECN/MCN.", "Requestor details, product line, build stage, and ECR/ECN/MCN references.");
        Add("Thêm các linh kiện chịu ảnh hưởng. Part Number, Part Name và Quantity là thông tin bắt buộc.", "Add affected parts. Part Number, Part Name, and Quantity are required.");
        Add("Mô tả vấn đề, giải pháp, phòng ban bị ảnh hưởng và tải tài liệu kỹ thuật liên quan.", "Describe the issue and solution, affected departments, and upload supporting technical documents.");
        Add("Xác nhận nhận diện vật liệu, MRD timing, rework và khoảng thời gian áp dụng DCR.", "Confirm material identification, MRD timing, rework, and the DCR effective period.");
        Add("Kiểm tra toàn bộ nội dung trước khi Submit và theo dõi lịch sử phê duyệt/audit.", "Review all information before submission and track approval/audit history.");
        Add("← Quay lại", "← Back"); Add("Tiếp theo →", "Next →"); Add("Lưu nháp", "Save Draft"); Add("Gửi phê duyệt", "Submit");
        Add("Phê duyệt", "Approve"); Add("Từ chối", "Reject"); Add("Yêu cầu bổ sung", "Request Info");
        Add("Final PDF", "Final PDF"); Add("Export PDF", "Export PDF"); Add("In", "Print");
        Add("1. Thông tin chung", "1. General Information");
        Add("2. Tiêu đề yêu cầu", "2. Request Title");
        Add("Các trường có dấu * phải được hoàn thành trước khi chuyển bước.", "Fields marked * must be completed before continuing.");
        Add("Mô tả ngắn gọn mục đích hoặc vấn đề của DCR.", "Provide a concise description of the DCR purpose or issue.");
        Add("3. Deviation description", "3. Deviation Description");
        Add("Mô tả rõ vấn đề, giải pháp và thay đổi vật liệu/thiết kế nếu có.", "Describe the issue, solution, and any material/design changes.");
        Add("4. Impacted department(s)", "4. Impacted Departments");
        Add("Các phòng ban được chọn sẽ được đưa vào ma trận routing ở stage Impacted Department.", "Selected departments are included in routing for the impacted-department stage.");
        Add("Tệp đính kèm đã Submit", "Submitted Attachments");
        Add("Người tạo và tất cả approver có liên quan có thể mở lại toàn bộ tài liệu kỹ thuật đã đính kèm cùng DCR.", "The requestor and involved approvers can reopen all technical documents submitted with the DCR.");
        Add("Mở tệp", "Open File"); Add("Làm mới PDF", "Refresh PDF"); Add("Mở PDF", "Open PDF");
        Add("PDF review sẽ được tạo khi DCR đã được lưu.", "PDF review is generated after the DCR is saved.");
        Add("PDF đã tạo nhưng WebView2 không hiển thị được. Bấm 'Mở PDF' để xem ngoài ứng dụng.", "The PDF was generated but WebView2 could not display it. Click 'Open PDF' to view it externally.");
        Add("Luồng phê duyệt cho DCR này", "Approval Workflow for This DCR");
        Add("Tìm người phê duyệt (tên / email / username / phòng ban)", "Find approver (name / email / username / department)");
        Add("Kết quả", "Results"); Add("Cấp", "Level"); Add("Tên cấp", "Level Name"); Add("Cấp phê duyệt 1", "Approval Level 1");
        Add("Thêm approver", "Add Approver"); Add("+ Cấp mới", "+ New Level"); Add("Xóa dòng", "Remove Row"); Add("Gợi ý theo tổ chức", "Suggest from Organization");
        Add("Mẫu line cá nhân", "Personal Approval Template"); Add("Nạp line đã lưu", "Load Saved Line"); Add("Lưu line hiện tại", "Save Current Line");
        Add("Routing: Approval Matrix mặc định (chưa chọn approver tùy chỉnh).", "Routing: default Approval Matrix (no custom approvers selected).");
        Add("Approval History", "Approval History"); Add("Quyết định", "Decision"); Add("Ngày", "Date"); Add("Nhận xét", "Comments"); Add("Xác thực", "Auth");
        Add("Loại tệp đính kèm", "Attachment Type"); Add("Chi phí ước tính", "Estimated Cost"); Add("Trạm sử dụng vật liệu", "Station for Material Usage");

        // User / organization / role administration.
        Add("User Management", "User Management"); Add("Quản lý người dùng", "User Management");
        Add("+  Thêm người dùng", "+  Add User"); Add("Danh sách người dùng", "User List");
        Add("Username, tên, email, Khối, phòng ban, role...", "Username, name, email, business unit, department, role...");
        Add("Cho phép đăng nhập", "Allow Sign In");
        Add("Windows Account dạng DOMAIN\\username. Có thể để trống password nếu user chỉ dùng Windows/AD SSO.", "Windows Account format: DOMAIN\\username. Password may be blank for Windows/AD SSO-only users.");
        Add("Staff/Manager thuộc Phòng ban; Director và các cấp cao hơn thuộc Khối. Direct Manager được kiểm soát theo cấp tổ chức.", "Staff/Managers belong to Departments; Directors and higher roles belong to Business Units. Direct Manager follows the organization hierarchy.");
        Add("Staff/Manager thuộc Phòng ban. Director và cấp cao hơn thuộc Khối. Manager báo cáo Director của Khối.", "Staff/Managers belong to Departments. Directors and higher roles belong to Business Units. Managers report to the Business Unit Director.");
        Add("Director/CTO/COO/DCEO/CEO và các role cấp tương đương thuộc Khối, không thuộc Phòng ban.", "Director/CTO/COO/DCEO/CEO and equivalent roles belong to a Business Unit, not a Department.");
        Add("Role tùy chỉnh: chọn phạm vi tổ chức và Direct Manager phù hợp với hierarchy level.", "Custom role: select the appropriate organization scope and Direct Manager for its hierarchy level.");
        Add("Họ và tên", "Full Name"); Add("Điện thoại", "Phone"); Add("Tài khoản Windows", "Windows Account"); Add("Quản lý trực tiếp", "Direct Manager"); Add("Mật khẩu mới", "New Password");

        Add("Business Unit Management", "Business Unit Management"); Add("Quản lý Khối", "Business Unit Management"); Add("+  Thêm Khối", "+  Add Business Unit"); Add("Danh sách Khối", "Business Unit List");
        Add("Mã Khối, tên Khối, Director...", "Business unit code, name, Director...");
        Add("Khối chứa nhiều phòng ban. Director/CTO/COO/DCEO/CEO và các cấp tương đương thuộc Khối, không thuộc Phòng ban.", "A Business Unit contains multiple Departments. Director/CTO/COO/DCEO/CEO and equivalent roles belong to the Business Unit, not a Department.");
        Add("Mỗi Khối chứa nhiều Phòng ban. Director/Block Head thuộc Khối và là cấp quản lý trực tiếp của Manager các phòng trong Khối.", "Each Business Unit contains multiple Departments. The Director/Block Head manages the Department Managers within the unit.");
        Add("Business Unit / Khối", "Business Unit"); Add("Director / Block Head", "Director / Block Head"); Add("Khối đang hoạt động", "Business Unit Active");

        Add("Department Management", "Department Management"); Add("Quản lý phòng ban", "Department Management"); Add("+  Thêm phòng ban", "+  Add Department"); Add("Danh sách phòng ban", "Department List");
        Add("Mã, tên phòng, Khối, Manager...", "Code, department name, business unit, Manager...");
        Add("Mỗi phòng ban thuộc một Khối. Manager là người đứng đầu phòng; Director được cấu hình ở cấp Khối.", "Each Department belongs to a Business Unit. The Manager leads the Department; the Director is configured at Business Unit level.");
        Add("Manager đứng đầu Phòng ban. Direct Manager của Manager sẽ tự động là Director của Khối.", "The Manager leads the Department. The Manager's Direct Manager is automatically the Business Unit Director.");
        Add("Director của Khối", "Business Unit Director"); Add("Phòng ban đang hoạt động", "Department Active");

        Add("Role Management", "Role Management"); Add("Quản lý Role", "Role Management"); Add("+  Thêm Role", "+  Add Role"); Add("Danh sách Role", "Role List");
        Add("Tên role, mô tả...", "Role name, description..."); Add("Phạm vi", "Scope"); Add("Cấp bậc", "Level"); Add("Role đang hoạt động", "Role Active");
        Add("Thêm, sửa, xóa Role và thiết lập cấp bậc dùng cho gợi ý luồng phê duyệt.", "Add, edit, or delete roles and configure hierarchy levels used for approval suggestions.");
        Add("Số càng lớn thì cấp càng cao. Staff/Manager thuộc Phòng ban; Role có level từ Director trở lên thuộc Khối.", "Higher values indicate higher authority. Staff/Managers belong to Departments; roles at Director level and above belong to Business Units.");
        Add("Hệ thống", "System"); Add("Tùy chỉnh", "Custom");

        // Workflow / matrix.
        Add("Cấu hình luồng phê duyệt", "Workflow Configuration"); Add("Cấu hình luồng phê duyệt", "Approval Workflow Configuration"); Add("Lưu cấu hình", "Save Configuration");
        Add("Quy tắc workflow", "Workflow Rules"); Add("Workflow stages", "Workflow Stages"); Add("Cấp #", "Stage #"); Add("Mã cấp", "Stage Code"); Add("Tên cấp", "Stage Name"); Add("Role người duyệt", "Approver Role"); Add("Phòng ảnh hưởng", "Impacted Dept");
        Add("Stage Number và Stage Code là cố định. Approval Matrix quyết định người duyệt theo Requesting/Impacted Department, role hoặc user cụ thể.", "Stage Number and Stage Code are fixed. The Approval Matrix selects approvers by Requesting/Impacted Department, role, or specific user.");
        Add("Ma trận phê duyệt", "Approval Matrix"); Add("Ma trận phê duyệt động", "Dynamic Approval Matrix"); Add("Lưu ma trận", "Save Matrix"); Add("Routing rules", "Routing Rules");
        Add("Phòng yêu cầu", "Requesting Dept"); Add("Phòng mục tiêu", "Target Dept"); Add("Nguồn người duyệt", "Approver Source"); Add("Người duyệt cụ thể", "Specific User");

        Add("Tên Khối", "Business Unit Name"); Add("Director / Trưởng Khối", "Director / Head"); Add("Manager", "Manager");
        Add("Tên Role", "Role Name"); Add("Cấp bậc", "Hierarchy Level");
        Add("Địa chỉ SQL Server", "SQL Server Address"); Add("Cổng", "Port"); Add("Tên Database", "Database Name");
        Add("Xác thực", "Authentication"); Add("Địa chỉ Server", "Server Address"); Add("Thư mục Share", "Folder Share"); Add("Thư mục con", "Subfolder");

        // System settings / mail / storage.
        Add("Thiết lập hệ thống", "System Settings");
        Add("Cấu hình SQL Server, Mail Server Microsoft 365, Local Server/NAS và cơ chế nén tài liệu kỹ thuật dùng trong nhà máy.", "Configure SQL Server, Microsoft 365 Mail Server, Local Server/NAS, and technical-document compression.");
        Add("SQL Server / Database", "SQL Server / Database"); Add("Hiện mật khẩu", "Show Password"); Add("Mã hóa kết nối", "Encrypt connection"); Add("Tin cậy chứng chỉ máy chủ", "Trust server certificate");
        Add("Kiểm tra SQL", "Test SQL"); Add("Lưu SQL Server", "Save SQL Server");
        Add("Local Server / Network Share", "Local Server / Network Share"); Add("Lưu attachment lên Network Share", "Store attachments on Network Share");
        Add("Nén tự động file kỹ thuật", "Automatic Technical File Compression"); Add("Bật ZIP tự động", "Enable Automatic ZIP"); Add("Ngưỡng nén (MB)", "Compression Threshold (MB)"); Add("Phần mở rộng", "Extensions");
        Add("Kiểm tra File Server", "Test File Server"); Add("Lưu File Server", "Save File Server"); Add("Storage Root: cấu hình chưa hợp lệ", "Storage Root: invalid configuration");
        Add("Thông báo Email / Microsoft Graph Mail Server", "Email Notification / Microsoft Graph Mail Server"); Add("A. App Registration - chỉ làm một lần", "A. App Registration - one-time setup");
        Add("B. Trạng thái Mail Server", "B. Mail Server Status"); Add("Bật Email Notification qua Server Mail Worker", "Enable Email Notifications via Server Mail Worker");
        Add("Mở Microsoft Entra", "Open Microsoft Entra"); Add("Copy hướng dẫn App", "Copy App Setup Guide"); Add("Đăng nhập / Đổi tài khoản", "Sign In / Change Account"); Add("Đăng xuất Microsoft", "Sign Out of Microsoft");
        Add("Gửi email test", "Send Test Email"); Add("Xử lý Queue", "Process Queue"); Add("Làm mới trạng thái", "Refresh Status");
        Add("DCR Mail Server Console", "DCR Mail Server Console");
        Add("Tài khoản gửi / Gợi ý đăng nhập", "Sender Account / Login Hint"); Add("Email nhận thử", "Test Recipient Email");
        Add("Tài khoản gửi / Login hint", "Sender Account / Login Hint");

        Add("5. Tài liệu đính kèm", "5. Attachments");
        Add("Audit Trail", "Audit Trail");
        Add("Kế hoạch, MRD & Tracking", "Plan, MRD & Tracking");
        Add("Xác nhận xóa DCR", "Confirm DCR Deletion");
        Add("Xóa DCR khỏi hệ thống", "Delete DCR from System");
        Add("Chỉ ZIP khi file thuộc danh sách extension kỹ thuật và có dung lượng lớn hơn ngưỡng. Database chỉ lưu metadata/path/hash, không lưu byte[].", "ZIP only technical-file extensions above the configured size threshold. The database stores metadata/path/hash only, not byte[].");
        Add("Chỉ cần cấu hình Microsoft 365 trên một máy chủ. Tất cả máy client chỉ ghi EmailOutbox vào SQL; người dùng DCR không cần đăng nhập Outlook/Microsoft.", "Configure Microsoft 365 on one server only. Client machines write EmailOutbox records to SQL; DCR users do not sign in to Outlook/Microsoft.");
        Add("Cùng cấu hình với Quản trị > Thiết lập hệ thống. Console này chỉ giữ lại để thao tác trực tiếp trên máy chủ khi cần.", "Uses the same configuration as Administration > System Settings. This console is retained only for direct server operations when needed.");
        Add("Cấu hình SQL được lưu tại %LOCALAPPDATA%\\DCRManagementSystem\\Data\\Config\\database.config.json và dùng chung giữa Debug/Release.", "SQL configuration is stored under %LOCALAPPDATA%\\DCRManagementSystem\\Data\\Config\\database.config.json and is shared by Debug/Release builds.");
        Add("Database mặc định: 172.168.8.183:3333 / DCRManagement. Cấu hình này được đọc trước khi ứng dụng kết nối database.", "Default database: 172.168.8.183:3333 / DCRManagement. This configuration is read before the application connects to the database.");
        Add("Lưu ý: Planned Start Date không được sau Planned End Date. Nếu yêu cầu nhận diện vật liệu, Station for Material Usage là bắt buộc.", "Note: Planned Start Date cannot be after Planned End Date. If material identification is required, Station for Material Usage is mandatory.");
        Add("Mặc định: \\\\172.168.8.209\\AutoUpdate\\DCR. Ứng dụng sử dụng quyền Windows hiện tại để truy cập share.", "Default: \\\\172.168.8.209\\AutoUpdate\\DCR. The application uses the current Windows credentials to access the share.");
        Add("Trong Microsoft Entra tạo Public/Desktop App: Redirect URI http://localhost; Microsoft Graph Delegated permission Mail.Send. Sau đó dán Tenant ID và Client ID vào bên dưới. Khi đổi tài khoản gửi sau này KHÔNG cần tạo App Registration mới.", "In Microsoft Entra create a Public/Desktop App with redirect URI http://localhost and Microsoft Graph delegated permission Mail.Send. Then paste the Tenant ID and Client ID below. Changing the sender account later does NOT require a new App Registration.");
        Add("Ưu tiên nhỏ hơn được xét trước. Requesting Department để trống = áp dụng chung. ", "Lower priority values are evaluated first. Blank Requesting Department = applies to all. ");

        // Detailed DCR form labels and table headers.
        Add("Tiêu đề", "Title"); Add("Chương trình", "Program"); Add("Giai đoạn Build", "Build Stage");
        Add("Người tạo", "Owner"); Add("Trạng thái", "Status"); Add("Ngày tạo", "Created"); Add("Lưu gần nhất", "Last Saved");
        Add("Cấp", "Stage"); Add("Phiên bản", "Rev"); Add("Hoạt động", "Active"); Add("Bảo vệ", "Protected");
        Add("Mô tả", "Description"); Add("Ưu tiên", "Priority");
        Add("Mã DCR", "DCR Number"); Add("Ngày tạo", "Created Date"); Add("Phòng ban yêu cầu *", "Requesting Department *");
        Add("Nhóm Module", "Module Group"); Add("Người tạo yêu cầu", "Request Owner"); Add("Điện thoại", "Cell Phone");
        Add("Chương trình *", "Program *"); Add("Dòng sản phẩm *", "Product Line *"); Add("Giai đoạn Build *", "Build Stage *"); Add("ECR liên quan #", "Related ECR #");
        Add("PPS liên quan #", "Related PPS #"); Add("ECN liên quan #", "Related ECN #"); Add("MCN liên quan #", "Related MCN #");
        Add("Tiêu đề *", "Title *"); Add("Mô tả vấn đề *", "Problem Description *"); Add("Giải pháp *", "Solution *");
        Add("Thay đổi vật liệu", "Material Change"); Add("Chi tiết Form / Fit / Function", "Form / Fit / Function detail");
        Add("Số lượng Retrofit", "Retrofit Volume"); Add("Hướng dẫn Retrofit", "Retrofit Instruction");
        Add("Tải tệp", "Upload File"); Add("Mở", "Open"); Add("Xóa", "Delete");
        Add("Loại", "Type"); Add("Tên tệp", "File Name"); Add("Dung lượng gốc", "Original"); Add("Dung lượng lưu", "Stored");
        Add("Người tải lên", "Uploaded By"); Add("Thời điểm tải", "Uploaded At");
        Add("Dán từ Excel", "Paste from Excel"); Add("Nhập Excel (.xlsx)", "Import Excel (.xlsx)");
        Add("Loại thay đổi", "Change Type"); Add("Mã linh kiện *", "Part Number *"); Add("Tên linh kiện *", "Part Name *");
        Add("Số lượng *", "Quantity *"); Add("Thay thế bởi", "Replaced By");
        Add("Ngày hàng dự kiến", "Expected Arrival Date"); Add("Số Production Order", "Production Order Number");
        Add("Ngày bắt đầu kế hoạch", "Planned Start Date"); Add("Ngày kết thúc kế hoạch", "Planned End Date");
        Add("Vật liệu DCR phải được nhận diện bằng nhãn DCO", "DCR material must be identified with DCO material labels");
        Add("Nhà cung cấp đáp ứng MRD timing", "Supplier can support MRD timing");
        Add("Cần quy trình tạm thời", "Temporary process needed"); Add("Cần rework linh kiện", "Rework on the part needed");
        Add("Người phê duyệt", "Approver"); Add("Phòng", "Dept"); Add("Quyết định", "Decision"); Add("Ngày", "Date");
        Add("Nhận xét", "Comments"); Add("Xác thực", "Auth"); Add("Xác minh", "Verify"); Add("Chữ ký", "Signature");
        Add("Thời gian", "Time"); Add("Người dùng", "User"); Add("Hành động", "Action"); Add("Giá trị cũ", "Old Value");
        Add("Giá trị mới", "New Value"); Add("Máy tính", "Computer"); Add("Windows Identity", "Windows Identity");
        Add("Họ và tên", "Full Name"); Add("Điện thoại", "Phone"); Add("Tài khoản Windows", "Windows Account");
        Add("Quản lý trực tiếp", "Direct Manager"); Add("Mật khẩu mới", "New Password");
        Add("Mã", "Code"); Add("Tên", "Name"); Add("Director / Trưởng Khối", "Director / Block Head");
        Add("Cấp bậc", "Level"); Add("Phạm vi", "Scope"); Add("Tên Role", "Role Name"); Add("Cấp bậc", "Hierarchy Level");
        Add("Phòng yêu cầu", "Requesting Dept"); Add("Phòng mục tiêu", "Target Dept"); Add("Nguồn người duyệt", "Approver Source");
        Add("Role người duyệt", "Approver Role"); Add("Người duyệt cụ thể", "Specific User"); Add("Phòng ảnh hưởng", "Impacted Dept");
        Add("Quy tắc định tuyến", "Routing rules"); Add("Các cấp phê duyệt", "Workflow stages"); Add("Cấp #", "Stage #");
        Add("Mã cấp", "Stage Code"); Add("Tên cấp", "Stage Name"); Add("Chi phí ước tính", "Estimated Cost");
        Add("Loại tệp đính kèm", "Attachment Type");

        Add("Nhận xét có thể để trống. Nhập mật khẩu để xác thực lại quyết định.", "Comment is optional. Enter your password to re-authenticate the decision.");
        Add("Lý do là bắt buộc. Nhập mật khẩu để xác thực lại quyết định.", "A reason is required. Enter your password to re-authenticate the decision.");
        Add("Vui lòng nhập lý do.", "Please enter a reason."); Add("Vui lòng nhập mật khẩu để xác thực quyết định.", "Please enter your password to authenticate the decision.");

        // Decision dialog and standard labels.
        Add("Phê duyệt DCR", "Approve DCR"); Add("Từ chối DCR", "Reject DCR"); Add("Yêu cầu bổ sung thông tin", "Request Information"); Add("Nhận xét", "Comment"); Add("Lý do *", "Reason *");
        Add("Loại", "Type"); Add("Tên tệp", "File Name"); Add("Dung lượng", "Size"); Add("ZIP", "ZIP"); Add("Người tải lên", "Uploaded By"); Add("Thời điểm tải", "Uploaded At");
        Add("Mã", "Code"); Add("Tên", "Name"); Add("Email", "Email"); Add("Điện thoại", "Cell Phone"); Add("Ngày tạo", "Created Date"); Add("Mã DCR", "DCR Number"); Add("Phòng ban yêu cầu *", "Requesting Department *"); Add("Nhóm Module", "Module Group"); Add("Người tạo yêu cầu", "Request Owner");

        return result;
    }

    private static readonly (string From, string To)[] ViToEnFragments =
    {
        ("Không thể ", "Unable to "), ("Không có ", "No "), ("Vui lòng ", "Please "),
        ("Đã đăng nhập", "Signed in"), ("Đăng nhập thành công", "Sign-in successful"),
        ("Đã đăng xuất", "Signed out"), ("Đã lưu ", "Saved "), ("Đã thêm ", "Added "),
        ("Đang xác thực", "Authenticating"), ("Đang kết nối", "Connecting"), ("Đang kiểm tra", "Checking"),
        ("Đang mở trình duyệt Microsoft", "Opening Microsoft browser"),
        ("Tài khoản gửi", "Sender account"), ("người phê duyệt", "approver"), ("phòng ban", "department"),
        ("Khối", "Business Unit"), ("Bản nháp", "Draft"), ("Đã duyệt", "Approved"), ("Bị từ chối", "Rejected")
    };

    private static readonly (string From, string To)[] EnToViFragments =
    {
        ("Unable to ", "Không thể "), ("Please ", "Vui lòng "), ("Signed in", "Đã đăng nhập"),
        ("Sign-in successful", "Đăng nhập thành công"), ("Signed out", "Đã đăng xuất"),
        ("Authenticating", "Đang xác thực"), ("Connecting", "Đang kết nối"), ("Checking", "Đang kiểm tra")
    };
}
