using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace Demo;

// 案例列表里的一行：一个在屏元素、一句"这条在验什么"，以及这一行自己的三个动作。
// 令牌、描述与动作里。三个动作是 Action 而不是事件处理函数：行本身不该知道调度器、
internal sealed record CaseRow(
    string Title,
    string Description,
    FrameworkElement Element,
    double ElementWidth,
    string StartToken,
    string StopToken,
    string ResetToken,
    Action Start,
    Action Stop,
    Action Reset,
    Action? BulkStart = null);

// 案例列表：一条案例一行。左边是那条案例真正在动的在屏元素，中间是这条在验什么的文字，
// 右边固定宽度是这一行自己的 启动 / 关闭 / 重置。
// 采样器那一类案例的元素是 SamplerSubject —— 采样器直接写在它上面，它自己按属性重绘，
internal sealed class SamplerBench
{
    private const double StageWidth = 96d;
    private const double StageHeight = 62d;

    // 右侧控制区的固定宽度 —— 三列按钮在任何一行里都落在同一竖线上。
    private const double ControlWidth = 186d;

    private readonly Dictionary<string, SamplerSubject> _subjects = new(StringComparer.Ordinal);

    // 某条采样器那一行的被写元素 —— 采样器写在它上面，载荷也从它读回。
    internal SamplerSubject SubjectFor(string sampler) => _subjects[sampler];

    // 造一条采样器案例行：元素是一个在它自己那一格里被写的控件，令牌沿用 over.sampler.<类型名>。
    internal CaseRow SamplerRow(string sampler, Action start, Action stop, Action reset, Action bulkStart)
    {
        var stage = new Canvas
        {
            Width = StageWidth,
            Height = StageHeight,
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
            ClipToBounds = true,
        };

        var subject = new SamplerSubject { Kind = sampler };
        stage.Children.Add(subject);
        _subjects[sampler] = subject;

        return new CaseRow(
            sampler,
            SamplerProbe.Description(sampler),
            Frame(stage),
            StageWidth,
            $"over.sampler.{sampler}",
            $"over.row.stop.{sampler}",
            $"over.row.reset.{sampler}",
            start,
            stop,
            reset,
            bulkStart);
    }

    // 把一串案例行铺成一张表。
    internal static FrameworkElement Build(IReadOnlyList<CaseRow> rows)
    {
        var list = new StackPanel();

        foreach (var row in rows)
        {
            list.Children.Add(BuildRow(row));
        }

        return list;
    }

    // 元素自己画在深色底上、底色与窗口一样，所以每一行的元素都得有个框，否则"这一行的元素在哪"看不出来。
    internal static Border Frame(FrameworkElement element) => new()
    {
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40)),
        BorderThickness = new Thickness(1),
        Child = element,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    private static FrameworkElement BuildRow(CaseRow row)
    {
        // 名字加粗打头，后面跟这条在验什么。
        var caption = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0),
        };
        caption.Inlines.Add(new System.Windows.Documents.Run(row.Title) { FontWeight = FontWeights.Bold });
        caption.Inlines.Add(new System.Windows.Documents.Run($"  {row.Description}"));

        var controls = new Grid { Width = ControlWidth, VerticalAlignment = VerticalAlignment.Center };
        for (var column = 0; column < 3; column++)
        {
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        controls.Children.Add(MakeControl("启动", row.StartToken, row.Start, 0));
        controls.Children.Add(MakeControl("关闭", row.StopToken, row.Stop, 1));
        controls.Children.Add(MakeControl("重置", row.ResetToken, row.Reset, 2));

        var layout = new Grid { Margin = new Thickness(2, 1, 2, 1) };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var element = row.Element;
        element.VerticalAlignment = VerticalAlignment.Center;
        element.HorizontalAlignment = HorizontalAlignment.Left;

        Grid.SetColumn(element, 0);
        Grid.SetColumn(caption, 1);
        Grid.SetColumn(controls, 2);
        layout.Children.Add(element);
        layout.Children.Add(caption);
        layout.Children.Add(controls);

        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = layout,
        };
    }

    private static Button MakeControl(string text, string automationId, Action action, int column)
    {
        var button = new Button
        {
            Content = text,
            Margin = new Thickness(2, 3, 2, 3),
            FontSize = 11,
            Height = 26,
        };

        AutomationProperties.SetAutomationId(button, automationId);
        button.Click += (_, _) => action();
        Grid.SetColumn(button, column);
        return button;
    }
}
