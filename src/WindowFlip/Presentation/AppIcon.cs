using System.Drawing;
using System.Drawing.Drawing2D;
using WindowFlip.Platform.Windows.Interop;

namespace WindowFlip.Presentation;

internal static class AppIcon
{
    public static Icon Create()
    {
        using Bitmap bitmap = new(32, 32);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        using (Brush background = new SolidBrush(Color.FromArgb(14, 99, 156)))
        using (Pen windowPen = new(Color.White, 2.0f))
        using (Pen arrowPen = new(Color.FromArgb(146, 255, 211), 2.5f))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            graphics.FillRectangle(background, 1, 1, 30, 30);
            graphics.DrawRectangle(windowPen, 6, 7, 14, 12);
            graphics.DrawRectangle(windowPen, 12, 13, 14, 12);
            arrowPen.StartCap = LineCap.Round;
            arrowPen.EndCap = LineCap.ArrowAnchor;
            graphics.DrawLine(arrowPen, 7, 25, 22, 25);
        }

        nint handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }
}
