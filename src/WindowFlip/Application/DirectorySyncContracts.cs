namespace WindowFlip.Application;

internal enum SyncWindowKind { Other, Explorer, FileDialog, Transient }

internal readonly record struct SyncWindow(nint Handle, uint ProcessId, SyncWindowKind Kind);

internal enum DirectorySyncResult { Skipped, Succeeded, Cancelled, Unavailable, Failed }

internal interface IForegroundWindowEvents : IDisposable
{
    event Action<SyncWindow>? Changed;
    event Action<nint>? Destroyed;
    void Start();
    void Stop();
}

internal interface IExplorerDirectoryReader
{
    Task<string?> ReadAsync(SyncWindow window, CancellationToken cancellationToken);
}

internal interface IFileDialogNavigator
{
    Task<DirectorySyncResult> NavigateAsync(SyncWindow window, string path, CancellationToken cancellationToken);
}

internal interface IDirectorySyncSettings
{
    bool LoadEnabled();
    bool SaveEnabled(bool enabled);
}
