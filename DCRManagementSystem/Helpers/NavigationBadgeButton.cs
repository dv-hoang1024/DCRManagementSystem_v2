using System.Drawing.Drawing2D;

namespace DCRManagementSystem.Helpers;

public sealed class NavigationBadgeButton : Button
{
    private int _badgeCount;

    public int BadgeCount
    {
        get => _badgeCount;
        set
        {
            var normalized = Math.Max(0, value);
            if (_badgeCount == normalized) return;
            _badgeCount = normalized;
            Invalidate();
        }
    }

    public Color BadgeBackColor { get; set; } = Color.FromArgb(230, 91, 64);
    public Color BadgeForeColor { get; set; } = Color.White;

    protected override void OnPaint(PaintEventArgs pevent)
    {
        base.OnPaint(pevent);
        if (_badgeCount <= 0) return;

        pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var text = _badgeCount > 99 ? "99+" : _badgeCount.ToString();
        using var font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold);
        var measured = pevent.Graphics.MeasureString(text, font);
        var width = Math.Max(25, (int)Math.Ceiling(measured.Width) + 12);
        var height = 22;
        var rect = new Rectangle(Math.Max(6, ClientSize.Width - width - 13), (ClientSize.Height - height) / 2, width, height);

        using var path = RoundedRect(rect, 11);
        using var brush = new SolidBrush(BadgeBackColor);
        using var textBrush = new SolidBrush(BadgeForeColor);
        pevent.Graphics.FillPath(brush, path);
        pevent.Graphics.DrawString(text, font, textBrush, rect, new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        });
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
