using System.Runtime.InteropServices;

namespace DCRManagementSystem.Helpers;

/// <summary>
/// Borderless, application-branded window chrome shared by every WinForms window.
/// It keeps the existing form contents untouched by moving them into a content host,
/// then adds a compact title bar with minimize / maximize / close controls.
/// </summary>
public static class ModernWindowChrome
{
    public const int LogicalTitleBarHeight = 44;
    private const string MarkerName = "__DCR_MODERN_CHROME__";

    public static void Prepare(Form form)
    {
        if (form is null)
            throw new ArgumentNullException(nameof(form));

        if (form.Tag is ChromeMarker)
            return;

        var originalBorder = form.FormBorderStyle;
        var marker = new ChromeMarker(originalBorder);
        form.Tag = marker;

        form.Load += (_, _) => Install(form, marker);
        form.HandleDestroyed += (_, _) => marker.NativeWindow?.ReleaseHandle();
    }

    private static void Install(Form form, ChromeMarker marker)
    {
        if (marker.Installed || form.IsDisposed)
            return;

        marker.Installed = true;

        var scale = Math.Max(1f, form.DeviceDpi / 96f);
        var titleBarHeight = Math.Max(40, (int)Math.Round(LogicalTitleBarHeight * scale));
        var originalClientSize = form.ClientSize;
        var wasMaximized = form.WindowState == FormWindowState.Maximized;
        var canResize = marker.OriginalBorderStyle is FormBorderStyle.Sizable or FormBorderStyle.SizableToolWindow;

        form.SuspendLayout();
        try
        {
            var existingControls = form.Controls.Cast<Control>().ToArray();
            form.Controls.Clear();

            form.FormBorderStyle = FormBorderStyle.None;
            form.Icon = (Icon)AppBranding.AppIcon.Clone();

            if (!wasMaximized)
                form.ClientSize = new Size(originalClientSize.Width, originalClientSize.Height + titleBarHeight);

            if (form.MinimumSize.Height > 0)
                form.MinimumSize = new Size(form.MinimumSize.Width, form.MinimumSize.Height + titleBarHeight);
            if (form.MaximumSize.Height > 0 && form.MaximumSize.Height < int.MaxValue - titleBarHeight)
                form.MaximumSize = new Size(form.MaximumSize.Width, form.MaximumSize.Height + titleBarHeight);

            var contentHost = new Panel
            {
                Name = MarkerName + "_CONTENT",
                Dock = DockStyle.Fill,
                BackColor = form.BackColor,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            contentHost.Controls.AddRange(existingControls);

            var titleBar = BuildTitleBar(form, titleBarHeight);

            // Thêm các control vào form
            form.Controls.Add(contentHost);
            form.Controls.Add(titleBar);

            // Đưa titleBar về Back để DockStyle.Top được ưu tiên phân bổ layout trước,
            // đảm bảo contentHost (DockStyle.Fill) bắt đầu từ dưới titleBar mà không bị đè lên.
            titleBar.SendToBack();
            contentHost.BringToFront();

            marker.NativeWindow = new ChromeNativeWindow(form, canResize, Math.Max(5, (int)Math.Round(6 * scale)));
            marker.NativeWindow.AssignHandle(form.Handle);
            ApplyRoundedCorners(form);
        }
        finally
        {
            form.ResumeLayout(true);
        }
    }

    private static Control BuildTitleBar(Form form, int height)
    {
        var bar = new Panel
        {
            Name = MarkerName,
            Dock = DockStyle.Top,
            Height = height,
            BackColor = Color.FromArgb(248, 251, 248),
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            TabStop = false
        };

        var accent = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 1,
            BackColor = UiTheme.Accent
        };

        var logoSize = Math.Max(24, height - 16);
        var logo = AppBranding.CreateLogoPictureBox(
            new Size(logoSize, logoSize),
            new Point(Math.Max(10, (height - logoSize) / 2 + 2), (height - logoSize) / 2));

        var title = new Label
        {
            Text = form.Text,
            AutoEllipsis = true,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
            ForeColor = UiTheme.TextPrimary,
            BackColor = Color.Transparent,
            Location = new Point(logo.Right + 8, 0),
            Height = height,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        var close = CreateCaptionButton("×", form, height, isClose: true);
        close.Click += (_, _) => form.Close();

        var maximize = CreateCaptionButton("□", form, height);
        maximize.Visible = form.MaximizeBox;
        maximize.Enabled = form.MaximizeBox;
        maximize.Click += (_, _) => ToggleMaximize(form, maximize);

        var minimize = CreateCaptionButton("−", form, height);
        minimize.Visible = form.MinimizeBox;
        minimize.Enabled = form.MinimizeBox;
        minimize.Click += (_, _) => form.WindowState = FormWindowState.Minimized;

        void LayoutButtons()
        {
            var x = bar.ClientSize.Width;
            close.Location = new Point(x - close.Width, 0);
            x -= close.Width;
            maximize.Location = new Point(x - maximize.Width, 0);
            if (maximize.Visible) x -= maximize.Width;
            minimize.Location = new Point(x - minimize.Width, 0);
            if (minimize.Visible) x -= minimize.Width;
            title.Width = Math.Max(40, x - title.Left - 8);
        }

        bar.Resize += (_, _) => LayoutButtons();
        form.TextChanged += (_, _) => title.Text = form.Text;
        form.Resize += (_, _) =>
        {
            maximize.Text = form.WindowState == FormWindowState.Maximized ? "❐" : "□";
            ApplyRoundedCorners(form);
        };

        AttachDragBehavior(bar, form, maximize);
        AttachDragBehavior(title, form, maximize);
        AttachDragBehavior(logo, form, maximize);

        bar.Controls.Add(accent);
        bar.Controls.Add(logo);
        bar.Controls.Add(title);
        bar.Controls.Add(minimize);
        bar.Controls.Add(maximize);
        bar.Controls.Add(close);
        LayoutButtons();

        return bar;
    }

    private static Button CreateCaptionButton(string text, Form form, int height, bool isClose = false)
    {
        var width = Math.Max(46, (int)Math.Round(50 * Math.Max(1f, form.DeviceDpi / 96f)));
        var button = new Button
        {
            Text = text,
            Width = width,
            Height = height - 1,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(248, 251, 248),
            ForeColor = isClose ? Color.FromArgb(112, 60, 36) : Color.FromArgb(35, 73, 49),
            Font = new Font("Segoe UI", isClose ? 17F : 12F, FontStyle.Regular),
            TabStop = false,
            Cursor = Cursors.Hand,
            TextAlign = ContentAlignment.MiddleCenter,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseDownBackColor = isClose ? Color.FromArgb(219, 55, 68) : UiTheme.PrimarySoft;
        button.FlatAppearance.MouseOverBackColor = isClose ? Color.FromArgb(232, 65, 78) : UiTheme.PrimarySoft;
        if (isClose)
            button.MouseEnter += (_, _) => button.ForeColor = Color.White;
        if (isClose)
            button.MouseLeave += (_, _) => button.ForeColor = Color.FromArgb(112, 60, 36);
        return button;
    }

    private static void AttachDragBehavior(Control control, Form form, Button maximizeButton)
    {
        control.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
                return;

            if (e.Clicks >= 2 && form.MaximizeBox)
            {
                ToggleMaximize(form, maximizeButton);
                return;
            }

            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessage(form.Handle, NativeMethods.WM_NCLBUTTONDOWN, (IntPtr)NativeMethods.HTCAPTION, IntPtr.Zero);
        };
    }

    private static void ToggleMaximize(Form form, Button maximizeButton)
    {
        if (!form.MaximizeBox)
            return;

        form.WindowState = form.WindowState == FormWindowState.Maximized
            ? FormWindowState.Normal
            : FormWindowState.Maximized;
        maximizeButton.Text = form.WindowState == FormWindowState.Maximized ? "❐" : "□";
    }

    private static void ApplyRoundedCorners(Form form)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || !form.IsHandleCreated)
            return;

        try
        {
            var preference = form.WindowState == FormWindowState.Maximized
                ? NativeMethods.DWMWCP_DONOTROUND
                : NativeMethods.DWMWCP_ROUND;
            NativeMethods.DwmSetWindowAttribute(
                form.Handle,
                NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE,
                ref preference,
                Marshal.SizeOf<int>());
        }
        catch
        {
            // Cosmetic only; never prevent a window from opening.
        }
    }

    private sealed class ChromeMarker
    {
        public ChromeMarker(FormBorderStyle originalBorderStyle) => OriginalBorderStyle = originalBorderStyle;
        public FormBorderStyle OriginalBorderStyle { get; }
        public bool Installed { get; set; }
        public ChromeNativeWindow? NativeWindow { get; set; }
    }

    private sealed class ChromeNativeWindow : NativeWindow
    {
        private readonly Form _form;
        private readonly bool _canResize;
        private readonly int _resizeBorder;

        public ChromeNativeWindow(Form form, bool canResize, int resizeBorder)
        {
            _form = form;
            _canResize = canResize;
            _resizeBorder = resizeBorder;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_NCHITTEST && _canResize && _form.WindowState == FormWindowState.Normal)
            {
                base.WndProc(ref m);
                if ((int)m.Result == NativeMethods.HTCLIENT)
                {
                    var screenPoint = new Point(NativeMethods.GetSignedLowWord(m.LParam), NativeMethods.GetSignedHighWord(m.LParam));
                    var clientPoint = _form.PointToClient(screenPoint);
                    var left = clientPoint.X <= _resizeBorder;
                    var right = clientPoint.X >= _form.ClientSize.Width - _resizeBorder;
                    var top = clientPoint.Y <= _resizeBorder;
                    var bottom = clientPoint.Y >= _form.ClientSize.Height - _resizeBorder;

                    if (left && top) m.Result = (IntPtr)NativeMethods.HTTOPLEFT;
                    else if (right && top) m.Result = (IntPtr)NativeMethods.HTTOPRIGHT;
                    else if (left && bottom) m.Result = (IntPtr)NativeMethods.HTBOTTOMLEFT;
                    else if (right && bottom) m.Result = (IntPtr)NativeMethods.HTBOTTOMRIGHT;
                    else if (left) m.Result = (IntPtr)NativeMethods.HTLEFT;
                    else if (right) m.Result = (IntPtr)NativeMethods.HTRIGHT;
                    else if (top) m.Result = (IntPtr)NativeMethods.HTTOP;
                    else if (bottom) m.Result = (IntPtr)NativeMethods.HTBOTTOM;
                }
                return;
            }

            if (m.Msg == NativeMethods.WM_GETMINMAXINFO)
            {
                base.WndProc(ref m);
                AdjustMaximizedBounds(m.HWnd, m.LParam);
                return;
            }

            base.WndProc(ref m);
        }

        private void AdjustMaximizedBounds(IntPtr hwnd, IntPtr lParam)
        {
            var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero)
                return;

            var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            if (!NativeMethods.GetMonitorInfo(monitor, ref info))
                return;

            var workWidth = Math.Max(1, info.rcWork.Right - info.rcWork.Left);
            var workHeight = Math.Max(1, info.rcWork.Bottom - info.rcWork.Top);

            // Respect the configured MaximumSize when a resizable form is maximized.
            // If the monitor is larger than that maximum, center the maximized window
            // inside the monitor work area instead of pinning it to the top-left corner.
            // On smaller monitors the window still uses the whole available work area.
            var configuredMaximum = _form.MaximumSize;
            var maxWidth = configuredMaximum.Width > 0
                ? Math.Min(workWidth, configuredMaximum.Width)
                : workWidth;
            var maxHeight = configuredMaximum.Height > 0
                ? Math.Min(workHeight, configuredMaximum.Height)
                : workHeight;

            var centeredOffsetX = Math.Max(0, (workWidth - maxWidth) / 2);
            var centeredOffsetY = Math.Max(0, (workHeight - maxHeight) / 2);

            var mmi = Marshal.PtrToStructure<NativeMethods.MINMAXINFO>(lParam);
            mmi.ptMaxPosition.x = info.rcWork.Left - info.rcMonitor.Left + centeredOffsetX;
            mmi.ptMaxPosition.y = info.rcWork.Top - info.rcMonitor.Top + centeredOffsetY;
            mmi.ptMaxSize.x = maxWidth;
            mmi.ptMaxSize.y = maxHeight;
            Marshal.StructureToPtr(mmi, lParam, false);
        }
    }

    private static class NativeMethods
    {
        public const int WM_NCLBUTTONDOWN = 0x00A1;
        public const int WM_NCHITTEST = 0x0084;
        public const int WM_GETMINMAXINFO = 0x0024;
        public const int HTCAPTION = 2;
        public const int HTCLIENT = 1;
        public const int HTLEFT = 10;
        public const int HTRIGHT = 11;
        public const int HTTOP = 12;
        public const int HTTOPLEFT = 13;
        public const int HTTOPRIGHT = 14;
        public const int HTBOTTOM = 15;
        public const int HTBOTTOMLEFT = 16;
        public const int HTBOTTOMRIGHT = 17;
        public const int MONITOR_DEFAULTTONEAREST = 2;
        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        public const int DWMWCP_DONOTROUND = 1;
        public const int DWMWCP_ROUND = 2;

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

        public static int GetSignedLowWord(IntPtr value) => unchecked((short)(long)value);
        public static int GetSignedHighWord(IntPtr value) => unchecked((short)((long)value >> 16));

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }
    }
}