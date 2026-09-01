using System.Drawing;
using System.Drawing.Text;
using WindowFlip.Application;
using WindowFlip.Core.Switching;
using WindowFlip.Platform.Windows.Interop;

namespace WindowFlip.Presentation;

internal sealed class SwitchOverlay : Form
{
    private const int MaximumVisibleRows = 7;

    private readonly System.Windows.Forms.Timer hideTimer;
    private readonly IWindowIconProvider iconProvider;
    private readonly List<OverlayWindow> windows = [];
    private string applicationName = string.Empty;
    private string? message;
    private nint selectedHandle;
    private float scale = 1.0f;

    public SwitchOverlay(IWindowIconProvider iconProvider)
    {
        this.iconProvider = iconProvider;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = Color.FromArgb(32, 33, 36);
        DoubleBuffered = true;

        hideTimer = new System.Windows.Forms.Timer { Interval = 1100 };
        hideTimer.Tick += (_, _) =>
        {
            hideTimer.Stop();
            Hide();
        };
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow;
            parameters.ClassStyle |= NativeMethods.CsDropShadow;
            return parameters;
        }
    }

    public void ShowSelection(
        string appName,
        string? executablePath,
        IReadOnlyList<WindowDescriptor> orderedWindows,
        nint selected)
    {
        applicationName = appName ?? string.Empty;
        message = null;
        selectedHandle = selected;
        DisposeWindowResources();

        foreach (WindowDescriptor window in orderedWindows)
        {
            windows.Add(new OverlayWindow(
                window,
                iconProvider.GetIcon(window.Handle, executablePath)));
        }

        ShowOverlay(selected);
    }

    public void ShowMessage(string appName, string text, nint anchor)
    {
        applicationName = appName ?? string.Empty;
        message = text;
        selectedHandle = 0;
        DisposeWindowResources();
        ShowOverlay(anchor);
    }

    private void ShowOverlay(nint anchor)
    {
        if (!IsHandleCreated)
        {
            CreateControl();
        }

        scale = NativeMethods.GetDpiForWindow(Handle) / 96.0f;
        if (scale <= 0)
        {
            using Graphics graphics = Graphics.FromHwnd(0);
            scale = graphics.DpiX / 96.0f;
        }

        int visibleRows = message is null ? Math.Min(windows.Count, MaximumVisibleRows) : 1;
        int footerHeight = message is null && windows.Count > visibleRows ? Px(28) : 0;
        Size = new Size(Px(520), Px(46) + (visibleRows * Px(46)) + footerHeight + Px(2));

        Screen screen = anchor == 0 ? Screen.PrimaryScreen! : Screen.FromHandle(anchor);
        Rectangle area = screen.WorkingArea;
        int x = area.Left + Math.Max(0, (area.Width - Width) / 2);
        int y = area.Bottom - Height - Px(92);
        Location = new Point(x, Math.Max(area.Top, y));

        if (!Visible)
        {
            Show();
        }

        NativeMethods.SetWindowPos(
            Handle,
            NativeMethods.HwndTopmost,
            Left,
            Top,
            Width,
            Height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
        Invalidate();
        hideTimer.Stop();
        hideTimer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics graphics = e.Graphics;
        graphics.Clear(Color.FromArgb(32, 33, 36));
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        using (Pen border = new(Color.FromArgb(78, 80, 84), Math.Max(1.0f, scale)))
        {
            graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        }

        DrawHeader(graphics);
        using (Pen separator = new(Color.FromArgb(60, 64, 67), Math.Max(1.0f, scale)))
        {
            graphics.DrawLine(separator, Px(16), Px(45), Width - Px(16), Px(45));
        }

        if (message is not null)
        {
            DrawMessage(graphics);
            return;
        }

        List<OverlayWindow> visibleWindows = GetVisibleWindows();
        for (int index = 0; index < visibleWindows.Count; index++)
        {
            DrawWindowRow(graphics, visibleWindows[index], index);
        }

        if (windows.Count > visibleWindows.Count)
        {
            DrawFooter(graphics, visibleWindows.Count);
        }
    }

    private void DrawHeader(Graphics graphics)
    {
        using Font headerFont = new("Segoe UI", 10.0f, FontStyle.Bold, GraphicsUnit.Point);
        Rectangle header = new(Px(16), 0, Width - Px(32), Px(46));
        TextRenderer.DrawText(
            graphics,
            applicationName,
            headerFont,
            header,
            Color.FromArgb(232, 234, 237),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        if (message is null)
        {
            string countText = windows.Count + " 个窗口";
            Size countSize = TextRenderer.MeasureText(countText, headerFont);
            Rectangle countArea = new(Width - Px(16) - countSize.Width, 0, countSize.Width, Px(46));
            TextRenderer.DrawText(
                graphics,
                countText,
                headerFont,
                countArea,
                Color.FromArgb(154, 160, 166),
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    private List<OverlayWindow> GetVisibleWindows()
    {
        if (windows.Count <= MaximumVisibleRows)
        {
            return [.. windows];
        }

        int selectedIndex = windows.FindIndex(window => window.Descriptor.Handle == selectedHandle);
        if (selectedIndex < 0)
        {
            selectedIndex = 0;
        }

        int start = Math.Max(0, Math.Min(selectedIndex - 3, windows.Count - MaximumVisibleRows));
        return windows.Skip(start).Take(MaximumVisibleRows).ToList();
    }

    private void DrawWindowRow(Graphics graphics, OverlayWindow window, int visibleIndex)
    {
        int top = Px(46) + visibleIndex * Px(46);
        bool selected = window.Descriptor.Handle == selectedHandle;
        if (selected)
        {
            using Brush highlight = new SolidBrush(Color.FromArgb(14, 99, 156));
            graphics.FillRectangle(highlight, Px(6), top + Px(3), Width - Px(12), Px(40));
        }

        graphics.DrawIcon(window.Icon, new Rectangle(Px(16), top + Px(11), Px(24), Px(24)));

        using Font font = new("Segoe UI", 9.5f, selected ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
        Rectangle textArea = new(Px(52), top, Width - Px(68), Px(46));
        TextRenderer.DrawText(
            graphics,
            window.Descriptor.Title,
            font,
            textArea,
            selected ? Color.White : Color.FromArgb(218, 220, 224),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }

    private void DrawMessage(Graphics graphics)
    {
        using Font font = new("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
        TextRenderer.DrawText(
            graphics,
            message,
            font,
            new Rectangle(Px(16), Px(46), Width - Px(32), Px(46)),
            Color.FromArgb(218, 220, 224),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }

    private void DrawFooter(Graphics graphics, int visibleCount)
    {
        int footerTop = Px(46) + visibleCount * Px(46);
        using Font font = new("Segoe UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
        TextRenderer.DrawText(
            graphics,
            "另有 " + (windows.Count - visibleCount) + " 个窗口",
            font,
            new Rectangle(Px(16), footerTop, Width - Px(32), Px(28)),
            Color.FromArgb(154, 160, 166),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    private int Px(int value)
    {
        return Math.Max(1, (int)Math.Round(value * scale));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            hideTimer.Dispose();
            DisposeWindowResources();
        }

        base.Dispose(disposing);
    }

    private void DisposeWindowResources()
    {
        foreach (OverlayWindow window in windows)
        {
            window.Icon.Dispose();
        }

        windows.Clear();
    }

    private sealed record OverlayWindow(WindowDescriptor Descriptor, Icon Icon);
}
