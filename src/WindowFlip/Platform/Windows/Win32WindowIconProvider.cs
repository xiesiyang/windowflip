using System.Drawing;
using WindowFlip.Application;
using WindowFlip.Platform.Windows.Interop;

namespace WindowFlip.Platform.Windows;

internal sealed class Win32WindowIconProvider : IWindowIconProvider
{
    public Icon GetIcon(nint window, string? executablePath)
    {
        nint iconHandle = GetWindowIcon(window);
        if (iconHandle != 0)
        {
            try
            {
                return (Icon)Icon.FromHandle(iconHandle).Clone();
            }
            catch
            {
            }
        }

        if (!string.IsNullOrEmpty(executablePath))
        {
            try
            {
                Icon? icon = Icon.ExtractAssociatedIcon(executablePath);
                if (icon is not null)
                {
                    return icon;
                }
            }
            catch
            {
            }
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    private static nint GetWindowIcon(nint window)
    {
        NativeMethods.SendMessageTimeout(
            window,
            NativeMethods.WmGetIcon,
            NativeMethods.IconSmall2,
            0,
            NativeMethods.SmtoAbortIfHung,
            100,
            out nuint iconResult);

        nint result = unchecked((nint)iconResult);
        if (result == 0)
        {
            result = NativeMethods.GetClassLongPtr(window, NativeMethods.GclpHIconSm);
        }

        if (result == 0)
        {
            result = NativeMethods.GetClassLongPtr(window, NativeMethods.GclpHIcon);
        }

        return result;
    }
}
