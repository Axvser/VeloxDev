using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Threading;
using VeloxDev.AI;

namespace Demo;

/// <summary>Code-first Jalium dialogs for the Agent's RequestSelection / RequestConfirmation
/// interaction tools. The MainWindow wires these as <c>AgentHelper.SelectionHandler</c> /
/// <c>ConfirmationHandler</c>; without them the tools would return "rejected" with no dialog.</summary>
internal static class AgentDialogs
{
    private static readonly SolidColorBrush Bg = new(Color.FromRgb(0x1A, 0x1A, 0x2E));
    private static readonly SolidColorBrush HeaderBg = new(Color.FromRgb(0x16, 0x21, 0x3E));
    private static readonly SolidColorBrush AccentBlue = new(Color.FromRgb(0x7E, 0xC8, 0xFF));
    private static readonly SolidColorBrush TextFg = new(Color.FromRgb(0xE0, 0xE0, 0xE0));
    private static readonly SolidColorBrush SubFg = new(Color.FromRgb(0xB0, 0xB0, 0xB0));
    private static readonly SolidColorBrush FieldBg = new(Color.FromRgb(0x2D, 0x2D, 0x2D));
    private static readonly SolidColorBrush OptionBg = new(Color.FromRgb(0x0F, 0x34, 0x60));
    private static readonly SolidColorBrush AccentYellow = new(Color.FromRgb(0xFF, 0xD1, 0x66));
    private static readonly SolidColorBrush DenyBg = new(Color.FromRgb(0x3B, 0x00, 0x00));
    private static readonly SolidColorBrush DenyFg = new(Color.FromRgb(0xFF, 0x6B, 0x6B));
    private static readonly SolidColorBrush GreenBg = new(Color.FromRgb(0x0D, 0x3B, 0x1A));
    private static readonly SolidColorBrush GreenFg = new(Color.FromRgb(0x6B, 0xFF, 0xB8));

    /// <summary>Shows the selection dialog on the UI thread and fills <paramref name="args"/>.</summary>
    public static Task ShowSelectionAsync(Dispatcher dispatcher, AgentSelectionEventArgs args)
    {
        dispatcher.Invoke(() => ShowSelection(args));
        return Task.CompletedTask;
    }

    private static void ShowSelection(AgentSelectionEventArgs args)
    {
        var isMulti = args.AllowMultiSelect;
        var win = new Window
        {
            Title = isMulti ? "Agent · 请多选" : "Agent · 请选择",
            Width = 440,
            SizeToContent = SizeToContent.Height,
            Background = Bg,
        };

        var headerPanel = new StackPanel { Margin = new Thickness(18, 14) };
        headerPanel.Children.Add(new TextBlock
        {
            Text = isMulti ? "Agent · 请多选" : "Agent · 请选择",
            Foreground = AccentBlue,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
        });
        headerPanel.Children.Add(new TextBlock
        {
            Text = args.Prompt,
            Foreground = TextFg,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 0),
        });
        var header = new Border { Background = HeaderBg, Child = headerPanel };

        var optionsPanel = new StackPanel { Margin = new Thickness(16, 12), Spacing = 6 };
        List<CheckBox>? checkBoxes = isMulti ? [] : null;
        TextBox freeTextBox = null!;
        foreach (var opt in args.Options)
        {
            if (isMulti)
            {
                var cb = new CheckBox { Content = opt, Foreground = TextFg, FontSize = 12 };
                checkBoxes!.Add(cb);
                optionsPanel.Children.Add(cb);
            }
            else
            {
                var captured = opt;
                var btn = new Button
                {
                    Content = opt,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(14, 10),
                    FontSize = 12,
                    Background = OptionBg,
                    Foreground = TextFg,
                    BorderBrush = AccentBlue,
                    BorderThickness = new Thickness(1),
                };
                btn.Click += (_, _) =>
                {
                    args.SelectedOption = captured;
                    CollectFreeText(args, freeTextBox);
                    win.Close();
                };
                optionsPanel.Children.Add(btn);
            }
        }

        optionsPanel.Children.Add(new TextBlock
        {
            Text = args.FreeTextPrompt,
            Foreground = SubFg,
            FontSize = 11,
            Margin = new Thickness(0, 6, 0, 2),
        });
        freeTextBox = new TextBox
        {
            Background = FieldBg,
            Foreground = new SolidColorBrush(Colors.White),
            Padding = new Thickness(8, 6),
            FontSize = 12,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            BorderThickness = new Thickness(1),
        };
        optionsPanel.Children.Add(freeTextBox);

        if (isMulti)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 0, 0),
                Spacing = 6,
            };
            var cancelBtn = MakeActionButton("取消", FieldBg, SubFg, new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)));
            cancelBtn.Click += (_, _) => { CollectFreeText(args, freeTextBox); win.Close(); };
            var confirmBtn = MakeActionButton("确认选择", OptionBg, AccentBlue, AccentBlue);
            confirmBtn.Click += (_, _) =>
            {
                CollectFreeText(args, freeTextBox);
                args.SelectedOptions = checkBoxes!
                    .Where(cb => cb.IsChecked == true)
                    .Select(cb => cb.Content?.ToString())
                    .Where(s => s is not null)
                    .Cast<string>()
                    .ToList();
                win.Close();
            };
            row.Children.Add(cancelBtn);
            row.Children.Add(confirmBtn);
            optionsPanel.Children.Add(row);
        }
        else
        {
            var cancelBtn = MakeActionButton("取消（不选择）", FieldBg, SubFg, new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)));
            cancelBtn.HorizontalAlignment = HorizontalAlignment.Stretch;
            cancelBtn.Click += (_, _) => { CollectFreeText(args, freeTextBox); win.Close(); };
            optionsPanel.Children.Add(cancelBtn);
        }

        var scroll = new ScrollViewer
        {
            MaxHeight = 420,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = optionsPanel,
        };

        var root = new StackPanel { Background = Bg };
        root.Children.Add(header);
        root.Children.Add(scroll);
        win.Content = root;
        win.ShowDialog();
    }

    private static void CollectFreeText(AgentSelectionEventArgs args, TextBox box)
    {
        var text = box.Text?.Trim();
        args.FreeTextResponse = string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>Shows the confirmation dialog on the UI thread and fills <paramref name="args"/>.</summary>
    public static Task ShowConfirmationAsync(Dispatcher dispatcher, AgentConfirmationEventArgs args)
    {
        dispatcher.Invoke(() => ShowConfirmation(args));
        return Task.CompletedTask;
    }

    private static void ShowConfirmation(AgentConfirmationEventArgs args)
    {
        var win = new Window
        {
            Title = "Agent · 操作确认",
            Width = 440,
            SizeToContent = SizeToContent.Height,
            Background = Bg,
        };

        var headerPanel = new StackPanel { Margin = new Thickness(18, 14) };
        headerPanel.Children.Add(new TextBlock
        {
            Text = "Agent · 操作确认",
            Foreground = AccentYellow,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
        });
        headerPanel.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x1F, 0x00)),
            BorderBrush = AccentYellow,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(10, 6),
            Margin = new Thickness(0, 4, 0, 0),
            Child = new TextBlock { Text = args.OperationKey, Foreground = AccentYellow, FontSize = 11 },
        });
        var header = new Border { Background = HeaderBg, Child = headerPanel };

        var body = new StackPanel { Margin = new Thickness(18, 14), MinWidth = 300 };
        body.Children.Add(new TextBlock
        {
            Text = args.Description,
            TextWrapping = TextWrapping.Wrap,
            Foreground = TextFg,
            FontSize = 12,
        });

        var result = AgentConfirmationResult.Deny;
        var denyBtn = MakeActionButton("拒绝", DenyBg, DenyFg, DenyFg);
        denyBtn.Click += (_, _) => { result = AgentConfirmationResult.Deny; win.Close(); };
        var onceBtn = MakeActionButton("仅同意一次", OptionBg, AccentBlue, AccentBlue);
        onceBtn.Click += (_, _) => { result = AgentConfirmationResult.AllowOnce; win.Close(); };
        var alwaysBtn = MakeActionButton("本次会话始终同意", GreenBg, GreenFg, GreenFg);
        alwaysBtn.Click += (_, _) => { result = AgentConfirmationResult.AllowAlways; win.Close(); };

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
            Spacing = 6,
        };
        btnRow.Children.Add(denyBtn);
        btnRow.Children.Add(onceBtn);
        btnRow.Children.Add(alwaysBtn);
        body.Children.Add(btnRow);

        var root = new StackPanel { Background = Bg };
        root.Children.Add(header);
        root.Children.Add(body);
        win.Content = root;
        win.ShowDialog();
        args.Result = result;
    }

    private static Button MakeActionButton(string text, Brush bg, Brush fg, Brush border) => new()
    {
        Content = text,
        Padding = new Thickness(14, 9),
        FontSize = 12,
        Background = bg,
        Foreground = fg,
        BorderBrush = border,
        BorderThickness = new Thickness(1),
    };
}
