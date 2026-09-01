using WindowFlip.Core.Switching;

namespace WindowFlip.Application;

internal sealed class SwitchRequestedEventArgs(SwitchDirection direction) : EventArgs
{
    public SwitchDirection Direction { get; } = direction;
}

internal sealed record HotkeyRegistration(string NextLabel, string PreviousLabel);

internal sealed class HotkeyRegistrationException(string message) : Exception(message);
