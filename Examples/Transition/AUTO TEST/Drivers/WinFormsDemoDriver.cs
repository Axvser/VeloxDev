using VeloxDev.AT.Engine;
using VeloxDev.AT.Theory;

namespace VeloxDev.AT.Drivers;

/// <summary>
/// Drives the WinForms transition demo through UI Automation.
/// </summary>
/// <remarks>
/// The demo's observation surface is built at runtime in <c>Form1.Overshoot.cs</c>, not in the designer, so the strip's
/// controls exist before the window is shown and readiness is purely the readout timer ticking. The numbers below are
/// the demo's own and differ from the WPF ones: the displacement animates the absolute <c>Location</c> from 38 to 188,
/// so the target is 188 and not the 150 it travels, and the size runs 60 to 110. The scenario table is a transcription
/// of what those click handlers do, not an invention.
/// </remarks>
internal sealed class WinFormsDemoDriver : DemoDriverBase
{
    /// <summary>The name this platform is registered and filtered under.</summary>
    internal const string PlatformName = "WinForms";

    /// <summary>
    /// The demo's readout interval. One 40ms timer writes both the human readout and the payload, and the interval is
    /// what the peak bound is derived from — see <see cref="ScalarScenario.ReadoutInterval"/>.
    /// </summary>
    private static readonly TimeSpan ReadoutInterval = TimeSpan.FromMilliseconds(40);

    /// <summary>
    /// The displacement's start, as the absolute <c>Location.X</c> the animation writes. The demo's strip element sits
    /// at <c>(38, 640)</c> and moves by 150, so the payload reports 38 and the target 188.
    /// </summary>
    private const double MoveStart = 38d;

    /// <summary>The displacement's target — the absolute location, not the 150 travelled.</summary>
    private const double MoveTarget = 188d;

    /// <summary>The square's edge before and after, both animated as one scalar.</summary>
    private const double SizeStart = 60d;

    /// <summary>The square's edge at rest.</summary>
    private const double SizeTarget = 110d;

    public override string Platform => PlatformName;

    /// <summary>The demo writes <c>v=1</c>; the suites check it before trusting any other field.</summary>
    public override int PayloadVersion => 1;

    protected override string ExecutablePath => AtConfig.DemoExecutable(
        @"Examples\Transition\WinForms\Demo\bin\Debug\net9.0-windows\Demo.exe");

    protected override string ReadyAutomationId => "over.state";

    protected override string PayloadAutomationId => "over.state";

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
                Start = MoveStart,
                Target = MoveTarget,
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
                Start = MoveStart,
                Target = MoveTarget,
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
            // 谁都不会先撞上 255 而被钳住；这正是本场景能观察到过冲的前提。
            Color = new ColorSpan(RgbColor.Parse("#3a6ea5"), RgbColor.Parse("#8080d0"), "t1.cur"),
        },
        new ScenarioSpec
        {
            Id = ScenarioId.Size,
            ButtonAutomationId = "over.btn.size",
            PayloadToken = "size",
            Duration = TimeSpan.FromMilliseconds(1100),
            Scalar = new ScalarScenario
            {
                Ease = EaseKind.ElasticOut,
                Start = SizeStart,
                Target = SizeTarget,
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
            // WinForms 的 BackColor 只有纯色，没有渐变插值，跨框架的「非纯色刷」在这里无法表达：demo 用同一条
            // BoundedProgress 饱和路径替代 —— 目标色 R 顶到 255，最先到界的通道把整组进度钉在端点。所以本场景
            // 没有过冲，套件断言的是"任何通道都不越过目标、并在 done=1 时精确落在目标上"。起点/目标/字段名由
            // ColorSpan 承载，套件据此断言，不再自己写一份常数。
            Color = new ColorSpan(RgbColor.Parse("#3a6ea5"), RgbColor.Parse("#ff80d0"), "t3.cur"),
            BrushKey = "t3.cur",
        },
    ];
}
