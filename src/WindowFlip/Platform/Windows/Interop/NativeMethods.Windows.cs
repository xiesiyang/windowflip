using System.Runtime.InteropServices;
using System.Text;

namespace WindowFlip.Platform.Windows.Interop;

internal static partial class NativeMethods
{
    internal const int WmGetIcon = 0x007F;
    internal const int IconSmall2 = 2;
    internal const int GwlExStyle = -20;
    internal const int WsExToolWindow = 0x00000080;
    internal const int WsExAppWindow = 0x00040000;
    internal const uint GwOwner = 4;
    internal const int DwmaCloaked = 14;
    internal const uint SmtoAbortIfHung = 0x0002;
    internal const int GclpHIcon = -14;
    internal const int GclpHIconSm = -34;
    internal const int SwRestore = 9;

    internal delegate bool EnumWindowsProc(nint window, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint window, nint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(nint window, StringBuilder text, int maximumCount);

    [DllImport("user32.dll")]
    internal static extern int GetWindowTextLength(nint window);

    [DllImport("user32.dll")]
    internal static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern nint GetWindowLongPtr64(nint window, int index);

    internal static nint GetWindowLongPtr(nint window, int index)
    {
        return nint.Size == 8 ? GetWindowLongPtr64(window, index) : new nint(GetWindowLong32(window, index));
    }

    [DllImport("user32.dll", EntryPoint = "GetClassLong")]
    private static extern uint GetClassLong32(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtr")]
    private static extern nint GetClassLongPtr64(nint window, int index);

    internal static nint GetClassLongPtr(nint window, int index)
    {
        return nint.Size == 8
            ? GetClassLongPtr64(window, index)
            : new nint(unchecked((int)GetClassLong32(window, index)));
    }

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SendMessageTimeout(
        nint window,
        uint message,
        nint wParam,
        nint lParam,
        uint flags,
        uint timeout,
        out nuint result);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindowAsync(nint window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool BringWindowToTop(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AttachThreadInput(uint attachThread, uint attachToThread, bool attach);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();
}
