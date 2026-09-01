using WindowFlip.Core.Switching;

namespace WindowFlip.Application;

internal sealed class WindowSwitchCoordinator(
    IForegroundWindowProvider foregroundWindowProvider,
    IApplicationIdentityResolver identityResolver,
    IWindowCatalog windowCatalog,
    IWindowActivator windowActivator,
    WindowSwitchSession session,
    TimeProvider timeProvider)
{
    public SwitchResult Switch(SwitchDirection direction)
    {
        nint foreground = foregroundWindowProvider.GetForegroundWindow();
        if (foreground == 0)
        {
            return new SwitchResult(SwitchStatus.NoForegroundWindow);
        }

        ApplicationIdentity? identity = identityResolver.Resolve(foreground);
        if (identity is null)
        {
            return new SwitchResult(SwitchStatus.NoForegroundWindow);
        }

        IReadOnlyList<WindowDescriptor> windows = windowCatalog.GetWindows(identity);
        SwitchSelectionResult selection = session.Select(
            identity.Key,
            foreground,
            windows,
            direction,
            timeProvider.GetUtcNow());

        if (selection.Status is SwitchSelectionStatus.NoWindows or SwitchSelectionStatus.OnlyOneWindow)
        {
            return new SwitchResult(
                SwitchStatus.OnlyOneWindow,
                identity.DisplayName,
                identity.ExecutablePath,
                foreground,
                OrderedWindows: selection.OrderedWindows);
        }

        bool activated = windowActivator.Activate(selection.SelectedHandle);
        return new SwitchResult(
            activated ? SwitchStatus.Switched : SwitchStatus.ActivationFailed,
            identity.DisplayName,
            identity.ExecutablePath,
            foreground,
            selection.SelectedHandle,
            selection.OrderedWindows);
    }
}
