using VeloxDev.AT.Engine;
using VeloxDev.AT.Theory;

namespace VeloxDev.AT.Drivers;

/// <summary>
/// Drives the Blazor transition demo through a headless browser.
/// </summary>
/// <remarks>
/// The demo's observation surface is the <c>over.*</c> block of <c>Home.razor</c> and the payload its readout timer
/// builds in <c>Home.razor.cs</c>. Controls carry a <c>data-at</c> token instead of an id, so the tokens below are read
/// as CSS attribute selectors — the demo deliberately has no ids and none are to be added. The scenario table is a
/// transcription of the click handlers: the durations, the ranges and the curves are read out of the demo, and its
/// numbers are its own (220 for the translate and the width, where the desktop demos use 300).
/// <para>
/// Two things are specific to this platform. The readout interval is 50ms, which is what the peak bound is derived
/// from. And the fill scenarios animate a CSS string rather than a brush, so mid-flight frames arrive as
/// <c>rgba(...)</c> and only the endpoints are <c>#rrggbb</c>; the sixth scenario is a colour saturation rather than a
/// non-solid fill for the same reason, registered under the same <c>brush</c> token so the two demos' surfaces line up.
/// </para>
/// </remarks>
internal sealed class BlazorDemoDriver : DemoDriverBase
{
    /// <summary>The name this platform is registered and filtered under.</summary>
    internal const string PlatformName = "Blazor";

    /// <summary>
    /// The demo's readout interval. One 50ms timer writes both the human readout and the payload, and the interval is
    /// what the peak bound is derived from — see <see cref="ScalarScenario.ReadoutInterval"/>.
    /// </summary>
    private static readonly TimeSpan ReadoutInterval = TimeSpan.FromMilliseconds(50);

    public override string Platform => PlatformName;

    /// <summary>The demo writes <c>v=1</c>; the suites check it before trusting any other field.</summary>
    public override int PayloadVersion => 1;

    protected override string ExecutablePath => AtConfig.DemoExecutable(
        @"Examples\Transition\Blazor\Demo\Demo\bin\Debug\net10.0\Demo.exe");

    // 页面上的把手都是 data-at，没有 id；BlazorHost 把它读成 [data-at="…"] 选择器。
    protected override string ReadyAutomationId => "over.state";

    protected override string PayloadAutomationId => "over.state";

    /// <summary>The browser host, which owns Kestrel and the page rather than a window.</summary>
    protected override IDemoHost CreateHost() => new BlazorHost();

    public override IReadOnlyList<ScenarioSpec> Scenarios { get; } =
    [
        new ScenarioSpec
        {
            Id = ScenarioId.Back,
            ButtonAutomationId = "over.btn.back",
            PayloadToken = "back",
            Duration = TimeSpan.FromMilliseconds(900),
            Scalar = new ScalarScenario
            {
                Ease = EaseKind.BackOut,
                Start = 0d,
                Target = 220d,
                PeakKey = "t0.peak",
                CurrentKey = "t0.cur",
                ReadoutInterval = ReadoutInterval,
            },
        },
        new ScenarioSpec
        {
            Id = ScenarioId.Elastic,
            ButtonAutomationId = "over.btn.elastic",
            PayloadToken = "elastic",
            Duration = TimeSpan.FromMilliseconds(1100),
            Scalar = new ScalarScenario
            {
                Ease = EaseKind.ElasticOut,
                Start = 0d,
                Target = 220d,
                PeakKey = "t0.peak",
                CurrentKey = "t0.cur",
                ReadoutInterval = ReadoutInterval,
            },
        },
        new ScenarioSpec
        {
            Id = ScenarioId.Color,
            ButtonAutomationId = "over.btn.color",
            PayloadToken = "color",
            Duration = TimeSpan.FromMilliseconds(900),
            // 目标色的每个通道都留了余量（58→128、110→128、165→208，上限 255），所以共享进度让三个通道一起过冲，
            // 谁都不会先撞上 255 而被夹住 —— 与桌面端同一个目标色，过冲只有靠读数才看得出来。
            Color = new ColorSpan(RgbColor.Parse("#3a6ea5"), RgbColor.Parse("#8080d0"), "t1.cur"),
        },
        new ScenarioSpec
        {
            Id = ScenarioId.Size,
            ButtonAutomationId = "over.btn.size",
            PayloadToken = "size",
            Duration = TimeSpan.FromMilliseconds(1100),
            // 宽度起点是条带自己的 60，不是 80：本平台的数字由本平台给。
            Scalar = new ScalarScenario
            {
                Ease = EaseKind.ElasticOut,
                Start = 60d,
                Target = 220d,
                PeakKey = "t2.peak",
                CurrentKey = "t2.cur",
                ReadoutInterval = ReadoutInterval,
            },
        },
        new ScenarioSpec
        {
            Id = ScenarioId.Brush,
            ButtonAutomationId = "over.btn.brush",
            PayloadToken = "brush",
            Duration = TimeSpan.FromMilliseconds(900),
            // 本平台没有刷子对象，非纯色场景以"颜色饱和"顶替：目标色把红通道顶到 255 上限，
            // 验证共享进度在边界停住而不是回绕。两端颜色记在 ColorSpan 里，因为颜色的起止就是它的位置。
            Color = new ColorSpan(RgbColor.Parse("#3a6ea5"), RgbColor.Parse("#f6e68c"), "t3.cur"),
            BrushKey = "t3.cur",
        },
    ];
}
