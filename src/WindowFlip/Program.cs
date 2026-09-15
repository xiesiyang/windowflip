using System.Drawing;
using System.Threading;
using WindowFlip.Application;
using WindowFlip.Core.Switching;
using WindowFlip.Platform.Windows;
using WindowFlip.Presentation;

namespace WindowFlip;

internal static class Program
{
    private const string MutexName = "Local\\WindowFlip.6D9B13A9-20E7-4FC5-82BF-B63E8ED1EC86";

    [STAThread]
    private static int Main(string[] args)
    {
        System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

        if (args.Length > 0 && string.Equals(args[0], "--self-test", StringComparison.OrdinalIgnoreCase))
        {
            return SelfTest.Run();
        }

        bool integrationTest = args.Any(argument =>
            string.Equals(argument, "--integration-test", StringComparison.OrdinalIgnoreCase));
        string mutexName = integrationTest ? MutexName + ".Integration" : MutexName;
        using Mutex mutex = new(true, mutexName, out bool createdNew);
        if (!createdNew)
        {
            return 0;
        }

        try
        {
            using TrayApplicationContext context = CreateApplicationContext(integrationTest);
            System.Windows.Forms.Application.Run(context);
            return 0;
        }
        catch (HotkeyRegistrationException ex)
        {
            MessageBox.Show(
                ex.Message,
                "WindowFlip",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 2;
        }
    }

    private static TrayApplicationContext CreateApplicationContext(bool integrationTest)
    {
        IWindowIconProvider iconProvider = new Win32WindowIconProvider();
        SwitchOverlay overlay = new(iconProvider);
        Icon appIcon = AppIcon.Create();
        WindowSwitchCoordinator coordinator = new(
            new Win32ForegroundWindowProvider(),
            new Win32ApplicationIdentityResolver(),
            new Win32WindowCatalog(),
            new Win32WindowActivator(),
            new WindowSwitchSession(),
            TimeProvider.System);

        StaTaskRunner syncWorker = new();
        DirectorySyncCoordinator directorySync = new(
            new Win32ForegroundWindowEvents(),
            new ShellExplorerDirectoryReader(syncWorker),
            new WindowsFileDialogNavigator(syncWorker));
        IDirectorySyncSettings settings = integrationTest
            ? new IntegrationSyncSettings()
            : new JsonDirectorySyncSettings();

        return new TrayApplicationContext(
            new HotkeyWindow(),
            new WindowsStartupRegistration(),
            coordinator,
            overlay,
            appIcon,
            directorySync,
            settings,
            syncWorker);
    }

    private sealed class IntegrationSyncSettings : IDirectorySyncSettings
    {
        public bool LoadEnabled() => false;
        public bool SaveEnabled(bool enabled) => true;
    }
}
