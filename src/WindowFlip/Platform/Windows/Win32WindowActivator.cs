using WindowFlip.Application;
using WindowFlip.Platform.Windows.Interop;

namespace WindowFlip.Platform.Windows;

internal sealed class Win32WindowActivator : IWindowActivator
{
    public bool Activate(nint target)
    {
        if (target == 0 || !NativeMethods.IsWindow(target))
        {
            return false;
        }

        if (NativeMethods.IsIconic(target))
        {
            NativeMethods.ShowWindowAsync(target, NativeMethods.SwRestore);
        }

        nint foreground = NativeMethods.GetForegroundWindow();
        uint foregroundThread = foreground == 0
            ? 0
            : NativeMethods.GetWindowThreadProcessId(foreground, 0);
        uint currentThread = NativeMethods.GetCurrentThreadId();
        bool attached = false;

        try
        {
            if (foregroundThread != 0 && foregroundThread != currentThread)
            {
                attached = NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
            }

            NativeMethods.BringWindowToTop(target);
            return NativeMethods.SetForegroundWindow(target);
        }
        finally
        {
            if (attached)
            {
                NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
    }
}
