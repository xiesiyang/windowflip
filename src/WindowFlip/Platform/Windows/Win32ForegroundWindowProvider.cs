using WindowFlip.Application;
using WindowFlip.Platform.Windows.Interop;

namespace WindowFlip.Platform.Windows;

internal sealed class Win32ForegroundWindowProvider : IForegroundWindowProvider
{
    public nint GetForegroundWindow()
    {
        return NativeMethods.GetForegroundWindow();
    }
}
