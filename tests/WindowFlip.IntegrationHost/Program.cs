using System.Drawing;

namespace WindowFlip.IntegrationHost;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 4 && args[0] == "--directory-sync-probe")
        {
            Environment.ExitCode = DirectorySyncProbe.Run(args[1], nint.Parse(args[2]), args[3]);
            return;
        }
        if (args.Length == 2 && args[0] == "--directory-sync-test")
        {
            Environment.ExitCode = DirectorySyncIntegration.Run(args[1]);
            return;
        }

        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

        using Form first = CreateWindow("WindowFlip Integration A", 140, 140);
        using Form second = CreateWindow("WindowFlip Integration B", 620, 180);
        first.Show();
        second.Show();
        System.Windows.Forms.Application.DoEvents();

        if (args.Length > 0)
        {
            File.WriteAllLines(
                args[0],
                [first.Handle.ToInt64().ToString(), second.Handle.ToInt64().ToString()]);
        }

        System.Windows.Forms.Application.Run();
    }

    private static Form CreateWindow(string title, int left, int top)
    {
        return new Form
        {
            Text = title,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(left, top),
            Size = new Size(420, 260),
            ShowInTaskbar = true
        };
    }
}
