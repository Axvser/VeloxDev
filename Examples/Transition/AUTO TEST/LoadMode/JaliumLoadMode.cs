namespace VeloxDev.AT.LoadMode;

/// <summary>
/// The Jalium demo's three load-mode shapes: what a test watches, and what they read at rest.
/// </summary>
/// <remarks>
/// The initial values are transcribed from the demo's own <c>Reset()</c> — the only place it declares what rest is.
/// Note the fills are formatted upper-case here where WPF's are lower-case, because the two demos' <c>Describe</c>
/// helpers differ; the suite compares them exactly, as it must, since a colour is not a number.
/// </remarks>
internal static class JaliumLoadMode
{
    internal const string Platform = "Jalium";

    internal static LoadModeEntry Entry { get; } = new(Platform, new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["r0.x"] = "0",
        ["r0.fill"] = "#00FFFF",
        ["r1.x"] = "0",
        ["r1.fill"] = "#00FF00",
        ["r2.x"] = "0",
        // Rec2 起止都是渐变：它是上侧那排里唯一跑"渐变 → 渐变"的一块，走的是画刷交叉淡出那条路。
        ["r2.fill"] = "LinearGradientBrush",
    })
    {
        // Jalium 的画刷交叉淡出**按设计**只能产出纯色（两条"真正两层叠加"的路在这个框架上都渲染不出来，
        // 见适配器里的说明）。所以飞行中 r2.fill 必须是一个颜色：它一旦是某个画刷类型名，就说明淡出没有发生
        // —— 那正是这个 demo 之前整块消失的形态。这条比"状态变了"严：退回旧行为时，只有它变红。
        InFlight = payload => payload.Text("r2.fill").StartsWith('#')
            ? null
            : $"r2.fill 在飞行中是 {payload.Text("r2.fill")}，而交叉淡出必须产出纯色",
    };
}
