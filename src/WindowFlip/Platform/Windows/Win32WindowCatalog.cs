using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using WindowFlip.Application;
using WindowFlip.Core.Switching;
using WindowFlip.Platform.Windows.Interop;
using AppIdentity = WindowFlip.Application.ApplicationIdentity;

namespace WindowFlip.Platform.Windows;

internal sealed class Win32WindowCatalog(bool includeOwnProcess = false) : IWindowCatalog
{
    public IReadOnlyList<WindowDescriptor> GetWindows(AppIdentity identity)
    {
        List<WindowDescriptor> result = [];
        int ownProcessId = Environment.ProcessId;

        NativeMethods.EnumWindows((window, _) =>
        {
            if (!IsSwitchableWindow(window))
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(window, out uint processId);
            if (processId == 0 || (!includeOwnProcess && processId == ownProcessId))
            {
                return true;
            }

            try
            {
                using Process process = Process.GetProcessById((int)processId);
                if (!Win32ApplicationIdentityResolver.Matches(identity, process))
                {
                    return true;
                }
            }
            catch
            {
                return true;
            }

            string title = GetWindowTitle(window);
            if (!string.IsNullOrWhiteSpace(title))
            {
                result.Add(new WindowDescriptor(window, title.Trim()));
            }

            return true;
        }, 0);

        return result;
    }

    private static bool IsSwitchableWindow(nint window)
    {
        if (!NativeMethods.IsWindowVisible(window))
        {
            return false;
        }

        long extendedStyle = NativeMethods.GetWindowLongPtr(window, NativeMethods.GwlExStyle).ToInt64();
        bool isToolWindow = (extendedStyle & NativeMethods.WsExToolWindow) != 0;
        bool isAppWindow = (extendedStyle & NativeMethods.WsExAppWindow) != 0;
        if (isToolWindow && !isAppWindow)
        {
            return false;
        }

        if (NativeMethods.GetWindow(window, NativeMethods.GwOwner) != 0 && !isAppWindow)
        {
            return false;
        }

        int result = NativeMethods.DwmGetWindowAttribute(
            window,
            NativeMethods.DwmaCloaked,
            out int cloaked,
            Marshal.SizeOf<int>());
        return result != 0 || cloaked == 0;
    }

    private static string GetWindowTitle(nint window)
    {
        int length = NativeMethods.GetWindowTextLength(window);
        if (length <= 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new(length + 1);
        NativeMethods.GetWindowText(window, builder, builder.Capacity);
        return builder.ToString();
    }
}
