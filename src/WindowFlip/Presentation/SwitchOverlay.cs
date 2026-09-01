using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using WindowFlip.Application;
using WindowFlip.Core.Switching;
using WindowFlip.Platform.Windows.Interop;

namespace WindowFlip.Presentation;

internal sealed class SwitchOverlay : Form
{
    private const int MaximumVisibleCards = 5;
    private const int CardWidth = 210;
    private const int CardHeight = 178;
    private const int CardGap = 12;
    private const int OuterPadding = 16;
    private const int HeaderHeight = 52;
    private const int PreviewInset = 8;
    private const int PreviewHeight = 118;

    private readonly System.Windows.Forms.Timer hideTimer;
    private readonly IWindowIconProvider iconProvider;
    private readonly List<OverlayWindow> windows = [];
    private string applicationName = string.Empty;
    private string? message;
    private nint selectedHandle;
    private float scale = 1.0f;
    private int visibleCardCount;
    private bool selectionShowing;

    public SwitchOverlay(IWindowIconProvider iconProvider)
    {
        this.iconProvider = iconProvider;
        FormBorderStyle = FormBorderStyle.None;
        Text = "WindowFlip Switcher";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = Color.FromArgb(31, 32, 35);
        DoubleBuffered = true;

        hideTimer = new System.Windows.Forms.Timer { Interval = 1600 };
        hideTimer.Tick += (_, _) =>
        {
            hideTimer.Stop();
            HideOverlay();
        };
    }

    protected override bool ShowWithoutActivation => true;

    internal int RegisteredThumbnailCount => windows.Count(window => window.Thumbnail != 0);

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
        selectionShowing = true;
        hideTimer.Stop();

        if (HasSameWindows(orderedWindows))
        {
            for (int index = 0; index < orderedWindows.Count; index++)
            {
                windows[index].Descriptor = orderedWindows[index];
            }
        }
        else
        {
            DisposeWindowResources();
            foreach (WindowDescriptor window in orderedWindows)
            {
                windows.Add(new OverlayWindow(
                    window,
                    iconProvider.GetIcon(window.Handle, executablePath)));
            }
        }

        ShowOverlay(selected, autoHide: false);
    }

    public void ShowMessage(string appName, string text, nint anchor)
    {
        applicationName = appName ?? string.Empty;
        message = text;
        selectedHandle = 0;
        selectionShowing = false;
        DisposeWindowResources();
        ShowOverlay(anchor, autoHide: true);
    }

    public void HideSelection()
    {
        if (!selectionShowing)
        {
            return;
        }

        HideOverlay();
    }

    private void ShowOverlay(nint anchor, bool autoHide)
    {
        if (!IsHandleCreated)
        {
            CreateControl();
        }

        Screen screen = anchor == 0 ? Screen.PrimaryScreen! : Screen.FromHandle(anchor);
        uint dpi = anchor == 0
            ? NativeMethods.GetDpiForWindow(Handle)
            : NativeMethods.GetDpiForWindow(anchor);
        scale = dpi > 0 ? dpi / 96.0f : 1.0f;

        Rectangle area = screen.WorkingArea;
        if (message is null)
        {
            int availableWidth = Math.Max(Px(CardWidth), area.Width - Px(48));
            int fit = Math.Max(1, (availableWidth - Px(OuterPadding * 2) + Px(CardGap)) /
                Px(CardWidth + CardGap));
            visibleCardCount = Math.Min(windows.Count, Math.Min(MaximumVisibleCards, fit));
            int contentWidth = visibleCardCount * Px(CardWidth) +
                Math.Max(0, visibleCardCount - 1) * Px(CardGap);
            Size = new Size(
                contentWidth + Px(OuterPadding * 2),
                Px(HeaderHeight + CardHeight + OuterPadding));
        }
        else
        {
            visibleCardCount = 0;
            Size = new Size(Math.Min(Px(520), area.Width - Px(32)), Px(98));
        }

        int x = area.Left + Math.Max(0, (area.Width - Width) / 2);
        int y = area.Top + Math.Max(0, (area.Height - Height) / 2);
        Location = new Point(x, y);

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

        ConfigureThumbnails();
        Invalidate();

        hideTimer.Stop();
        if (autoHide)
        {
            hideTimer.Start();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        graphics.Clear(Color.FromArgb(31, 32, 35));

        using (Pen border = new(Color.FromArgb(86, 88, 92), Math.Max(1.0f, scale)))
        {
            graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        }

        DrawHeader(graphics);
        if (message is not null)
        {
            DrawMessage(graphics);
            return;
        }

        List<OverlayWindow> visibleWindows = GetVisibleWindows();
        for (int index = 0; index < visibleWindows.Count; index++)
        {
            DrawWindowCard(graphics, visibleWindows[index], index, visibleWindows.Count);
        }
    }

    private void DrawHeader(Graphics graphics)
    {
        using Font headerFont = new("Segoe UI", 10.0f, FontStyle.Bold, GraphicsUnit.Point);
        int countWidth = 0;
        if (message is null)
        {
            string countText = windows.Count + " 个窗口";
            countWidth = TextRenderer.MeasureText(countText, headerFont).Width;
            Rectangle countArea = new(
                Width - Px(OuterPadding) - countWidth,
                0,
                countWidth,
                Px(HeaderHeight));
            TextRenderer.DrawText(
                graphics,
                countText,
                headerFont,
                countArea,
                Color.FromArgb(174, 176, 180),
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        Rectangle header = new(
            Px(OuterPadding),
            0,
            Width - Px(OuterPadding * 2) - countWidth - (countWidth > 0 ? Px(12) : 0),
            Px(HeaderHeight));
        TextRenderer.DrawText(
            graphics,
            applicationName,
            headerFont,
            header,
            Color.FromArgb(242, 242, 242),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

    }

    private void DrawWindowCard(
        Graphics graphics,
        OverlayWindow window,
        int visibleIndex,
        int count)
    {
        Rectangle card = GetCardBounds(visibleIndex, count);
        bool selected = window.Descriptor.Handle == selectedHandle;
        using Brush cardBackground = new SolidBrush(
            selected ? Color.FromArgb(54, 57, 62) : Color.FromArgb(42, 44, 48));
        graphics.FillRectangle(cardBackground, card);

        Rectangle preview = GetPreviewBounds(card);
        using Brush previewBackground = new SolidBrush(Color.FromArgb(18, 18, 19));
        graphics.FillRectangle(previewBackground, preview);

        if (window.Thumbnail == 0)
        {
            int iconSize = Math.Min(Px(48), Math.Min(preview.Width, preview.Height));
            graphics.DrawIcon(
                window.Icon,
                new Rectangle(
                    preview.Left + (preview.Width - iconSize) / 2,
                    preview.Top + (preview.Height - iconSize) / 2,
                    iconSize,
                    iconSize));
        }

        int titleTop = preview.Bottom + Px(7);
        graphics.DrawIcon(
            window.Icon,
            new Rectangle(card.Left + Px(9), titleTop + Px(2), Px(20), Px(20)));

        using Font titleFont = new(
            "Segoe UI",
            9.0f,
            selected ? FontStyle.Bold : FontStyle.Regular,
            GraphicsUnit.Point);
        Rectangle titleArea = new(
            card.Left + Px(35),
            titleTop,
            card.Width - Px(43),
            card.Bottom - titleTop - Px(5));
        TextRenderer.DrawText(
            graphics,
            window.Descriptor.Title,
            titleFont,
            titleArea,
            Color.FromArgb(242, 242, 242),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

        Color borderColor = selected
            ? Color.FromArgb(96, 205, 255)
            : Color.FromArgb(75, 77, 82);
        float borderWidth = selected ? Math.Max(2.0f, Px(2)) : Math.Max(1.0f, scale);
        using Pen cardBorder = new(borderColor, borderWidth);
        graphics.DrawRectangle(cardBorder, card);
    }

    private void DrawMessage(Graphics graphics)
    {
        using Font font = new("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
        TextRenderer.DrawText(
            graphics,
            message,
            font,
            new Rectangle(Px(OuterPadding), Px(HeaderHeight), Width - Px(OuterPadding * 2), Px(38)),
            Color.FromArgb(224, 224, 224),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }

    private void ConfigureThumbnails()
    {
        if (!IsHandleCreated || message is not null)
        {
            return;
        }

        List<OverlayWindow> visibleWindows = GetVisibleWindows();
        foreach (OverlayWindow window in windows)
        {
            int visibleIndex = visibleWindows.IndexOf(window);
            bool visible = visibleIndex >= 0;

            if (visible && window.Thumbnail == 0 &&
                NativeMethods.DwmRegisterThumbnail(Handle, window.Descriptor.Handle, out nint thumbnail) == 0)
            {
                window.Thumbnail = thumbnail;
            }

            if (window.Thumbnail == 0)
            {
                continue;
            }

            Rectangle destination = visible
                ? GetFittedThumbnailBounds(
                    window.Thumbnail,
                    GetPreviewBounds(GetCardBounds(visibleIndex, visibleWindows.Count)))
                : Rectangle.Empty;
            NativeMethods.DwmThumbnailProperties properties = new()
            {
                Flags = visible
                    ? NativeMethods.DwmTnpRectDestination |
                        NativeMethods.DwmTnpOpacity |
                        NativeMethods.DwmTnpVisible |
                        NativeMethods.DwmTnpSourceClientAreaOnly
                    : NativeMethods.DwmTnpVisible,
                Destination = new NativeMethods.NativeRect(destination),
                Opacity = byte.MaxValue,
                Visible = visible,
                SourceClientAreaOnly = false
            };
            if (NativeMethods.DwmUpdateThumbnailProperties(window.Thumbnail, ref properties) != 0)
            {
                NativeMethods.DwmUnregisterThumbnail(window.Thumbnail);
                window.Thumbnail = 0;
            }
        }
    }

    private Rectangle GetFittedThumbnailBounds(nint thumbnail, Rectangle bounds)
    {
        if (NativeMethods.DwmQueryThumbnailSourceSize(thumbnail, out NativeMethods.NativeSize source) != 0 ||
            source.Width <= 0 || source.Height <= 0)
        {
            return bounds;
        }

        double ratio = Math.Min(
            (double)bounds.Width / source.Width,
            (double)bounds.Height / source.Height);
        int width = Math.Max(1, (int)Math.Round(source.Width * ratio));
        int height = Math.Max(1, (int)Math.Round(source.Height * ratio));
        return new Rectangle(
            bounds.Left + (bounds.Width - width) / 2,
            bounds.Top + (bounds.Height - height) / 2,
            width,
            height);
    }

    private Rectangle GetCardBounds(int visibleIndex, int count)
    {
        int totalWidth = count * Px(CardWidth) + Math.Max(0, count - 1) * Px(CardGap);
        int left = (Width - totalWidth) / 2 + visibleIndex * Px(CardWidth + CardGap);
        return new Rectangle(left, Px(HeaderHeight), Px(CardWidth), Px(CardHeight));
    }

    private Rectangle GetPreviewBounds(Rectangle card)
    {
        return new Rectangle(
            card.Left + Px(PreviewInset),
            card.Top + Px(PreviewInset),
            card.Width - Px(PreviewInset * 2),
            Px(PreviewHeight));
    }

    private List<OverlayWindow> GetVisibleWindows()
    {
        if (windows.Count <= visibleCardCount)
        {
            return [.. windows];
        }

        int selectedIndex = windows.FindIndex(window => window.Descriptor.Handle == selectedHandle);
        if (selectedIndex < 0)
        {
            selectedIndex = 0;
        }

        int start = Math.Max(
            0,
            Math.Min(selectedIndex - (visibleCardCount / 2), windows.Count - visibleCardCount));
        return windows.Skip(start).Take(visibleCardCount).ToList();
    }

    private bool HasSameWindows(IReadOnlyList<WindowDescriptor> orderedWindows)
    {
        return windows.Count == orderedWindows.Count &&
            windows.Select(window => window.Descriptor.Handle)
                .SequenceEqual(orderedWindows.Select(window => window.Handle));
    }

    private int Px(int value)
    {
        return Math.Max(1, (int)Math.Round(value * scale));
    }

    private void HideOverlay()
    {
        hideTimer.Stop();
        selectionShowing = false;
        message = null;
        Hide();
        DisposeWindowResources();
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
            if (window.Thumbnail != 0)
            {
                NativeMethods.DwmUnregisterThumbnail(window.Thumbnail);
                window.Thumbnail = 0;
            }

            window.Icon.Dispose();
        }

        windows.Clear();
    }

    private sealed class OverlayWindow(WindowDescriptor descriptor, Icon icon)
    {
        public WindowDescriptor Descriptor { get; set; } = descriptor;

        public Icon Icon { get; } = icon;

        public nint Thumbnail { get; set; }
    }
}
