using VeloxDev.AT.Engine;
using VeloxDev.AT.Theory;

namespace VeloxDev.AT.Drivers;

/// <summary>
/// Drives the MAUI transition demo through UI Automation, on its Windows head.
/// </summary>
/// <remarks>
/// The observation surface is described in <c>MainPage.xaml</c> and <c>MainPage.xaml.cs</c>: six overshoot buttons and
/// two readouts, reached by the automation ids the XAML puts on them, plus the <c>over.state</c> payload the suites
/// actually read. A MAUI Windows app is a WinUI 3 application underneath, so the same UI Automation client that drives
/// the WPF demo sees this one too. The scenario table below transcribes what the click handlers do — the durations, the
/// ranges and the curves come out of the demo, not out of this file.
/// </remarks>
internal sealed class MauiDemoDriver : DemoDriverBase
{
    /// <summary>The name this platform is registered and filtered under.</summary>
    internal const string PlatformName = "MAUI";

    /// <summary>
    /// The demo's readout interval. One 40ms dispatcher timer writes both the human readout and the payload, and the
    /// interval is what the peak bound is derived from — see <see cref="ScalarScenario.ReadoutInterval"/>.
    /// </summary>
    private static readonly TimeSpan ReadoutInterval = TimeSpan.FromMilliseconds(40);

    public override string Platform => PlatformName;

    /// <summary>The demo writes <c>v=1</c>; the suites check it before trusting any other field.</summary>
    public override int PayloadVersion => 1;

    protected override string ExecutablePath => AtConfig.DemoExecutable(
        @"Examples\Transition\MAUI\Demo\bin\Debug\net10.0-windows10.0.19041.0\win-x64\Demo.exe");

    protected override string ReadyAutomationId => "over.state";

    protected override string PayloadAutomationId => "over.state";

    /// <remarks>
    /// 这份表比 WPF 那份更值当的地方在于它走的是无优先级分支：<c>Transition&lt;T&gt;</c> 的优先级类型参数在这里填的是
    /// <c>NonPriority</c>（MAUI 没有 <c>DispatcherPriority</c>），所以这一套断言覆盖的是 <c>NonPriority</c> 采样路径，
    /// 而不是 WPF 那条带优先级的路径。
    /// </remarks>
    public override IReadOnlyList<ScenarioSpec> Scenarios { get; } =
    [
        // 位移场景：Over0 的 TranslationX，0 → 300。Back 与 Elastic 共用同一个目标，每次点击前 demo 自己把它同步归零。
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
            // 目标色每个通道都留了余量（58→128、110→128、165→208，上限 255），共享进度才能把三个通道一起推过目标；
            // 有通道先触顶被夹住的话色相就会偏，这也正是这个场景存在的意义。
            Color = new ColorSpan(RgbColor.Parse("#3a6ea5"), RgbColor.Parse("#8080d0"), "t1.cur"),
        },
        new ScenarioSpec
        {
            Id = ScenarioId.Size,
            ButtonAutomationId = "over.btn.size",
            PayloadToken = "size",
            Duration = TimeSpan.FromMilliseconds(1100),
            // MAUI 的 VisualElement 上没有可写的 Size 型属性，最接近的落点是 double 型的 WidthRequest：同样由
            // DoubleSampler 采样、上界不设限，高度不动，所以它仍然是"尺寸"这一路的可观察替身。
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
