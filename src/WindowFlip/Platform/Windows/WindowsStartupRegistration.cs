using Microsoft.Win32;
using WindowFlip.Application;

namespace WindowFlip.Platform.Windows;

internal sealed class WindowsStartupRegistration : IStartupRegistration
{
    private const string RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    private const string ValueName = "WindowFlip";

    public bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        string? value = key?.GetValue(ValueName) as string;
        return string.Equals(value, GetCommand(), StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(bool enabled)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
        if (key is null)
        {
            throw new InvalidOperationException("无法打开当前用户的启动项注册表路径。");
        }

        if (enabled)
        {
            key.SetValue(ValueName, GetCommand(), RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, false);
        }
    }

    private static string GetCommand()
    {
        string executablePath = Environment.ProcessPath ?? System.Windows.Forms.Application.ExecutablePath;
        return $"\"{executablePath}\" --startup";
    }
}
