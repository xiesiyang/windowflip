using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

internal static class IntegrationHost
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Form first = CreateWindow("WindowFlip Integration A", 140, 140);
        Form second = CreateWindow("WindowFlip Integration B", 620, 180);
        first.Show();
        second.Show();
        Application.DoEvents();
        if (args.Length > 0)
        {
            File.WriteAllLines(args[0], new[]
            {
                first.Handle.ToInt64().ToString(),
                second.Handle.ToInt64().ToString()
            });
        }
        Application.Run();
        first.Dispose();
        second.Dispose();
    }

    private static Form CreateWindow(string title, int left, int top)
    {
        Form form = new Form();
        form.Text = title;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(left, top);
        form.Size = new Size(420, 260);
        form.ShowInTaskbar = true;
        return form;
    }
}
