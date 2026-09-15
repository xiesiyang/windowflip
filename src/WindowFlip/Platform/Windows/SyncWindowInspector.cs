using System.Text;
using WindowFlip.Application;
using WindowFlip.Platform.Windows.Interop;

namespace WindowFlip.Platform.Windows;

internal static class SyncWindowInspector
{
    public static string ClassName(nint window)
    {
        StringBuilder name = new(256);
        NativeMethods.GetClassName(window, name, name.Capacity);
        return name.ToString();
    }

    public static List<nint> Children(nint window)
    {
        List<nint> result = [];
        NativeMethods.EnumChildWindows(window, (child, _) => { result.Add(child); return true; }, 0);
        return result;
    }

    public static SyncWindow Inspect(nint window)
    {
        NativeMethods.GetWindowThreadProcessId(window, out uint processId);
        string name = ClassName(window);
        SyncWindowKind kind = name switch
        {
            "CabinetWClass" or "ExploreWClass" => SyncWindowKind.Explorer,
            "#32768" or "MultitaskingViewFrame" or "TaskSwitcherWnd" => SyncWindowKind.Transient,
            _ => SyncWindowKind.Other
        };
        if (window == 0) kind = SyncWindowKind.Transient;
        if (name == "#32770" && FileNameEdit(window) != 0 &&
            NativeMethods.GetDlgItem(window, 1) != 0 && NativeMethods.GetDlgItem(window, 2) != 0 &&
            Children(window).Any(child => ClassName(child) is "SHELLDLL_DefView" or "DirectUIHWND"))
        {
            kind = SyncWindowKind.FileDialog;
        }
        return new SyncWindow(window, processId, kind);
    }

    public static nint FileNameEdit(nint dialog)
    {
        List<nint> children = Children(dialog);
        nint combo = children.FirstOrDefault(child => NativeMethods.GetDlgCtrlID(child) == 1148 &&
            ClassName(child) is "ComboBoxEx32" or "ComboBox");
        if (combo != 0) return Children(combo).FirstOrDefault(child => ClassName(child) == "Edit");
        nint modernSave = children.FirstOrDefault(child => NativeMethods.GetDlgCtrlID(child) == 1001 &&
            ClassName(child) == "Edit" && ClassName(NativeMethods.GetAncestor(child, 1)) == "ComboBox" &&
            ClassName(NativeMethods.GetAncestor(NativeMethods.GetAncestor(child, 1), 1)) == "FloatNotifySink");
        if (modernSave != 0) return modernSave;
        return children.FirstOrDefault(child => NativeMethods.GetDlgCtrlID(child) == 1152 &&
            ClassName(child) == "Edit");
    }

    public static bool IsAlive(SyncWindow window) =>
        NativeMethods.IsWindow(window.Handle) &&
        NativeMethods.GetWindowThreadProcessId(window.Handle, out uint processId) != 0 &&
        processId == window.ProcessId;
}
