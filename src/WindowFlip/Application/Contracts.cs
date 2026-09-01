using System.Drawing;
using WindowFlip.Core.Switching;

namespace WindowFlip.Application;

internal interface IForegroundWindowProvider
{
    nint GetForegroundWindow();
}

internal interface IApplicationIdentityResolver
{
    ApplicationIdentity? Resolve(nint window);
}

internal interface IWindowCatalog
{
    IReadOnlyList<WindowDescriptor> GetWindows(ApplicationIdentity identity);
}

internal interface IWindowActivator
{
    bool Activate(nint target);
}

internal interface IWindowIconProvider
{
    Icon GetIcon(nint window, string? executablePath);
}

internal interface IStartupRegistration
{
    bool IsEnabled();

    void SetEnabled(bool enabled);
}

internal interface ISwitchInputSource : IDisposable
{
    event EventHandler<SwitchRequestedEventArgs>? SwitchRequested;

    bool TryRegister(out HotkeyRegistration? registration);
}
