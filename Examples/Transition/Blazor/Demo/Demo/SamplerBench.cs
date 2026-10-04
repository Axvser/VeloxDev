using System.Globalization;
using Demo.Models;
using VeloxDev.TransitionSystem;

namespace Demo;

// 一行案例属于哪一类。
// 三类行的形状一模一样 —— 元素、加粗的名字、一句"这条在验什么"、右边三个按钮 —— 区别只在元素区里
// 画的是什么，而这件事由这里说明：加载与过冲行的元素是一块被 VeloxDev 写的 BoxModel，
// 采样器行的元素就是那个按采样器产物着色的 div。渲染时按它分叉，行本身仍是同一张表里的一行。
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
// 采样器行、加载行、过冲行共用这一个形状 —— 用户看到的是一张统一的表，行的种类只体现在各自的令牌、描述与
// 动作里。三个动作是 Action 而不是事件处理函数：行本身不该知道定时器、载荷与元素之间的关系，
// 那些留在页面那侧（与 WPF / WinForms / Jalium / Avalonia / WinUI / MAUI 版同一分工）。
// Title: 行首那个名字，加粗显示。
// Description: 这条在验什么。采样器那条来自探针表，与把手、端点同源。
// Kind: 这一行的元素区画什么。
// Subject: 被 VeloxDev 写的那块元素；采样器行没有（它写的是页面持有的那个常驻目标）。
// StageWidth: 元素区的宽度。位移类那几条要放得下整段行程，其余的按元素本身给。
// Inset: 元素在元素区里的起点。能旋转与放大的那几条要留出最大的外接半径。
// StartToken: "启动"按钮的自动化令牌。
// StopToken: "关闭"按钮的自动化令牌。
// ResetToken: "重置"按钮的自动化令牌。
// Start: 点"启动"。
// Stop: 点"关闭"。
// Reset: 点"重置"。
// 顶栏"全部启动"用的动作，不填就等于 Start。
// 两者之所以可能不同：行里那个"启动"是**单独**驱动这一行，它得管载荷、得盯着这一条跑完；而"全部启动"
// 是十几条一起发起，那时谁都不能去动别人。采样器行的"启动"里有一句"停掉上一次被观察的那一条"，那是为了
// 单点时不至于两条同时写载荷 —— 走到批量路径上就变成了"后一条把前一条掐掉"，只剩最后一条在跑。
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
// 一块 BoxModel（由 PropertyChanged 驱动重渲染）或者采样器那个着色 div —— 行携带的是"这一行
// 的元素画什么"，而渲染那一步在 Home.razor 里。
// 每一行一样高，所以每一条案例的行程都必须落在同一块台子里 —— 台子窄一点或矮一点，元素就会在最该被
// 看见的那一瞬跑出边界，而"跑出去看不见"和"没在跑"在屏幕上分不开。
// 给台子留多宽由各条案例自己的横向行程决定（见页面那侧的 *StageWidth），<b>留多高则由最高的那条行程
// 决定</b>（见 StageHeight）：方块在台子里只占 60，其余是留给行程的留白。Blazor 加载那第二条
// 一边转一圈一边放大到 1.5 倍，60 的方块在 45° 附近的外接尺寸是 30×1.44×√2 ≈ 61 半径 —— 台子按 132 给，
// 上下各留 5 像素。
// 台子必须裁边（overflow:hidden）：不裁的话，跑出台子的元素会滑到邻行的文字上，而"跑到别处"
// 和"没动过"一样糟 —— 这个演示要消掉的正是这两种错觉。
internal static class SamplerBench
{
    // 案例行里元素区统一的高度。
    // 装得下最高的那条行程：加载那第二条把 60×60 的方块转到 315° 附近、同时放大到 1.44 倍，外接半径约 61 ——
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
    // 比过冲行宽得多：那三块方块里有两条要绕自己的中心旋转并放大，最大的外接半径约 61，而方块中心离左边
    // Inset + 30 —— 不留出来，方块在转到 45° 附近时会被台子的左边界裁掉一个角。
    internal const double LoadInset = 36d;

    // 过冲行的起边：那五条只有横向位移，不放大也不旋转，留一点边就够。
    internal const double BodyInset = 10d;

    // 采样器那一格元素区的宽度。
    // 比别的行窄，但行高是统一的：采样器那一格里没有"行程"，它只是一块按产物着色的 div（那个 div 自己的尺寸与
    // 落点在 Home.razor 的样式表里，与任何行程无关）。
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
    // 两段合成一处再交给标记：内联样式是按 ; 分段的，拼的时候少一个分隔符，后面那一段会整段作废 ——
    // 而浏览器对作废的那一段一声不响，元素于是没有宽度，看上去就是"这一行没在动"，与真的没跑起来在屏幕上
    // 分不开。分隔符因此留在这一处，标记那侧只写一个表达式。
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
    // 元素区的宽度。位移那两条要放得下整段行程（端点是 220，方块本身 60），不按原值留出位置的话，
    // 它一跑就整块滑出格子 —— 看上去和"没动"一模一样，正是这个演示要消除的错觉。
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
    // "启动"的令牌刻意不跟另外两条走同一套命名：验收套件点的就是它，点它要写闭式解载荷并起那条真动画。
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
