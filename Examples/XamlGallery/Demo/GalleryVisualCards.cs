using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using XamlBinding = VeloxDev.WinForms.Xaml.Binding;
using XamlBrush = VeloxDev.WinForms.Xaml.Brush;
using VisualPopup = VeloxDev.WinForms.Xaml.Visuals.Controls.VisualPopup;
using Demo.Views;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;
using VS = VeloxDev.WinForms.Xaml.Visuals.Controls.Shapes;
using XamlHorizontalAlignment = VeloxDev.WinForms.Xaml.Controls.HorizontalAlignment;
using XamlStretch = VeloxDev.WinForms.Xaml.Controls.Stretch;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;
using XamlVerticalAlignment = VeloxDev.WinForms.Xaml.Controls.VerticalAlignment;

namespace Demo;

/// <summary>One gallery card: metadata, its usage snippet and the visual factory that builds it.</summary>
internal sealed record GalleryCard(
    string Id,
    string Group,
    string Title,
    string Summary,
    string Usage,
    Func<GalleryModel, VisualElement> Create);

/// <summary>Builds every visual-backend card shown by the gallery.</summary>
internal static class GalleryVisualCards
{
    /// <summary>Creates the card list in catalog order.</summary>
    /// <param name="model">The shared data context.</param>
    /// <param name="rebuild">Rebuilds the whole gallery; used by the theme card.</param>
    public static IReadOnlyList<GalleryCard> Create(GalleryModel model, Action? rebuild)
    {
        var cards = new List<GalleryCard>();
        cards.AddRange(LayoutCards());
        cards.AddRange(TextCards());
        cards.AddRange(DataCards(model));
        cards.AddRange(InputCards());
        cards.AddRange(GraphicsCards());
        cards.AddRange(OverlayCards());
        cards.AddRange(StylingCards());
        cards.AddRange(BindingCards(model));
        cards.AddRange(ThemeCards(rebuild));
        cards.AddRange(VisualsCards());
        return cards;
    }

    private static List<GalleryCard> LayoutCards() => new()
    {
        Card("layout.stack-panel", "Layout", "StackPanel", "Vertical and horizontal stacks with an explicit gap.",
            "new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 }",
            _ => Row(10,
                Tile("One", Theme.Current.Accent),
                Tile("Two", Theme.Current.FocusRing),
                Tile("Three", Theme.Current.FocusRing))),
        Card("layout.dock-panel", "Layout", "DockPanel", "Children dock to the four edges; the last child fills.",
            "DockPanel.SetDock(child, DockPlacement.Left)",
            _ =>
            {
                var dock = new VC.DockPanel { Width = 420, Height = 160 };
                var top = Tile("Top", Theme.Current.Accent); VC.DockPanel.SetDock(top, VeloxDev.WinForms.Xaml.Controls.DockPlacement.Top);
                var left = Tile("Left", Theme.Current.FocusRing); VC.DockPanel.SetDock(left, VeloxDev.WinForms.Xaml.Controls.DockPlacement.Left);
                var fill = new VC.Border { Background = Theme.Current.SurfaceAlt, Child = Label("Fill", Theme.Current.Foreground) };
                dock.Add(top); dock.Add(left); dock.Add(fill);
                return dock;
            }),
        Card("layout.grid", "Layout", "Grid", "Pixel, auto and star tracks with attached row/column placement.",
            "Grid.SetRow(child, 1); Grid.SetColumn(child, 0)",
            _ =>
            {
                var grid = new VC.Grid { Width = 420, Height = 180 };
                grid.RowDefinitions.Add(new VeloxDev.WinForms.Xaml.Controls.RowDefinition { Height = VeloxDev.WinForms.Xaml.Controls.GridLength.Star });
                grid.RowDefinitions.Add(new VeloxDev.WinForms.Xaml.Controls.RowDefinition { Height = VeloxDev.WinForms.Xaml.Controls.GridLength.Auto });
                grid.ColumnDefinitions.Add(new VeloxDev.WinForms.Xaml.Controls.ColumnDefinition { Width = VeloxDev.WinForms.Xaml.Controls.GridLength.Star });
                grid.ColumnDefinitions.Add(new VeloxDev.WinForms.Xaml.Controls.ColumnDefinition { Width = VeloxDev.WinForms.Xaml.Controls.GridLength.Star });
                var first = Tile("0,0", Theme.Current.Accent); VC.Grid.SetColumn(first, 1);
                var second = Tile("1,0", Theme.Current.FocusRing); VC.Grid.SetRow(second, 1);
                var third = Tile("1,1", Theme.Current.FocusRing); VC.Grid.SetRow(third, 1); VC.Grid.SetColumn(third, 1);
                grid.Add(first); grid.Add(second); grid.Add(third);
                return grid;
            }),
        Card("layout.wrap-panel", "Layout", "WrapPanel", "Wraps items into lines within the available width.",
            "new WrapPanel { ItemWidth = 70, ItemHeight = 32, Spacing = 6 }",
            _ =>
            {
                var wrap = new VC.WrapPanel { Width = 300, ItemWidth = 70, ItemHeight = 32, Spacing = 6 };
                for (var index = 0; index < 8; index++)
                {
                    var tile = Tile($"{index + 1}", index % 2 == 0 ? Theme.Current.Accent : Theme.Current.FocusRing);
                    tile.Width = 70; tile.Height = 32;
                    wrap.Add(tile);
                }

                return wrap;
            }),
        Card("layout.canvas", "Layout", "Canvas", "Absolute placement with attached left/top.",
            "Canvas.SetLeft(child, 40); Canvas.SetTop(child, 25)",
            _ =>
            {
                var canvas = new VC.Canvas { Width = 420, Height = 170, Background = Theme.Current.SurfaceAlt };
                var first = Tile("Left/Top", Theme.Current.Accent); VC.Canvas.SetLeft(first, 30); VC.Canvas.SetTop(first, 30);
                var second = Tile("Right/Bottom", Theme.Current.FocusRing); VC.Canvas.SetLeft(second, 190); VC.Canvas.SetTop(second, 90);
                canvas.Add(first); canvas.Add(second);
                return canvas;
            }),
        Card("layout.uniform-grid", "Layout", "UniformGrid", "Equal cells; the column count wraps the rows.",
            "new UniformGrid { Columns = 3 }",
            _ =>
            {
                var grid = new VC.UniformGrid { Columns = 3, Width = 420, Height = 150 };
                for (var index = 0; index < 6; index++)
                {
                    grid.Add(Tile($"Cell {index + 1}", index % 2 == 0 ? Theme.Current.Accent : Theme.Current.FocusRing));
                }

                return grid;
            }),
        Card("layout.viewbox", "Layout", "Viewbox", "Scales one child into the box with a visual transform.",
            "new Viewbox { Width = 260, Height = 120, Child = content }",
            _ =>
            {
                var content = new VC.Border
                {
                    Width = 400,
                    Height = 200,
                    Background = Theme.Current.SurfaceAlt,
                    BorderBrush = Theme.Current.Accent,
                    BorderThickness = new XamlThickness(2f),
                    CornerRadius = 12f,
                    Padding = new XamlThickness(16f),
                    Child = Label("Design at 400×200", Theme.Current.Foreground, 16f),
                };
                return new VC.Viewbox { Width = 260, Height = 120, Child = content };
            }),
        Card("layout.splitter", "Layout", "GridSplitter", "Drag the sash to resize the adjacent pane.",
            "new GridSplitter { Target = leftPane, Orientation = Orientation.Vertical }",
            _ =>
            {
                var pane = new VC.Border
                {
                    Width = 150,
                    Background = Theme.Current.SurfaceAlt,
                    BorderBrush = Theme.Current.ControlBorder,
                    BorderThickness = new XamlThickness(1f),
                    CornerRadius = 6f,
                    Child = Label("Resize me", Theme.Current.Foreground),
                };
                var splitter = new VC.GridSplitter { Target = pane, Width = 6, Height = 120, Orientation = Orientation.Vertical };
                var row = Row(6, pane, splitter,
                    new VC.Border { Width = 220, Height = 120, Background = Theme.Current.Surface, Child = Label("Fill pane", Theme.Current.MutedForeground) });
                return row;
            }),
        Card("layout.group-box", "Layout", "GroupBox", "A titled frame with one child.",
            "new GroupBox { Header = \"Settings\", Child = content }",
            _ => new VC.GroupBox
            {
                Width = 320,
                Header = "Settings",
                Child = new VC.StackPanel
                {
                    Spacing = 6,
                    Children = { Label("Content sits inside the frame.", Theme.Current.Foreground) },
                },
            }),
        Card("layout.expander", "Layout", "Expander", "A header toggles one content element.",
            "new Expander { Header = \"Advanced\", Content = content }",
            _ => new VC.Expander
            {
                Width = 340,
                IsExpanded = true,
                Header = "Advanced options",
                Content = new VC.TextBlock
                {
                    Text = "The content is measured only while the expander is open.",
                    Foreground = Theme.Current.MutedForeground,
                    Wrap = true,
                },
            }),
        Card("layout.tab-control", "Layout", "TabControl", "One visible page at a time, switched by pointer or arrows.",
            "new TabControl { Items = { page1, page2 } }",
            _ =>
            {
                var tabs = new VC.TabControl { Width = 420, Height = 170 };
                tabs.Items.Add(new VC.TabItem
                {
                    Header = "Layout",
                    Content = new VC.TextBlock { Text = "Layout page", Foreground = Theme.Current.Foreground },
                });
                tabs.Items.Add(new VC.TabItem
                {
                    Header = "Data",
                    Content = new VC.ListBox { ItemsSource = new[] { "One", "Two", "Three" }, Height = 110 },
                });
                tabs.SelectedIndex = 0;
                tabs.Refresh();
                return tabs;
            }),
    };

    private static List<GalleryCard> TextCards() => new()
    {
        Card("text.text-block", "Text", "TextBlock", "Single-line and wrapped text with theme colours.",
            "new TextBlock { Text = \"Hello\", Wrap = true }",
            _ =>
            {
                var stack = new VC.StackPanel { Spacing = 8 };
                stack.Add(Label("Heading", Theme.Current.Accent, 18f));
                stack.Add(Label("Body text uses the theme foreground colour.", Theme.Current.Foreground));
                stack.Add(new VC.TextBlock
                {
                    Text = "A wrapped paragraph keeps flowing inside the available width, and the desired height includes every line.",
                    Foreground = Theme.Current.MutedForeground,
                    Wrap = true,
                    Width = 420,
                });
                return stack;
            }),
        Card("text.text-box", "Text", "TextBox", "Self-drawn editing with placeholder and selection.",
            "new TextBox { Placeholder = \"Type here\" }",
            _ => new VC.TextBox { Width = 320, Placeholder = "Type here" }),
        Card("text.password-box", "Text", "PasswordBox", "The same editor with the password glyphs.",
            "new TextBox { IsPassword = true }",
            _ => new VC.TextBox { Width = 320, IsPassword = true, Placeholder = "Password" }),
        Card("text.multiline", "Text", "Multiline TextBox", "Return inserts a line; wrapping and wheel scrolling work.",
            "new TextBox { AcceptsReturn = true, Wrap = true }",
            _ => new VC.TextBox
            {
                Width = 420,
                Height = 130,
                AcceptsReturn = true,
                Wrap = true,
                Text = "First line\nSecond line\nThird line with enough text to wrap inside the fixed width.",
            }),
        Card("text.image", "Text", "Image", "A bitmap drawn inside the visual tree.",
            "new Image { Source = bitmap, Stretch = Stretch.Uniform }",
            _ => new VC.Image { Source = CreateBitmap(), Width = 220, Height = 120, Stretch = XamlStretch.Uniform }),
        Card("text.rich-text-box", "Text", "RichTextBox", "Styled runs, selection, clipboard, undo and keyboard formatting.",
            "rich.Select(0, 4); rich.ToggleBold()",
            _ =>
            {
                var rich = new VC.RichTextBox { Width = 440, Height = 170 };
                rich.SetPlainText("VeloxDev RichTextBox\nSelect text, then press Ctrl+B / Ctrl+I / Ctrl+U.\nCtrl+Z and Ctrl+Y walk the edit history.");
                rich.Select(0, 8);
                rich.ToggleBold();
                rich.Select(0, 0);
                return rich;
            }),
    };

    private static List<GalleryCard> DataCards(GalleryModel model) => new()
    {
        Card("data.list-box", "Data", "ListBox", "Rows from a collection; selection is a dependency property.",
            "new ListBox { ItemsSource = rows, DisplayMemberPath = \"Name\" }",
            _ =>
            {
                var list = new VC.ListBox
                {
                    Width = 320,
                    Height = 200,
                    ItemsSource = model.Rows,
                    DisplayMemberPath = "Name",
                    SelectedIndex = 0,
                };
                return list;
            }),
        Card("data.combo-box", "Data", "ComboBox", "A drop-down that opens in the surface overlay.",
            "new ComboBox { Items = { \"Alpha\", \"Beta\" } }",
            _ =>
            {
                var combo = new VC.ComboBox { Width = 240, SelectedIndex = 0 };
                combo.Items.Add("Balanced");
                combo.Items.Add("Throughput");
                combo.Items.Add("Latency");
                combo.Items.Add("Custom");
                return combo;
            }),
        Card("data.virtual-list", "Data", "VirtualList", "Fixed-row virtualization driven by a ScrollViewer.",
            "new VirtualList { ItemsSource = rows, ItemHeight = 26 }",
            _ => new VC.ScrollViewer
            {
                Width = 360,
                Height = 220,
                Content = new VC.VirtualList
                {
                    ItemsSource = model.VirtualRows,
                    ItemHeight = 26,
                    DisplayMemberPath = "Name",
                },
            }),
        Card("data.tree-view", "Data", "TreeView", "A hierarchical catalog with expansion and selection.",
            "new TreeView { Items = { group } }",
            _ =>
            {
                var tree = new VC.TreeView { Width = 320, Height = 220 };
                foreach (var node in model.TreeNodes)
                {
                    tree.Items.Add(BuildTreeItem(node));
                }

                tree.SelectedItem = tree.Items[0];
                return tree;
            }),
        Card("data.calendar", "Data", "Calendar", "A month grid drawn by GDI+.",
            "new Calendar { SelectedDate = DateTime.Today }",
            _ => new VC.Calendar { Width = 250, Height = 226, SelectedDate = DateTime.Today }),
        Card("data.date-picker", "Data", "DatePicker", "A date field whose calendar opens in the overlay.",
            "new DatePicker { SelectedDate = DateTime.Today }",
            _ => new VC.DatePicker { Width = 220, SelectedDate = DateTime.Today }),
    };

    private static List<GalleryCard> InputCards() => new()
    {
        Card("input.buttons", "Input", "Button and RepeatButton", "Click by pointer, Space or Enter; the repeat button fires while held.",
            "new Button { Text = \"Run\" }",
            _ =>
            {
                var status = Label("No clicks yet", Theme.Current.MutedForeground);
                var clicks = 0;
                var button = new VC.Button { Text = "Run", Height = 32 };
                button.Click += (_, _) => status.Text = $"Clicks: {++clicks}";
                var repeat = new VC.RepeatButton { Text = "Repeat", Height = 32 };
                repeat.Click += (_, _) => status.Text = $"Repeat ticks: {++clicks}";
                var stack = new VC.StackPanel { Spacing = 10 };
                stack.Add(Row(10, button, repeat));
                stack.Add(status);
                return stack;
            }),
        Card("input.toggles", "Input", "Toggles", "ToggleButton, CheckBox and RadioButton share the checked pipe.",
            "new CheckBox { IsChecked = true }",
            _ =>
            {
                var toggle = new VC.ToggleButton { IsChecked = true };
                var check = new VC.CheckBox { IsChecked = true };
                var radio = new VC.RadioButton { GroupName = "demo", IsChecked = true };
                var row = Row(16, toggle, check, radio);
                var status = Label("Toggle · Check · Radio", Theme.Current.MutedForeground);
                var stack = new VC.StackPanel { Spacing = 10 };
                stack.Add(row);
                stack.Add(status);
                return stack;
            }),
        Card("input.slider", "Input", "Slider", "Pointer drag and arrow keys change the bounded value.",
            "new Slider { Minimum = 0, Maximum = 100 }",
            _ =>
            {
                var slider = new VC.Slider { Width = 280, Minimum = 0, Maximum = 100, Value = 40 };
                var readout = new VC.TextBlock { Text = "40", Foreground = Theme.Current.Accent, Width = 48 };
                readout.SetBinding(VC.TextBlock.TextProperty, new XamlBinding("Value") { Source = slider, StringFormat = "F0" });
                var row = Row(12, slider, readout);
                return row;
            }),
        Card("input.native-host", "Input", "NativeControlHost", "The only native path: a real HWND floats above the visual tree.", "new NativeControlHost { Child = new Button() }",
            _ =>
            {
                var host = new VC.NativeControlHost { Width = 190f, Height = 32f };
                host.Child = new System.Windows.Forms.Button { Text = "Native WinForms button" };
                var stack = new VC.StackPanel { Spacing = 8 };
                stack.Add(Label("WinForms controls are not bridged; the host keeps one rectangle above the visual tree.", Theme.Current.MutedForeground, wrap: true).WithWidth(360));
                stack.Add(host);
                return stack;
            }),
        Card("input.progress", "Input", "ProgressBar", "A bounded range painted as a track and fill.",
            "new ProgressBar { Value = 65 }",
            _ =>
            {
                var slider = new VC.Slider { Width = 240, Maximum = 100, Value = 65 };
                var bar = new VC.ProgressBar { Width = 240, Height = 10, Maximum = 100 };
                bar.SetBinding(VC.ProgressBar.ValueProperty, new XamlBinding("Value") { Source = slider });
                var stack = new VC.StackPanel { Spacing = 12 };
                stack.Add(bar);
                stack.Add(slider);
                stack.Add(Label("The bar follows the slider through a one-way visual binding.", Theme.Current.MutedForeground));
                return stack;
            }),
    };

    private static List<GalleryCard> GraphicsCards() => new()
    {
        Card("graphics.shapes", "Graphics", "Shapes", "Rectangle, ellipse, line, polygon, polyline and path.",
            "new Rectangle { Fill = brush, Stroke = brush }",
            _ =>
            {
                var row = Row(14);
                row.Add(new VS.Rectangle
                {
                    Width = 60,
                    Height = 60,
                    CornerRadius = 10f,
                    Fill = Theme.Current.Accent,
                });
                row.Add(new VS.Ellipse { Width = 60, Height = 60, Fill = Theme.Current.Selection });
                row.Add(new VS.Polygon
                {
                    Width = 60,
                    Height = 60,
                    Points = [new PointF(0, 60), new PointF(30, 0), new PointF(60, 60)],
                    Fill = Theme.Current.FocusRing,
                });
                row.Add(new VS.Path
                {
                    Width = 60,
                    Height = 60,
                    Data = "M8,52 A28,28 0 0 1 52,52 M8,52 L8,20 M52,52 L52,20",
                    Stroke = Theme.Current.Foreground,
                    StrokeThickness = 2f,
                });
                row.Add(new VS.Line
                {
                    Width = 60,
                    Height = 60,
                    X1 = 4f,
                    Y1 = 54f,
                    X2 = 56f,
                    Y2 = 6f,
                    Stroke = Theme.Current.MutedForeground,
                    StrokeThickness = 2f,
                });
                return row;
            }),
        Card("graphics.presenter", "Graphics", "VisualPresenter", "Hosts a lightweight Visual tree inside the control tree.",
            "new VisualPresenter { Content = containerVisual }",
            _ =>
            {
                var container = new ContainerVisual();
                container.Add(new ShapeVisual
                {
                    Bounds = new RectangleF(8f, 8f, 78f, 78f),
                    Geometry = new EllipseGeometry(),
                    Fill = new SolidColorBrush(Theme.Current.Accent),
                    Opacity = 0.85d,
                });
                container.Add(new ShapeVisual
                {
                    Bounds = new RectangleF(58f, 8f, 78f, 78f),
                    Geometry = new RectangleGeometry(),
                    Fill = new SolidColorBrush(Theme.Current.Selection),
                    Opacity = 0.6d,
                });
                container.Add(new TextVisual
                {
                    Bounds = new RectangleF(0f, 96f, 220f, 24f),
                    Text = "ContainerVisual · ShapeVisual · TextVisual",
                    Foreground = new SolidColorBrush(Theme.Current.Foreground),
                });
                return new VC.VisualPresenter { Width = 240, Height = 140, Content = container };
            }),
        Card("graphics.opacity", "Graphics", "Opacity and clip", "A visual subtree is composited through one bitmap.",
            "new Border { Opacity = ... }",
            _ =>
            {
                var stack = new VC.StackPanel { Spacing = 8 };
                stack.Add(Label("The border below uses Visual.Opacity; the child stays inside the clip.", Theme.Current.MutedForeground, wrap: true).WithWidth(360));
                var border = new VC.Border
                {
                    Width = 240,
                    Height = 80,
                    Background = Theme.Current.Accent,
                    CornerRadius = 8f,
                    Child = Label("50% opacity", Theme.Current.AccentForeground),
                };
                border.Opacity = 0.5d;
                border.Clip = new RectangleF(0f, 0f, 180f, 80f);
                stack.Add(border);
                return stack;
            }),
    };

    private static List<GalleryCard> OverlayCards() => new()
    {
        Card("overlay.popup", "Overlay", "VisualPopup", "A child hosted in the surface overlay, above the root tree.",
            "new VisualPopup { PlacementTarget = button, Child = content }.Open()",
            _ =>
            {
                var status = Label("Popup closed", Theme.Current.MutedForeground);
                var button = new VC.Button { Text = "Show popup", Height = 32 };
                VisualPopup? popup = null;
                button.Click += (_, _) =>
                {
                    if (popup is { IsOpen: true })
                    {
                        popup.Close();
                        return;
                    }

                    popup?.Dispose();
                    popup = new VC.VisualPopup
                    {
                        PlacementTarget = button,
                        Placement = VC.VisualPlacement.Bottom,
                        Child = new VC.Border
                        {
                            Width = 240,
                            Padding = new XamlThickness(14f),
                            Background = Theme.Current.SurfaceAlt,
                            BorderBrush = Theme.Current.ControlBorder,
                            BorderThickness = new XamlThickness(1f),
                            CornerRadius = 8f,
                            Child = new VC.StackPanel
                            {
                                Spacing = 6,
                                Children =
                                {
                                    Label("Overlay popup", Theme.Current.Accent, 12f),
                                    new VC.TextBlock
                                    {
                                        Text = "The popup escapes ancestor clips and hit-tests before the root tree.",
                                        Foreground = Theme.Current.Foreground,
                                        Wrap = true,
                                    },
                                },
                            },
                        },
                    };
                    popup.Opened += (_, _) => status.Text = "Popup open";
                    popup.Closed += (_, _) => status.Text = "Popup closed";
                    popup.IsOpen = true;
                };
                var stack = new VC.StackPanel { Spacing = 10 };
                stack.Add(button);
                stack.Add(status);
                return stack;
            }),
        Card("overlay.menu", "Overlay", "VisualMenu", "A menu bar with drop-down items and shortcut text.",
            "new VisualMenu { Items = { fileMenu, editMenu } }",
            _ =>
            {
                var status = Label("Choose a command", Theme.Current.MutedForeground);
                var menu = new VC.VisualMenu();
                var file = new VC.VisualMenuItem { Header = "File" };
                file.Items.Add(MenuItem("New", "Ctrl+N", () => status.Text = "File · New"));
                file.Items.Add(MenuItem("Open…", "Ctrl+O", () => status.Text = "File · Open"));
                file.Items.Add(new VC.VisualMenuItem { IsSeparator = true });
                file.Items.Add(MenuItem("Exit", "Alt+F4", () => status.Text = "File · Exit"));
                var edit = new VC.VisualMenuItem { Header = "Edit" };
                edit.Items.Add(MenuItem("Undo", "Ctrl+Z", () => status.Text = "Edit · Undo"));
                edit.Items.Add(MenuItem("Redo", "Ctrl+Y", () => status.Text = "Edit · Redo"));
                var view = new VC.VisualMenuItem { Header = "View" };
                var checkedItem = MenuItem("Show grid", null, () => status.Text = "View · Show grid");
                checkedItem.IsChecked = true;
                view.Items.Add(checkedItem);
                menu.Items.Add(file);
                menu.Items.Add(edit);
                menu.Items.Add(view);
                var stack = new VC.StackPanel { Spacing = 10 };
                stack.Add(menu);
                stack.Add(status);
                return stack;
            }),
        Card("overlay.context-menu", "Overlay", "Context menu", "A detached menu opened at a surface point.",
            "menu.OpenAt(target, point)",
            _ =>
            {
                var status = Label("Context menu closed", Theme.Current.MutedForeground);
                var button = new VC.Button { Text = "Open context menu", Height = 32 };
                var menu = new VC.VisualContextMenu();
                menu.Items.Add(MenuItem("Duplicate", "Ctrl+D", () => status.Text = "Duplicate"));
                menu.Items.Add(MenuItem("Rename", "F2", () => status.Text = "Rename"));
                menu.Items.Add(new VC.VisualMenuItem { IsSeparator = true });
                menu.Items.Add(MenuItem("Delete", "Del", () => status.Text = "Delete"));
                button.Click += (_, _) =>
                    menu.OpenAt(button, new PointF(button.AbsoluteBounds.X + 12f, button.AbsoluteBounds.Bottom));
                var stack = new VC.StackPanel { Spacing = 10 };
                stack.Add(button);
                stack.Add(status);
                return stack;
            }),
    };

    private static List<GalleryCard> StylingCards() => new()
    {
        Card("style.implicit", "Styling", "Implicit style", "A TargetType style applies to every matching descendant.",
            "panel.Styles.Add(new VisualStyle { TargetType = typeof(Button), ... })",
            _ =>
            {
                var panel = new VC.StackPanel { Spacing = 10, Orientation = Orientation.Horizontal };
                panel.Styles.Add(new VisualStyle
                {
                    TargetType = typeof(VC.Button),
                    Setters =
                    {
                        new VisualSetter(VisualElement.BackgroundProperty, Theme.Current.Accent),
                        new VisualSetter(VC.Button.ForegroundProperty, Theme.Current.AccentForeground),
                    },
                });
                panel.Add(new VC.Button { Text = "Styled A", Height = 32 });
                panel.Add(new VC.Button { Text = "Styled B", Height = 32 });
                return panel;
            }),
        Card("style.explicit", "Styling", "Explicit style", "An assigned style outranks the implicit one until cleared.",
            "button.Style = style",
            _ =>
            {
                var style = new VisualStyle { TargetType = typeof(VC.Button) };
                style.Setters.Add(new VisualSetter(VisualElement.BackgroundProperty, Theme.Current.Selection));
                style.Setters.Add(new VisualSetter(VC.Button.ForegroundProperty, Theme.Current.Foreground));
                var button = new VC.Button { Text = "Explicit style", Height = 32, Style = style };
                return button;
            }),
        Card("style.trigger", "Styling", "Trigger", "A property equality activates setters at the trigger layer.",
            "new VisualTrigger(IsEnabledProperty, false) { Setters = { ... } }",
            _ =>
            {
                var button = new VC.Button { Text = "Trigger moves with IsEnabled", Height = 32 };
                var style = new VisualStyle { TargetType = typeof(VC.Button) };
                style.Setters.Add(new VisualSetter(VC.Button.ForegroundProperty, Theme.Current.AccentForeground));
                var trigger = new VisualTrigger(VisualElement.IsEnabledProperty, false);
                trigger.Setters.Add(new VisualSetter(VisualElement.BackgroundProperty, Theme.Current.DisabledBackground));
                style.Triggers.Add(trigger);
                button.Style = style;
                var check = new VC.CheckBox { IsChecked = true };
                check.PropertyChanged += (_, e) =>
                {
                    if (e.Property == VC.CheckBox.IsCheckedProperty)
                    {
                        button.IsEnabled = check.IsChecked == true;
                    }
                };
                var stack = new VC.StackPanel { Spacing = 10 };
                stack.Add(button);
                stack.Add(check);
                return stack;
            }),
        Card("style.template", "Styling", "ControlTemplate", "A visual template with a content presenter and a named part.",
            "new VisualControlTemplate(scope => ...) { TargetType = typeof(ContentControl) }",
            _ =>
            {
                var host = new VC.ContentControl
                {
                    Width = 380,
                    Content = new VC.TextBlock
                    {
                        Text = "The content is shown by the template's ContentPresenter.",
                        Foreground = Theme.Current.Foreground,
                        Wrap = true,
                    },
                };
                var template = new VisualControlTemplate(scope =>
                {
                    var stack = new VC.StackPanel { Spacing = 6 };
                    stack.Add(Label("ControlTemplate · PART_Frame", Theme.Current.Accent, 10.5f));
                    stack.Add(new VC.ContentPresenter());
                    var frame = new VC.Border
                    {
                        Background = Theme.Current.SurfaceAlt,
                        BorderBrush = Theme.Current.ControlBorder,
                        BorderThickness = new XamlThickness(1f),
                        CornerRadius = 8f,
                        Padding = new XamlThickness(12f),
                        Child = stack,
                    };
                    scope.Register("PART_Frame", frame);
                    return frame;
                })
                {
                    TargetType = typeof(VC.ContentControl),
                };
                host.Template = template;
                return host;
            }),
        Card("style.resources", "Styling", "Resources", "Named resources resolve through the ancestor chain.",
            "VisualResourceLookup.Get<Brush>(element, \"Accent\")",
            _ =>
            {
                var root = new VC.StackPanel { Spacing = 10 };
                root.Resources["Accent"] = Theme.Current.Accent;
                var border = new VC.Border
                {
                    Width = 220,
                    Height = 70,
                    CornerRadius = 8f,
                };
                root.Add(border);
                border.Background = VisualResourceLookup.Get<XamlBrush>(border, "Accent");
                root.Add(Label("The brush came from the parent's resource dictionary.", Theme.Current.MutedForeground, wrap: true));
                return root;
            }),
    };

    private static List<GalleryCard> BindingCards(GalleryModel model) => new()
    {
        Card("binding.one-way", "Binding", "One-way binding", "A path binding follows the source property.",
            "text.SetBinding(TextBlock.TextProperty, new XamlBinding(\"Amount\"))",
            _ =>
            {
                var text = new VC.TextBlock { Foreground = Theme.Current.Foreground };
                text.DataContext = model;
                text.SetBinding(VC.TextBlock.TextProperty, new XamlBinding("Amount") { StringFormat = "N2" });
                var stack = new VC.StackPanel { Spacing = 6 };
                stack.Add(Label("model.Amount", Theme.Current.MutedForeground));
                stack.Add(text);
                return stack;
            }),
        Card("binding.two-way", "Binding", "Two-way binding", "The editor writes the value back to the source.",
            "new XamlBinding(\"OptionalName\") { Mode = BindingMode.TwoWay }",
            _ =>
            {
                var editor = new VC.TextBox { Width = 320 };
                editor.DataContext = model;
                editor.SetBinding(VC.TextBox.TextProperty, new XamlBinding("OptionalName") { Mode = BindingMode.TwoWay });
                var mirror = new VC.TextBlock { Foreground = Theme.Current.MutedForeground };
                mirror.DataContext = model;
                mirror.SetBinding(VC.TextBlock.TextProperty, new XamlBinding("OptionalName") { StringFormat = "Source: {0}" });
                var stack = new VC.StackPanel { Spacing = 8 };
                stack.Add(editor);
                stack.Add(mirror);
                return stack;
            }),
        Card("binding.data-context", "Binding", "DataContext", "Descendants inherit one data context.",
            "panel.DataContext = model",
            _ =>
            {
                var panel = new VC.StackPanel { Spacing = 6, DataContext = model };
                var title = new VC.TextBlock { Foreground = Theme.Current.Foreground };
                title.SetBinding(VC.TextBlock.TextProperty, new XamlBinding("SaveCount") { StringFormat = "SaveCount: {0}" });
                panel.Add(title);
                var button = new VC.Button { Text = "Run SaveCommand", Height = 30 };
                button.Click += (_, _) => model.SaveCommand.Execute(null);
                panel.Add(button);
                return panel;
            }),
        Card("binding.converter", "Binding", "Converter", "A value converter formats a number for display.",
            "new XamlBinding(\"Amount\") { Converter = converter }",
            _ =>
            {
                var text = new VC.TextBlock { Foreground = Theme.Current.Accent, Font = new Font("Segoe UI Semibold", 16f) };
                text.DataContext = model;
                text.SetBinding(VC.TextBlock.TextProperty, new XamlBinding("Amount") { Converter = new MoneyConverter() });
                return text;
            }),
    };

    private static List<GalleryCard> ThemeCards(Action? rebuild) => new()
    {
        Card("theme.palette", "Theme", "Palette", "The VS Code Dark+/Light+ tokens used by the visual backend.",
            "Theme.Current.Accent",
            _ =>
            {
                var theme = Theme.Current;
                var stack = new VC.StackPanel { Spacing = 8, Orientation = Orientation.Horizontal };
                stack.Add(Swatch("Window", theme.WindowBackground));
                stack.Add(Swatch("Surface", theme.Surface));
                stack.Add(Swatch("Raised", theme.SurfaceAlt));
                stack.Add(Swatch("Accent", theme.Accent));
                stack.Add(Swatch("Selection", theme.Selection));
                stack.Add(Swatch("Focus", theme.FocusRing));
                stack.Add(Swatch("Border", theme.ControlBorder));
                stack.Add(Swatch("Text", theme.Foreground));
                return stack;
            }),
        Card("theme.typography", "Theme", "Typography", "Font sizes and weights from the visual text pipeline.",
            "new Font(\"Segoe UI Semibold\", 16f)",
            _ =>
            {
                var stack = new VC.StackPanel { Spacing = 8 };
                stack.Add(Label("Segoe UI Semibold 18", Theme.Current.Foreground, 18f));
                stack.Add(Label("Segoe UI 14", Theme.Current.Foreground, 14f));
                stack.Add(Label("Segoe UI 11.5", Theme.Current.MutedForeground, 11.5f));
                stack.Add(Label("Segoe UI 9.5 · default", Theme.Current.MutedForeground));
                return stack;
            }),
        Card("theme.mode", "Theme", "Theme mode", "Dark+ and Light+ palettes; rebuilding the root applies the mode.",
            "Theme.Mode = ThemeMode.Light",
            _ =>
            {
                var stack = new VC.StackPanel { Spacing = 10 };
                stack.Add(Label($"Current mode: {Theme.Mode}", Theme.Current.Foreground));
                var button = new VC.Button { Text = "Switch palette", Height = 32 };
                if (rebuild is not null)
                {
                    button.Click += (_, _) => rebuild();
                }

                stack.Add(button);
                stack.Add(Label("The gallery rebuilds its visual root so every control reads the new Theme.Current values.", Theme.Current.MutedForeground, wrap: true).WithWidth(360));
                return stack;
            }),
    };

    private static List<GalleryCard> VisualsCards() => new()
    {
        Card("visuals.visual-xaml", "Visuals", "Visual XAML", "A generated v: profile with a click handler.",
            "x:Class=\"Demo.Views.VisualSample\"",
            _ => new VisualSample()),
        Card("visuals.workflow-graph", "Visuals", "Workflow graph", "A real WorkflowTree model on the visual backend.",
            "new WorkflowGraphSample()",
            _ => new WorkflowGraphSample()),
    };

    private static VC.TextBlock Label(string text, Color color, float size = 0f, bool wrap = false)
        => new()
        {
            Text = text,
            Foreground = color,
            Wrap = wrap,
            Font = size > 0f ? new Font("Segoe UI", size) : VisualElement.DefaultFont,
        };

    private static VC.Border Tile(string text, Color color)
        => new()
        {
            Width = 90,
            Height = 54,
            Background = color,
            CornerRadius = 6f,
            Padding = new XamlThickness(8f),
            Child = new VC.TextBlock
            {
                Text = text,
                Foreground = Theme.Current.AccentForeground,
                TextAlignment = VeloxDev.WinForms.Xaml.Controls.TextAlignment.Center,
            },
        };

    private static VC.StackPanel Row(int spacing, params VisualElement[] children)
    {
        var row = new VC.StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
        foreach (var child in children)
        {
            row.Add(child);
        }

        return row;
    }

    private static VC.VisualMenuItem MenuItem(string header, string? gesture, Action action)
    {
        var item = new VC.VisualMenuItem { Header = header, InputGestureText = gesture, Command = action };
        return item;
    }

    private static VC.Border Swatch(string name, Color color)
    {
        var stack = new VC.StackPanel { Spacing = 4 };
        stack.Add(new VC.Border { Width = 54, Height = 28, Background = color, CornerRadius = 4f });
        stack.Add(new VC.TextBlock { Text = name, Foreground = Theme.Current.MutedForeground });
        return new VC.Border
        {
            Padding = new XamlThickness(8f),
            Background = Theme.Current.Surface,
            CornerRadius = 6f,
            Child = stack,
        };
    }

    private static VC.TreeViewItem BuildTreeItem(GalleryNode node)
    {
        var item = new VC.TreeViewItem { Header = node.Name, IsExpanded = true };
        foreach (var child in node.Children)
        {
            item.Items.Add(BuildTreeItem(child));
        }

        return item;
    }

    private static Bitmap CreateBitmap()
    {
        var bitmap = new Bitmap(96, 64);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var background = new System.Drawing.Drawing2D.LinearGradientBrush(
                   new Rectangle(0, 0, 96, 64),
                   Color.FromArgb(0xFF, 0x5B, 0x8C, 0xFF),
                   Color.FromArgb(0xFF, 0xA8, 0x4B, 0xF0),
                   45f))
        {
            graphics.FillRectangle(background, 0, 0, 96, 64);
        }

        using var pen = new System.Drawing.Pen(Color.White, 3f);
        graphics.DrawEllipse(pen, 28f, 12f, 40f, 40f);
        return bitmap;
    }

    private static GalleryCard Card(
        string id,
        string group,
        string title,
        string summary,
        string usage,
        Func<GalleryModel, VisualElement> create)
        => new(id, group, title, summary, usage, create);

    private static T WithWidth<T>(this T element, double width) where T : VisualElement
    {
        element.Width = width;
        return element;
    }

    private static T WithHeight<T>(this T element, double height) where T : VisualElement
    {
        element.Height = height;
        return element;
    }

    private sealed class MoneyConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType)
            => value is double amount ? amount.ToString("C2") : value;

        public object? ConvertBack(object? value, Type sourceType)
            => value is string text && double.TryParse(text, out var amount) ? amount : value;
    }
}
