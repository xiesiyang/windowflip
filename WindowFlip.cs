using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("WindowFlip")]
[assembly: AssemblyDescription("Switch between windows of the current application")]
[assembly: AssemblyCompany("WindowFlip")]
[assembly: AssemblyProduct("WindowFlip")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace WindowFlip
{
    internal static class Program
    {
        private const string MutexName = "Local\\WindowFlip.6D9B13A9-20E7-4FC5-82BF-B63E8ED1EC86";

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && string.Equals(args[0], "--self-test", StringComparison.OrdinalIgnoreCase))
            {
                return SelfTest.Run();
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try
                {
                    using (WindowFlipContext context = new WindowFlipContext())
                    {
                        Application.Run(context);
                    }
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

            return 0;
        }
    }

    internal sealed class WindowFlipContext : ApplicationContext
    {
        private readonly HotkeyWindow hotkeyWindow;
        private readonly NotifyIcon notifyIcon;
        private readonly WindowSwitcher switcher;
        private readonly SwitchOverlay overlay;
        private readonly ToolStripMenuItem startupMenuItem;
        private readonly Icon appIcon;
        private bool disposed;

        public WindowFlipContext()
        {
            appIcon = AppIcon.Create();
            overlay = new SwitchOverlay();
            switcher = new WindowSwitcher();
            hotkeyWindow = new HotkeyWindow();
            hotkeyWindow.SwitchRequested += OnSwitchRequested;

            HotkeyRegistration registration;
            if (!hotkeyWindow.TryRegister(out registration))
            {
                hotkeyWindow.Dispose();
                overlay.Dispose();
                appIcon.Dispose();
                throw new HotkeyRegistrationException(
                    "无法注册窗口切换快捷键。请关闭占用 Alt + ` 或 Win + ` 的程序后重试。");
            }

            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem nextItem = new ToolStripMenuItem("下一个窗口    " + registration.NextLabel);
            nextItem.Click += delegate { Switch(1); };
            menu.Items.Add(nextItem);

            ToolStripMenuItem previousItem = new ToolStripMenuItem("上一个窗口    " + registration.PreviousLabel);
            previousItem.Click += delegate { Switch(-1); };
            menu.Items.Add(previousItem);
            menu.Items.Add(new ToolStripSeparator());

            startupMenuItem = new ToolStripMenuItem("开机启动");
            startupMenuItem.Checked = StartupRegistration.IsEnabled();
            startupMenuItem.Click += ToggleStartup;
            menu.Items.Add(startupMenuItem);
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem exitItem = new ToolStripMenuItem("退出");
            exitItem.Click += delegate { ExitThread(); };
            menu.Items.Add(exitItem);

            notifyIcon = new NotifyIcon();
            notifyIcon.Icon = appIcon;
            notifyIcon.Text = "WindowFlip - 同应用窗口切换";
            notifyIcon.ContextMenuStrip = menu;
            notifyIcon.Visible = true;
            notifyIcon.DoubleClick += delegate { Switch(1); };
        }

        private void OnSwitchRequested(object sender, SwitchRequestedEventArgs e)
        {
            Switch(e.Direction);
        }

        private void Switch(int direction)
        {
            SwitchResult result = switcher.Switch(direction);
            if (result.Status == SwitchStatus.Switched)
            {
                overlay.ShowSelection(result.ApplicationName, result.OrderedWindows, result.TargetHandle);
                return;
            }

            if (result.Status == SwitchStatus.OnlyOneWindow)
            {
                DisposeWindowIcons(result.OrderedWindows);
                overlay.ShowMessage(result.ApplicationName, "当前应用没有其他可切换窗口", result.ForegroundHandle);
                return;
            }

            DisposeWindowIcons(result.OrderedWindows);
        }

        private static void DisposeWindowIcons(IEnumerable<WindowInfo> windowList)
        {
            if (windowList == null)
            {
                return;
            }

            foreach (WindowInfo window in windowList)
            {
                if (window.Icon != null)
                {
                    window.Icon.Dispose();
                }
            }
        }

        private void ToggleStartup(object sender, EventArgs e)
        {
            bool enable = !StartupRegistration.IsEnabled();
            try
            {
                StartupRegistration.SetEnabled(enable);
                startupMenuItem.Checked = enable;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "更新开机启动设置失败：\r\n" + ex.Message,
                    "WindowFlip",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                startupMenuItem.Checked = StartupRegistration.IsEnabled();
            }
        }

        protected override void ExitThreadCore()
        {
            if (!disposed)
            {
                disposed = true;
                if (notifyIcon != null)
                {
                    notifyIcon.Visible = false;
                    notifyIcon.Dispose();
                }

                hotkeyWindow.SwitchRequested -= OnSwitchRequested;
                hotkeyWindow.Dispose();
                overlay.Dispose();
                appIcon.Dispose();
            }

            base.ExitThreadCore();
        }
    }

    internal sealed class HotkeyRegistrationException : Exception
    {
        public HotkeyRegistrationException(string message)
            : base(message)
        {
        }
    }

    internal sealed class SwitchRequestedEventArgs : EventArgs
    {
        public SwitchRequestedEventArgs(int direction)
        {
            Direction = direction;
        }

        public int Direction { get; private set; }
    }

    internal sealed class HotkeyRegistration
    {
        public HotkeyRegistration(string nextLabel, string previousLabel)
        {
            NextLabel = nextLabel;
            PreviousLabel = previousLabel;
        }

        public string NextLabel { get; private set; }
        public string PreviousLabel { get; private set; }
    }

    internal sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private const int HotkeyNext = 0x2101;
        private const int HotkeyPrevious = 0x2102;
        private bool registered;

        public HotkeyWindow()
        {
            CreateParams parameters = new CreateParams();
            parameters.Caption = "WindowFlip.HotkeyWindow";
            CreateHandle(parameters);
        }

        public event EventHandler<SwitchRequestedEventArgs> SwitchRequested;

        public bool TryRegister(out HotkeyRegistration registration)
        {
            registration = null;

            if (TryRegisterPair(NativeMethods.ModAlt, "Alt + `", "Alt + Shift + `"))
            {
                registration = new HotkeyRegistration("Alt + `", "Alt + Shift + `");
                return true;
            }

            if (TryRegisterPair(NativeMethods.ModWin, "Win + `", "Win + Shift + `"))
            {
                registration = new HotkeyRegistration("Win + `", "Win + Shift + `");
                return true;
            }

            return false;
        }

        private bool TryRegisterPair(uint modifier, string nextLabel, string previousLabel)
        {
            uint common = modifier | NativeMethods.ModNoRepeat;
            bool next = NativeMethods.RegisterHotKey(Handle, HotkeyNext, common, NativeMethods.VkOem3);
            if (!next)
            {
                return false;
            }

            bool previous = NativeMethods.RegisterHotKey(
                Handle,
                HotkeyPrevious,
                common | NativeMethods.ModShift,
                NativeMethods.VkOem3);
            if (!previous)
            {
                NativeMethods.UnregisterHotKey(Handle, HotkeyNext);
                return false;
            }

            registered = true;
            return true;
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == NativeMethods.WmHotkey)
            {
                int id = message.WParam.ToInt32();
                EventHandler<SwitchRequestedEventArgs> handler = SwitchRequested;
                if (handler != null)
                {
                    handler(this, new SwitchRequestedEventArgs(id == HotkeyPrevious ? -1 : 1));
                }
            }

            base.WndProc(ref message);
        }

        public void Dispose()
        {
            if (registered && Handle != IntPtr.Zero)
            {
                NativeMethods.UnregisterHotKey(Handle, HotkeyNext);
                NativeMethods.UnregisterHotKey(Handle, HotkeyPrevious);
                registered = false;
            }

            if (Handle != IntPtr.Zero)
            {
                DestroyHandle();
            }
        }
    }

    internal enum SwitchStatus
    {
        NoForegroundWindow,
        OnlyOneWindow,
        Switched,
        ActivationFailed
    }

    internal sealed class SwitchResult
    {
        public SwitchStatus Status { get; set; }
        public string ApplicationName { get; set; }
        public IntPtr ForegroundHandle { get; set; }
        public IntPtr TargetHandle { get; set; }
        public IList<WindowInfo> OrderedWindows { get; set; }
    }

    internal sealed class WindowSwitcher
    {
        private static readonly TimeSpan SessionDuration = TimeSpan.FromSeconds(2.5);
        private readonly WindowCatalog catalog;
        private List<IntPtr> sessionOrder = new List<IntPtr>();
        private string sessionApplicationKey;
        private DateTime lastSwitchUtc;

        public WindowSwitcher()
            : this(new WindowCatalog())
        {
        }

        internal WindowSwitcher(WindowCatalog catalog)
        {
            this.catalog = catalog;
        }

        public SwitchResult Switch(int direction)
        {
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (foreground == IntPtr.Zero)
            {
                return new SwitchResult { Status = SwitchStatus.NoForegroundWindow };
            }

            AppIdentity identity = AppIdentity.FromWindow(foreground);
            if (identity == null)
            {
                return new SwitchResult { Status = SwitchStatus.NoForegroundWindow };
            }

            List<WindowInfo> windows = catalog.GetWindows(identity);
            if (windows.Count <= 1)
            {
                return new SwitchResult
                {
                    Status = SwitchStatus.OnlyOneWindow,
                    ApplicationName = identity.DisplayName,
                    ForegroundHandle = foreground,
                    OrderedWindows = windows
                };
            }

            DateTime now = DateTime.UtcNow;
            bool continueSession = string.Equals(sessionApplicationKey, identity.Key, StringComparison.OrdinalIgnoreCase)
                && now - lastSwitchUtc <= SessionDuration;

            if (!continueSession)
            {
                sessionOrder = windows.Select(window => window.Handle).ToList();
                sessionApplicationKey = identity.Key;
            }
            else
            {
                HashSet<IntPtr> available = new HashSet<IntPtr>(windows.Select(window => window.Handle));
                sessionOrder.RemoveAll(handle => !available.Contains(handle));
                foreach (WindowInfo window in windows)
                {
                    if (!sessionOrder.Contains(window.Handle))
                    {
                        sessionOrder.Add(window.Handle);
                    }
                }
            }

            int currentIndex = sessionOrder.IndexOf(foreground);
            if (currentIndex < 0)
            {
                currentIndex = direction > 0 ? sessionOrder.Count - 1 : 0;
            }

            int targetIndex = WrapIndex(currentIndex + (direction >= 0 ? 1 : -1), sessionOrder.Count);
            IntPtr target = sessionOrder[targetIndex];
            lastSwitchUtc = now;

            Dictionary<IntPtr, WindowInfo> byHandle = windows.ToDictionary(window => window.Handle);
            List<WindowInfo> ordered = sessionOrder
                .Where(byHandle.ContainsKey)
                .Select(handle => byHandle[handle])
                .ToList();

            bool activated = WindowActivation.Activate(target);
            return new SwitchResult
            {
                Status = activated ? SwitchStatus.Switched : SwitchStatus.ActivationFailed,
                ApplicationName = identity.DisplayName,
                ForegroundHandle = foreground,
                TargetHandle = target,
                OrderedWindows = ordered
            };
        }

        internal static int WrapIndex(int index, int count)
        {
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException("count");
            }

            int wrapped = index % count;
            return wrapped < 0 ? wrapped + count : wrapped;
        }
    }

    internal sealed class WindowInfo
    {
        public IntPtr Handle { get; set; }
        public string Title { get; set; }
        public Icon Icon { get; set; }
    }

    internal sealed class AppIdentity
    {
        private AppIdentity()
        {
        }

        public string Key { get; private set; }
        public string ProcessName { get; private set; }
        public string ExecutablePath { get; private set; }
        public string DisplayName { get; private set; }

        public static AppIdentity FromWindow(IntPtr window)
        {
            uint processId;
            NativeMethods.GetWindowThreadProcessId(window, out processId);
            if (processId == 0)
            {
                return null;
            }

            try
            {
                using (Process process = Process.GetProcessById((int)processId))
                {
                    string processName = process.ProcessName;
                    string executablePath = TryGetExecutablePath(process);
                    string key = string.IsNullOrEmpty(executablePath)
                        ? "name:" + processName
                        : "path:" + executablePath;

                    return new AppIdentity
                    {
                        Key = key,
                        ProcessName = processName,
                        ExecutablePath = executablePath,
                        DisplayName = GetDisplayName(processName, executablePath)
                    };
                }
            }
            catch
            {
                return null;
            }
        }

        public bool Matches(Process process)
        {
            string candidatePath = TryGetExecutablePath(process);
            if (!string.IsNullOrEmpty(ExecutablePath) && !string.IsNullOrEmpty(candidatePath))
            {
                return string.Equals(ExecutablePath, candidatePath, StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(ProcessName, process.ProcessName, StringComparison.OrdinalIgnoreCase);
        }

        private static string TryGetExecutablePath(Process process)
        {
            try
            {
                return process.MainModule == null ? null : process.MainModule.FileName;
            }
            catch
            {
                return null;
            }
        }

        private static string GetDisplayName(string processName, string executablePath)
        {
            if (!string.IsNullOrEmpty(executablePath))
            {
                try
                {
                    FileVersionInfo version = FileVersionInfo.GetVersionInfo(executablePath);
                    if (!string.IsNullOrWhiteSpace(version.FileDescription))
                    {
                        return version.FileDescription.Trim();
                    }
                }
                catch
                {
                }
            }

            return processName;
        }
    }

    internal sealed class WindowCatalog
    {
        private readonly bool includeOwnProcess;

        public WindowCatalog()
            : this(false)
        {
        }

        internal WindowCatalog(bool includeOwnProcess)
        {
            this.includeOwnProcess = includeOwnProcess;
        }

        public List<WindowInfo> GetWindows(AppIdentity identity)
        {
            List<WindowInfo> result = new List<WindowInfo>();
            int ownProcessId = Process.GetCurrentProcess().Id;

            NativeMethods.EnumWindows(delegate(IntPtr window, IntPtr parameter)
            {
                if (!IsSwitchableWindow(window))
                {
                    return true;
                }

                uint processId;
                NativeMethods.GetWindowThreadProcessId(window, out processId);
                if (processId == 0 || (!includeOwnProcess && processId == ownProcessId))
                {
                    return true;
                }

                try
                {
                    using (Process process = Process.GetProcessById((int)processId))
                    {
                        if (!identity.Matches(process))
                        {
                            return true;
                        }
                    }
                }
                catch
                {
                    return true;
                }

                string title = GetWindowTitle(window);
                if (string.IsNullOrWhiteSpace(title))
                {
                    return true;
                }

                result.Add(new WindowInfo
                {
                    Handle = window,
                    Title = title.Trim(),
                    Icon = WindowIcon.Get(window, identity.ExecutablePath)
                });
                return true;
            }, IntPtr.Zero);

            return result;
        }

        private static bool IsSwitchableWindow(IntPtr window)
        {
            if (!NativeMethods.IsWindowVisible(window))
            {
                return false;
            }

            long extendedStyle = NativeMethods.GetWindowLongPtr(window, NativeMethods.GwlExStyle).ToInt64();
            bool isToolWindow = (extendedStyle & NativeMethods.WsExToolWindow) != 0;
            bool isAppWindow = (extendedStyle & NativeMethods.WsExAppWindow) != 0;
            if (isToolWindow && !isAppWindow)
            {
                return false;
            }

            if (NativeMethods.GetWindow(window, NativeMethods.GwOwner) != IntPtr.Zero && !isAppWindow)
            {
                return false;
            }

            int cloaked;
            int size = Marshal.SizeOf(typeof(int));
            int result = NativeMethods.DwmGetWindowAttribute(
                window,
                NativeMethods.DwmaCloaked,
                out cloaked,
                size);
            return result != 0 || cloaked == 0;
        }

        private static string GetWindowTitle(IntPtr window)
        {
            int length = NativeMethods.GetWindowTextLength(window);
            if (length <= 0)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(length + 1);
            NativeMethods.GetWindowText(window, builder, builder.Capacity);
            return builder.ToString();
        }
    }

    internal static class WindowIcon
    {
        public static Icon Get(IntPtr window, string executablePath)
        {
            IntPtr iconHandle = GetWindowIcon(window);
            if (iconHandle != IntPtr.Zero)
            {
                try
                {
                    return (Icon)Icon.FromHandle(iconHandle).Clone();
                }
                catch
                {
                }
            }

            if (!string.IsNullOrEmpty(executablePath))
            {
                try
                {
                    Icon icon = Icon.ExtractAssociatedIcon(executablePath);
                    if (icon != null)
                    {
                        return icon;
                    }
                }
                catch
                {
                }
            }

            return (Icon)SystemIcons.Application.Clone();
        }

        private static IntPtr GetWindowIcon(IntPtr window)
        {
            IntPtr result;
            UIntPtr ignored;
            NativeMethods.SendMessageTimeout(
                window,
                NativeMethods.WmGetIcon,
                new IntPtr(NativeMethods.IconSmall2),
                IntPtr.Zero,
                NativeMethods.SmtoAbortIfHung,
                100,
                out ignored);
            result = new IntPtr(unchecked((long)ignored.ToUInt64()));

            if (result == IntPtr.Zero)
            {
                result = NativeMethods.GetClassLongPtr(window, NativeMethods.GclpHIconSm);
            }

            if (result == IntPtr.Zero)
            {
                result = NativeMethods.GetClassLongPtr(window, NativeMethods.GclpHIcon);
            }

            return result;
        }
    }

    internal static class WindowActivation
    {
        public static bool Activate(IntPtr target)
        {
            if (target == IntPtr.Zero || !NativeMethods.IsWindow(target))
            {
                return false;
            }

            if (NativeMethods.IsIconic(target))
            {
                NativeMethods.ShowWindowAsync(target, NativeMethods.SwRestore);
            }

            IntPtr foreground = NativeMethods.GetForegroundWindow();
            uint foregroundThread = foreground == IntPtr.Zero
                ? 0
                : NativeMethods.GetWindowThreadProcessId(foreground, IntPtr.Zero);
            uint currentThread = NativeMethods.GetCurrentThreadId();
            bool attached = false;

            try
            {
                if (foregroundThread != 0 && foregroundThread != currentThread)
                {
                    attached = NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
                }

                NativeMethods.BringWindowToTop(target);
                return NativeMethods.SetForegroundWindow(target);
            }
            finally
            {
                if (attached)
                {
                    NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
                }
            }
        }
    }

    internal sealed class SwitchOverlay : Form
    {
        private readonly System.Windows.Forms.Timer hideTimer;
        private readonly List<WindowInfo> windows = new List<WindowInfo>();
        private string applicationName = string.Empty;
        private string message;
        private IntPtr selectedHandle;
        private float scale = 1.0f;

        public SwitchOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Color.FromArgb(32, 33, 36);
            DoubleBuffered = true;

            hideTimer = new System.Windows.Forms.Timer();
            hideTimer.Interval = 1100;
            hideTimer.Tick += delegate
            {
                hideTimer.Stop();
                Hide();
            };
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow;
                parameters.ClassStyle |= NativeMethods.CsDropShadow;
                return parameters;
            }
        }

        public void ShowSelection(string appName, IList<WindowInfo> orderedWindows, IntPtr selected)
        {
            applicationName = appName ?? string.Empty;
            message = null;
            selectedHandle = selected;
            DisposeWindowIcons();
            windows.Clear();
            windows.AddRange(orderedWindows);
            ShowOverlay(selected);
        }

        public void ShowMessage(string appName, string text, IntPtr anchor)
        {
            applicationName = appName ?? string.Empty;
            message = text;
            selectedHandle = IntPtr.Zero;
            DisposeWindowIcons();
            windows.Clear();
            ShowOverlay(anchor);
        }

        private void ShowOverlay(IntPtr anchor)
        {
            if (!IsHandleCreated)
            {
                CreateControl();
            }

            try
            {
                scale = NativeMethods.GetDpiForWindow(Handle) / 96.0f;
            }
            catch (EntryPointNotFoundException)
            {
                using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero))
                {
                    scale = graphics.DpiX / 96.0f;
                }
            }

            int visibleRows = message == null ? Math.Min(windows.Count, 7) : 1;
            int footerHeight = message == null && windows.Count > visibleRows ? Px(28) : 0;
            Size = new Size(Px(520), Px(46) + (visibleRows * Px(46)) + footerHeight + Px(2));

            Screen screen = anchor == IntPtr.Zero ? Screen.PrimaryScreen : Screen.FromHandle(anchor);
            Rectangle area = screen.WorkingArea;
            int x = area.Left + Math.Max(0, (area.Width - Width) / 2);
            int y = area.Bottom - Height - Px(92);
            Location = new Point(x, Math.Max(area.Top, y));

            if (!Visible)
            {
                Show();
            }

            NativeMethods.SetWindowPos(
                Handle,
                NativeMethods.HwndTopmost,
                Left,
                Top,
                Width,
                Height,
                NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
            Invalidate();
            hideTimer.Stop();
            hideTimer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;
            graphics.Clear(Color.FromArgb(32, 33, 36));
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (Pen border = new Pen(Color.FromArgb(78, 80, 84), Math.Max(1.0f, scale)))
            {
                graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            }

            using (Font headerFont = new Font("Segoe UI", 10.0f, FontStyle.Bold, GraphicsUnit.Point))
            using (Brush headerBrush = new SolidBrush(Color.FromArgb(232, 234, 237)))
            using (Brush countBrush = new SolidBrush(Color.FromArgb(154, 160, 166)))
            {
                Rectangle header = new Rectangle(Px(16), 0, Width - Px(32), Px(46));
                TextRenderer.DrawText(
                    graphics,
                    applicationName,
                    headerFont,
                    header,
                    ((SolidBrush)headerBrush).Color,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

                if (message == null)
                {
                    string countText = windows.Count + " 个窗口";
                    Size countSize = TextRenderer.MeasureText(countText, headerFont);
                    Rectangle countArea = new Rectangle(Width - Px(16) - countSize.Width, 0, countSize.Width, Px(46));
                    TextRenderer.DrawText(
                        graphics,
                        countText,
                        headerFont,
                        countArea,
                        ((SolidBrush)countBrush).Color,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }
            }

            using (Pen separator = new Pen(Color.FromArgb(60, 64, 67), Math.Max(1.0f, scale)))
            {
                graphics.DrawLine(separator, Px(16), Px(45), Width - Px(16), Px(45));
            }

            if (message != null)
            {
                DrawMessage(graphics);
                return;
            }

            List<WindowInfo> visibleWindows = GetVisibleWindows();
            for (int index = 0; index < visibleWindows.Count; index++)
            {
                DrawWindowRow(graphics, visibleWindows[index], index);
            }

            if (windows.Count > visibleWindows.Count)
            {
                int footerTop = Px(46) + visibleWindows.Count * Px(46);
                string footer = "另有 " + (windows.Count - visibleWindows.Count) + " 个窗口";
                using (Font font = new Font("Segoe UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point))
                {
                    TextRenderer.DrawText(
                        graphics,
                        footer,
                        font,
                        new Rectangle(Px(16), footerTop, Width - Px(32), Px(28)),
                        Color.FromArgb(154, 160, 166),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }
            }
        }

        private List<WindowInfo> GetVisibleWindows()
        {
            if (windows.Count <= 7)
            {
                return new List<WindowInfo>(windows);
            }

            int selectedIndex = windows.FindIndex(window => window.Handle == selectedHandle);
            if (selectedIndex < 0)
            {
                selectedIndex = 0;
            }

            int start = Math.Max(0, Math.Min(selectedIndex - 3, windows.Count - 7));
            return windows.Skip(start).Take(7).ToList();
        }

        private void DrawWindowRow(Graphics graphics, WindowInfo window, int visibleIndex)
        {
            int top = Px(46) + visibleIndex * Px(46);
            bool selected = window.Handle == selectedHandle;
            if (selected)
            {
                using (Brush highlight = new SolidBrush(Color.FromArgb(14, 99, 156)))
                {
                    graphics.FillRectangle(highlight, Px(6), top + Px(3), Width - Px(12), Px(40));
                }
            }

            if (window.Icon != null)
            {
                graphics.DrawIcon(window.Icon, new Rectangle(Px(16), top + Px(11), Px(24), Px(24)));
            }

            using (Font font = new Font("Segoe UI", 9.5f, selected ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point))
            {
                Rectangle textArea = new Rectangle(Px(52), top, Width - Px(68), Px(46));
                TextRenderer.DrawText(
                    graphics,
                    window.Title,
                    font,
                    textArea,
                    selected ? Color.White : Color.FromArgb(218, 220, 224),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }
        }

        private void DrawMessage(Graphics graphics)
        {
            using (Font font = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point))
            {
                TextRenderer.DrawText(
                    graphics,
                    message,
                    font,
                    new Rectangle(Px(16), Px(46), Width - Px(32), Px(46)),
                    Color.FromArgb(218, 220, 224),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }
        }

        private int Px(int value)
        {
            return Math.Max(1, (int)Math.Round(value * scale));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                hideTimer.Dispose();
                DisposeWindowIcons();
                windows.Clear();
            }

            base.Dispose(disposing);
        }

        private void DisposeWindowIcons()
        {
            foreach (WindowInfo window in windows)
            {
                if (window.Icon != null)
                {
                    window.Icon.Dispose();
                }
            }
        }
    }

    internal static class StartupRegistration
    {
        private const string RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        private const string ValueName = "WindowFlip";

        public static bool IsEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
            {
                string value = key == null ? null : key.GetValue(ValueName) as string;
                return string.Equals(value, GetCommand(), StringComparison.OrdinalIgnoreCase);
            }
        }

        public static void SetEnabled(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
            {
                if (key == null)
                {
                    throw new InvalidOperationException("无法打开当前用户的启动项注册表路径。");
                }

                if (enabled)
                {
                    key.SetValue(ValueName, GetCommand(), RegistryValueKind.String);
                }
                else
                {
                    key.DeleteValue(ValueName, false);
                }
            }
        }

        private static string GetCommand()
        {
            return "\"" + Application.ExecutablePath + "\" --startup";
        }
    }

    internal static class AppIcon
    {
        public static Icon Create()
        {
            Bitmap bitmap = new Bitmap(32, 32);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Brush background = new SolidBrush(Color.FromArgb(14, 99, 156)))
            using (Pen windowPen = new Pen(Color.White, 2.0f))
            using (Pen arrowPen = new Pen(Color.FromArgb(146, 255, 211), 2.5f))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                graphics.FillRectangle(background, 1, 1, 30, 30);
                graphics.DrawRectangle(windowPen, 6, 7, 14, 12);
                graphics.DrawRectangle(windowPen, 12, 13, 14, 12);
                arrowPen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                arrowPen.EndCap = System.Drawing.Drawing2D.LineCap.ArrowAnchor;
                graphics.DrawLine(arrowPen, 7, 25, 22, 25);
            }

            IntPtr handle = bitmap.GetHicon();
            try
            {
                return (Icon)Icon.FromHandle(handle).Clone();
            }
            finally
            {
                NativeMethods.DestroyIcon(handle);
                bitmap.Dispose();
            }
        }
    }

    internal static class SelfTest
    {
        public static int Run()
        {
            try
            {
                Assert(WindowSwitcher.WrapIndex(1, 3) == 1, "forward index");
                Assert(WindowSwitcher.WrapIndex(3, 3) == 0, "forward wrap");
                Assert(WindowSwitcher.WrapIndex(-1, 3) == 2, "backward wrap");

                using (Icon icon = AppIcon.Create())
                {
                    Assert(icon.Width > 0 && icon.Height > 0, "application icon");
                }

                using (Form first = new Form())
                using (Form second = new Form())
                {
                    first.Text = "WindowFlip self-test A";
                    second.Text = "WindowFlip self-test B";
                    first.Show();
                    second.Show();
                    Application.DoEvents();

                    AppIdentity identity = AppIdentity.FromWindow(first.Handle);
                    Assert(identity != null, "application identity");

                    List<WindowInfo> windows = new WindowCatalog(true).GetWindows(identity);
                    Assert(windows.Any(window => window.Handle == first.Handle), "first test window");
                    Assert(windows.Any(window => window.Handle == second.Handle), "second test window");

                    foreach (WindowInfo window in windows)
                    {
                        if (window.Icon != null)
                        {
                            window.Icon.Dispose();
                        }
                    }
                }

                return 0;
            }
            catch
            {
                return 1;
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

    internal static class NativeMethods
    {
        public const int WmHotkey = 0x0312;
        public const int WmGetIcon = 0x007F;
        public const int IconSmall2 = 2;
        public const int GwlExStyle = -20;
        public const int WsExToolWindow = 0x00000080;
        public const int WsExAppWindow = 0x00040000;
        public const int WsExNoActivate = 0x08000000;
        public const int CsDropShadow = 0x00020000;
        public const uint ModAlt = 0x0001;
        public const uint ModShift = 0x0004;
        public const uint ModWin = 0x0008;
        public const uint ModNoRepeat = 0x4000;
        public const uint VkOem3 = 0xC0;
        public const uint GwOwner = 4;
        public const int DwmaCloaked = 14;
        public const uint SmtoAbortIfHung = 0x0002;
        public const int GclpHIcon = -14;
        public const int GclpHIconSm = -34;
        public const int SwRestore = 9;
        public const uint SwpNoActivate = 0x0010;
        public const uint SwpShowWindow = 0x0040;
        public static readonly IntPtr HwndTopmost = new IntPtr(-1);

        public delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(IntPtr window, int id);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(IntPtr window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr window);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr window, StringBuilder text, int maximumCount);

        [DllImport("user32.dll")]
        public static extern int GetWindowTextLength(IntPtr window);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr window, uint command);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr window, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);

        public static IntPtr GetWindowLongPtr(IntPtr window, int index)
        {
            return IntPtr.Size == 8 ? GetWindowLongPtr64(window, index) : new IntPtr(GetWindowLong32(window, index));
        }

        [DllImport("user32.dll", EntryPoint = "GetClassLong")]
        private static extern uint GetClassLong32(IntPtr window, int index);

        [DllImport("user32.dll", EntryPoint = "GetClassLongPtr")]
        private static extern IntPtr GetClassLongPtr64(IntPtr window, int index);

        public static IntPtr GetClassLongPtr(IntPtr window, int index)
        {
            return IntPtr.Size == 8 ? GetClassLongPtr64(window, index) : new IntPtr(unchecked((int)GetClassLong32(window, index)));
        }

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SendMessageTimeout(
            IntPtr window,
            uint message,
            IntPtr wParam,
            IntPtr lParam,
            uint flags,
            uint timeout,
            out UIntPtr result);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindowAsync(IntPtr window, int command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BringWindowToTop(IntPtr window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AttachThreadInput(uint attachThread, uint attachToThread, bool attach);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(
            IntPtr window,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyIcon(IntPtr icon);
    }
}
