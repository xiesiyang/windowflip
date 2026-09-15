using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using WindowFlip.Application;
using WindowFlip.Platform.Windows.Interop;

namespace WindowFlip.Platform.Windows;

internal sealed class WindowsFileDialogNavigator(StaTaskRunner runner) : IFileDialogNavigator
{
    public Task<DirectorySyncResult> NavigateAsync(SyncWindow window, string path, CancellationToken token) =>
        runner.RunAsync(() => NavigateCoreAsync(window, path, token), token);

    private static async Task<DirectorySyncResult> NavigateCoreAsync(SyncWindow window, string path, CancellationToken token)
    {
        if (!Path.IsPathFullyQualified(path) || !Directory.Exists(path)) return DirectorySyncResult.Unavailable;
        if (!CanNavigate(window, token)) return DirectorySyncResult.Cancelled;
        if (SyncWindowInspector.Inspect(window.Handle).Kind != SyncWindowKind.FileDialog)
            return DirectorySyncResult.Unavailable;

        // Allow Alt+Tab and mouse activation to finish before editing a control.
        if (!await WaitAsync(() => !ModifiersPressed(), window, token, 600)) return DirectorySyncResult.Cancelled;
        nint filename = SyncWindowInspector.FileNameEdit(window.Handle);
        string? originalName = ReadText(filename);
        if (originalName is null) return DirectorySyncResult.Unavailable;
        bool selectionRead = NativeMethods.GetEditSelection(filename, 0xB0, out uint selectionStart,
            out uint selectionEnd, 2, 150, out _) != 0;
        nint originalFocus = FocusedControl(window.Handle);
        bool modern = SyncWindowInspector.Children(window.Handle)
            .Any(child => SyncWindowInspector.ClassName(child) == "Address Band Root");
        bool filenameChanged = false;
        bool commandCompleted = true;
        bool addressActivated = false;
        try
        {
            if (modern)
            {
                if (!CanNavigate(window, token) || !ActivateAddress(window, token)) return DirectorySyncResult.Cancelled;
                addressActivated = true;
                if (!await WaitAsync(() => AddressEdit(window.Handle) != 0, window, token, 600))
                    return DirectorySyncResult.Unavailable;
                nint address = AddressEdit(window.Handle);
                if (SamePath(ReadText(address), path)) return DirectorySyncResult.Succeeded;
                if (!CanNavigate(window, token) || ModifiersPressed()) return DirectorySyncResult.Cancelled;
                AutomationElement element = AutomationElement.FromHandle(address);
                if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out object pattern))
                    return DirectorySyncResult.Unavailable;
                ((ValuePattern)pattern).SetValue(path);
                if (!CanNavigate(window, token)) return DirectorySyncResult.Cancelled;
                // Targeted messages cannot deliver the path/Enter to another application's input queue.
                NativeMethods.PostMessage(address, NativeMethods.WmKeyDown, 13, 1);
                NativeMethods.PostMessage(address, NativeMethods.WmKeyUp, 13, unchecked((nint)(int)0xC0000001));
                bool arrived = await WaitAsync(() => AddressShowsPath(window.Handle, path), window, token, 1800);
                return arrived ? DirectorySyncResult.Succeeded : DirectorySyncResult.Failed;
            }

            using RemoteDialogFolder folderReader = new(window.ProcessId);
            string? oldFolder = folderReader.Read(window.Handle);
            if (oldFolder is null) return DirectorySyncResult.Unavailable;
            if (SamePath(oldFolder, path)) return DirectorySyncResult.Succeeded;
            if (!CanNavigate(window, token) || ModifiersPressed()) return DirectorySyncResult.Cancelled;
            string directory = Path.TrimEndingDirectorySeparator(path) + Path.DirectorySeparatorChar;
            if (!SetText(filename, directory)) return DirectorySyncResult.Unavailable;
            filenameChanged = true;
            if (!CanNavigate(window, token)) return DirectorySyncResult.Cancelled;
            // Do not restore the filename before the target has consumed the navigation command.
            commandCompleted = NativeMethods.SendMessageTimeout(window.Handle, 0x111, 1,
                NativeMethods.GetDlgItem(window.Handle, 1), 2 | 0x20, 300, out _) != 0;
            if (!commandCompleted) return DirectorySyncResult.Failed;
            return await WaitAsync(() => SamePath(folderReader.Read(window.Handle), path), window, token, 1800)
                ? DirectorySyncResult.Succeeded : DirectorySyncResult.Failed;
        }
        catch (OperationCanceledException) { return DirectorySyncResult.Cancelled; }
        catch (Exception ex) when (ex is COMException or ElementNotAvailableException or InvalidOperationException)
        {
            return DirectorySyncResult.Unavailable;
        }
        finally
        {
            // Restoring text is targeted and is allowed after cancellation; never steal foreground focus.
            if (SyncWindowInspector.IsAlive(window) && SyncWindowInspector.FileNameEdit(window.Handle) == filename)
            {
                string? currentName = ReadText(filename);
                if (filenameChanged && commandCompleted &&
                    (string.IsNullOrEmpty(currentName) || SamePath(currentName, path)))
                {
                    SetText(filename, originalName);
                }
                if (NativeMethods.GetForegroundWindow() == window.Handle)
                {
                    if (addressActivated)
                    {
                        nint address = AddressEdit(window.Handle);
                        if (address != 0) NativeMethods.PostMessage(address, NativeMethods.WmKeyDown, 27, 1);
                    }
                    if (originalFocus != 0 && NativeMethods.IsChild(window.Handle, originalFocus))
                    {
                        NativeMethods.SendMessageTimeout(window.Handle, 0x28, originalFocus, 1, 2, 150, out _);
                    }
                    if (selectionRead && commandCompleted && ReadText(filename) == originalName)
                        NativeMethods.SendMessageTimeout(filename, 0xB1, (nint)selectionStart,
                            (nint)selectionEnd, 2, 150, out _);
                }
            }
        }
    }

    private static bool CanNavigate(SyncWindow window, CancellationToken token) =>
        !token.IsCancellationRequested && SyncWindowInspector.IsAlive(window) &&
        NativeMethods.GetForegroundWindow() == window.Handle;

    private static bool ModifiersPressed() => new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }
        .Any(key => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0);

    private static bool ActivateAddress(SyncWindow window, CancellationToken token)
    {
        if (!CanNavigate(window, token) || ModifiersPressed()) return false;
        NativeMethods.Input[] inputs =
        [
            Key(0x12, false), Key(0x44, false), Key(0x44, true), Key(0x12, true)
        ];
        return NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>()) == inputs.Length;
    }

    private static NativeMethods.Input Key(ushort key, bool up) => new()
    {
        Type = 1,
        Data = new NativeMethods.InputUnion
        {
            Keyboard = new NativeMethods.KeyboardInput { Key = key, Flags = up ? 2u : 0u }
        }
    };

    private static nint FocusedControl(nint dialog)
    {
        uint thread = NativeMethods.GetWindowThreadProcessId(dialog, out _);
        NativeMethods.GuiThreadInfo info = new() { Size = (uint)Marshal.SizeOf<NativeMethods.GuiThreadInfo>() };
        return NativeMethods.GetGUIThreadInfo(thread, ref info) ? info.Focus : 0;
    }

    internal static nint AddressEdit(nint dialog)
    {
        nint filename = SyncWindowInspector.FileNameEdit(dialog);
        foreach (nint child in SyncWindowInspector.Children(dialog))
        {
            if (child == filename || !NativeMethods.IsWindowVisible(child) || SyncWindowInspector.ClassName(child) != "Edit") continue;
            for (nint parent = NativeMethods.GetAncestor(child, 1); parent != 0 && parent != dialog;
                parent = NativeMethods.GetAncestor(parent, 1))
            {
                if (SyncWindowInspector.ClassName(parent) is "Breadcrumb Parent" or "Address Band Root") return child;
            }
        }
        return 0;
    }

    private static bool AddressShowsPath(nint dialog, string path)
    {
        // An edited address alone is not proof of navigation: wait for the breadcrumb toolbar to return.
        if (AddressEdit(dialog) != 0) return false;
        foreach (nint child in SyncWindowInspector.Children(dialog))
        {
            if (SyncWindowInspector.ClassName(child) != "ToolbarWindow32" ||
                SyncWindowInspector.ClassName(NativeMethods.GetAncestor(child, 1)) != "Breadcrumb Parent") continue;
            string name = AutomationElement.FromHandle(child).Current.Name;
            int separator = name.IndexOf(':');
            if (SamePath(name, path) || (separator >= 0 && SamePath(name[(separator + 1)..].Trim(), path))) return true;
        }
        return false;
    }

    internal static string? ReadText(nint control)
    {
        if (control == 0) return null;
        StringBuilder text = new(32768);
        return NativeMethods.ReadTextTimeout(control, 0xD, text.Capacity, text, 2, 150, out _) != 0 ? text.ToString() : null;
    }

    private static bool SetText(nint control, string text) =>
        NativeMethods.SendTextTimeout(control, 0xC, 0, text, 2, 150, out nuint result) != 0 && result != 0;

    private static bool SamePath(string? left, string right) => left is not null &&
        string.Equals(Path.TrimEndingDirectorySeparator(left), Path.TrimEndingDirectorySeparator(right), StringComparison.OrdinalIgnoreCase);

    private static async Task<bool> WaitAsync(Func<bool> condition, SyncWindow window,
        CancellationToken token, int milliseconds)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        while (CanNavigate(window, token) && elapsed.ElapsedMilliseconds < milliseconds)
        {
            if (condition()) return true;
            await Task.Delay(30, token);
        }
        return false;
    }
}
