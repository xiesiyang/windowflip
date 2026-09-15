namespace WindowFlip.Platform.Windows;

internal sealed class StaTaskRunner : IDisposable
{
    private readonly Control dispatcher;
    private readonly Thread thread;
    private volatile bool disposed;
    private readonly SemaphoreSlim gate = new(1, 1);

    public StaTaskRunner()
    {
        TaskCompletionSource<Control> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        thread = new Thread(() =>
        {
            using Control control = new();
            _ = control.Handle;
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            ready.SetResult(control);
            System.Windows.Forms.Application.Run();
        }) { IsBackground = true, Name = "WindowFlip directory sync" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        dispatcher = ready.Task.GetAwaiter().GetResult();
    }

    public Task<T> RunAsync<T>(Func<Task<T>> action, CancellationToken token)
    {
        if (disposed) return Task.FromCanceled<T>(new CancellationToken(true));
        TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            dispatcher.BeginInvoke(async () =>
            {
                bool acquired = false;
                try
                {
                    token.ThrowIfCancellationRequested();
                    await gate.WaitAsync(token);
                    acquired = true;
                    token.ThrowIfCancellationRequested();
                    completion.TrySetResult(await action());
                }
                catch (OperationCanceledException) { completion.TrySetCanceled(); }
                catch (Exception ex) { completion.TrySetException(ex); }
                finally { if (acquired) gate.Release(); }
            });
        }
        catch (InvalidOperationException) { completion.TrySetCanceled(); }
        return completion.Task;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            dispatcher.BeginInvoke(async () =>
            {
                await gate.WaitAsync();
                System.Windows.Forms.Application.ExitThread();
            });
        }
        catch (InvalidOperationException) { }
    }
}
