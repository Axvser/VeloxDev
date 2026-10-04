// 同名类型一律显式取 MAUI 的那一侧：System.Drawing 里也有一份 PointF/RectF/SizeF，量纲不同、不能混。
using MauiColor = Microsoft.Maui.Graphics.Color;

namespace Demo;

// 案例列表里的一行：一个在屏元素、一句"这条在验什么"，以及这一行自己的三个动作。
// 动作里。三个动作是 Action 而不是事件处理函数：行本身不该知道定时器、载荷与元素之间的关系，
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

// 案例列表：一条案例一行。左边是那条案例真正在动的在屏元素，中间是这条在验什么的文字，
// 右边固定宽度是这一行自己的 启动 / 关闭 / 重置。
// 采样器那一类案例的元素是 SamplerSubject —— 采样器直接写在它上面，它自己按属性重绘，
// 每一行一样高，所以每一条案例的行程都必须落在同一块台子里 —— 台子窄一点或矮一点，元素就会在最该被
// 采样器那一格只有 56 高（标尺 SamplerSubject 的绘制缩放是按它定出来的，不能跟着长），
// 台子必须裁边。MAUI 的裁切是 Layout.IsClippedToBounds：不裁的话，跑出台子的元素会滑到邻行的
internal sealed class SamplerBench
{
    // 案例行里元素区统一的高度。
    internal const double StageHeight = 104d;

    // 行高：每一行都是它，台子与文字都在这条带子里垂直居中。
    internal const double RowHeight = StageHeight + 8d;

    // 元素在台子里的起点。
    internal const double ElementInset = 12d;

    // 60 高的方块在等高的台子里垂直居中。
    internal const double ElementTop = (StageHeight - 60d) / 2d;

    // 采样器那一格的尺寸。标尺是按它定出来的（见 SamplerSubject），不能随手改。
    internal const double SamplerStageWidth = 84d;

    internal const double SamplerStageHeight = 56d;

    // 右侧控制区的固定宽度 —— 三列按钮在任何一行里都落在同一竖线上。
    private const double ControlWidth = 186d;

    private const double ButtonHeight = 26d;

    private static readonly MauiColor StageBackColor = MauiColor.FromRgb(0x1E, 0x1E, 0x1E);
    private static readonly MauiColor FrameColor = MauiColor.FromRgb(0x40, 0x40, 0x40);
    private static readonly MauiColor LineColor = MauiColor.FromRgb(0x33, 0x33, 0x33);
    private static readonly MauiColor CaptionColor = MauiColor.FromRgb(0xAA, 0xAA, 0xAA);

    private readonly Dictionary<string, SamplerSubject> _subjects = new(StringComparer.Ordinal);

    // 某条采样器那一行的被写元素 —— 采样器写在它上面，载荷也从它读回。
    internal SamplerSubject SubjectFor(string sampler) => _subjects[sampler];

    // 一条案例的元素区：定宽、等高、裁边、深色底。
    // 台子就是这一条案例的场地：宽度由那条案例真正走多远定，跑出去会被裁掉 —— 而"跑出去看不见"与
    internal static Grid Stage(double width) => new()
    {
        WidthRequest = width,
        HeightRequest = StageHeight,
        BackgroundColor = StageBackColor,
        IsClippedToBounds = true,
    };

    // 把一块矮的内容（采样器那一格）在等高的元素区里垂直居中。
    internal static Grid Center(View content, double width)
    {
        var box = Stage(width);
        content.HorizontalOptions = LayoutOptions.Start;
        content.VerticalOptions = LayoutOptions.Center;
        box.Children.Add(content);
        return box;
    }

    // 造一条采样器案例行：元素是一个在它自己那一格里被写的控件，令牌沿用 over.sampler.<类型名>。
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

    // 把一串案例行铺成一张表。
    internal static View Build(IReadOnlyList<CaseRow> rows)
    {
        var list = new VerticalStackLayout();

        foreach (var row in rows)
        {
            list.Children.Add(BuildRow(row));
        }

        return list;
    }

    // 元素自己画在深色底上、底色与窗口一样，所以每一行的元素都得有个框，否则"这一行的元素在哪"看不出来。
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

    // 把这一行滚进视野。
    // animated: false 是为了"套件聚焦完紧接着就量它的矩形"，滚动必须当场落地。
    // 往上找 ScrollView 而不是由行自己拿着它：行不该知道自己在谁的里面。
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
