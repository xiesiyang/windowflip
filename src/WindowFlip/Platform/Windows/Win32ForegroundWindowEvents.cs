using WindowFlip.Application;
using WindowFlip.Platform.Windows.Interop;

namespace WindowFlip.Platform.Windows;

internal sealed class Win32ForegroundWindowEvents : IForegroundWindowEvents
{
    private readonly NativeMethods.WinEventProc callback;
    private readonly Control dispatcher = new();
    private nint hook;
    private nint focusHook;
    private nint destroyHook;
    private nint lastQueuedWindow;
    private int generation;
    private int sequence;

    public Win32ForegroundWindowEvents()
    {
        _ = dispatcher.Handle;
        callback = OnEvent;
    }

    public event Action<SyncWindow>? Changed;
    public event Action<nint>? Destroyed;

    public void Start()
    {
        if (hook != 0) return;
        hook = NativeMethods.SetWinEventHook(3, 3, 0, callback, 0, 0, 0);
        focusHook = NativeMethods.SetWinEventHook(0x8005, 0x8005, 0, callback, 0, 0, 0);
        destroyHook = NativeMethods.SetWinEventHook(0x8001, 0x8001, 0, callback, 0, 0, 0);
        if (hook != 0) Queue(NativeMethods.GetForegroundWindow());
    }

    private void OnEvent(nint hookHandle, uint eventType, nint window,
        int objectId, int childId, uint threadId, uint time)
    {
        if (eventType == 0x8001)
        {
            if (objectId != 0 || childId != 0) return;
            int captured = generation;
            try
            {
                dispatcher.BeginInvoke(() =>
                {
                    if (hook != 0 && captured == generation) Destroyed?.Invoke(window);
                });
            }
            catch (InvalidOperationException) { }
            return;
        }
        if (eventType == 0x8005)
        {
            window = NativeMethods.GetAncestor(window, 2);
            if (window == 0 || window != NativeMethods.GetForegroundWindow() || window == lastQueuedWindow) return;
        }
        else
        {
            nint root = NativeMethods.GetAncestor(window, 2);
            if (root != 0) window = root;
        }
        Queue(window);
    }

    private void Queue(nint window)
    {
        lastQueuedWindow = window;
        int captured = generation;
        try
        {
            dispatcher.BeginInvoke(async () =>
            {
                if (hook == 0 || captured != generation) return;
                int currentSequence = ++sequence;
                SyncWindow inspected = SyncWindowInspector.Inspect(window);
                Changed?.Invoke(inspected);
                // Foreground notification can precede creation of the common-dialog controls.
                for (int attempt = 0; attempt < 12 && inspected.Kind == SyncWindowKind.Other &&
                    SyncWindowInspector.ClassName(window) == "#32770"; attempt++)
                {
                    await Task.Delay(40);
                    if (hook == 0 || captured != generation || currentSequence != sequence ||
                        NativeMethods.GetForegroundWindow() != window) return;
                    inspected = SyncWindowInspector.Inspect(window);
                    if (inspected.Kind == SyncWindowKind.FileDialog) Changed?.Invoke(inspected);
                }
            });
        }
        catch (InvalidOperationException) { }
    }

    public void Stop()
    {
        generation++;
        if (hook != 0) NativeMethods.UnhookWinEvent(hook);
        if (focusHook != 0) NativeMethods.UnhookWinEvent(focusHook);
        if (destroyHook != 0) NativeMethods.UnhookWinEvent(destroyHook);
        hook = 0;
        focusHook = 0;
        destroyHook = 0;
        lastQueuedWindow = 0;
    }

    public void Dispose()
    {
        Stop();
        dispatcher.Dispose();
    }
}
