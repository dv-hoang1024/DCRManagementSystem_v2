using System.Collections.Concurrent;

namespace DCRManagementSystem.Helpers;

/// <summary>
/// Shared typography factory kept under the original helper name for source
/// compatibility. Fonts are intentionally created in POINT units so WinForms can
/// render them at the destination monitor DPI. Do not convert these fonts to fixed
/// pixel sizes: doing so makes text look too small when the window is moved to a
/// monitor that uses 125%, 150% or a higher Windows display scale.
/// </summary>
public static class FixedTypography
{
    private static readonly ConcurrentDictionary<FontKey, Font> FontCache = new();

    public static Font CreateFont(string familyName, float designPointSize, FontStyle style = FontStyle.Regular)
    {
        if (string.IsNullOrWhiteSpace(familyName))
            familyName = "Segoe UI";

        return GetCachedFont(familyName, Math.Max(1F, designPointSize), style);
    }

    public static Font CreateFont(Font source)
    {
        if (source is null)
            throw new ArgumentNullException(nameof(source));

        return GetCachedFont(source.Name, Math.Max(1F, source.SizeInPoints), source.Style);
    }

    private static Font GetCachedFont(string familyName, float pointSize, FontStyle style)
    {
        var key = new FontKey(
            familyName,
            (int)Math.Round(pointSize * 1000F),
            style);

        return FontCache.GetOrAdd(key, static k =>
        {
            var size = Math.Max(1F, k.PointSizeMilli / 1000F);
            try
            {
                return new Font(k.FamilyName, size, k.Style, GraphicsUnit.Point);
            }
            catch
            {
                return new Font("Segoe UI", size, k.Style, GraphicsUnit.Point);
            }
        });
    }

    private readonly record struct FontKey(
        string FamilyName,
        int PointSizeMilli,
        FontStyle Style);
}
