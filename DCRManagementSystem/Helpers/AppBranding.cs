using System.Reflection;

namespace DCRManagementSystem.Helpers;

public static class AppBranding
{
    private const string LogoResourceName = "DCRManagementSystem.Assets.DcrLogo.png";
    private const string IconResourceName = "DCRManagementSystem.Assets.DcrApp.ico";

    private static Image? _logo;
    private static Icon? _icon;

    public static Image Logo
    {
        get
        {
            if (_logo is not null)
                return _logo;

            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(LogoResourceName)
                ?? throw new InvalidOperationException($"Không tìm thấy logo resource '{LogoResourceName}'.");
            using var source = Image.FromStream(stream);
            _logo = new Bitmap(source);
            return _logo;
        }
    }

    public static Icon AppIcon
    {
        get
        {
            if (_icon is not null)
                return _icon;

            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(IconResourceName)
                ?? throw new InvalidOperationException($"Không tìm thấy icon resource '{IconResourceName}'.");
            using var source = new Icon(stream);
            _icon = (Icon)source.Clone();
            return _icon;
        }
    }

    public static PictureBox CreateLogoPictureBox(Size size, Point location)
    {
        return new PictureBox
        {
            Image = Logo,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
            Size = size,
            Location = location,
            TabStop = false
        };
    }
}
