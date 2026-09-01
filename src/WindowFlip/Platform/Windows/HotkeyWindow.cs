using System.Runtime.InteropServices;
using WindowFlip.Application;
using WindowFlip.Core.Switching;
using WindowFlip.Platform.Windows.Interop;

namespace WindowFlip.Platform.Windows;

internal sealed class HotkeyWindow : NativeWindow, ISwitchInputSource
{
    private const int HotkeyNext = 0x2101;
    private const int HotkeyPrevious = 0x2102;
    private const int SelectMessage = 0x8000 + 0x2101;
    private const int CommitMessage = 0x8000 + 0x2102;
    private const int CancelMessage = 0x8000 + 0x2103;

    private readonly NativeMethods.LowLevelKeyboardProc keyboardCallback;
    private readonly HashSet<uint> pressedModifiers = [];
    private readonly HashSet<uint> pressedShifts = [];
    private nint keyboardHook;
    private uint registeredModifier;
    private bool registered;
    private bool selectionActive;

    public HotkeyWindow()
    {
        keyboardCallback = OnKeyboardEvent;
        CreateHandle(new CreateParams { Caption = "WindowFlip.HotkeyWindow" });
    }

    public event EventHandler<SwitchRequestedEventArgs>? SwitchRequested;

    public event EventHandler<SwitchCommitRequestedEventArgs>? SwitchCommitRequested;

    public event EventHandler<SwitchCancelRequestedEventArgs>? SwitchCancelRequested;

    public bool TryRegister(out HotkeyRegistration? registration)
    {
        registration = null;
        if (TryRegisterPair(NativeMethods.ModAlt))
        {
            if (!TryInstallKeyboardHook())
            {
                UnregisterPair();
                return false;
            }

            registration = new HotkeyRegistration("Alt + `", "Alt + Shift + `");
            return true;
        }

        if (TryRegisterPair(NativeMethods.ModWin))
        {
            if (!TryInstallKeyboardHook())
            {
                UnregisterPair();
                return false;
            }

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
        registeredModifier = modifier;
        return true;
    }

    private bool TryInstallKeyboardHook()
    {
        keyboardHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhKeyboardLl,
            keyboardCallback,
            NativeMethods.GetModuleHandle(null),
            0);
        return keyboardHook != 0;
    }

    private nint OnKeyboardEvent(int code, nint message, nint data)
    {
        if (code >= 0)
        {
            uint virtualKey = unchecked((uint)Marshal.ReadInt32(data));
            int keyboardMessage = message.ToInt32();
            bool keyDown = keyboardMessage is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown;
            bool keyUp = keyboardMessage is NativeMethods.WmKeyUp or NativeMethods.WmSysKeyUp;

            if (IsModifierKey(virtualKey))
            {
                if (keyDown)
                {
                    pressedModifiers.Add(virtualKey);
                }
                else if (keyUp)
                {
                    pressedModifiers.Remove(virtualKey);
                    if (pressedModifiers.Count == 0 && selectionActive)
                    {
                        selectionActive = false;
                        NativeMethods.PostMessage(Handle, CommitMessage, 0, 0);
                    }
                }
            }
            else if (IsShiftKey(virtualKey))
            {
                if (keyDown)
                {
                    pressedShifts.Add(virtualKey);
                }
                else if (keyUp)
                {
                    pressedShifts.Remove(virtualKey);
                }
            }
            else if (virtualKey == NativeMethods.VkOem3 && keyDown && IsModifierDown())
            {
                selectionActive = true;
                SwitchDirection direction = IsShiftDown()
                    ? SwitchDirection.Previous
                    : SwitchDirection.Next;
                NativeMethods.PostMessage(Handle, SelectMessage, (nint)(int)direction, 0);
            }
            else if (virtualKey == NativeMethods.VkEscape && keyDown && selectionActive)
            {
                selectionActive = false;
                NativeMethods.PostMessage(Handle, CancelMessage, 0, 0);
                return 1;
            }
        }

        return NativeMethods.CallNextHookEx(keyboardHook, code, message, data);
    }

    private bool IsModifierDown()
    {
        if (pressedModifiers.Count > 0)
        {
            return true;
        }

        if (registeredModifier == NativeMethods.ModAlt)
        {
            return NativeMethods.GetAsyncKeyState((int)NativeMethods.VkMenu) < 0;
        }

        return NativeMethods.GetAsyncKeyState((int)NativeMethods.VkLWin) < 0 ||
            NativeMethods.GetAsyncKeyState((int)NativeMethods.VkRWin) < 0;
    }

    private bool IsShiftDown()
    {
        return pressedShifts.Count > 0 || NativeMethods.GetAsyncKeyState((int)NativeMethods.VkShift) < 0;
    }

    private bool IsModifierKey(uint virtualKey)
    {
        return registeredModifier == NativeMethods.ModAlt
            ? virtualKey is NativeMethods.VkMenu or NativeMethods.VkLMenu or NativeMethods.VkRMenu
            : virtualKey is NativeMethods.VkLWin or NativeMethods.VkRWin;
    }

    private static bool IsShiftKey(uint virtualKey)
    {
        return virtualKey is NativeMethods.VkShift or NativeMethods.VkLShift or NativeMethods.VkRShift;
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == SelectMessage)
        {
            SwitchDirection direction = (SwitchDirection)message.WParam.ToInt32();
            SwitchRequested?.Invoke(this, new SwitchRequestedEventArgs(direction));
        }
        else if (message.Msg == CommitMessage)
        {
            SwitchCommitRequested?.Invoke(this, SwitchCommitRequestedEventArgs.Instance);
        }
        else if (message.Msg == CancelMessage)
        {
            SwitchCancelRequested?.Invoke(this, SwitchCancelRequestedEventArgs.Instance);
        }

        base.WndProc(ref message);
    }

    public void Dispose()
    {
        if (keyboardHook != 0)
        {
            NativeMethods.UnhookWindowsHookEx(keyboardHook);
            keyboardHook = 0;
        }

        UnregisterPair();

        if (Handle != 0)
        {
            DestroyHandle();
        }
    }

    private void UnregisterPair()
    {
        if (!registered || Handle == 0)
        {
            return;
        }

        NativeMethods.UnregisterHotKey(Handle, HotkeyNext);
        NativeMethods.UnregisterHotKey(Handle, HotkeyPrevious);
        registered = false;
        registeredModifier = 0;
        pressedModifiers.Clear();
        pressedShifts.Clear();
    }
}
