using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Automation;
using WindowFlip.Application;
using WindowFlip.Platform.Windows;
using WindowFlip.Platform.Windows.Interop;

namespace WindowFlip.IntegrationHost;

internal static class DirectorySyncIntegration
{
    internal static int Run(string outputPath)
    {
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
        using Form owner = new() { Text = "WindowFlip directory sync integration", Width = 600, Height = 400 };
        List<string> passed = [];
        Exception? error = null;
        string diagnostic = "";
        List<string> foregroundEvents = [];
        owner.Shown += async (_, _) =>
        {
            string root = Path.Combine(Path.GetTempPath(), "WindowFlip.DirectorySync." + Guid.NewGuid().ToString("N"));
            string destination = Path.Combine(root, "nested", "\u4e2d\u6587 folder");
            nint explorer = 0, dialogHandle = 0;
            object? shell = null;
            using StaTaskRunner worker = new();
            Win32ForegroundWindowEvents events = new();
            events.Changed += window => foregroundEvents.Add($"Event {window} class={SyncWindowInspector.ClassName(window.Handle)}");
            using DirectorySyncCoordinator coordinator = new(events,
                new ShellExplorerDirectoryReader(worker), new WindowsFileDialogNavigator(worker));
            try
            {
                Directory.CreateDirectory(destination);
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
                HashSet<nint> previous = ExplorerHandles();
                Process.Start(new ProcessStartInfo("explorer.exe", $"/n,\"{root}\"") { UseShellExecute = true });
                await WaitAsync(() => (explorer = ExplorerHandles().Except(previous).FirstOrDefault()) != 0, "new Explorer window");
                await ActivateAsync(explorer);
                foregroundEvents.Add($"Created Explorer {SyncWindowInspector.Inspect(explorer)}");
                await Task.Delay(500);
                coordinator.SetEnabled(true);
                uint clipboard = GetClipboardSequenceNumber();

                foreach (bool modern in new[] { true, false })
                foreach (bool save in new[] { false, true })
                {
                    using FileDialog dialog = save ? new SaveFileDialog() : new OpenFileDialog();
                    dialog.AutoUpgradeEnabled = modern;
                    dialog.InitialDirectory = root;
                    dialog.FileName = "report.txt";
                    dialog.Filter = "Text files|*.txt|All files|*.*";
                    dialog.FilterIndex = 2;
                    dialog.RestoreDirectory = true;
                    dialog.Title = $"WindowFlip sync {(modern ? "modern" : "classic")} {(save ? "save" : "open")}";
                    bool closed = false;
                    await ActivateAsync(owner.Handle);
                    owner.BeginInvoke(() => { dialog.ShowDialog(owner); closed = true; });
                    await WaitAsync(() =>
                    {
                        NativeMethods.EnumWindows((window, _) =>
                        {
                            if (SyncWindowInspector.ClassName(window) == "#32770" && NativeMethods.IsWindowVisible(window) &&
                                NativeMethods.GetWindowThreadProcessId(window, out uint pid) != 0 && pid == Environment.ProcessId)
                                dialogHandle = window;
                            return true;
                        }, 0);
                        return dialogHandle != 0 && SyncWindowInspector.Inspect(dialogHandle).Kind == SyncWindowKind.FileDialog;
                    }, "file dialog");
                    await ActivateAsync(dialogHandle);
                    foregroundEvents.Add($"Before departure {SyncWindowInspector.Inspect(NativeMethods.GetForegroundWindow())}");
                    await Task.Delay(200);
                    if (SyncWindowInspector.Inspect(dialogHandle).Kind != SyncWindowKind.FileDialog)
                        throw new InvalidOperationException("Standard dialog was not recognized.");

                    nint filename = SyncWindowInspector.FileNameEdit(dialogHandle);
                    AutomationElement.FromHandle(filename).SetFocus();
                    await Task.Delay(100);
                    NativeMethods.SendMessageTimeout(filename, 0xB1, 2, 5, 2, 150, out _);
                    CheckSelection(filename);
                    string? originalName = WindowsFileDialogNavigator.ReadText(filename);
                    string originalFilter = ReadFilter(dialogHandle);
                    await ActivateAsync(explorer);
                    foregroundEvents.Add($"After departure {SyncWindowInspector.Inspect(NativeMethods.GetForegroundWindow())}");
                    NavigateExplorer(shell!, explorer, destination);
                    ShellExplorerDirectoryReader reader = new(worker);
                    string? read = null;
                    Stopwatch deadline = Stopwatch.StartNew();
                    while (read != destination && deadline.Elapsed < TimeSpan.FromSeconds(6))
                    {
                        read = await reader.ReadAsync(SyncWindowInspector.Inspect(explorer), CancellationToken.None);
                        await Task.Delay(50);
                    }
                    if (read != destination) throw new InvalidOperationException("Explorer path mismatch: " + read);
                    Task<DirectorySyncResult> previousOperation = coordinator.LastOperation;
                    await ActivateAsync(dialogHandle);
                    foregroundEvents.Add($"After return {SyncWindowInspector.Inspect(NativeMethods.GetForegroundWindow())}");
                    await WaitAsync(() => coordinator.LastOperation != previousOperation, "sync trigger");
                    DirectorySyncResult result = await coordinator.LastOperation;
                    if (result != DirectorySyncResult.Succeeded) throw new InvalidOperationException("Navigation result: " + result);
                    if (closed || !NativeMethods.IsWindow(dialogHandle)) throw new InvalidOperationException("Dialog was closed by navigation.");
                    if (WindowsFileDialogNavigator.ReadText(filename) != originalName)
                        throw new InvalidOperationException("Filename was not preserved.");
                    if (ReadFilter(dialogHandle) != originalFilter) throw new InvalidOperationException("File type changed.");
                    if (GetClipboardSequenceNumber() != clipboard) throw new InvalidOperationException("Clipboard was modified.");
                    passed.Add($"{(modern ? "Modern" : "Classic")} {(save ? "Save" : "Open")}: latest directory, filename, file type, open state, clipboard");

                    string probeResult = Path.Combine(root, "probe.txt");
                    // Window activation itself may select all; isolate selection preservation from that behavior.
                    AutomationElement.FromHandle(filename).SetFocus();
                    await Task.Delay(100);
                    NativeMethods.SendMessageTimeout(filename, 0xB1, 2, 5, 2, 150, out _);
                    CheckSelection(filename);
                    ProcessStartInfo probeInfo = new(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
                    foreach (string argument in new[] { "--directory-sync-probe", probeResult, dialogHandle.ToString(), root })
                        probeInfo.ArgumentList.Add(argument);
                    using (Process probe = Process.Start(probeInfo)!)
                    {
                        try { await probe.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(9)); }
                        finally { if (!probe.HasExited) probe.Kill(); }
                        string probeOutput = File.Exists(probeResult) ? File.ReadAllText(probeResult) : "No probe result";
                        File.Delete(probeResult);
                        if (probe.ExitCode != 0) throw new InvalidOperationException("Cross-process navigation: " + probeOutput);
                    }
                    if (closed || WindowsFileDialogNavigator.ReadText(filename) != originalName || ReadFilter(dialogHandle) != originalFilter)
                        throw new InvalidOperationException("Cross-process navigation changed dialog input.");
                    if (GetClipboardSequenceNumber() != clipboard) throw new InvalidOperationException("Cross-process clipboard change.");
                    CheckSelection(filename);
                    passed.Add($"{(modern ? "Modern" : "Classic")} {(save ? "Save" : "Open")}: cross-process navigation and input preservation");

                    if (modern && !save)
                    {
                        coordinator.SetEnabled(false);
                        previousOperation = coordinator.LastOperation;
                        await ActivateAsync(explorer);
                        NavigateExplorer(shell!, explorer, root);
                        await Task.Delay(200);
                        await ActivateAsync(dialogHandle);
                        await Task.Delay(300);
                        if (coordinator.LastOperation != previousOperation) throw new InvalidOperationException("Disabled sync triggered.");
                        passed.Add("Disabled switch: no navigation");
                        coordinator.SetEnabled(true);
                    }

                    NativeMethods.PostMessage(dialogHandle, 0x111, 2, NativeMethods.GetDlgItem(dialogHandle, 2));
                    await WaitAsync(() => closed, "dialog cancellation");
                    dialogHandle = 0;
                    NavigateExplorer(shell!, explorer, root);
                }

                using Form ordinary = new() { Text = "WindowFlip ordinary window" };
                ordinary.Show(owner);
                if (SyncWindowInspector.Inspect(ordinary.Handle).Kind == SyncWindowKind.FileDialog)
                    throw new InvalidOperationException("Ordinary window misidentified.");
                passed.Add("Ordinary window rejected");
            }
            catch (Exception ex)
            {
                error = ex;
                if (dialogHandle != 0)
                {
                    diagnostic = string.Join("\n", SyncWindowInspector.Children(dialogHandle).Select(child =>
                        $"{child} {SyncWindowInspector.ClassName(child)} id={NativeMethods.GetDlgCtrlID(child)} visible={NativeMethods.IsWindowVisible(child)} text={WindowsFileDialogNavigator.ReadText(child)}"));
                }
            }
            finally
            {
                coordinator.SetEnabled(false);
                if (dialogHandle != 0) NativeMethods.PostMessage(dialogHandle, 0x111, 2, NativeMethods.GetDlgItem(dialogHandle, 2));
                if (explorer != 0) NativeMethods.PostMessage(explorer, 0x10, 0, 0);
                if (shell is not null) Marshal.ReleaseComObject(shell);
                await Task.Delay(250);
                try
                {
                    Directory.Delete(destination);
                    Directory.Delete(Path.GetDirectoryName(destination)!);
                    Directory.Delete(root);
                }
                catch (IOException) { }
                File.WriteAllText(outputPath, JsonSerializer.Serialize(new
                {
                    Passed = error is null,
                    Checks = passed,
                    Error = error?.ToString(),
                    Diagnostic = diagnostic,
                    ForegroundEvents = error is null ? null : foregroundEvents,
                    WindowsBuild = Environment.OSVersion.Version.Build
                }, new JsonSerializerOptions { WriteIndented = true }));
                owner.Close();
                System.Windows.Forms.Application.ExitThread();
            }
        };
        System.Windows.Forms.Application.Run(owner);
        return error is null ? 0 : 1;
    }

    private static HashSet<nint> ExplorerHandles()
    {
        HashSet<nint> result = [];
        NativeMethods.EnumWindows((window, _) =>
        {
            if (SyncWindowInspector.Inspect(window).Kind == SyncWindowKind.Explorer) result.Add(window);
            return true;
        }, 0);
        return result;
    }

    private static void NavigateExplorer(object shell, nint window, string path)
    {
        object windows = ((dynamic)shell).Windows();
        try
        {
            for (int i = 0; i < (int)((dynamic)windows).Count; i++)
            {
                object item = ((dynamic)windows).Item(i);
                try
                {
                    if ((nint)(long)((dynamic)item).HWND == window)
                    {
                        ((dynamic)item).Navigate2(path);
                        return;
                    }
                }
                finally { Marshal.ReleaseComObject(item); }
            }
            throw new InvalidOperationException("Test Explorer window missing.");
        }
        finally { Marshal.ReleaseComObject(windows); }
    }

    private static async Task ActivateAsync(nint window)
    {
        Stopwatch deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(6))
        {
            new Win32WindowActivator().Activate(window);
            await Task.Delay(120);
            if (NativeMethods.GetForegroundWindow() == window) return;
        }
        throw new InvalidOperationException("Could not keep test window in the foreground.");
    }

    private static string ReadFilter(nint dialog)
    {
        nint filter = SyncWindowInspector.Children(dialog).FirstOrDefault(child =>
            SyncWindowInspector.ClassName(child) == "ComboBox" &&
            (NativeMethods.GetDlgCtrlID(child) == 1136 ||
                (NativeMethods.GetDlgCtrlID(child) == 0 && !SyncWindowInspector.Children(child)
                    .Any(edit => SyncWindowInspector.ClassName(edit) == "Edit"))));
        if (filter == 0) throw new InvalidOperationException("File type control not found.");
        NativeMethods.SendMessageTimeout(filter, 0x147, 0, 0, 2, 150, out nuint selected);
        return $"{selected}:{WindowsFileDialogNavigator.ReadText(filter)}";
    }

    private static async Task WaitAsync(Func<bool> condition, string description)
    {
        Stopwatch deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(8))
        {
            if (condition()) return;
            await Task.Delay(50);
        }
        throw new TimeoutException(description);
    }

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    private static void CheckSelection(nint filename)
    {
        NativeMethods.GetEditSelection(filename, 0xB0, out uint start, out uint end, 2, 150, out _);
        if (start != 2 || end != 5)
            throw new InvalidOperationException($"Filename selection was not preserved: {start}, {end}");
    }
}
