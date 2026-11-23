using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Visuals;

namespace Compat;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var form = new Form
        {
            Text = "VeloxDev WinForms net48 smoke",
            ClientSize = new Size(560, 420),
            StartPosition = FormStartPosition.CenterScreen,
        };
        using var host = new VisualHost { Dock = DockStyle.Fill };
        var view = new SmokeView();
        host.Root = view;
        form.Controls.Add(host);

        var result = 1;
        using var timer = new System.Windows.Forms.Timer { Interval = 900 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                var box = FindByName(view, "SeedBox");
                box?.Focus();
                Application.DoEvents();

                using var bitmap = new Bitmap(
                    Math.Max(1, host.ClientSize.Width),
                    Math.Max(1, host.ClientSize.Height));
                using var graphics = Graphics.FromImage(bitmap);
                host.RenderTo(graphics, host.ClientSize);
                var background = bitmap.GetPixel(8, 8);
                result = view.Children.Count > 0 && background.A > 0 ? 0 : 1;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                result = 1;
            }

            form.Close();
        };

        form.Shown += (_, _) => timer.Start();
        Application.Run(form);
        return result;
    }

    private static VisualElement? FindByName(VisualElement root, string name)
    {
        if (string.Equals(root.Name, name, StringComparison.Ordinal))
        {
            return root;
        }

        foreach (var child in root.Children)
        {
            if (child is VisualElement element && FindByName(element, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
