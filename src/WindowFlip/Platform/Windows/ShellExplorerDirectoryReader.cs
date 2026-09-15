using System.Runtime.InteropServices;
using WindowFlip.Application;
using WindowFlip.Platform.Windows.Interop;

namespace WindowFlip.Platform.Windows;

internal sealed class ShellExplorerDirectoryReader(StaTaskRunner runner) : IExplorerDirectoryReader
{
    public Task<string?> ReadAsync(SyncWindow window, CancellationToken cancellationToken) =>
        runner.RunAsync(() => Task.FromResult(Read(window, cancellationToken)), cancellationToken);

    private static string? Read(SyncWindow window, CancellationToken token)
    {
        if (!SyncWindowInspector.IsAlive(window)) return null;
        object? shell = null, windows = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
            windows = ((dynamic)shell!).Windows();
            List<nint> tabs = SyncWindowInspector.Children(window.Handle)
                .Where(child => SyncWindowInspector.ClassName(child) == "ShellTabWindowClass").ToList();
            List<nint> visibleTabs = tabs.Where(NativeMethods.IsWindowVisible).ToList();
            if (tabs.Count > 0 && visibleTabs.Count != 1) return null;
            List<string> matches = [];
            int count = ((dynamic)windows).Count;
            for (int index = 0; index < count; index++)
            {
                token.ThrowIfCancellationRequested();
                object? item = null, document = null, folder = null, self = null;
                try
                {
                    item = ((dynamic)windows).Item(index);
                    if (item is null || (nint)(long)((dynamic)item).HWND != window.Handle) continue;
                    if (tabs.Count > 0 && !MatchesTab(item, visibleTabs[0])) continue;
                    document = ((dynamic)item).Document;
                    folder = ((dynamic)document).Folder;
                    self = ((dynamic)folder).Self;
                    if (!(bool)((dynamic)self).IsFileSystem) continue;
                    string path = ((dynamic)self).Path;
                    if (Path.IsPathFullyQualified(path)) matches.Add(path);
                }
                catch (COMException) { }
                finally { Release(self); Release(folder); Release(document); Release(item); }
            }
            token.ThrowIfCancellationRequested();
            return SyncWindowInspector.IsAlive(window) && matches.Count == 1 ? matches[0] : null;
        }
        finally { Release(windows); Release(shell); }
    }

    private static bool MatchesTab(object item, nint tab)
    {
        object? browser = null;
        try
        {
            Guid service = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
            Guid iid = new("000214E2-0000-0000-C000-000000000046");
            if (((IShellServiceProvider)item).QueryService(ref service, ref iid, out browser) != 0) return false;
            return ((IOleWindow)browser).GetWindow(out nint handle) == 0 &&
                (handle == tab || NativeMethods.IsChild(tab, handle));
        }
        catch (COMException) { return false; }
        finally { Release(browser); }
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellServiceProvider
    {
        [PreserveSig] int QueryService(ref Guid service, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object result);
    }

    [ComImport, Guid("00000114-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IOleWindow
    {
        [PreserveSig] int GetWindow(out nint window);
        [PreserveSig] int ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool enterMode);
    }
}
