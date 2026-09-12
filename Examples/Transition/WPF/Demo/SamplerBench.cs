using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace Demo;

/// <summary>
/// 案例列表里的一行：一个在屏元素、一句"这条在验什么"，以及这一行自己的三个动作。
/// </summary>
/// <remarks>
/// 采样器行、加载行、过冲行共用这一个形状 —— 用户看到的是一张统一的表，行的种类只体现在各自的
/// 令牌、描述与动作里。三个动作是 <see cref="Action"/> 而不是事件处理函数：行本身不该知道调度器、
/// 载荷与元素之间的关系，那些留在窗口那侧。
/// </remarks>
/// <param name="Title">行首那个名字，加粗显示。</param>
/// <param name="Description">这条在验什么。采样器那几条来自探针表，与把手、载荷同源。</param>
/// <param name="Element">这一行真正在动的那个在屏元素。</param>
/// <param name="ElementWidth">元素区的宽度。位移类那几条要放得下整段行程，其余的按元素本身给。</param>
/// <param name="StartToken">"启动"按钮的自动化令牌。</param>
/// <param name="StopToken">"关闭"按钮的自动化令牌。</param>
/// <param name="ResetToken">"重置"按钮的自动化令牌。</param>
/// <param name="Start">点"启动"。</param>
/// <param name="Stop">点"关闭"。</param>
/// <param name="Reset">点"重置"。</param>
/// <param name="BulkStart">
/// 顶栏"全部启动"用的动作，不填就等于 <paramref name="Start"/>。
/// </param>
/// <remarks>
/// 两者之所以可能不同：行里那个"启动"是**单独**驱动这一行，它得管载荷、得盯着这一条跑完；而"全部启动"
/// 是十几条一起发起，那时谁都不能去动别人。采样器行的"启动"里有一句"停掉上一次被观察的那一行"，
/// 那是为了单点时不至于两条同时写载荷 —— 走到批量路径上就变成了"后一条把前一条掐掉"，只剩最后一条在跑。
/// </remarks>
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

/// <summary>
/// 案例列表：一条案例一行。左边是那条案例真正在动的在屏元素，中间是这条在验什么的文字，
/// 右边固定宽度是这一行自己的 启动 / 关闭 / 重置。
/// </summary>
/// <remarks>
/// 采样器那一类案例的元素是 <see cref="SamplerSubject"/> —— 采样器直接写在它上面，它自己按属性重绘，
/// 所以"画出来"这件事不需要另一套映射代码。行的顺序就是传进来的顺序。
/// </remarks>
internal sealed class SamplerBench
{
    private const double StageWidth = 96d;
    private const double StageHeight = 62d;

    /// <summary>右侧控制区的固定宽度 —— 三列按钮在任何一行里都落在同一竖线上。</summary>
    private const double ControlWidth = 186d;

    private readonly Dictionary<string, SamplerSubject> _subjects = new(StringComparer.Ordinal);

    /// <summary>某条采样器那一行的被写元素 —— 采样器写在它上面，载荷也从它读回。</summary>
    internal SamplerSubject SubjectFor(string sampler) => _subjects[sampler];

    /// <summary>
    /// 造一条采样器案例行：元素是一个在它自己那一格里被写的控件，令牌沿用 <c>over.sampler.&lt;类型名&gt;</c>。
    /// </summary>
    /// <remarks>
    /// "启动"的令牌刻意不跟另外两条走同一套命名：验收套件点的就是它，点它要写闭式解载荷并起那条真动画。
    /// </remarks>
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

    /// <summary>
    /// 把一串案例行铺成一张表。
    /// </summary>
    internal static FrameworkElement Build(IReadOnlyList<CaseRow> rows)
    {
        var list = new StackPanel();

        foreach (var row in rows)
        {
            list.Children.Add(BuildRow(row));
        }

        return list;
    }

    /// <summary>元素自己画在深色底上、底色与窗口一样，所以每一行的元素都得有个框，否则"这一行的元素在哪"看不出来。</summary>
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
