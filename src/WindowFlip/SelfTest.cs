using System.Drawing;
using WindowFlip.Core.Switching;
using WindowFlip.Platform.Windows;
using WindowFlip.Platform.Windows.Interop;
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

            Screen anchorScreen = Screen.PrimaryScreen!;
            Screen selectedScreen = Screen.AllScreens.FirstOrDefault(
                screen => screen.DeviceName != anchorScreen.DeviceName) ?? anchorScreen;
            forms[0].Location = new Point(
                anchorScreen.WorkingArea.Left + 20,
                anchorScreen.WorkingArea.Top + 20);
            forms[^1].Location = new Point(
                selectedScreen.WorkingArea.Left + 20,
                selectedScreen.WorkingArea.Top + 20);
            System.Windows.Forms.Application.DoEvents();

            using SwitchOverlay overlay = new(new Win32WindowIconProvider());
            nint committedHandle = 0;
            overlay.SelectionCommitted += (_, eventArgs) => committedHandle = eventArgs.TargetHandle;
            overlay.ShowSelection(
                "WindowFlip self-test",
                Environment.ProcessPath,
                forms.Select(form => new WindowDescriptor(form.Handle, form.Text)).ToArray(),
                forms[^1].Handle,
                forms[0].Handle);
            System.Windows.Forms.Application.DoEvents();

            Assert(overlay.Visible, "thumbnail overlay visibility");
            Assert(
                Screen.FromHandle(overlay.Handle).DeviceName == Screen.FromHandle(forms[0].Handle).DeviceName,
                "thumbnail overlay screen anchor");
            Assert(overlay.CardRowCount >= 2, "thumbnail overlay wrapping");
            Assert(overlay.RegisteredThumbnailCount == forms.Length, "all DWM thumbnails registered");

            Rectangle firstTitle = overlay.GetTitleBounds(0);
            Rectangle firstPreview = overlay.GetPreviewBounds(0);
            Assert(firstTitle.Bottom <= firstPreview.Top, "title above thumbnail");
            Point firstPreviewCenter = new(
                firstPreview.Left + (firstPreview.Width / 2),
                firstPreview.Top + (firstPreview.Height / 2));
            nint mousePosition = PackMousePosition(firstPreviewCenter);
            Assert(
                NativeMethods.PostMessage(overlay.Handle, NativeMethods.WmMouseMove, 0, mousePosition),
                "post thumbnail mouse move");
            System.Windows.Forms.Application.DoEvents();
            Assert(overlay.HoveredHandle == forms[0].Handle, "mouse thumbnail preview");
            Assert(
                overlay.KeyboardSelectedHandle == forms[^1].Handle,
                "mouse preview preserves keyboard selection");

            Point firstTitleCenter = new(
                firstTitle.Left + (firstTitle.Width / 2),
                firstTitle.Top + (firstTitle.Height / 2));
            Assert(
                NativeMethods.PostMessage(
                    overlay.Handle,
                    NativeMethods.WmMouseMove,
                    0,
                    PackMousePosition(firstTitleCenter)),
                "post title mouse move");
            System.Windows.Forms.Application.DoEvents();
            Assert(overlay.HoveredHandle == 0, "title does not preview thumbnail");

            Assert(
                NativeMethods.PostMessage(overlay.Handle, NativeMethods.WmMouseMove, 0, mousePosition),
                "restore thumbnail mouse move");
            System.Windows.Forms.Application.DoEvents();

            Assert(
                NativeMethods.PostMessage(overlay.Handle, NativeMethods.WmLeftButtonDown, 1, mousePosition),
                "post thumbnail mouse down");
            Assert(
                NativeMethods.PostMessage(overlay.Handle, NativeMethods.WmLeftButtonUp, 0, mousePosition),
                "post thumbnail mouse up");
            System.Windows.Forms.Application.DoEvents();
            Assert(committedHandle == forms[0].Handle, "mouse thumbnail commit");
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

    private static nint PackMousePosition(Point point)
    {
        return (nint)((point.Y << 16) | (point.X & 0xffff));
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Self-test failed: " + name);
        }
    }
}
