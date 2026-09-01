using System.Diagnostics;
using WindowFlip.Application;
using WindowFlip.Platform.Windows.Interop;
using AppIdentity = WindowFlip.Application.ApplicationIdentity;

namespace WindowFlip.Platform.Windows;

internal sealed class Win32ApplicationIdentityResolver : IApplicationIdentityResolver
{
    public AppIdentity? Resolve(nint window)
    {
        NativeMethods.GetWindowThreadProcessId(window, out uint processId);
        if (processId == 0)
        {
            return null;
        }

        try
        {
            using Process process = Process.GetProcessById((int)processId);
            string processName = process.ProcessName;
            string? executablePath = TryGetExecutablePath(process);
            string key = string.IsNullOrEmpty(executablePath)
                ? "name:" + processName
                : "path:" + executablePath;

            return new AppIdentity(
                key,
                processName,
                executablePath,
                GetDisplayName(processName, executablePath));
        }
        catch
        {
            return null;
        }
    }

    internal static bool Matches(AppIdentity identity, Process process)
    {
        string? candidatePath = TryGetExecutablePath(process);
        if (!string.IsNullOrEmpty(identity.ExecutablePath) && !string.IsNullOrEmpty(candidatePath))
        {
            return string.Equals(identity.ExecutablePath, candidatePath, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(identity.ProcessName, process.ProcessName, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static string GetDisplayName(string processName, string? executablePath)
    {
        if (!string.IsNullOrEmpty(executablePath))
        {
            try
            {
                FileVersionInfo version = FileVersionInfo.GetVersionInfo(executablePath);
                if (!string.IsNullOrWhiteSpace(version.FileDescription))
                {
                    return version.FileDescription.Trim();
                }
            }
            catch
            {
            }
        }

        return processName;
    }
}
