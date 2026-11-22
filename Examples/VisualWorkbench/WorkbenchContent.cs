using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using VisualWorkbench.Views;
using VisualBorder = VeloxDev.WinForms.Xaml.Visuals.Controls.Border;
using VisualButton = VeloxDev.WinForms.Xaml.Visuals.Controls.Button;
using VisualCanvas = VeloxDev.WinForms.Xaml.Visuals.Controls.Canvas;
using VisualGrid = VeloxDev.WinForms.Xaml.Visuals.Controls.Grid;
using VisualImage = VeloxDev.WinForms.Xaml.Visuals.Controls.Image;
using VisualNativeControlHost = VeloxDev.WinForms.Xaml.Visuals.Controls.NativeControlHost;
using VisualScrollViewer = VeloxDev.WinForms.Xaml.Visuals.Controls.ScrollViewer;
using VisualStackPanel = VeloxDev.WinForms.Xaml.Visuals.Controls.StackPanel;
using VisualTextBlock = VeloxDev.WinForms.Xaml.Visuals.Controls.TextBlock;
using VisualTextBox = VeloxDev.WinForms.Xaml.Visuals.Controls.TextBox;
using XamlColumnDefinition = VeloxDev.WinForms.Xaml.Controls.ColumnDefinition;
using XamlGridLength = VeloxDev.WinForms.Xaml.Controls.GridLength;
using XamlRowDefinition = VeloxDev.WinForms.Xaml.Controls.RowDefinition;
using XamlHorizontalAlignment = VeloxDev.WinForms.Xaml.Controls.HorizontalAlignment;
using XamlStretch = VeloxDev.WinForms.Xaml.Controls.Stretch;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;
using XamlVerticalAlignment = VeloxDev.WinForms.Xaml.Controls.VerticalAlignment;

namespace VisualWorkbench;

/// <summary>Builds the code-only visual tree shown by <see cref="WorkbenchForm"/>.</summary>
internal static class WorkbenchContent
{
    /// <summary>Creates the root border and every section below it.</summary>
    public static VisualElement CreateRoot(Action? rebuild = null)
    {
        var theme = Theme.Current;
        var page = new VisualStackPanel { Orientation = Orientation.Vertical, Spacing = 14 };
        page.Add(CreateTitleBar(theme, rebuild));
        page.Add(CreateCards(theme));
        page.Add(CreateColumns(theme));
        page.Add(CreateSection(theme, "Visual XAML · v: profile", new VisualSample()));
        page.Add(CreateStyleSection(theme));
        page.Add(CreateGraphSection(theme));

        return new VisualBorder
        {
            Background = theme.WindowBackground,
            CornerRadius = 0f,
            Padding = new XamlThickness(22f, 20f),
            Child = new VisualScrollViewer { Content = page },
        };
    }

    private static VisualBorder CreateTitleBar(ThemePalette theme, Action? rebuild)
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

        VisualElement content = stack;
        if (rebuild is not null)
        {
            var bar = new VisualGrid();
            bar.RowDefinitions.Add(new XamlRowDefinition { Height = XamlGridLength.Auto });
            bar.ColumnDefinitions.Add(new XamlColumnDefinition { Width = XamlGridLength.Star });
            bar.ColumnDefinitions.Add(new XamlColumnDefinition { Width = XamlGridLength.Auto });
            VisualGrid.SetColumn(stack, 0);
            bar.Add(stack);

            var toggle = new VisualButton
            {
                Text = Theme.Mode == ThemeMode.Dark ? "Light mode" : "Dark mode",
                Height = 32,
                HorizontalAlignment = XamlHorizontalAlignment.Right,
                VerticalAlignment = XamlVerticalAlignment.Center,
            };
            toggle.Click += (_, _) => rebuild();
            VisualGrid.SetColumn(toggle, 1);
            bar.Add(toggle);
            content = bar;
        }

        return new VisualBorder
        {
            Background = theme.Surface,
            BorderBrush = theme.ControlBorder,
            BorderThickness = new XamlThickness(1f),
            CornerRadius = 12f,
            Padding = new XamlThickness(20f, 14f),
            Child = content,
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
        counter.Bind<string>(VisualTextBlock.TextProperty, state.GetObservable<string>(CounterState.CaptionProperty));

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

    private static VisualBorder CreateGraphSection(ThemePalette theme)
    {
        var canvas = new VisualCanvas { Width = 760, Height = 220 };
        var first = CreateGraphNode(theme, "Prompt", "Workflow input", 30, 40);
        var second = CreateGraphNode(theme, "Agent", "Run the graph", 310, 120);
        VisualCanvas.SetLeft(first, 30);
        VisualCanvas.SetTop(first, 40);
        VisualCanvas.SetLeft(second, 330);
        VisualCanvas.SetTop(second, 130);

        var link = new LinkElement { Width = 170, Height = 110, PenColor = theme.Accent };
        VisualCanvas.SetLeft(link, 210);
        VisualCanvas.SetTop(link, 70);

        canvas.Add(link);
        canvas.Add(first);
        canvas.Add(second);
        return CreateSection(theme, "Graph · visual nodes and a drawn link", canvas);
    }

    private static VisualBorder CreateGraphNode(ThemePalette theme, string title, string body, float left, float top)
    {
        var stack = new VisualStackPanel { Spacing = 4 };
        stack.Add(new VisualTextBlock
        {
            Text = title,
            Font = new Font("Segoe UI Semibold", 11f),
            Foreground = theme.Accent,
        });
        stack.Add(new VisualTextBlock { Text = body, Foreground = theme.MutedForeground });

        return new VisualBorder
        {
            Width = 190,
            Height = 76,
            Background = theme.SurfaceAlt,
            BorderBrush = theme.ControlBorder,
            BorderThickness = new XamlThickness(1f),
            CornerRadius = 8f,
            Padding = new XamlThickness(12f, 10f),
            Child = stack,
        };
    }

    private sealed class LinkElement : VisualElement
    {
        public Color PenColor { get; set; } = Color.White;

        public LinkElement()
        {
            IsHitTestVisible = false;
        }

        protected override void OnRender(Graphics graphics)
        {
            using var pen = new System.Drawing.Pen(PenColor, 2f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            graphics.DrawBezier(
                pen,
                0f,
                18f,
                Bounds.Width * 0.45f,
                0f,
                Bounds.Width * 0.55f,
                Bounds.Height,
                Bounds.Width,
                Bounds.Height - 18f);
        }
    }

    private static VisualBorder CreateStyleSection(ThemePalette theme)
    {
        var buttons = new VisualStackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        buttons.Styles.Add(new VisualStyle
        {
            TargetType = typeof(VisualButton),
            Setters =
            {
                new VisualSetter(VisualElement.BackgroundProperty, new SolidColorBrush(theme.Accent)),
                new VisualSetter(VisualButton.ForegroundProperty, theme.AccentForeground),
            },
        });
        buttons.Add(new VisualButton { Text = "Styled A", Height = 32 });
        buttons.Add(new VisualButton { Text = "Styled B", Height = 32 });

        var stack = new VisualStackPanel { Orientation = Orientation.Vertical, Spacing = 8 };
        stack.Styles.Add(new VisualStyle
        {
            TargetType = typeof(VisualTextBlock),
            Setters =
            {
                new VisualSetter(VisualTextBlock.ForegroundProperty, theme.MutedForeground),
            },
        });
        stack.Add(new VisualTextBlock
        {
            Text = "Implicit styles write at the Style priority; local values still win.",
        });
        stack.Add(buttons);
        return CreateSection(theme, "Styles · implicit setters", stack);
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
    public static readonly DependencyProperty CountProperty =
        DependencyProperty.Register(
            nameof(Count), typeof(int), typeof(CounterState), new PropertyMetadata(0));

    /// <summary>The formatted caption shown by the label.</summary>
    public static readonly DependencyProperty CaptionProperty =
        DependencyProperty.Register(
            nameof(Caption), typeof(string), typeof(CounterState), new PropertyMetadata("Clicks: 0"));

    /// <summary>Gets or sets the numeric click count and refreshes the caption.</summary>
    public int Count
    {
        get => (int)GetValue(CountProperty)!;
        set
        {
            SetValue(CountProperty, value);
            Caption = $"Clicks: {value}";
        }
    }

    /// <summary>Gets or sets the formatted caption.</summary>
    public string Caption
    {
        get => (string)GetValue(CaptionProperty)!;
        set => SetValue(CaptionProperty, value);
    }
}
