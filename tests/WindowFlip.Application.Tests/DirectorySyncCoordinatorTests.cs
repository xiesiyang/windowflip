using WindowFlip.Application;
using Xunit;

namespace WindowFlip.Application.Tests;

public sealed class DirectorySyncCoordinatorTests
{
    private static readonly SyncWindow Dialog = new(1, 10, SyncWindowKind.FileDialog);
    private static readonly SyncWindow Explorer = new(2, 20, SyncWindowKind.Explorer);

    [Fact]
    public async Task RoundTripReadsLatestDirectoryOnlyOnReturn()
    {
        using Fixture f = new();
        f.Events.Emit(Dialog);
        f.Events.Emit(Explorer);
        Assert.Equal(0, f.Reads);
        f.Path = @"C:\new folder\nested";
        f.Events.Emit(Dialog);
        Assert.Equal(DirectorySyncResult.Succeeded, await f.Coordinator.LastOperation);
        Assert.Equal(f.Path, Assert.Single(f.Navigations).Path);
        f.Events.Emit(Dialog);
        Assert.Equal(1, f.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RequiresSamePreviouslyVisitedDialog(bool differentProcess)
    {
        using Fixture f = new();
        f.Events.Emit(Explorer);
        f.Events.Emit(Dialog);
        Assert.Equal(0, f.Reads);
        f.Events.Emit(Explorer);
        f.Events.Emit(differentProcess ? Dialog with { ProcessId = 99 } : Dialog with { Handle = 5 });
        Assert.Equal(0, f.Reads);
    }

    [Fact]
    public void OtherApplicationBreaksRoundTrip()
    {
        using Fixture f = new();
        f.Events.Emit(Dialog);
        f.Events.Emit(Explorer);
        f.Events.Emit(new(9, 90, SyncWindowKind.Other));
        f.Events.Emit(Explorer);
        f.Events.Emit(Dialog);
        Assert.Equal(0, f.Reads);
    }

    [Fact]
    public async Task TaskSwitcherDoesNotBreakRoundTrip()
    {
        using Fixture f = new();
        f.Events.Emit(Dialog);
        f.Events.Emit(new(3, 30, SyncWindowKind.Transient));
        f.Events.Emit(Explorer);
        f.Events.Emit(new(3, 30, SyncWindowKind.Transient));
        f.Events.Emit(Dialog);
        Assert.Equal(DirectorySyncResult.Succeeded, await f.Coordinator.LastOperation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDiscardsLateDirectoryResult(bool disable)
    {
        using Fixture f = new();
        TaskCompletionSource<string?> read = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.ReadOverride = () => read.Task;
        f.RoundTrip();
        Task<DirectorySyncResult> operation = f.Coordinator.LastOperation;
        if (disable) f.Coordinator.SetEnabled(false);
        else f.Events.Emit(new(4, 40, SyncWindowKind.Other));
        read.SetResult(@"C:\stale");
        Assert.Equal(DirectorySyncResult.Cancelled, await operation);
        Assert.Empty(f.Navigations);
    }

    [Theory]
    [InlineData(null, DirectorySyncResult.Unavailable)]
    [InlineData("", DirectorySyncResult.Unavailable)]
    [InlineData("throw", DirectorySyncResult.Failed)]
    public async Task UnavailableOrFailedReadDoesNotNavigate(string? path, object expected)
    {
        using Fixture f = new() { Path = path };
        if (path == "throw") f.ReadOverride = () => throw new InvalidOperationException();
        f.RoundTrip();
        Assert.Equal((DirectorySyncResult)expected, await f.Coordinator.LastOperation);
        Assert.Empty(f.Navigations);
    }

    [Fact]
    public void ReenableDoesNotReusePreviousSession()
    {
        using Fixture f = new();
        f.Events.Emit(Dialog);
        f.Events.Emit(Explorer);
        f.Coordinator.SetEnabled(false);
        f.Coordinator.SetEnabled(true);
        f.Events.Emit(Dialog);
        Assert.Equal(0, f.Reads);
    }

    private sealed class Events : IForegroundWindowEvents
    {
        public event Action<SyncWindow>? Changed;
        public event Action<nint>? Destroyed;
        public void Destroy(nint window) => Destroyed?.Invoke(window);
        private bool enabled;
        public void Emit(SyncWindow window) { if (enabled) Changed?.Invoke(window); }
        public void Start() => enabled = true;
        public void Stop() => enabled = false;
        public void Dispose() => Stop();
    }

    [Fact]
    public void ClosingExplorerDiscardsRememberedSource()
    {
        using Fixture f = new();
        f.Events.Emit(Dialog);
        f.Events.Emit(Explorer);
        f.Events.Destroy(Explorer.Handle);
        f.Events.Emit(Dialog);
        Assert.Equal(0, f.Reads);
    }

    [Fact]
    public async Task FailedNavigationDoesNotPreventNextRoundTrip()
    {
        using Fixture f = new() { NavigationResult = DirectorySyncResult.Failed };
        f.RoundTrip();
        Assert.Equal(DirectorySyncResult.Failed, await f.Coordinator.LastOperation);
        f.NavigationResult = DirectorySyncResult.Succeeded;
        f.RoundTrip();
        Assert.Equal(DirectorySyncResult.Succeeded, await f.Coordinator.LastOperation);
        Assert.Equal(2, f.Navigations.Count);
    }

    [Fact]
    public async Task ClosingOriginalDialogCancelsReadAndPreventsHandleReuse()
    {
        using Fixture f = new();
        TaskCompletionSource<string?> read = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.ReadOverride = () => read.Task;
        f.RoundTrip();
        Task<DirectorySyncResult> operation = f.Coordinator.LastOperation;
        f.Events.Destroy(Dialog.Handle);
        read.SetResult(@"C:\stale");
        Assert.Equal(DirectorySyncResult.Cancelled, await operation);
        f.Events.Emit(Dialog);
        Assert.Equal(1, f.Reads);
        Assert.Empty(f.Navigations);
    }

    private sealed class Fixture : IExplorerDirectoryReader, IFileDialogNavigator, IDisposable
    {
        public Events Events { get; } = new();
        public DirectorySyncCoordinator Coordinator { get; }
        public string? Path { get; set; } = @"C:\initial";
        public int Reads { get; private set; }
        public Func<Task<string?>>? ReadOverride { get; set; }
        public DirectorySyncResult NavigationResult { get; set; } = DirectorySyncResult.Succeeded;
        public List<(SyncWindow Window, string Path)> Navigations { get; } = [];
        public Fixture()
        {
            Coordinator = new(Events, this, this);
            Coordinator.SetEnabled(true);
        }
        public void RoundTrip() { Events.Emit(Dialog); Events.Emit(Explorer); Events.Emit(Dialog); }
        public Task<string?> ReadAsync(SyncWindow window, CancellationToken cancellationToken)
        {
            Assert.Equal(Explorer, window);
            Reads++;
            return ReadOverride?.Invoke() ?? Task.FromResult(Path);
        }
        public Task<DirectorySyncResult> NavigateAsync(SyncWindow window, string path, CancellationToken cancellationToken)
        {
            Navigations.Add((window, path));
            return Task.FromResult(NavigationResult);
        }
        public void Dispose() => Coordinator.Dispose();
    }
}
