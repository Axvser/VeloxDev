using VeloxDev.AT.Engine;

namespace VeloxDev.AT.Drivers;

/// <summary>
/// Drives the WPF transition demo through UI Automation.
/// </summary>
/// <remarks>
/// The demo's observation surface is described in <c>MainWindow.xaml</c> and <c>MainWindow.xaml.cs</c>: reached by
/// language-independent automation ids, plus the <c>over.state</c> payload whose advancing sequence number is what
/// proves the demo is live.
/// </remarks>
internal sealed class WpfDemoDriver : DemoDriverBase
{
    /// <summary>The name this platform is registered and filtered under.</summary>
    internal const string PlatformName = "WPF";

    public override string Platform => PlatformName;

    /// <summary>The demo writes <c>v=1</c>; the suites check it before trusting any other field.</summary>
    public override int PayloadVersion => 1;

    protected override string ExecutablePath => AtConfig.DemoExecutable(
        @"Examples\Transition\WPF\Demo\bin\Debug\net9.0-windows\Demo.exe");

    protected override string ReadyAutomationId => "over.state";

    protected override string PayloadAutomationId => "over.state";
}
