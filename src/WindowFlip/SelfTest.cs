using System.Drawing;
using WindowFlip.Core.Switching;
using WindowFlip.Platform.Windows;
using WindowFlip.Presentation;

namespace WindowFlip;

internal static class SelfTest
{
    public static int Run()
    {
        try
        {
            VerifySwitchSession();
            VerifyApplicationIcon();
            VerifyWindowCatalog();
            VerifyThumbnailOverlay();
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    private static void VerifySwitchSession()
    {
        WindowDescriptor[] windows =
        [
            new(1, "A"),
            new(2, "B"),
            new(3, "C")
        ];
        DateTimeOffset now = DateTimeOffset.UtcNow;

        WindowSwitchSession forwardSession = new();
        SwitchSelectionResult forward = forwardSession.Select("self-test", 1, windows, SwitchDirection.Next, now);
        Assert(forward.SelectedHandle == 2, "forward selection");

        WindowSwitchSession backwardSession = new();
        SwitchSelectionResult backward = backwardSession.Select("self-test", 1, windows, SwitchDirection.Previous, now);
        Assert(backward.SelectedHandle == 3, "backward wrap");
    }

    private static void VerifyApplicationIcon()
    {
        using Icon icon = AppIcon.Create();
        Assert(icon.Width > 0 && icon.Height > 0, "application icon");
    }

    private static void VerifyWindowCatalog()
    {
        using Form first = new() { Text = "WindowFlip self-test A" };
        using Form second = new() { Text = "WindowFlip self-test B" };
        first.Show();
        second.Show();
        System.Windows.Forms.Application.DoEvents();

        Win32ApplicationIdentityResolver resolver = new();
        WindowFlip.Application.ApplicationIdentity? identity = resolver.Resolve(first.Handle);
        Assert(identity is not null, "application identity");

        IReadOnlyList<WindowDescriptor> windows = new Win32WindowCatalog(true).GetWindows(identity!);
        Assert(windows.Any(window => window.Handle == first.Handle), "first test window");
        Assert(windows.Any(window => window.Handle == second.Handle), "second test window");
    }

    private static void VerifyThumbnailOverlay()
    {
        Form[] forms = Enumerable.Range(1, 6)
            .Select(index => new Form
            {
                Text = "WindowFlip thumbnail " + index,
                Size = new Size(360, 220)
            })
            .ToArray();

        try
        {
            foreach (Form form in forms)
            {
                form.Show();
            }

            System.Windows.Forms.Application.DoEvents();

            using SwitchOverlay overlay = new(new Win32WindowIconProvider());
            overlay.ShowSelection(
                "WindowFlip self-test",
                Environment.ProcessPath,
                forms.Select(form => new WindowDescriptor(form.Handle, form.Text)).ToArray(),
                forms[^1].Handle);
            System.Windows.Forms.Application.DoEvents();

            Assert(overlay.Visible, "thumbnail overlay visibility");
            Assert(overlay.CardRowCount >= 2, "thumbnail overlay wrapping");
            Assert(overlay.RegisteredThumbnailCount == forms.Length, "all DWM thumbnails registered");
            overlay.HideSelection();
        }
        finally
        {
            foreach (Form form in forms)
            {
                form.Dispose();
            }
        }
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Self-test failed: " + name);
        }
    }
}
