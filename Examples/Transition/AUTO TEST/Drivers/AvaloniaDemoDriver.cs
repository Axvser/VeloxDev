using VeloxDev.AT.Engine;

namespace VeloxDev.AT.Drivers;

/// <summary>
/// Drives the Avalonia transition demo through UI Automation.
/// </summary>
/// <remarks>
/// The demo's observation surface is described in <c>Views\MainWindow.axaml</c> and its code-behind: reached by the
/// language-independent automation ids XAML sets with <c>AutomationProperties.AutomationId</c>. The view-model class
/// the XAML declares is an empty shell — all of the logic is in the code-behind.
/// </remarks>
internal sealed class AvaloniaDemoDriver : DemoDriverBase
{
    /// <summary>The name this platform is registered and filtered under.</summary>
    internal const string PlatformName = "Avalonia";

    public override string Platform => PlatformName;

    /// <summary>The demo writes <c>v=1</c>; the suites check it before trusting any other field.</summary>
    public override int PayloadVersion => 1;

    protected override string ExecutablePath => AtConfig.DemoExecutable(
        @"Examples\Transition\Avalonia\Demo\bin\Debug\net9.0\Demo.exe");

    protected override string ReadyAutomationId => "over.state";

    protected override string PayloadAutomationId => "over.state";
}
