using WindowFlip.Core.Switching;

namespace WindowFlip.Application;

internal sealed class SwitchRequestedEventArgs(SwitchDirection direction) : EventArgs
{
    public SwitchDirection Direction { get; } = direction;
}

internal sealed class SwitchCommitRequestedEventArgs : EventArgs
{
    public static readonly SwitchCommitRequestedEventArgs Instance = new();

    private SwitchCommitRequestedEventArgs()
    {
    }
}

internal sealed class SwitchCancelRequestedEventArgs : EventArgs
{
    public static readonly SwitchCancelRequestedEventArgs Instance = new();

    private SwitchCancelRequestedEventArgs()
    {
    }
}

internal sealed record HotkeyRegistration(string NextLabel, string PreviousLabel);

internal sealed class HotkeyRegistrationException(string message) : Exception(message);
