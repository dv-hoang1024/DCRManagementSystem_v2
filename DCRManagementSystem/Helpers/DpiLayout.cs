using System.Runtime.CompilerServices;

namespace DCRManagementSystem.Helpers;

/// <summary>
/// Per-monitor DPI layout manager for the code-built WinForms UI.
///
/// Every form/control in this application is authored with 96-DPI logical pixel
/// constants. WinForms AutoScale is deliberately disabled because controls are
/// created at runtime; allowing AutoScale to rescale those runtime values again
/// when a form moves between monitors causes cumulative geometry drift.
///
/// This helper captures the immutable 96-DPI control geometry before the form is
/// shown, then reapplies that baseline at the destination monitor DPI. Therefore a
/// 150% -> 100% -> 150% round trip always returns to exactly the same proportions.
/// Point-unit fonts are not multiplied here; they remain readable and stable while
/// the control geometry, paddings, hit targets and grid rows follow monitor DPI.
/// </summary>
public static class DpiLayout
{
    public const int DesignDpi = 96;

    private static readonly ConditionalWeakTable<Form, FormState> AttachedForms = new();
    private static readonly ConditionalWeakTable<DataGridViewColumn, GridColumnMetric> GridColumns = new();
    private static readonly ConditionalWeakTable<Control, ResponsiveLayoutState> ResponsiveLayouts = new();

    public static int Scale(Control? control, int logicalPixels)
    {
        if (logicalPixels == 0)
            return 0;

        var dpi = GetDpi(control);
        return ScaleAtDpi(logicalPixels, dpi);
    }

    public static Size Scale(Control? control, Size logicalSize)
        => new(Scale(control, logicalSize.Width), Scale(control, logicalSize.Height));

    public static Padding Scale(Control? control, Padding logicalPadding)
        => new(
            Scale(control, logicalPadding.Left),
            Scale(control, logicalPadding.Top),
            Scale(control, logicalPadding.Right),
            Scale(control, logicalPadding.Bottom));


    /// <summary>
    /// Enables or disables geometry scaling for a specific top-level form.
    /// This is useful for compact fixed dialogs such as LoginForm where the
    /// complete 96-DPI layout should remain the same physical size on every
    /// monitor while point-unit fonts remain readable.
    /// </summary>
    public static void SetGeometryScalingEnabled(Form form, bool enabled)
    {
        if (form is null)
            throw new ArgumentNullException(nameof(form));

        if (AttachedForms.TryGetValue(form, out var state))
            state.GeometryScalingEnabled = enabled;
    }

    /// <summary>
    /// Controls whether a normal-state form may be reduced to fit the destination
    /// monitor work area. Fixed-size windows can opt out so a DPI transition never
    /// changes their designed client dimensions.
    /// </summary>
    public static void SetWorkingAreaClampingEnabled(Form form, bool enabled)
    {
        if (form is null)
            throw new ArgumentNullException(nameof(form));

        if (AttachedForms.TryGetValue(form, out var state))
            state.WorkingAreaClampingEnabled = enabled;
    }

    /// <summary>
    /// Registers a custom responsive layout pass that must run after the immutable
    /// DPI baseline has been restored. This is used by code-built cards/toolbars
    /// whose geometry is deliberately recalculated from the current viewport width.
    /// </summary>
    public static void RegisterResponsiveLayout(Control owner, Action layout)
    {
        if (owner is null)
            throw new ArgumentNullException(nameof(owner));
        if (layout is null)
            throw new ArgumentNullException(nameof(layout));

        var state = ResponsiveLayouts.GetValue(owner, _ => new ResponsiveLayoutState());
        if (!state.Layouts.Contains(layout))
            state.Layouts.Add(layout);
    }

    public static void Attach(Form form)
    {
        if (form is null)
            throw new ArgumentNullException(nameof(form));

        if (AttachedForms.TryGetValue(form, out _))
            return;

        var state = new FormState();
        AttachedForms.Add(form, state);

        // Capture controls as the code-built tree is assembled. This happens before
        // the HWND is shown and therefore before any destination-monitor DPI can leak
        // into runtime Resize handlers. Load performs a final sweep for anything that
        // was created before Attach or added through an unusual path.
        form.ControlAdded += (_, e) => CaptureAndApplyAddedControl(form, e.Control);

        // Capture BEFORE ModernWindowChrome installs/scales the top-level window.
        // UiTheme registers DpiLayout before the chrome load handler.
        form.Load += (_, _) => CaptureBaseline(form, state);
        form.Shown += (_, _) => RefreshAfterLayout(form, state, captureIfMissing: true);
        form.DpiChanged += (_, _) => RefreshAfterLayout(form, state, captureIfMissing: true);
        form.ResizeEnd += (_, _) => EnsureFormFitsWorkingArea(form);
    }

    public static void ApplyDataGridViewMetrics(DataGridView grid)
    {
        if (grid is null || grid.IsDisposed)
            return;

        var rowHeight = Math.Max(1, Scale(grid, 38));
        var headerHeight = Math.Max(1, Scale(grid, 42));
        var horizontalPadding = Math.Max(0, Scale(grid, 6));
        var verticalPadding = Math.Max(0, Scale(grid, 2));

        grid.RowTemplate.Height = rowHeight;
        grid.ColumnHeadersHeight = headerHeight;

        ApplyPadding(grid.DefaultCellStyle, horizontalPadding, verticalPadding);
        ApplyPadding(grid.AlternatingRowsDefaultCellStyle, horizontalPadding, verticalPadding);
        ApplyPadding(grid.RowsDefaultCellStyle, horizontalPadding, verticalPadding);
        ApplyPadding(grid.ColumnHeadersDefaultCellStyle, horizontalPadding, 0);

        foreach (DataGridViewColumn column in grid.Columns)
        {
            if (column.AutoSizeMode != DataGridViewAutoSizeColumnMode.None)
                continue;

            if (!GridColumns.TryGetValue(column, out var metric))
            {
                metric = new GridColumnMetric(Math.Max(1, column.Width), Math.Max(2, column.MinimumWidth));
                GridColumns.Add(column, metric);
            }

            column.MinimumWidth = Math.Max(2, Scale(grid, metric.MinimumWidth));
            column.Width = Math.Max(column.MinimumWidth, Scale(grid, metric.Width));
        }

        foreach (DataGridViewRow row in grid.Rows)
        {
            if (!row.IsNewRow)
                row.Height = rowHeight;
        }
    }

    public static void EnsureFormFitsWorkingArea(Form form, int logicalMargin = 8)
    {
        if (form.IsDisposed || !form.IsHandleCreated || form.WindowState != FormWindowState.Normal)
            return;

        if (AttachedForms.TryGetValue(form, out var state) && !state.WorkingAreaClampingEnabled)
            return;

        var workingArea = Screen.FromHandle(form.Handle).WorkingArea;
        if (workingArea.Width <= 0 || workingArea.Height <= 0)
            return;

        var margin = Math.Max(0, Scale(form, logicalMargin));
        var maxWidth = Math.Max(1, workingArea.Width - margin * 2);
        var maxHeight = Math.Max(1, workingArea.Height - margin * 2);

        var width = Math.Min(form.Width, maxWidth);
        var height = Math.Min(form.Height, maxHeight);
        var left = form.Left;
        var top = form.Top;

        if (left < workingArea.Left + margin)
            left = workingArea.Left + margin;
        if (top < workingArea.Top + margin)
            top = workingArea.Top + margin;
        if (left + width > workingArea.Right - margin)
            left = Math.Max(workingArea.Left + margin, workingArea.Right - margin - width);
        if (top + height > workingArea.Bottom - margin)
            top = Math.Max(workingArea.Top + margin, workingArea.Bottom - margin - height);

        var bounds = new Rectangle(left, top, width, height);
        if (form.Bounds != bounds)
            form.Bounds = bounds;
    }

    private static void CaptureBaseline(Form form, FormState state)
    {
        if (state.BaselineCaptured || form.IsDisposed)
            return;

        state.Applying = true;
        try
        {
            CaptureTree(form, state);
            state.BaselineCaptured = true;
        }
        finally
        {
            state.Applying = false;
        }
    }

    private static void CaptureTree(Control root, FormState state)
    {
        if (root.IsDisposed || IsChromeControl(root))
            return;

        foreach (Control child in root.Controls)
        {
            if (child.IsDisposed || IsChromeControl(child))
                continue;

            if (!state.Metrics.ContainsKey(child))
            {
                state.Metrics.Add(child, ControlMetric.Capture(child));
                child.ControlAdded += (_, e) => CaptureAndApplyAddedControl(root.FindForm(), e.Control);
            }

            CaptureTree(child, state);
        }
    }

    private static void CaptureAndApplyAddedControl(Form? form, Control control)
    {
        if (form is null || control.IsDisposed || IsChromeControl(control) ||
            !AttachedForms.TryGetValue(form, out var state) || state.Applying)
        {
            return;
        }

        state.Applying = true;
        try
        {
            CaptureControlRecursive(control, state);
            if (state.BaselineCaptured && form.IsHandleCreated && state.GeometryScalingEnabled)
            {
                ApplyControlRecursive(control, state, GetDpi(form));
                RefreshTree(control);
                control.PerformLayout();
            }
        }
        finally
        {
            state.Applying = false;
        }
    }

    private static void CaptureControlRecursive(Control control, FormState state)
    {
        if (control.IsDisposed || IsChromeControl(control))
            return;

        if (!state.Metrics.ContainsKey(control))
        {
            state.Metrics.Add(control, ControlMetric.Capture(control));
            control.ControlAdded += (_, e) => CaptureAndApplyAddedControl(control.FindForm(), e.Control);
        }

        foreach (Control child in control.Controls)
            CaptureControlRecursive(child, state);
    }

    private static void RefreshAfterLayout(Form form, FormState state, bool captureIfMissing)
    {
        if (form.IsDisposed || !form.IsHandleCreated)
            return;

        try
        {
            form.BeginInvoke(new Action(() =>
            {
                if (form.IsDisposed)
                    return;

                if (captureIfMissing && !state.BaselineCaptured)
                    CaptureBaseline(form, state);

                ApplyBaseline(form, state);
                RefreshTree(form);

                // Baseline geometry is intentionally applied first. Responsive
                // containers then recompute widths/positions from the destination
                // viewport so their final values are never overwritten by the
                // baseline sweep. Parent callbacks run before child callbacks.
                RunResponsiveLayouts(form);
                PerformLayoutTree(form);
                RunResponsiveLayouts(form);
                PerformLayoutTree(form);
                EnsureFormFitsWorkingArea(form);
                form.Invalidate(true);
            }));
        }
        catch (InvalidOperationException)
        {
            // A DPI notification can race with form shutdown.
        }
    }

    private static void ApplyBaseline(Form form, FormState state)
    {
        if (!state.BaselineCaptured || state.Applying || !state.GeometryScalingEnabled)
            return;

        state.Applying = true;
        try
        {
            var dpi = GetDpi(form);

            // Apply descendants before their containers. A container's final resize
            // then runs the application's existing Resize-based responsive logic
            // AFTER child baseline values have been restored. This is important for
            // right-aligned buttons, full-width cards, wizard fields and sidebars.
            // Applying parents first would let the later child sweep overwrite those
            // responsive calculations and recreate the cross-monitor clipping bug.
            var ordered = state.Metrics
                .Where(pair => !pair.Key.IsDisposed)
                .OrderByDescending(pair => GetControlDepth(pair.Key))
                .ToArray();

            foreach (var pair in ordered)
                ApplyMetric(pair.Key, pair.Value, dpi);

            foreach (var disposed in state.Metrics.Keys.Where(x => x.IsDisposed).ToArray())
                state.Metrics.Remove(disposed);
        }
        finally
        {
            state.Applying = false;
        }
    }

    private static void ApplyControlRecursive(Control control, FormState state, int dpi)
    {
        if (control.IsDisposed || IsChromeControl(control))
            return;

        foreach (Control child in control.Controls)
            ApplyControlRecursive(child, state, dpi);

        if (state.Metrics.TryGetValue(control, out var metric))
            ApplyMetric(control, metric, dpi);
    }

    private static void ApplyMetric(Control control, ControlMetric metric, int dpi)
    {
        control.SuspendLayout();
        try
        {
            control.Margin = ScaleAtDpi(metric.Margin, dpi);
            control.Padding = ScaleAtDpi(metric.Padding, dpi);
            control.MinimumSize = ScaleAtDpi(metric.MinimumSize, dpi);
            control.MaximumSize = ScaleAtDpi(metric.MaximumSize, dpi);

            var bounds = ScaleAtDpi(metric.Bounds, dpi);
            switch (control.Dock)
            {
                case DockStyle.Top:
                case DockStyle.Bottom:
                    control.Height = Math.Max(1, bounds.Height);
                    break;
                case DockStyle.Left:
                case DockStyle.Right:
                    control.Width = Math.Max(1, bounds.Width);
                    break;
                case DockStyle.Fill:
                    break;
                default:
                    // TextBox.AutoSize is true by default but only constrains its
                    // preferred HEIGHT; its width still has to follow monitor DPI.
                    // Assign Bounds for every non-docked control and let AutoSize
                    // controls (labels/check boxes/text boxes) correct only the axis
                    // they own during the following layout pass.
                    control.Bounds = bounds;
                    break;
            }

            if (control is ScrollableControl scrollable)
            {
                scrollable.AutoScrollMargin = ScaleAtDpi(metric.AutoScrollMargin, dpi);
                if (!metric.AutoScrollMinSize.IsEmpty)
                    scrollable.AutoScrollMinSize = ScaleAtDpi(metric.AutoScrollMinSize, dpi);
            }

            if (control is SplitContainer split && metric.SplitterWidth > 0)
                split.SplitterWidth = Math.Max(1, ScaleAtDpi(metric.SplitterWidth, dpi));
        }
        finally
        {
            control.ResumeLayout(false);
        }
    }

    private static int GetControlDepth(Control control)
    {
        var depth = 0;
        for (var parent = control.Parent; parent is not null; parent = parent.Parent)
            depth++;
        return depth;
    }

    private static void RunResponsiveLayouts(Control root)
    {
        if (root.IsDisposed)
            return;

        if (ResponsiveLayouts.TryGetValue(root, out var state))
        {
            foreach (var layout in state.Layouts.ToArray())
            {
                try
                {
                    layout();
                }
                catch (ObjectDisposedException)
                {
                    // A layout callback can race with form shutdown.
                }
                catch (InvalidOperationException) when (root.IsDisposed)
                {
                    // Ignore shutdown races only; real layout errors should surface.
                }
            }
        }

        foreach (Control child in root.Controls)
            RunResponsiveLayouts(child);
    }

    private static void PerformLayoutTree(Control root)
    {
        if (root.IsDisposed)
            return;

        foreach (Control child in root.Controls)
            PerformLayoutTree(child);

        root.PerformLayout();
    }

    private static void RefreshTree(Control root)
    {
        if (root.IsDisposed)
            return;

        if (root is DataGridView grid)
            ApplyDataGridViewMetrics(grid);
        else if (root is NavigationBadgeButton)
            root.Invalidate();

        foreach (Control child in root.Controls)
            RefreshTree(child);
    }

    private static void ApplyPadding(DataGridViewCellStyle style, int horizontal, int vertical)
    {
        style.Padding = new Padding(horizontal, vertical, horizontal, vertical);
    }

    private static bool IsChromeControl(Control control)
        => control.Name.StartsWith("__DCR_MODERN_CHROME__", StringComparison.Ordinal);

    private static int GetDpi(Control? control)
    {
        if (control is null || control.IsDisposed)
            return DesignDpi;

        try
        {
            return control.DeviceDpi > 0 ? control.DeviceDpi : DesignDpi;
        }
        catch
        {
            return DesignDpi;
        }
    }

    private static int ScaleAtDpi(int logicalPixels, int dpi)
    {
        if (logicalPixels == 0)
            return 0;

        var safeDpi = dpi <= 0 ? DesignDpi : dpi;
        var scaled = (int)Math.Round(logicalPixels * safeDpi / (double)DesignDpi);
        if (scaled != 0)
            return scaled;
        return logicalPixels > 0 ? 1 : -1;
    }

    private static Size ScaleAtDpi(Size logicalSize, int dpi)
        => new(ScaleAtDpi(logicalSize.Width, dpi), ScaleAtDpi(logicalSize.Height, dpi));

    private static Point ScaleAtDpi(Point logicalPoint, int dpi)
        => new(ScaleAtDpi(logicalPoint.X, dpi), ScaleAtDpi(logicalPoint.Y, dpi));

    private static Rectangle ScaleAtDpi(Rectangle logicalBounds, int dpi)
        => new(ScaleAtDpi(logicalBounds.Location, dpi), ScaleAtDpi(logicalBounds.Size, dpi));

    private static Padding ScaleAtDpi(Padding logicalPadding, int dpi)
        => new(
            ScaleAtDpi(logicalPadding.Left, dpi),
            ScaleAtDpi(logicalPadding.Top, dpi),
            ScaleAtDpi(logicalPadding.Right, dpi),
            ScaleAtDpi(logicalPadding.Bottom, dpi));

    private sealed class ResponsiveLayoutState
    {
        public List<Action> Layouts { get; } = new();
    }

    private sealed class FormState
    {
        public bool BaselineCaptured { get; set; }
        public bool Applying { get; set; }
        public bool GeometryScalingEnabled { get; set; } = true;
        public bool WorkingAreaClampingEnabled { get; set; } = true;
        public Dictionary<Control, ControlMetric> Metrics { get; } = new(ReferenceEqualityComparer.Instance);
    }

    private sealed record ControlMetric(
        Rectangle Bounds,
        Padding Margin,
        Padding Padding,
        Size MinimumSize,
        Size MaximumSize,
        Size AutoScrollMargin,
        Size AutoScrollMinSize,
        int SplitterWidth)
    {
        public static ControlMetric Capture(Control control)
        {
            var autoScrollMargin = Size.Empty;
            var autoScrollMinSize = Size.Empty;
            if (control is ScrollableControl scrollable)
            {
                autoScrollMargin = scrollable.AutoScrollMargin;
                autoScrollMinSize = scrollable.AutoScrollMinSize;
            }

            var splitterWidth = control is SplitContainer split ? split.SplitterWidth : 0;
            return new ControlMetric(
                control.Bounds,
                control.Margin,
                control.Padding,
                control.MinimumSize,
                control.MaximumSize,
                autoScrollMargin,
                autoScrollMinSize,
                splitterWidth);
        }
    }

    private sealed record GridColumnMetric(int Width, int MinimumWidth);
}
