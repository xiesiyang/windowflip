namespace WindowFlip.Application;

internal sealed class DirectorySyncCoordinator : IDisposable
{
    private readonly IForegroundWindowEvents events;
    private readonly IExplorerDirectoryReader reader;
    private readonly IFileDialogNavigator navigator;
    private SyncWindow? dialog;
    private SyncWindow? explorer;
    private SyncWindow? previous;
    private CancellationTokenSource? pending;
    private bool disposed;

    public DirectorySyncCoordinator(IForegroundWindowEvents events,
        IExplorerDirectoryReader reader, IFileDialogNavigator navigator)
    {
        this.events = events;
        this.reader = reader;
        this.navigator = navigator;
        events.Changed += OnForegroundChanged;
        events.Destroyed += OnWindowDestroyed;
    }

    public bool Enabled { get; private set; }
    public Task<DirectorySyncResult> LastOperation { get; private set; } =
        Task.FromResult(DirectorySyncResult.Skipped);

    public void SetEnabled(bool enabled)
    {
        if (disposed || Enabled == enabled) return;
        Enabled = enabled;
        Reset();
        if (enabled) events.Start();
        else events.Stop();
    }

    private void OnForegroundChanged(SyncWindow current)
    {
        if (!Enabled || disposed || current.Kind == SyncWindowKind.Transient || current == previous) return;
        CancelPending();
        previous = current;

        if (current.Kind == SyncWindowKind.Explorer && dialog is not null)
        {
            explorer = current;
            return;
        }

        if (current.Kind == SyncWindowKind.FileDialog)
        {
            if (current == dialog && explorer is { } source)
            {
                pending = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                LastOperation = SynchronizeAsync(source, current, pending.Token);
            }
            dialog = current;
            explorer = null;
            return;
        }

        dialog = null;
        explorer = null;
    }

    private async Task<DirectorySyncResult> SynchronizeAsync(
        SyncWindow source, SyncWindow target, CancellationToken token)
    {
        try
        {
            string? path = await reader.ReadAsync(source, token).WaitAsync(token);
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(path)) return DirectorySyncResult.Unavailable;
            return await navigator.NavigateAsync(target, path, token).WaitAsync(token);
        }
        catch (OperationCanceledException) { return DirectorySyncResult.Cancelled; }
        catch (Exception) { return DirectorySyncResult.Failed; }
    }

    private void CancelPending()
    {
        pending?.Cancel();
        pending?.Dispose();
        pending = null;
    }

    private void OnWindowDestroyed(nint handle)
    {
        if (dialog?.Handle == handle || explorer?.Handle == handle) Reset();
    }

    private void Reset()
    {
        CancelPending();
        previous = dialog = explorer = null;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Enabled = false;
        Reset();
        events.Changed -= OnForegroundChanged;
        events.Destroyed -= OnWindowDestroyed;
        events.Dispose();
    }
}
