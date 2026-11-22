using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Windows.Forms;
using Demo.Views;

namespace Demo;

// 开发用截图/启动选项：--capture 按目录条目逐张抓图，--entry/--group/--scroll 也用于交互启动。
internal static class GalleryCapture
{
    /// <summary>Applies interactive selection, scroll and chrome switches after the window is shown.</summary>
    public static void ApplyStartup(Form form, string[] args)
    {
        if (HasFlag(args, "--capture"))
        {
            return;
        }

        var gallery = FindGallery(form);
        if (gallery is null)
        {
            return;
        }

        var id = ReadEntryId(args);
        var scroll = ReadInt(args, "--scroll", 0);
        var previewOnly = HasFlag(args, "--preview-only");
        if (id is null && scroll <= 0 && !previewOnly)
        {
            return;
        }

        form.Shown += (_, _) => form.BeginInvoke(() =>
        {
            if (form.IsDisposed || gallery.IsDisposed)
            {
                return;
            }

            if (previewOnly)
            {
                gallery.SetChromeVisible(false);
            }

            if (id is not null)
            {
                gallery.SelectEntry(id);
            }

            if (scroll > 0)
            {
                gallery.SetPreviewScrollOffset(scroll);
            }
        });
    }

    /// <summary>Runs capture mode when <c>--capture</c> is present.</summary>
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

        var gallery = FindGallery(form)
            ?? throw new InvalidOperationException("No GalleryView was found.");
        var includeGroups = HasFlag(args, "--include-groups");
        var targets = ResolveTargets(gallery, args, includeGroups);
        var scroll = ReadInt(args, "--scroll", 0);
        var dump = HasFlag(args, "--dump");
        var previewOnly = HasFlag(args, "--preview-only");

        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(60, 60);
        form.TopMost = true;

        form.Shown += (_, _) => form.BeginInvoke(() =>
        {
            try
            {
                if (previewOnly)
                {
                    gallery.SetChromeVisible(false);
                }

                for (var index = 0; index < targets.Count; index++)
                {
                    var target = targets[index];
                    if (!gallery.SelectEntry(target.Id))
                    {
                        File.WriteAllText(
                            Path.Combine(directory, $"capture-error-{SafeName(target.Id)}.txt"),
                            $"Entry '{target.Id}' was not found.");
                        continue;
                    }

                    if (scroll > 0 && targets.Count == 1)
                    {
                        gallery.SetPreviewScrollOffset(scroll);
                    }

                    gallery.ClearSelection();
                    Pump(form, index == 0 ? 24 : 8);

                    var name = targets.Count == 1
                        ? SafeName(target.Id)
                        : $"{index:00}-{SafeName(target.Id)}";
                    Capture(form, Path.Combine(directory, name + ".png"));

                    if (dump)
                    {
                        var builder = new StringBuilder();
                        builder.AppendLine($"window {form.Bounds}");
                        Dump(form, builder, 0);
                        File.WriteAllText(Path.Combine(directory, name + ".txt"), builder.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(directory, "capture-error.txt"), ex.ToString());
            }
            finally
            {
                form.Close();
            }
        });

        return true;
    }

    private static List<GalleryEntry> ResolveTargets(GalleryView gallery, string[] args, bool includeGroups)
    {
        if (HasFlag(args, "--all"))
        {
            return gallery.EnumerateEntries(includeGroups).ToList();
        }

        var id = ReadEntryId(args) ?? "layout.stack-panel";
        var entry = GalleryCatalog.Find(id);
        return entry is null ? [] : [entry];
    }

    private static string? ReadEntryId(string[] args)
    {
        var entry = ReadOption(args, "--entry");
        if (entry is not null)
        {
            return entry;
        }

        var group = ReadOption(args, "--group");
        if (group is null)
        {
            return null;
        }

        return GalleryCatalog.FindGroup(group)?.Id ?? group;
    }

    private static string? ReadOption(string[] args, string name)
    {
        var index = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith('-')
            ? args[index + 1]
            : null;
    }

    private static int ReadInt(string[] args, string name, int fallback)
    {
        var text = ReadOption(args, name);
        return int.TryParse(text, out var value) ? value : fallback;
    }

    private static bool HasFlag(string[] args, string name)
        => Array.Exists(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

    private static GalleryView? FindGallery(Form form)
    {
        if (form is GalleryForm galleryForm)
        {
            return galleryForm.Gallery;
        }

        return GalleryLayout.Descendants<GalleryView>(form).FirstOrDefault();
    }

    private static string SafeName(string id)
        => string.Join('-', id.Split(['.', '/', '\\', ' ', ':'], StringSplitOptions.RemoveEmptyEntries));

    private static void Pump(Form form, int frames)
    {
        form.Activate();
        for (var frame = 0; frame < frames; frame++)
        {
            Application.DoEvents();
            Thread.Sleep(frames > 8 ? 40 : 30);
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
}
