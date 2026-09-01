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
    private PendingSwitch? pendingSwitch;

    public SwitchResult Select(SwitchDirection direction)
    {
        nint foreground;
        nint current;
        ApplicationIdentity identity;

        if (pendingSwitch is null)
        {
            foreground = foregroundWindowProvider.GetForegroundWindow();
            if (foreground == 0)
            {
                return new SwitchResult(SwitchStatus.NoForegroundWindow);
            }

            ApplicationIdentity? resolvedIdentity = identityResolver.Resolve(foreground);
            if (resolvedIdentity is null)
            {
                return new SwitchResult(SwitchStatus.NoForegroundWindow);
            }

            identity = resolvedIdentity;
            current = foreground;
        }
        else
        {
            foreground = pendingSwitch.ForegroundHandle;
            current = pendingSwitch.TargetHandle;
            identity = pendingSwitch.Identity;
        }

        IReadOnlyList<WindowDescriptor> windows = windowCatalog.GetWindows(identity);
        SwitchSelectionResult selection = session.Select(
            identity.Key,
            current,
            windows,
            direction,
            timeProvider.GetUtcNow());

        if (selection.Status is SwitchSelectionStatus.NoWindows or SwitchSelectionStatus.OnlyOneWindow)
        {
            pendingSwitch = null;
            return new SwitchResult(
                SwitchStatus.OnlyOneWindow,
                identity.DisplayName,
                identity.ExecutablePath,
                foreground,
                OrderedWindows: selection.OrderedWindows);
        }

        pendingSwitch = new PendingSwitch(
            identity,
            foreground,
            selection.SelectedHandle,
            selection.OrderedWindows);

        return new SwitchResult(
            SwitchStatus.SelectionChanged,
            identity.DisplayName,
            identity.ExecutablePath,
            foreground,
            selection.SelectedHandle,
            selection.OrderedWindows);
    }

    public SwitchResult Commit()
    {
        PendingSwitch? selection = pendingSwitch;
        pendingSwitch = null;
        if (selection is null)
        {
            return new SwitchResult(SwitchStatus.NoPendingSelection);
        }

        bool activated = windowActivator.Activate(selection.TargetHandle);
        return new SwitchResult(
            activated ? SwitchStatus.Switched : SwitchStatus.ActivationFailed,
            selection.Identity.DisplayName,
            selection.Identity.ExecutablePath,
            selection.ForegroundHandle,
            selection.TargetHandle,
            selection.OrderedWindows);
    }

    public bool SelectTarget(nint targetHandle)
    {
        PendingSwitch? selection = pendingSwitch;
        if (selection is null ||
            !selection.OrderedWindows.Any(window => window.Handle == targetHandle))
        {
            return false;
        }

        pendingSwitch = selection with { TargetHandle = targetHandle };
        return true;
    }

    public void Cancel()
    {
        pendingSwitch = null;
    }

    private sealed record PendingSwitch(
        ApplicationIdentity Identity,
        nint ForegroundHandle,
        nint TargetHandle,
        IReadOnlyList<WindowDescriptor> OrderedWindows);
}
