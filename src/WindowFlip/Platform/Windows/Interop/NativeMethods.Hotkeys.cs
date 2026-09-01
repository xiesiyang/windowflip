using System.Runtime.InteropServices;

namespace WindowFlip.Platform.Windows.Interop;

internal static partial class NativeMethods
{
    internal const int WmHotkey = 0x0312;
    internal const int WmKeyDown = 0x0100;
    internal const int WmKeyUp = 0x0101;
    internal const int WmSysKeyDown = 0x0104;
    internal const int WmSysKeyUp = 0x0105;
    internal const int WhKeyboardLl = 13;
    internal const uint ModAlt = 0x0001;
    internal const uint ModShift = 0x0004;
    internal const uint ModWin = 0x0008;
    internal const uint ModNoRepeat = 0x4000;
    internal const uint VkEscape = 0x1B;
    internal const uint VkShift = 0x10;
    internal const uint VkLShift = 0xA0;
    internal const uint VkRShift = 0xA1;
    internal const uint VkMenu = 0x12;
    internal const uint VkLMenu = 0xA4;
    internal const uint VkRMenu = 0xA5;
    internal const uint VkLWin = 0x5B;
    internal const uint VkRWin = 0x5C;
    internal const uint VkOem3 = 0xC0;

    internal delegate nint LowLevelKeyboardProc(int code, nint message, nint data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(nint window, int id);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SetWindowsHookEx(
        int hook,
        LowLevelKeyboardProc callback,
        nint module,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    internal static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(nint window, int message, nint word, nint parameter);

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint GetModuleHandle(string? moduleName);
}
