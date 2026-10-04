using System.Globalization;
using Demo.Models;
using VeloxDev.TransitionSystem;

namespace Demo;

// 一行案例属于哪一类。
internal enum RowKind
{
    // 加载：元素是一块被三条动画之一驱动的方块。
    Load,

    // 过冲：元素就是那条案例的目标本身。
    Overshoot,

    // 采样器：元素是真正着色的那个 div（令牌 over.bench.<采样器名> 挂在它上面）。
    Sampler,
}

// 案例列表里的一行：一个在屏元素、一句"这条在验什么"，以及这一行自己的三个动作。
// 动作里。三个动作是 Action 而不是事件处理函数：行本身不该知道定时器、载荷与元素之间的关系，
internal sealed record CaseRow(
    string Title,
    string Description,
    RowKind Kind,
    BoxModel? Subject,
    double StageWidth,
    double Inset,
    string StartToken,
    string StopToken,
    string ResetToken,
    Action Start,
    Action Stop,
    Action Reset,
    Action? BulkStart = null);

// 案例列表：一条案例一行。左边是那条案例真正在动的元素，中间是这条在验什么的文字，
// 右边固定宽度是这一行自己的 启动 / 关闭 / 重置。
// 行的顺序就是传进来的顺序。浏览器里没有"控件属性"可写，所以桌面那几侧由行携带的在屏控件在这里换成两样：
// 每一行一样高，所以每一条案例的行程都必须落在同一块台子里 —— 台子窄一点或矮一点，元素就会在最该被
// 上下各留 5 像素。
// 台子必须裁边（overflow:hidden）：不裁的话，跑出台子的元素会滑到邻行的文字上，而"跑到别处"
internal static class SamplerBench
{
    // 案例行里元素区统一的高度。
    // 所以台子按 132 给，方块居中（见 BodyTop）后上下各留 5 像素。其余几条都在 60 以内。
    internal const double StageHeight = 132d;

    // 行高：每一行都是它，台子与文字都在这条带子里垂直居中。
    // 比台子多出来的 12 像素是给元素框那圈 1 像素描边与它上下各 4 像素的间距的 —— 元素框因此也装得进这一行。
    internal const double RowHeight = StageHeight + 12d;

    // 行里那块方块的尺寸。加载与过冲共用它：两种场景读起来是一张台子。
    internal const double BodyWidth = 60d;

    internal const double BodyHeight = 60d;

    // 方块在元素区里垂直居中 —— 旋转与缩放的轴心就是它的中心，居中之后行程才是左右展开的。
    internal const double BodyTop = (StageHeight - BodyHeight) / 2d;

    // 加载行的起边。
    // Inset + 30 —— 不留出来，方块在转到 45° 附近时会被台子的左边界裁掉一个角。
    internal const double LoadInset = 36d;

    // 过冲行的起边：那五条只有横向位移，不放大也不旋转，留一点边就够。
    internal const double BodyInset = 10d;

    // 采样器那一格元素区的宽度。
    // 比别的行窄，但行高是统一的：采样器那一格里没有"行程"，它只是一块按产物着色的 div（那个 div 自己的尺寸与
    internal const double SamplerStageWidth = 96d;

    // 把几何量写成 CSS 能读的数字：小数点是固定的，不跟着机器的区域设置走。
    internal static string Css(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    // 行那一层的样式 —— 行高只有这一处来源。
    internal static string RowStyle => $"height:{Css(RowHeight)}px";

    // 元素区那一层的样式。宽由那条案例的行程决定，高则由最高的那条行程决定（见 StageHeight）。
    internal static string StageStyle(double width) => $"width:{Css(width)}px;height:{Css(StageHeight)}px";

    // 元素在元素区里的落点。它自己的样式（尺寸、颜色、变换）由那块元素接着写。
    internal static string BodyStyle(double inset) => $"left:{Css(inset)}px;top:{Css(BodyTop)}px;";

    // 行里那块方块完整的内联样式：落点由台子定，尺寸与颜色由 Style 给。
    // 而浏览器对作废的那一段一声不响，元素于是没有宽度，看上去就是"这一行没在动"，与真的没跑起来在屏幕上
    internal static string ElementStyle(CaseRow row)
        => row.Subject is null ? string.Empty : BodyStyle(row.Inset) + row.Subject.Style;

    // 一条加载案例：元素就是那块被三条动画之一驱动的方块。
    internal static CaseRow LoadRow(
        string id, string title, string description, BoxModel subject, double stageWidth, Action start, Action restore)
        => new(
            title,
            description,
            RowKind.Load,
            subject,
            stageWidth,
            LoadInset,
            $"over.row.start.{id}",
            $"over.row.stop.{id}",
            $"over.row.reset.{id}",
            start,
            () => Transition.Exit(subject, IncludeMutual: true, IncludeNoMutual: true),
            () => ResetCase(subject, restore));

    // 一条过冲案例：元素是那块目标自己。
    internal static CaseRow OvershootRow(
        string id, string title, string description, BoxModel target, double stageWidth, Action start, Action restore)
        => new(
            title,
            description,
            RowKind.Overshoot,
            target,
            stageWidth,
            BodyInset,
            $"over.row.start.{id}",
            $"over.row.stop.{id}",
            $"over.row.reset.{id}",
            start,
            () => Transition.Exit(target, IncludeMutual: true, IncludeNoMutual: true),
            () => ResetCase(target, restore));

    // 造一条采样器案例行：元素是那个按采样器产物着色的 div，令牌沿用 over.sampler.<类型名>。
    internal static CaseRow SamplerRow(
        string sampler, Action start, Action stop, Action reset, Action bulkStart)
        => new(
            sampler,
            SamplerProbe.Description(sampler),
            RowKind.Sampler,
            null,
            SamplerStageWidth,
            BodyInset,
            $"over.sampler.{sampler}",
            $"over.row.stop.{sampler}",
            $"over.row.reset.{sampler}",
            start,
            stop,
            reset,
            bulkStart);

    // 停掉并把它放回声明的静止态。
    private static void ResetCase(BoxModel element, Action restore)
    {
        Transition.Exit(element, IncludeMutual: true, IncludeNoMutual: true);
        restore();
    }
}
