using System.Drawing;
using System.Runtime.InteropServices;

namespace WindowFlip.Platform.Windows.Interop;

internal static partial class NativeMethods
{
    internal const int WsExNoActivate = 0x08000000;
    internal const int CsDropShadow = 0x00020000;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpShowWindow = 0x0040;
    internal static readonly nint HwndTopmost = new(-1);

    internal const uint DwmTnpRectDestination = 0x00000001;
    internal const uint DwmTnpOpacity = 0x00000004;
    internal const uint DwmTnpVisible = 0x00000008;
    internal const uint DwmTnpSourceClientAreaOnly = 0x00000010;

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;

        internal NativeRect(Rectangle rectangle)
        {
            Left = rectangle.Left;
            Top = rectangle.Top;
            Right = rectangle.Right;
            Bottom = rectangle.Bottom;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeSize
    {
        internal int Width;
        internal int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DwmThumbnailProperties
    {
        internal uint Flags;
        internal NativeRect Destination;
        internal NativeRect Source;
        internal byte Opacity;

        [MarshalAs(UnmanagedType.Bool)]
        internal bool Visible;

        [MarshalAs(UnmanagedType.Bool)]
        internal bool SourceClientAreaOnly;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint window);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmRegisterThumbnail(
        nint destinationWindow,
        nint sourceWindow,
        out nint thumbnail);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmUnregisterThumbnail(nint thumbnail);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmUpdateThumbnailProperties(
        nint thumbnail,
        ref DwmThumbnailProperties properties);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmQueryThumbnailSourceSize(
        nint thumbnail,
        out NativeSize size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(nint icon);
}
