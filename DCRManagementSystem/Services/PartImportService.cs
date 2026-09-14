using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using DCRManagementSystem.Models;

namespace DCRManagementSystem.Services;

public static class PartImportService
{
    private const int MaxSupportedColumns = 6;

    public static List<PartEditItem> ImportXlsx(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Không tìm thấy file Excel.", filePath);

        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidDataException("Workbook không có worksheet.");

        var range = worksheet.RangeUsed();
        if (range is null)
            return new List<PartEditItem>();

        var columnCount = Math.Clamp(range.ColumnCount(), 1, MaxSupportedColumns);
        var rows = new List<string[]>();
        foreach (var row in range.Rows())
        {
            var values = new string[columnCount];
            for (var column = 1; column <= columnCount; column++)
                values[column - 1] = row.Cell(column).GetFormattedString().Trim();
            rows.Add(values);
        }

        return ConvertRows(rows);
    }

    public static List<PartEditItem> ParseClipboardText(string clipboardText)
    {
        if (string.IsNullOrWhiteSpace(clipboardText))
            return new List<PartEditItem>();

        var rows = clipboardText
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t').Select(x => x.Trim()).Take(MaxSupportedColumns).ToArray())
            .ToList();

        return ConvertRows(rows);
    }

    private static List<PartEditItem> ConvertRows(IReadOnlyList<string[]> rows)
    {
        if (rows.Count == 0)
            return new List<PartEditItem>();

        var hasHeader = LooksLikeHeader(rows[0]);
        var mapping = hasHeader ? ResolveHeaderMapping(rows[0]) : ResolvePositionalMapping(rows[0].Length);
        var startIndex = hasHeader ? 1 : 0;
        var result = new List<PartEditItem>();

        for (var i = startIndex; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.All(string.IsNullOrWhiteSpace))
                continue;

            result.Add(new PartEditItem
            {
                ChangeType = Get(row, mapping.ChangeType),
                PartNumber = Get(row, mapping.PartNumber),
                PartName = Get(row, mapping.PartName),
                Quantity = Get(row, mapping.Quantity),
                ReplacedBy = Get(row, mapping.ReplacedBy),
                KPC = string.Empty,
                SortOrder = result.Count + 1
            });
        }

        return result;
    }

    private static ColumnMapping ResolveHeaderMapping(IReadOnlyList<string> header)
    {
        var normalized = header.Select(NormalizeHeader).ToArray();
        int Find(params string[] candidates)
        {
            for (var i = 0; i < normalized.Length; i++)
            {
                if (candidates.Any(c => normalized[i].Contains(c, StringComparison.Ordinal)))
                    return i;
            }
            return -1;
        }

        return new ColumnMapping(
            Find("changetype", "change", "loaithaydoi"),
            Find("partnumber", "partno", "malinhkien"),
            Find("partname", "tenlinhkien"),
            Find("quantity", "qty", "soluong"),
            Find("replacedby", "replacement", "thaytheboi"));
    }

    private static ColumnMapping ResolvePositionalMapping(int columnCount)
    {
        // New format: Change Type | Part Number | Part Name | Quantity | Replaced By.
        // Legacy six-column files are still accepted; the old KPC column at index 3 is ignored.
        return columnCount >= 6
            ? new ColumnMapping(0, 1, 2, 4, 5)
            : new ColumnMapping(0, 1, 2, 3, 4);
    }

    private static bool LooksLikeHeader(IReadOnlyList<string> row)
    {
        var normalized = row.Select(NormalizeHeader).ToArray();
        return normalized.Any(x => x.Contains("partnumber", StringComparison.Ordinal) || x.Contains("malinhkien", StringComparison.Ordinal)) &&
               normalized.Any(x => x.Contains("partname", StringComparison.Ordinal) || x.Contains("tenlinhkien", StringComparison.Ordinal));
    }

    private static string NormalizeHeader(string value)
    {
        var normalized = (value ?? string.Empty).Normalize(NormalizationForm.FormD);
        var chars = normalized
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray();
        return new string(chars).Normalize(NormalizationForm.FormC);
    }

    private static string Get(IReadOnlyList<string> row, int index)
        => index >= 0 && index < row.Count ? row[index].Trim() : string.Empty;

    private readonly record struct ColumnMapping(int ChangeType, int PartNumber, int PartName, int Quantity, int ReplacedBy);
}
