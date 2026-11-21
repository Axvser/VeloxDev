using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Windows.Forms;
using XamlTabControl = VeloxDev.WinForms.Xaml.Controls.TabControl;

namespace Demo;

// 开发用截图模式：窗口显示后抓一张当前分页的截图，可选导出控件边界树。
// 一次进程只抓一页：先选定分页再显示窗口，首帧就是目标状态，不会拍到切页中间态。
internal static class GalleryCapture
{
    public static void ApplyTab(Form form, string[] args)
    {
        var tabs = FindTabControl(form);
        if (tabs is null || tabs.Items.Count == 0)
        {
            return;
        }

        var index = Math.Clamp(ReadInt(args, "--tab", 0), 0, tabs.Items.Count - 1);
        tabs.SelectedIndex = index;
    }

    public static void ApplyScroll(Form form, string[] args)
    {
        var offset = ReadInt(args, "--scroll", 0);
        if (offset <= 0)
        {
            return;
        }

        form.Shown += (_, _) => form.BeginInvoke(() =>
        {
            ScrollVisibleSection(form, offset);
            form.Invalidate(true);
        });
    }

    public static bool TryRun(Form form, string[] args)
    {
        var start = Array.FindIndex(args, a => string.Equals(a, "--capture", StringComparison.OrdinalIgnoreCase));
        if (start < 0)
        {
            return false;
        }

        var directory = start + 1 < args.Length && !args[start + 1].StartsWith('-')
            ? args[start + 1]
            : Path.Combine(AppContext.BaseDirectory, "captures");
        Directory.CreateDirectory(directory);

        var tabs = FindTabControl(form)
            ?? throw new InvalidOperationException("No XAML TabControl was found.");
        var tabIndex = Math.Clamp(ReadInt(args, "--tab", 0), 0, Math.Max(0, tabs.Items.Count - 1));
        tabs.SelectedIndex = tabIndex;

        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(60, 60);
        form.TopMost = true;
        var scroll = ReadInt(args, "--scroll", 0);
        form.Shown += (_, _) => form.BeginInvoke(() =>
        {
            try
            {
                Pump(form);
                ScrollVisibleSection(form, scroll);
                Pump(form);
                var header = tabs.Items[tabIndex]?.Header ?? $"tab{tabIndex}";
                var name = string.Join('-', header.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
                Capture(form, Path.Combine(directory, $"{tabIndex:00}-{name}.png"));

                if (args.Contains("--dump", StringComparer.OrdinalIgnoreCase))
                {
                    var builder = new StringBuilder();
                    builder.AppendLine($"window {form.Bounds}");
                    Dump(form, builder, 0);
                    File.WriteAllText(Path.Combine(directory, $"{tabIndex:00}-{name}.txt"), builder.ToString());
                }
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(directory, $"capture-error-{tabIndex}.txt"), ex.ToString());
            }
            finally
            {
                form.Close();
            }
        });

        return true;
    }

    private static int ReadInt(string[] args, string name, int fallback)
    {
        var index = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var value)
            ? value
            : fallback;
    }

    private static void ScrollVisibleSection(Form form, int offset)
    {
        if (offset <= 0)
        {
            return;
        }

        foreach (var host in GalleryLayout.Descendants<VeloxDev.WinForms.Xaml.Controls.ScrollableControl>(form))
        {
            if (!host.Visible || host.Height <= 0)
            {
                continue;
            }

            host.AutoScrollPosition = new Point(0, offset);
            return;
        }
    }

    private static void Pump(Form form)
    {
        form.Activate();
        for (var frame = 0; frame < 24; frame++)
        {
            Application.DoEvents();
            Thread.Sleep(50);
        }
    }

    private static void Capture(Form form, string path)
    {
        // PrintWindow 直接让窗口画进内存 DC，不依赖桌面坐标；DPI 缩放下不会截到窗口外。
        var bounds = form.Bounds;
        using var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            var hdc = graphics.GetHdc();
            try
            {
                _ = PrintWindow(form.Handle, hdc, 2);
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }
        }

        bitmap.Save(path, ImageFormat.Png);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    private static void Dump(System.Windows.Forms.Control parent, StringBuilder builder, int depth)
    {
        foreach (System.Windows.Forms.Control child in parent.Controls)
        {
            builder.Append(new string(' ', depth * 2));
            builder.Append(child.GetType().Name);
            if (!string.IsNullOrEmpty(child.Name))
            {
                builder.Append(' ').Append('#').Append(child.Name);
            }

            builder.Append(' ').Append(child.Bounds);
            if (child is VeloxDev.WinForms.Xaml.Controls.Control xamlControl)
            {
                builder.Append(" bg=");
                switch (xamlControl.Background)
                {
                    case null:
                        builder.Append("null");
                        break;
                    case VeloxDev.WinForms.Xaml.SolidColorBrush solid:
                        builder.Append(solid.Color.ToArgb().ToString("X8"));
                        break;
                    case VeloxDev.WinForms.Xaml.LinearGradientBrush gradient:
                        builder.Append("gradient:").Append(gradient.GradientStops.Count);
                        break;
                    default:
                        builder.Append(xamlControl.Background.GetType().Name);
                        break;
                }
                if (xamlControl is VeloxDev.WinForms.Xaml.Controls.Border border)
                {
                    builder.Append(" borderBg=");
                    switch (border.Background)
                    {
                        case null:
                            builder.Append("null");
                            break;
                        case VeloxDev.WinForms.Xaml.SolidColorBrush solid:
                            builder.Append(solid.Color.ToArgb().ToString("X8"));
                            break;
                        case VeloxDev.WinForms.Xaml.LinearGradientBrush gradient:
                            builder.Append("gradient:").Append(gradient.GradientStops.Count);
                            break;
                        default:
                            builder.Append(border.Background.GetType().Name);
                            break;
                    }

                    builder.Append(" thick=").Append(border.BorderThickness).Append(" stroke=");
                    switch (border.BorderBrush)
                    {
                        case null:
                            builder.Append("null");
                            break;
                        case VeloxDev.WinForms.Xaml.SolidColorBrush solidStroke:
                            builder.Append(solidStroke.Color.ToArgb().ToString("X8"));
                            break;
                        default:
                            builder.Append(border.BorderBrush.GetType().Name);
                            break;
                    }
                }
            }

            if (!child.Visible)
            {
                builder.Append(" hidden");
            }

            builder.AppendLine();
            Dump(child, builder, depth + 1);
        }
    }

    private static XamlTabControl? FindTabControl(System.Windows.Forms.Control root)
    {
        foreach (System.Windows.Forms.Control child in root.Controls)
        {
            if (child is XamlTabControl tabs)
            {
                return tabs;
            }

            var nested = FindTabControl(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
