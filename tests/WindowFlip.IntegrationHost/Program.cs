using System.Drawing;

namespace WindowFlip.IntegrationHost;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using Form first = CreateWindow("WindowFlip Integration A", 140, 140);
        using Form second = CreateWindow("WindowFlip Integration B", 620, 180);
        first.Show();
        second.Show();
        Application.DoEvents();

        if (args.Length > 0)
        {
            File.WriteAllLines(
                args[0],
                [first.Handle.ToInt64().ToString(), second.Handle.ToInt64().ToString()]);
        }

        Application.Run();
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
