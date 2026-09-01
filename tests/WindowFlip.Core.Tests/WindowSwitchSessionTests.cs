using WindowFlip.Core.Switching;
using Xunit;

namespace WindowFlip.Core.Tests;

public sealed class WindowSwitchSessionTests
{
    private static readonly DateTimeOffset StartTime = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Select_MovesForwardAndWraps()
    {
        WindowSwitchSession session = new();
        WindowDescriptor[] windows = CreateWindows(1, 2, 3);

        SwitchSelectionResult first = session.Select("app", 1, windows, SwitchDirection.Next, StartTime);
        SwitchSelectionResult wrapped = session.Select(
            "app",
            3,
            windows,
            SwitchDirection.Next,
            StartTime.AddMilliseconds(100));

        Assert.Equal((nint)2, first.SelectedHandle);
        Assert.Equal((nint)1, wrapped.SelectedHandle);
    }

    [Fact]
    public void Select_MovesBackwardAndWraps()
    {
        WindowSwitchSession session = new();
        WindowDescriptor[] windows = CreateWindows(1, 2, 3);

        SwitchSelectionResult result = session.Select(
            "app",
            1,
            windows,
            SwitchDirection.Previous,
            StartTime);

        Assert.Equal((nint)3, result.SelectedHandle);
    }

    [Fact]
    public void Select_PreservesInitialOrderWithinSession()
    {
        WindowSwitchSession session = new();
        session.Select("app", 1, CreateWindows(1, 2, 3), SwitchDirection.Next, StartTime);

        SwitchSelectionResult result = session.Select(
            "app",
            2,
            CreateWindows(2, 1, 3),
            SwitchDirection.Next,
            StartTime.AddSeconds(1));

        Assert.Equal((nint)3, result.SelectedHandle);
        Assert.Equal(new nint[] { 1, 2, 3 }, result.OrderedWindows.Select(window => window.Handle));
    }

    [Fact]
    public void Select_RebuildsOrderAfterSessionExpires()
    {
        WindowSwitchSession session = new();
        session.Select("app", 1, CreateWindows(1, 2, 3), SwitchDirection.Next, StartTime);

        SwitchSelectionResult result = session.Select(
            "app",
            3,
            CreateWindows(3, 2, 1),
            SwitchDirection.Next,
            StartTime.AddSeconds(3));

        Assert.Equal((nint)2, result.SelectedHandle);
        Assert.Equal(new nint[] { 3, 2, 1 }, result.OrderedWindows.Select(window => window.Handle));
    }

    [Fact]
    public void Select_RemovesClosedWindowsAndAppendsNewWindows()
    {
        WindowSwitchSession session = new();
        session.Select("app", 1, CreateWindows(1, 2, 3), SwitchDirection.Next, StartTime);

        SwitchSelectionResult result = session.Select(
            "app",
            2,
            CreateWindows(2, 3, 4),
            SwitchDirection.Next,
            StartTime.AddSeconds(1));

        Assert.Equal((nint)3, result.SelectedHandle);
        Assert.Equal(new nint[] { 2, 3, 4 }, result.OrderedWindows.Select(window => window.Handle));
    }

    [Theory]
    [InlineData(SwitchDirection.Next, 1)]
    [InlineData(SwitchDirection.Previous, 3)]
    public void Select_UsesDirectionalBoundaryWhenForegroundIsMissing(
        SwitchDirection direction,
        int expected)
    {
        WindowSwitchSession session = new();

        SwitchSelectionResult result = session.Select(
            "app",
            99,
            CreateWindows(1, 2, 3),
            direction,
            StartTime);

        Assert.Equal((nint)expected, result.SelectedHandle);
    }

    [Fact]
    public void Select_ReturnsNoWindowsForEmptyCatalog()
    {
        WindowSwitchSession session = new();

        SwitchSelectionResult result = session.Select(
            "app",
            1,
            [],
            SwitchDirection.Next,
            StartTime);

        Assert.Equal(SwitchSelectionStatus.NoWindows, result.Status);
        Assert.Empty(result.OrderedWindows);
    }

    [Fact]
    public void Select_ReturnsOnlyOneWindowWithoutSelectingIt()
    {
        WindowSwitchSession session = new();

        SwitchSelectionResult result = session.Select(
            "app",
            1,
            CreateWindows(1),
            SwitchDirection.Next,
            StartTime);

        Assert.Equal(SwitchSelectionStatus.OnlyOneWindow, result.Status);
        Assert.Equal((nint)0, result.SelectedHandle);
        Assert.Single(result.OrderedWindows);
    }

    [Fact]
    public void Select_StartsNewSessionForDifferentApplication()
    {
        WindowSwitchSession session = new();
        session.Select("first", 1, CreateWindows(1, 2, 3), SwitchDirection.Next, StartTime);

        SwitchSelectionResult result = session.Select(
            "second",
            3,
            CreateWindows(3, 1, 2),
            SwitchDirection.Next,
            StartTime.AddMilliseconds(100));

        Assert.Equal((nint)1, result.SelectedHandle);
    }

    private static WindowDescriptor[] CreateWindows(params int[] handles)
    {
        return handles.Select(handle => new WindowDescriptor(handle, "Window " + handle)).ToArray();
    }
}
