using WindowFlip.Application;
using WindowFlip.Platform.Windows;

namespace WindowFlip.IntegrationHost;

internal static class DirectorySyncProbe
{
    public static int Run(string outputPath, nint dialog, string path)
    {
        DirectorySyncResult result = DirectorySyncResult.Failed;
        using Control dispatcher = new();
        _ = dispatcher.Handle;
        using StaTaskRunner worker = new();
        dispatcher.BeginInvoke(async () =>
        {
            try
            {
                using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(6));
                result = await new WindowsFileDialogNavigator(worker)
                    .NavigateAsync(SyncWindowInspector.Inspect(dialog), path, timeout.Token).WaitAsync(timeout.Token);
                File.WriteAllText(outputPath, result.ToString());
            }
            catch (Exception ex) { File.WriteAllText(outputPath, ex.ToString()); }
            finally { System.Windows.Forms.Application.ExitThread(); }
        });
        System.Windows.Forms.Application.Run();
        return result == DirectorySyncResult.Succeeded ? 0 : 1;
    }
}
