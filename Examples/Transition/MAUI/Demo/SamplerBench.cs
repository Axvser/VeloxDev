// 同名类型一律显式取 MAUI 的那一侧：System.Drawing 里也有一份 PointF/RectF/SizeF，量纲不同、不能混。
using MauiColor = Microsoft.Maui.Graphics.Color;

namespace Demo;

/// <summary>
/// 案例列表里的一行：一个在屏元素、一句"这条在验什么"，以及这一行自己的三个动作。
/// </summary>
/// <remarks>
/// 采样器行、加载行、过冲行共用这一个形状 —— 用户看到的是一张统一的表，行的种类只体现在各自的令牌、描述与
/// 动作里。三个动作是 <see cref="Action"/> 而不是事件处理函数：行本身不该知道定时器、载荷与元素之间的关系，
/// 那些留在页面那侧（与 WPF / WinForms / Jalium / Avalonia / WinUI 版同一分工）。
/// </remarks>
/// <param name="Title">行首那个名字，加粗显示。</param>
/// <param name="Description">这条在验什么。采样器那几条来自探针表，与把手、载荷同源。</param>
/// <param name="Element">这一行真正在动的那个在屏元素的台子。</param>
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
    View Element,
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
/// <para>
/// <b>每一行一样高</b>，所以每一条案例的行程都必须落在同一块台子里 —— 台子窄一点或矮一点，元素就会在最该被
/// 看见的那一瞬跑出边界，而"跑出去看不见"和"没在跑"在屏幕上分不开。
/// </para>
/// <para>
/// 给台子留多宽由各条案例自己的横向行程决定（见页面那侧的 <c>*StageWidth</c>），<b>留多高则由最高的那条行程
/// 决定</b>（见 <see cref="StageHeight"/>）：方块在台子里只占 60，其余是留给行程的留白。
/// </para>
/// <para>
/// 采样器那一格只有 56 高（标尺 <see cref="SamplerSubject"/> 的绘制缩放是按它定出来的，不能跟着长），
/// 所以在等高的元素区里把它居中放一层。
/// </para>
/// <para>
/// <b>台子必须裁边。</b>MAUI 的裁切是 <c>Layout.IsClippedToBounds</c>：不裁的话，跑出台子的元素会滑到邻行的
/// 文字上，而"跑到别处"和"没动过"一样糟 —— 这个演示要消掉的正是这两种错觉。
/// </para>
/// </remarks>
internal sealed class SamplerBench
{
    /// <summary>
    /// 案例行里元素区统一的高度。
    /// </summary>
    /// <remarks>
    /// 装得下最高的那条行程：加载那第三条把 80×60 的方块绕自身中心放大到 1.3 倍，78 高 —— 所以台子按 104 给，
    /// 上下各留 13 像素，是留给 MAUI 那层三维投影的。实测：静止的 60 在台子里上下各留 17 像素，放大到 1.3 倍
    /// 时仍整个在台子里；而绕 X/Y 翻到最极端的那一瞬方块反而被压扁（三维投影把宽度拉长、把高度压短），
    /// 所以高度上的约束就是那个 78。WinUI 那侧同样按"最高的那条行程"取 104。
    /// </remarks>
    internal const double StageHeight = 104d;

    /// <summary>行高：每一行都是它，台子与文字都在这条带子里垂直居中。</summary>
    internal const double RowHeight = StageHeight + 8d;

    /// <summary>元素在台子里的起点。</summary>
    internal const double ElementInset = 12d;

    /// <summary>60 高的方块在等高的台子里垂直居中。</summary>
    internal const double ElementTop = (StageHeight - 60d) / 2d;

    /// <summary>采样器那一格的尺寸。标尺是按它定出来的（见 <see cref="SamplerSubject"/>），不能随手改。</summary>
    internal const double SamplerStageWidth = 84d;

    internal const double SamplerStageHeight = 56d;

    /// <summary>右侧控制区的固定宽度 —— 三列按钮在任何一行里都落在同一竖线上。</summary>
    private const double ControlWidth = 186d;

    private const double ButtonHeight = 26d;

    private static readonly MauiColor StageBackColor = MauiColor.FromRgb(0x1E, 0x1E, 0x1E);
    private static readonly MauiColor FrameColor = MauiColor.FromRgb(0x40, 0x40, 0x40);
    private static readonly MauiColor LineColor = MauiColor.FromRgb(0x33, 0x33, 0x33);
    private static readonly MauiColor CaptionColor = MauiColor.FromRgb(0xAA, 0xAA, 0xAA);

    private readonly Dictionary<string, SamplerSubject> _subjects = new(StringComparer.Ordinal);

    /// <summary>某条采样器那一行的被写元素 —— 采样器写在它上面，载荷也从它读回。</summary>
    internal SamplerSubject SubjectFor(string sampler) => _subjects[sampler];

    /// <summary>
    /// 一条案例的元素区：定宽、等高、裁边、深色底。
    /// </summary>
    /// <remarks>
    /// 台子就是这一条案例的场地：宽度由那条案例真正走多远定，跑出去会被裁掉 —— 而"跑出去看不见"与
    /// "没在跑"在屏幕上一样，正是这个演示要消除的错觉。
    /// </remarks>
    internal static Grid Stage(double width) => new()
    {
        WidthRequest = width,
        HeightRequest = StageHeight,
        BackgroundColor = StageBackColor,
        IsClippedToBounds = true,
    };

    /// <summary>把一块矮的内容（采样器那一格）在等高的元素区里垂直居中。</summary>
    internal static Grid Center(View content, double width)
    {
        var box = Stage(width);
        content.HorizontalOptions = LayoutOptions.Start;
        content.VerticalOptions = LayoutOptions.Center;
        box.Children.Add(content);
        return box;
    }

    /// <summary>
    /// 造一条采样器案例行：元素是一个在它自己那一格里被写的控件，令牌沿用 <c>over.sampler.&lt;类型名&gt;</c>。
    /// </summary>
    /// <remarks>
    /// "启动"的令牌刻意不跟另外两条走同一套命名：验收套件点的就是它，点它要写闭式解载荷并起那条真动画。
    /// </remarks>
    internal CaseRow SamplerRow(string sampler, Action start, Action stop, Action reset, Action bulkStart)
    {
        // 采样器那一格比别的行矮（标尺是按 56 高定出来的），所以在等高的元素区里居中放一层自己那块格子，
        // 于是每行的元素框一样高，采样器仍然按自己的标尺画。
        var cell = new Grid
        {
            WidthRequest = SamplerStageWidth,
            HeightRequest = SamplerStageHeight,
            BackgroundColor = StageBackColor,
            IsClippedToBounds = true,
        };

        var subject = new SamplerSubject { Kind = sampler };
        cell.Children.Add(subject);
        _subjects[sampler] = subject;

        return new CaseRow(
            sampler,
            SamplerProbe.Description(sampler),
            Frame(Center(cell, SamplerStageWidth)),
            SamplerStageWidth,
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
    internal static View Build(IReadOnlyList<CaseRow> rows)
    {
        var list = new VerticalStackLayout();

        foreach (var row in rows)
        {
            list.Children.Add(BuildRow(row));
        }

        return list;
    }

    /// <summary>元素自己画在深色底上、底色与窗口一样，所以每一行的元素都得有个框，否则"这一行的元素在哪"看不出来。</summary>
    internal static Border Frame(View element) => new()
    {
        Stroke = new SolidColorBrush(FrameColor),
        StrokeThickness = 1,
        Padding = new Thickness(0),
        Content = element,
        VerticalOptions = LayoutOptions.Center,
        HorizontalOptions = LayoutOptions.Start,
    };

    private static View BuildRow(CaseRow row)
    {
        // 名字加粗打头，后面跟这条在验什么 —— 一段文字里的两个 Span，所以描述跟着标题一起折行，
        // 而不是被挤成"标题一行、描述一行"的两截。
        var caption = new Label
        {
            FontSize = 11,
            TextColor = CaptionColor,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(8, 0, 8, 0),
            LineBreakMode = LineBreakMode.WordWrap,
            FormattedText = new FormattedString
            {
                Spans =
                {
                    new Span { Text = row.Title, FontAttributes = FontAttributes.Bold },
                    new Span { Text = $"  {row.Description}" },
                },
            },
        };

        var controls = new Grid { WidthRequest = ControlWidth, VerticalOptions = LayoutOptions.Center };
        for (var column = 0; column < 3; column++)
        {
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        }

        controls.Add(MakeControl("启动", row.StartToken, row.Start), 0);
        controls.Add(MakeControl("关闭", row.StopToken, row.Stop), 1);
        controls.Add(MakeControl("重置", row.ResetToken, row.Reset), 2);

        var layout = new Grid { Margin = new Thickness(2, 1, 2, 1) };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        layout.Add(row.Element, 0);
        layout.Add(caption, 1);
        layout.Add(controls, 2);

        // 行高写死在这一层：台子、文字与按钮都在它里面垂直居中，于是"每行一样高"由这一处保证，
        // 列表读起来才是一张表。
        var line = new Grid { HeightRequest = RowHeight };
        line.Add(layout);

        // MAUI 的 Border 只有均一粗细的描边，画不出"只画一条下边线"的框，所以行与行之间那条线由一条
        // BoxView 自己画，压在行的下沿。
        line.Add(new BoxView
        {
            HeightRequest = 1,
            Color = LineColor,
            VerticalOptions = LayoutOptions.End,
            HorizontalOptions = LayoutOptions.Fill,
        });

        return line;
    }

    private static Button MakeControl(string text, string token, Action action)
    {
        var button = new Button
        {
            Text = text,
            Margin = new Thickness(2, 3, 2, 3),
            FontSize = 11,
            HeightRequest = ButtonHeight,
            Padding = new Thickness(0),
        };

        // MAUI 用的是自己的 AutomationId 属性，不是 WPF/WinUI 那个附加属性 AutomationProperties。
        button.AutomationId = token;

        // 焦点落到行里的按钮上，就把这一行滚进视野 —— 验收套件把一行弄进视野走的正是"聚焦它"
        // （见 AutomationLocator.BringIntoView；UIA 的 Invoke 对滚出视野的控件照样生效，少了这一道，
        // 一排人够不着的行会绿着过去）。
        button.Focused += (_, _) => ScrollIntoView(button);

        button.Clicked += (_, _) => action();
        return button;
    }

    /// <summary>
    /// 把这一行滚进视野。
    /// </summary>
    /// <remarks>
    /// MAUI 的 <see cref="ScrollView"/> 不承诺跟着焦点滚（WinUI 那侧同样如此：UIA 的 SetFocus 走的是程序性焦点，
    /// 框架替它滚是另一回事），而套件的可达性检查正是"聚焦它，然后断言它在窗口里"。所以自己发一次滚动请求，
    /// <c>animated: false</c> 是为了"套件聚焦完紧接着就量它的矩形"，滚动必须当场落地。
    /// <para>
    /// 往上找 <see cref="ScrollView"/> 而不是由行自己拿着它：行不该知道自己在谁的里面。
    /// </para>
    /// </remarks>
    private static void ScrollIntoView(VisualElement element)
    {
        for (var ancestor = element.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor is ScrollView scroll)
            {
                _ = scroll.ScrollToAsync(element, ScrollToPosition.MakeVisible, animated: false);
                return;
            }
        }
    }
}
