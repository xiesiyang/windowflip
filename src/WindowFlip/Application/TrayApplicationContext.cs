using System.Drawing;
using WindowFlip.Core.Switching;
using WindowFlip.Presentation;

namespace WindowFlip.Application;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly ISwitchInputSource inputSource;
    private readonly IStartupRegistration startupRegistration;
    private readonly WindowSwitchCoordinator switchCoordinator;
    private readonly SwitchOverlay overlay;
    private readonly Icon appIcon;
    private readonly ContextMenuStrip menu;
    private readonly NotifyIcon notifyIcon;
    private readonly ToolStripMenuItem startupMenuItem;
    private bool disposed;

    public TrayApplicationContext(
        ISwitchInputSource inputSource,
        IStartupRegistration startupRegistration,
        WindowSwitchCoordinator switchCoordinator,
        SwitchOverlay overlay,
        Icon appIcon)
    {
        this.inputSource = inputSource;
        this.startupRegistration = startupRegistration;
        this.switchCoordinator = switchCoordinator;
        this.overlay = overlay;
        this.appIcon = appIcon;

        inputSource.SwitchRequested += OnSwitchRequested;
        if (!inputSource.TryRegister(out HotkeyRegistration? registration) || registration is null)
        {
            DisposeResources();
            throw new HotkeyRegistrationException(
                "无法注册窗口切换快捷键。请关闭占用 Alt + ` 或 Win + ` 的程序后重试。");
        }

        menu = BuildMenu(registration);
        startupMenuItem = (ToolStripMenuItem)menu.Items[3];

        notifyIcon = new NotifyIcon
        {
            Icon = appIcon,
            Text = "WindowFlip - 同应用窗口切换",
            ContextMenuStrip = menu,
            Visible = true
        };
        notifyIcon.DoubleClick += (_, _) => Switch(SwitchDirection.Next);
    }

    private ContextMenuStrip BuildMenu(HotkeyRegistration registration)
    {
        ContextMenuStrip contextMenu = new();

        ToolStripMenuItem nextItem = new("下一个窗口    " + registration.NextLabel);
        nextItem.Click += (_, _) => Switch(SwitchDirection.Next);
        contextMenu.Items.Add(nextItem);

        ToolStripMenuItem previousItem = new("上一个窗口    " + registration.PreviousLabel);
        previousItem.Click += (_, _) => Switch(SwitchDirection.Previous);
        contextMenu.Items.Add(previousItem);
        contextMenu.Items.Add(new ToolStripSeparator());

        ToolStripMenuItem startupItem = new("开机启动")
        {
            Checked = startupRegistration.IsEnabled()
        };
        startupItem.Click += ToggleStartup;
        contextMenu.Items.Add(startupItem);
        contextMenu.Items.Add(new ToolStripSeparator());

        ToolStripMenuItem exitItem = new("退出");
        exitItem.Click += (_, _) => ExitThread();
        contextMenu.Items.Add(exitItem);
        return contextMenu;
    }

    private void OnSwitchRequested(object? sender, SwitchRequestedEventArgs e)
    {
        Switch(e.Direction);
    }

    private void Switch(SwitchDirection direction)
    {
        SwitchResult result = switchCoordinator.Switch(direction);
        if (result.Status == SwitchStatus.Switched)
        {
            overlay.ShowSelection(
                result.ApplicationName,
                result.ExecutablePath,
                result.OrderedWindows ?? [],
                result.TargetHandle);
            return;
        }

        if (result.Status == SwitchStatus.OnlyOneWindow)
        {
            overlay.ShowMessage(result.ApplicationName, "当前应用没有其他可切换窗口", result.ForegroundHandle);
        }
    }

    private void ToggleStartup(object? sender, EventArgs e)
    {
        bool enable = !startupRegistration.IsEnabled();
        try
        {
            startupRegistration.SetEnabled(enable);
            startupMenuItem.Checked = enable;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "更新开机启动设置失败：\r\n" + ex.Message,
                "WindowFlip",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            startupMenuItem.Checked = startupRegistration.IsEnabled();
        }
    }

    protected override void ExitThreadCore()
    {
        DisposeResources();
        base.ExitThreadCore();
    }

    private void DisposeResources()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (notifyIcon is not null)
        {
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
        }

        inputSource.SwitchRequested -= OnSwitchRequested;
        inputSource.Dispose();
        overlay.Dispose();
        menu?.Dispose();
        appIcon.Dispose();
    }
}
