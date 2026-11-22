using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Visuals;

namespace Demo;

// 开发用截图/启动选项：--capture 按目录条目逐张抓图，--entry/--group/--scroll 也用于交互启动。
internal static class GalleryCapture
{
    /// <summary>Applies interactive selection, scroll and chrome switches after the window is shown.</summary>
    public static void ApplyStartup(Form form, string[] args)
    {
        if (HasFlag(args, "--capture") || form is not GalleryForm gallery)
        {
            return;
        }

        var id = ReadEntryId(gallery, args);
        var scroll = ReadInt(args, "--scroll", 0);
        var previewOnly = HasFlag(args, "--preview-only");
        if (id is null && scroll <= 0 && !previewOnly)
        {
            return;
        }

        form.Shown += (_, _) => form.BeginInvoke(() =>
        {
            if (form.IsDisposed)
            {
                return;
            }

            if (previewOnly)
            {
                gallery.SetChromeVisible(false);
            }

            if (HasFlag(args, "--toggle-theme"))
            {
                gallery.ToggleThemeForCapture();
            }

            if (id is not null)
            {
                gallery.SelectEntry(id);
            }

            if (scroll > 0)
            {
                gallery.SetScrollOffset(scroll);
            }

            gallery.Surface.InvalidateSurface();
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

        if (form is not GalleryForm gallery)
        {
            throw new InvalidOperationException("The capture mode needs a GalleryForm.");
        }

        var targets = ResolveTargets(gallery, args);
        var scroll = ReadInt(args, "--scroll", 0);
        var dump = HasFlag(args, "--dump");
        var previewOnly = HasFlag(args, "--preview-only");
        var includeGroups = HasFlag(args, "--include-groups");
        _ = includeGroups;

        PlaceOnCaptureScreen(form);
        form.TopMost = true;

        form.Shown += (_, _) => form.BeginInvoke(() =>
        {
            try
            {
                if (previewOnly)
                {
                    gallery.SetChromeVisible(false);
                }

                if (HasFlag(args, "--toggle-theme"))
                {
                    gallery.ToggleThemeForCapture();
                    Pump(form, 4);
                }

                for (var index = 0; index < targets.Count; index++)
                {
                    var id = targets[index];
                    if (!gallery.SelectEntry(id))
                    {
                        File.WriteAllText(
                            Path.Combine(directory, $"capture-error-{SafeName(id)}.txt"),
                            $"Entry '{id}' was not found.");
                        continue;
                    }

                    if (scroll > 0 && targets.Count == 1)
                    {
                        gallery.SetScrollOffset(scroll);
                    }

                    Pump(form, index == 0 ? 24 : 8);

                    var name = targets.Count == 1 ? SafeName(id) : $"{index:00}-{SafeName(id)}";
                    Capture(form, Path.Combine(directory, name + ".png"));

                    if (dump)
                    {
                        var builder = new StringBuilder();
                        builder.AppendLine($"window {form.Bounds}");
                        if (gallery.Surface.Root is { } root)
                        {
                            Dump(root, builder, 0);
                        }

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

    private static List<string> ResolveTargets(GalleryForm gallery, string[] args)
    {
        var cards = gallery.Shell.Cards;
        if (HasFlag(args, "--all"))
        {
            return cards.Select(static card => card.Id).ToList();
        }

        var group = ReadOption(args, "--group");
        if (group is not null)
        {
            return cards
                .Where(card => string.Equals(card.Group, group, StringComparison.OrdinalIgnoreCase))
                .Select(static card => card.Id)
                .ToList();
        }

        var entry = ReadOption(args, "--entry");
        if (entry is not null)
        {
            return [entry];
        }

        return cards.Count > 0 ? [cards[0].Id] : [];
    }

    private static string? ReadEntryId(GalleryForm gallery, string[] args)
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

        return gallery.Shell.Cards
            .FirstOrDefault(card => string.Equals(card.Group, group, StringComparison.OrdinalIgnoreCase))
            ?.Id;
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

    private static void PlaceOnCaptureScreen(Form form)
    {
        var screen = Screen.AllScreens.FirstOrDefault(static candidate => !candidate.Primary)
                     ?? Screen.PrimaryScreen
                     ?? Screen.AllScreens[0];
        var area = screen.WorkingArea;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(
            area.X + Math.Max(0, (area.Width - form.Width) / 2),
            area.Y + Math.Max(0, (area.Height - form.Height) / 2));
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
            bool captured;
            try
            {
                captured = PrintWindow(form.Handle, hdc, 2);
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }

            if (!captured && form is GalleryForm gallery)
            {
                graphics.Clear(gallery.Surface.BackColor);
                gallery.Surface.RenderTo(graphics, gallery.Surface.ClientSize);
            }
        }

        bitmap.Save(path, ImageFormat.Png);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    private static void Dump(VisualElement element, StringBuilder builder, int depth)
    {
        builder.Append(new string(' ', depth * 2));
        builder.Append(element.GetType().Name);
        if (!string.IsNullOrEmpty(element.Name))
        {
            builder.Append(' ').Append('#').Append(element.Name);
        }

        builder.Append(' ').Append(element.AbsoluteBounds);
        builder.Append(" vis=").Append(element.Visibility);
        if (element.Background is { } background)
        {
            builder.Append(" bg=").Append(background.ApproximateColor.ToArgb().ToString("X8"));
        }

        if (element.BorderBrush is { } border)
        {
            builder.Append(" border=").Append(border.ApproximateColor.ToArgb().ToString("X8"));
        }

        builder.AppendLine();
        foreach (var child in element.Children)
        {
            if (child is VisualElement visual)
            {
                Dump(visual, builder, depth + 1);
            }
        }
    }
}
