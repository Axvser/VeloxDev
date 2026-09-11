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
        // Rec2 是上侧那排里唯一"渐变 → 渐变"的一块，而 Jalium 的交叉淡出是**逐色标插值**：
        // 飞行中的每一帧都必须还是一条渐变画刷。
        //
        // 这条断言同时挡住两种曾经真实发生过的退化：产出一个在这个框架上渲染不出来的合成画刷（整块消失），
        // 以及把两端各压成一个代表色、于是从首帧起就变成一块平的纯色。两者都只会在飞行中暴露 ——
        // 静止态与重置后的断言看不出区别。
        // 字面量取自 demo 的 Describe：纯色写成 #rrggbb，其余写成类型名 —— 本工程不引用 Jalium，拿不到 nameof。
        InFlight = payload => payload.Text("r2.fill") == "LinearGradientBrush"
            ? null
            : $"r2.fill 在飞行中是 {payload.Text("r2.fill")}，而两个渐变之间的交叉淡出每一帧都该仍是渐变",
    };
}
