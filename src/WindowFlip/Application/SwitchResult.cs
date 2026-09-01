using WindowFlip.Core.Switching;

namespace WindowFlip.Application;

internal enum SwitchStatus
{
    NoForegroundWindow,
    OnlyOneWindow,
    Switched,
    ActivationFailed
}

internal sealed record SwitchResult(
    SwitchStatus Status,
    string ApplicationName = "",
    string? ExecutablePath = null,
    nint ForegroundHandle = 0,
    nint TargetHandle = 0,
    IReadOnlyList<WindowDescriptor>? OrderedWindows = null);
