using VeloxDev.AT.Engine;
using VeloxDev.AT.Theory;

namespace VeloxDev.AT.Drivers;

/// <summary>
/// Drives the Avalonia transition demo through UI Automation.
/// </summary>
/// <remarks>
/// The demo's observation surface is described in <c>MainWindow.axaml</c> and <c>MainWindow.axaml.cs</c>: six buttons and
/// two readouts, all reached by language-independent automation ids, plus the <c>over.state</c> payload the suites
/// actually read. The scenario table below is a transcription of what those click handlers do — the durations, the
/// ranges and the curves are read out of the demo, not invented here.
/// </remarks>
internal sealed class AvaloniaDemoDriver : DemoDriverBase
{
    /// <summary>The name this platform is registered and filtered under.</summary>
    internal const string PlatformName = "Avalonia";

    /// <summary>
    /// The demo's readout interval. One 40ms <c>DispatcherTimer</c> writes both the human readout and the payload, and
    /// the interval is what the peak bound is derived from — see <see cref="ScalarScenario.ReadoutInterval"/>.
    /// </summary>
    private static readonly TimeSpan ReadoutInterval = TimeSpan.FromMilliseconds(40);

    public override string Platform => PlatformName;

    /// <summary>The demo writes <c>v=1</c>; the suites check it before trusting any other field.</summary>
    public override int PayloadVersion => 1;

    protected override string ExecutablePath => AtConfig.DemoExecutable(
        @"Examples\Transition\Avalonia\Demo\bin\Debug\net9.0\Demo.exe");

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
                Start = 0d,
                Target = 300d,
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
                Target = 300d,
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
            // 谁都不会先撞上 255 而被夹住；这正是本场景能观察到过冲的前提。
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
                Start = 80d,
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
            Duration = TimeSpan.FromMilliseconds(1100),
            BrushKey = "t3.cur",
        },
    ];
}
