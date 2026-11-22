using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;

namespace VisualWorkbench;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var light = args.Contains("--light", StringComparer.OrdinalIgnoreCase);
        if (light)
        {
            Theme.Mode = ThemeMode.Light;
        }

        Application.SetColorMode(light ? SystemColorMode.Classic : SystemColorMode.Dark);

        var capturePath = ReadCapturePath(args);
        if (capturePath is not null)
        {
            Console.WriteLine("Screen.AllScreens:");
            foreach (var screen in Screen.AllScreens)
            {
                Console.WriteLine(
                    $"  {screen.DeviceName} primary={screen.Primary} bounds={screen.Bounds} working={screen.WorkingArea}");
            }
        }

        using var form = new WorkbenchForm();
        var scroll = ReadScrollOffset(args);
        if (capturePath is not null)
        {
            form.Shown += (_, _) => form.BeginInvoke(() =>
            {
                try
                {
                    // Let the surface paint and lay out before scrolling or capturing.
                    Pump(frames: 12);
                    if (scroll > 0)
                    {
                        form.SetScrollOffset(scroll);
                        Pump(frames: 4);
                    }

                    if (args.Contains("--open-combo", StringComparer.OrdinalIgnoreCase))
                    {
                        form.OpenFirstComboBox();
                        Pump(frames: 4);
                    }

                    if (args.Contains("--graph-drag", StringComparer.OrdinalIgnoreCase))
                    {
                        form.DragFirstGraphNode();
                        Pump(frames: 4);
                    }

                    if (args.Contains("--graph-delete", StringComparer.OrdinalIgnoreCase))
                    {
                        form.DeleteFirstGraphLink();
                        Pump(frames: 4);
                    }

                    if (args.Contains("--graph-zoom", StringComparer.OrdinalIgnoreCase))
                    {
                        form.ZoomGraph();
                        Pump(frames: 4);
                    }

                    Capture(form, capturePath);
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine(exception);
                    Environment.ExitCode = 1;
                }
                finally
                {
                    form.Close();
                }
            });
        }

        Application.Run(form);
    }

    private static float ReadScrollOffset(string[] args)
    {
        var index = Array.FindIndex(args, value => string.Equals(value, "--scroll", StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length && float.TryParse(args[index + 1], out var offset)
            ? offset
            : 0f;
    }

    private static string? ReadCapturePath(string[] args)
    {
        var index = Array.FindIndex(args, value => string.Equals(value, "--capture", StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return null;
        }

        return index + 1 < args.Length && !args[index + 1].StartsWith('-')
            ? Path.GetFullPath(args[index + 1])
            : Path.Combine(AppContext.BaseDirectory, "workbench.png");
    }

    private static void Pump(int frames)
    {
        for (var frame = 0; frame < frames; frame++)
        {
            Application.DoEvents();
            Thread.Sleep(30);
        }
    }

    private static void Capture(Form form, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var bounds = form.Bounds;
        using var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            var hdc = graphics.GetHdc();
            bool printed;
            try
            {
                // PW_RENDERFULLCONTENT draws the composed window into the DC, independent of screen position.
                printed = PrintWindow(form.Handle, hdc, 2);
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }

            if (!printed)
            {
                form.DrawToBitmap(bitmap, new Rectangle(0, 0, bounds.Width, bounds.Height));
            }
        }

        bitmap.Save(path, ImageFormat.Png);
        Console.WriteLine($"Captured {bounds.Width}x{bounds.Height} to {path}");
    }

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
}
