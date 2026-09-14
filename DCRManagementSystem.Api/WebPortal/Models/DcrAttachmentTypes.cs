namespace DCRManagementSystem.Api.WebPortal.Models;

public sealed record DcrAttachmentTypeOption(string Value, string DisplayName);

public static class DcrAttachmentTypes
{
    public const string General = "General";
    public const string PartIllustration = "PartIllustration";
    public const string Specification = "Specification";
    public const string TemporaryProcess = "TemporaryProcess";
    public const string ReworkInstruction = "ReworkInstruction";
    public const string SupportingDocument = "SupportingDocument";
    public const string Other = "Other";

    public static readonly IReadOnlyList<DcrAttachmentTypeOption> All =
    [
        new(General, "Tài liệu chung"),
        new(PartIllustration, "Bản vẽ / Hình ảnh chi tiết"),
        new(Specification, "Thông số kỹ thuật"),
        new(TemporaryProcess, "Quy trình tạm thời"),
        new(ReworkInstruction, "Hướng dẫn sửa chữa"),
        new(SupportingDocument, "Tài liệu hỗ trợ"),
        new(Other, "Tài liệu khác")
    ];

    public static string Normalize(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        var option = All.FirstOrDefault(x => x.Value.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        if (option is not null) return option.Value;

        return normalized.ToUpperInvariant() switch
        {
            "TECHNICALDOCUMENT" => SupportingDocument,
            "DRAWING" or "PHOTO" => PartIllustration,
            _ => throw new InvalidOperationException($"Loại tài liệu '{normalized}' không hợp lệ.")
        };
    }

    public static string FormatDisplayNames(string? storedValue)
    {
        var values = (storedValue ?? string.Empty)
            .Split(['|', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length == 0) return "Chưa phân loại";

        return string.Join(" · ", values.Select(DisplayName));
    }

    private static string DisplayName(string value)
    {
        var canonical = All.FirstOrDefault(x => x.Value.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (canonical is not null) return canonical.DisplayName;

        return value.Trim().ToUpperInvariant() switch
        {
            "TECHNICALDOCUMENT" => "Tài liệu hỗ trợ",
            "DRAWING" or "PHOTO" => "Bản vẽ / Hình ảnh chi tiết",
            _ => value
        };
    }
}
