using WindowFlip.Application;
using WindowFlip.Core.Switching;
using WindowFlip.Platform.Windows.Interop;

namespace WindowFlip.Platform.Windows;

internal sealed class HotkeyWindow : NativeWindow, ISwitchInputSource
{
    private const int HotkeyNext = 0x2101;
    private const int HotkeyPrevious = 0x2102;
    private bool registered;

    public HotkeyWindow()
    {
        CreateHandle(new CreateParams { Caption = "WindowFlip.HotkeyWindow" });
    }

    public event EventHandler<SwitchRequestedEventArgs>? SwitchRequested;

    public bool TryRegister(out HotkeyRegistration? registration)
    {
        registration = null;
        if (TryRegisterPair(NativeMethods.ModAlt))
        {
            registration = new HotkeyRegistration("Alt + `", "Alt + Shift + `");
            return true;
        }

        if (TryRegisterPair(NativeMethods.ModWin))
        {
            registration = new HotkeyRegistration("Win + `", "Win + Shift + `");
            return true;
        }

        return false;
    }

    private bool TryRegisterPair(uint modifier)
    {
        uint common = modifier | NativeMethods.ModNoRepeat;
        if (!NativeMethods.RegisterHotKey(Handle, HotkeyNext, common, NativeMethods.VkOem3))
        {
            return false;
        }

        bool previous = NativeMethods.RegisterHotKey(
            Handle,
            HotkeyPrevious,
            common | NativeMethods.ModShift,
            NativeMethods.VkOem3);
        if (!previous)
        {
            NativeMethods.UnregisterHotKey(Handle, HotkeyNext);
            return false;
        }

        registered = true;
        return true;
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == NativeMethods.WmHotkey)
        {
            SwitchDirection direction = message.WParam.ToInt32() == HotkeyPrevious
                ? SwitchDirection.Previous
                : SwitchDirection.Next;
            SwitchRequested?.Invoke(this, new SwitchRequestedEventArgs(direction));
        }

        base.WndProc(ref message);
    }

    public void Dispose()
    {
        if (registered && Handle != 0)
        {
            NativeMethods.UnregisterHotKey(Handle, HotkeyNext);
            NativeMethods.UnregisterHotKey(Handle, HotkeyPrevious);
            registered = false;
        }

        if (Handle != 0)
        {
            DestroyHandle();
        }
    }
}
