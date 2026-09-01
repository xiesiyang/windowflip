using WindowFlip.Application;
using WindowFlip.Core.Switching;
using Xunit;
using AppIdentity = WindowFlip.Application.ApplicationIdentity;

namespace WindowFlip.Application.Tests;

public sealed class WindowSwitchCoordinatorTests
{
    private static readonly AppIdentity Identity = new(
        "path:c:\\app.exe",
        "app",
        "c:\\app.exe",
        "Test App");

    [Fact]
    public void Switch_ReturnsNoForegroundWindowWhenDesktopHasNoForeground()
    {
        Fixture fixture = new() { ForegroundHandle = 0 };

        SwitchResult result = fixture.CreateCoordinator().Select(SwitchDirection.Next);

        Assert.Equal(SwitchStatus.NoForegroundWindow, result.Status);
        Assert.Equal(0, fixture.CatalogCalls);
        Assert.Empty(fixture.ActivationTargets);
    }

    [Fact]
    public void Switch_ReturnsNoForegroundWindowWhenIdentityCannotBeResolved()
    {
        Fixture fixture = new() { Identity = null };

        SwitchResult result = fixture.CreateCoordinator().Select(SwitchDirection.Next);

        Assert.Equal(SwitchStatus.NoForegroundWindow, result.Status);
        Assert.Equal(0, fixture.CatalogCalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Switch_DoesNotActivateWhenThereAreNotEnoughWindows(int windowCount)
    {
        Fixture fixture = new()
        {
            Windows = Enumerable.Range(1, windowCount)
                .Select(handle => new WindowDescriptor(handle, "Window " + handle))
                .ToArray()
        };

        SwitchResult result = fixture.CreateCoordinator().Select(SwitchDirection.Next);

        Assert.Equal(SwitchStatus.OnlyOneWindow, result.Status);
        Assert.Empty(fixture.ActivationTargets);
    }

    [Fact]
    public void Select_ReturnsSelectionWithoutActivatingWindow()
    {
        Fixture fixture = new();

        SwitchResult result = fixture.CreateCoordinator().Select(SwitchDirection.Next);

        Assert.Equal(SwitchStatus.SelectionChanged, result.Status);
        Assert.Equal((nint)2, result.TargetHandle);
        Assert.Equal(new nint[] { 1, 2, 3 }, result.OrderedWindows!.Select(window => window.Handle));
        Assert.Empty(fixture.ActivationTargets);
    }

    [Fact]
    public void Select_AdvancesFromPendingTargetAndCommitActivatesFinalSelection()
    {
        Fixture fixture = new();
        WindowSwitchCoordinator coordinator = fixture.CreateCoordinator();

        SwitchResult first = coordinator.Select(SwitchDirection.Next);
        SwitchResult second = coordinator.Select(SwitchDirection.Next);

        Assert.Equal((nint)2, first.TargetHandle);
        Assert.Equal((nint)3, second.TargetHandle);
        Assert.Equal(fixture.ForegroundHandle, first.ForegroundHandle);
        Assert.Equal(fixture.ForegroundHandle, second.ForegroundHandle);
        Assert.Empty(fixture.ActivationTargets);

        SwitchResult committed = coordinator.Commit();

        Assert.Equal(SwitchStatus.Switched, committed.Status);
        Assert.Equal((nint)3, committed.TargetHandle);
        Assert.Equal(new nint[] { 3 }, fixture.ActivationTargets);
    }

    [Fact]
    public void Commit_ReportsActivationFailureWithoutDiscardingResultContext()
    {
        Fixture fixture = new() { ActivationSucceeds = false };
        WindowSwitchCoordinator coordinator = fixture.CreateCoordinator();
        coordinator.Select(SwitchDirection.Previous);

        SwitchResult result = coordinator.Commit();

        Assert.Equal(SwitchStatus.ActivationFailed, result.Status);
        Assert.Equal((nint)3, result.TargetHandle);
        Assert.Equal(new nint[] { 3 }, fixture.ActivationTargets);
    }

    [Fact]
    public void SelectTarget_UpdatesPendingSelectionForMouseCommit()
    {
        Fixture fixture = new();
        WindowSwitchCoordinator coordinator = fixture.CreateCoordinator();
        coordinator.Select(SwitchDirection.Next);

        bool selected = coordinator.SelectTarget(3);
        SwitchResult committed = coordinator.Commit();

        Assert.True(selected);
        Assert.Equal(SwitchStatus.Switched, committed.Status);
        Assert.Equal((nint)3, committed.TargetHandle);
        Assert.Equal(new nint[] { 3 }, fixture.ActivationTargets);
    }

    [Fact]
    public void SelectTarget_RejectsWindowOutsidePendingSelection()
    {
        Fixture fixture = new();
        WindowSwitchCoordinator coordinator = fixture.CreateCoordinator();
        coordinator.Select(SwitchDirection.Next);

        bool selected = coordinator.SelectTarget(99);
        SwitchResult committed = coordinator.Commit();

        Assert.False(selected);
        Assert.Equal((nint)2, committed.TargetHandle);
        Assert.Equal(new nint[] { 2 }, fixture.ActivationTargets);
    }

    [Fact]
    public void Cancel_DiscardsPendingSelectionWithoutActivation()
    {
        Fixture fixture = new();
        WindowSwitchCoordinator coordinator = fixture.CreateCoordinator();
        coordinator.Select(SwitchDirection.Next);

        coordinator.Cancel();
        SwitchResult result = coordinator.Commit();

        Assert.Equal(SwitchStatus.NoPendingSelection, result.Status);
        Assert.Empty(fixture.ActivationTargets);
    }

    [Fact]
    public void Commit_ReturnsNoPendingSelectionBeforeSelect()
    {
        Fixture fixture = new();

        SwitchResult result = fixture.CreateCoordinator().Commit();

        Assert.Equal(SwitchStatus.NoPendingSelection, result.Status);
        Assert.Empty(fixture.ActivationTargets);
    }

    private sealed class Fixture :
        IForegroundWindowProvider,
        IApplicationIdentityResolver,
        IWindowCatalog,
        IWindowActivator
    {
        public nint ForegroundHandle { get; init; } = 1;
        public AppIdentity? Identity { get; init; } = WindowSwitchCoordinatorTests.Identity;
        public IReadOnlyList<WindowDescriptor> Windows { get; init; } =
        [
            new(1, "Window 1"),
            new(2, "Window 2"),
            new(3, "Window 3")
        ];
        public bool ActivationSucceeds { get; init; } = true;
        public int CatalogCalls { get; private set; }
        public List<nint> ActivationTargets { get; } = [];

        public WindowSwitchCoordinator CreateCoordinator()
        {
            return new WindowSwitchCoordinator(
                this,
                this,
                this,
                this,
                new WindowSwitchSession(),
                new FixedTimeProvider());
        }

        nint IForegroundWindowProvider.GetForegroundWindow()
        {
            return ForegroundHandle;
        }

        AppIdentity? IApplicationIdentityResolver.Resolve(nint window)
        {
            return Identity;
        }

        IReadOnlyList<WindowDescriptor> IWindowCatalog.GetWindows(AppIdentity identity)
        {
            CatalogCalls++;
            return Windows;
        }

        bool IWindowActivator.Activate(nint target)
        {
            ActivationTargets.Add(target);
            return ActivationSucceeds;
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        }
    }
}
