using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using VisualBorder = VeloxDev.WinForms.Xaml.Visuals.Controls.Border;
using VisualButton = VeloxDev.WinForms.Xaml.Visuals.Controls.Button;
using VisualImage = VeloxDev.WinForms.Xaml.Visuals.Controls.Image;
using VisualNativeControlHost = VeloxDev.WinForms.Xaml.Visuals.Controls.NativeControlHost;
using VisualScrollViewer = VeloxDev.WinForms.Xaml.Visuals.Controls.ScrollViewer;
using VisualStackPanel = VeloxDev.WinForms.Xaml.Visuals.Controls.StackPanel;
using VisualTextBlock = VeloxDev.WinForms.Xaml.Visuals.Controls.TextBlock;
using VisualTextBox = VeloxDev.WinForms.Xaml.Visuals.Controls.TextBox;
using XamlHorizontalAlignment = VeloxDev.WinForms.Xaml.Controls.HorizontalAlignment;
using XamlStretch = VeloxDev.WinForms.Xaml.Controls.Stretch;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;

namespace VisualWorkbench;

/// <summary>Builds the code-only visual tree shown by <see cref="WorkbenchForm"/>.</summary>
internal static class WorkbenchContent
{
    /// <summary>Creates the root border and every section below it.</summary>
    public static VisualElement CreateRoot()
    {
        var theme = Theme.Current;
        var page = new VisualStackPanel { Orientation = Orientation.Vertical, Spacing = 14 };
        page.Add(CreateTitleBar(theme));
        page.Add(CreateCards(theme));
        page.Add(CreateColumns(theme));

        return new VisualBorder
        {
            Background = theme.WindowBackground,
            CornerRadius = 0f,
            Padding = new XamlThickness(22f, 20f),
            Child = page,
        };
    }

    private static VisualBorder CreateTitleBar(ThemePalette theme)
    {
        var stack = new VisualStackPanel { Orientation = Orientation.Vertical, Spacing = 3 };
        stack.Add(new VisualTextBlock
        {
            Text = "Velox Visual Workbench",
            Font = new Font("Segoe UI Semibold", 18f),
            Foreground = theme.Foreground,
        });
        stack.Add(new VisualTextBlock
        {
            Text = "One HWND · a retained visual tree · a native WinForms island",
            Foreground = theme.MutedForeground,
        });

        return new VisualBorder
        {
            Background = theme.Surface,
            BorderBrush = theme.ControlBorder,
            BorderThickness = new XamlThickness(1f),
            CornerRadius = 12f,
            Padding = new XamlThickness(20f, 14f),
            Child = stack,
        };
    }

    private static VisualStackPanel CreateCards(ThemePalette theme)
    {
        var row = new VisualStackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        row.Add(CreateCard(theme, "Single HWND", "One surface paints the whole tree."));
        row.Add(CreateCard(theme, "Logical focus", "Keys and text follow logical focus."));
        row.Add(CreateCard(theme, "Native island", "A real control floats above the tree."));
        return row;
    }

    private static VisualBorder CreateCard(ThemePalette theme, string title, string body)
    {
        var stack = new VisualStackPanel { Orientation = Orientation.Vertical, Spacing = 6 };
        stack.Add(new VisualTextBlock
        {
            Text = title,
            Font = new Font("Segoe UI Semibold", 11.5f),
            Foreground = theme.Accent,
        });
        stack.Add(new VisualTextBlock { Text = body, Foreground = theme.MutedForeground });

        return new VisualBorder
        {
            Width = 340,
            Background = theme.Surface,
            BorderBrush = theme.ControlBorder,
            BorderThickness = new XamlThickness(1f),
            CornerRadius = 12f,
            Padding = new XamlThickness(16f, 14f),
            Child = stack,
        };
    }

    private static VisualStackPanel CreateColumns(ThemePalette theme)
    {
        var left = new VisualStackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 14,
            Width = 540,
        };
        left.Add(CreateInputSection(theme));
        left.Add(CreateScrollSection(theme));

        var right = new VisualStackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 14,
            Width = 660,
        };
        right.Add(CreateCounterSection(theme));
        right.Add(CreateImageSection(theme));
        right.Add(CreateNativeSection(theme));

        var columns = new VisualStackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        columns.Add(left);
        columns.Add(right);
        return columns;
    }

    private static VisualBorder CreateInputSection(ThemePalette theme)
    {
        var name = new VisualTextBox
        {
            Placeholder = "Type a name",
            Width = 380,
            HorizontalAlignment = XamlHorizontalAlignment.Left,
        };
        var password = new VisualTextBox
        {
            Placeholder = "Password",
            IsPassword = true,
            Width = 380,
            HorizontalAlignment = XamlHorizontalAlignment.Left,
        };

        var stack = new VisualStackPanel { Orientation = Orientation.Vertical, Spacing = 8 };
        stack.Add(name);
        stack.Add(password);
        return CreateSection(theme, "Text input · click a field, then type", stack);
    }

    private static VisualBorder CreateCounterSection(ThemePalette theme)
    {
        var state = new CounterState();
        var counter = new VisualTextBlock
        {
            Font = new Font("Segoe UI Semibold", 11f),
            Foreground = theme.Foreground,
        };
        counter.Bind(VisualTextBlock.TextProperty, state.GetObservable(CounterState.CaptionProperty));

        var increment = new VisualButton { Text = "Increment" };
        increment.Click += (_, _) => state.Count++;

        var reset = new VisualButton { Text = "Reset" };
        reset.Click += (_, _) => state.Count = 0;

        var row = new VisualStackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Add(increment);
        row.Add(reset);
        row.Add(counter);
        return CreateSection(theme, "Buttons · pointer or Space when focused", row);
    }

    private static VisualBorder CreateScrollSection(ThemePalette theme)
    {
        var rows = new VisualStackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
        for (var index = 1; index <= 100; index++)
        {
            var row = new VisualBorder
            {
                Background = index % 2 == 0 ? theme.SurfaceAlt : theme.Surface,
                Padding = new XamlThickness(10f, 5f),
                CornerRadius = 6f,
                BorderThickness = XamlThickness.Empty,
            };
            row.Child = new VisualTextBlock
            {
                Text = $"Row {index:000} · the viewport clips and scrolls this content",
                Foreground = index % 10 == 0 ? theme.Accent : theme.Foreground,
            };
            rows.Add(row);
        }

        var view = new VisualScrollViewer
        {
            Height = 300,
            Background = theme.SurfaceAlt,
            BorderBrush = theme.ControlBorder,
            BorderThickness = new XamlThickness(1f),
            CornerRadius = 10f,
        };
        view.Content = rows;
        return CreateSection(theme, "Scrolling · 100 rows (wheel over the list)", view);
    }

    private static VisualBorder CreateImageSection(ThemePalette theme)
    {
        var caption = new VisualStackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
        caption.Add(new VisualTextBlock
        {
            Text = "Generated 64 × 64 bitmap",
            Foreground = theme.Foreground,
        });
        caption.Add(new VisualTextBlock
        {
            Text = "Drawn by the Image visual, no native control.",
            Foreground = theme.MutedForeground,
        });

        var row = new VisualStackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        row.Add(new VisualImage
        {
            Source = CreateBitmap(),
            Stretch = XamlStretch.None,
            HorizontalAlignment = XamlHorizontalAlignment.Left,
        });
        row.Add(caption);
        return CreateSection(theme, "Image", row);
    }

    private static VisualBorder CreateNativeSection(ThemePalette theme)
    {
        var native = new System.Windows.Forms.TextBox
        {
            Text = "Native WinForms TextBox",
            BorderStyle = BorderStyle.FixedSingle,
        };
        var host = new VisualNativeControlHost
        {
            Width = 320,
            Height = 32,
            HorizontalAlignment = XamlHorizontalAlignment.Left,
            Child = native,
        };

        var stack = new VisualStackPanel { Orientation = Orientation.Vertical, Spacing = 8 };
        stack.Add(new VisualTextBlock
        {
            Text = "A real TextBox HWND floats above the visual tree (airspace).",
            Foreground = theme.MutedForeground,
        });
        stack.Add(host);
        return CreateSection(theme, "Native control host", stack);
    }

    private static VisualBorder CreateSection(ThemePalette theme, string header, VisualElement content)
    {
        var stack = new VisualStackPanel { Orientation = Orientation.Vertical, Spacing = 10 };
        stack.Add(new VisualTextBlock
        {
            Text = header,
            Font = new Font("Segoe UI Semibold", 10.5f),
            Foreground = theme.MutedForeground,
        });
        stack.Add(content);

        return new VisualBorder
        {
            Background = theme.Surface,
            BorderBrush = theme.ControlBorder,
            BorderThickness = new XamlThickness(1f),
            CornerRadius = 12f,
            Padding = new XamlThickness(16f),
            Child = stack,
        };
    }

    private static Bitmap CreateBitmap()
    {
        var bitmap = new Bitmap(64, 64);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var background = new System.Drawing.Drawing2D.LinearGradientBrush(
                   new Rectangle(0, 0, 64, 64),
                   Color.FromArgb(0xFF, 0x5B, 0x8C, 0xFF),
                   Color.FromArgb(0xFF, 0xA8, 0x4B, 0xF0),
                   45f))
        {
            graphics.FillRectangle(background, 0, 0, 64, 64);
        }

        using var pen = new System.Drawing.Pen(Color.White, 3f);
        graphics.DrawEllipse(pen, 16f, 16f, 32f, 32f);
        return bitmap;
    }
}

/// <summary>A non-visual view model for the counter card; the label binds to its caption property.</summary>
internal sealed class CounterState : WinformsObject
{
    /// <summary>The numeric click count.</summary>
    public static readonly WinformsProperty<int> CountProperty =
        WinformsProperty.Register<CounterState, int>(nameof(Count), 0);

    /// <summary>The formatted caption shown by the label.</summary>
    public static readonly WinformsProperty<string> CaptionProperty =
        WinformsProperty.Register<CounterState, string>(nameof(Caption), "Clicks: 0");

    /// <summary>Gets or sets the numeric click count and refreshes the caption.</summary>
    public int Count
    {
        get => GetValue(CountProperty);
        set
        {
            SetValue(CountProperty, value);
            Caption = $"Clicks: {value}";
        }
    }

    /// <summary>Gets or sets the formatted caption.</summary>
    public string Caption
    {
        get => GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }
}
