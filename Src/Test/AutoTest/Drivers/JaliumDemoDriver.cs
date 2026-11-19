using VeloxDev.AT.Engine;

namespace VeloxDev.AT.Drivers;

/// <summary>
/// Drives the Jalium transition demo through UI Automation.
/// </summary>
/// <remarks>
/// The demo's observation surface is described in <c>MainWindow.cs</c> — it is code-only, with no XAML, so every
/// control is constructed in the constructor and carries its token through the same helper that sets both the element's
/// <c>Name</c> and its <c>AutomationProperties.AutomationId</c>.
/// </remarks>
internal sealed class JaliumDemoDriver : DemoDriverBase
{
    /// <summary>The name this platform is registered and filtered under.</summary>
    internal const string PlatformName = "Jalium";

    /// <summary>
    /// The demo's built executable, relative to the repository root. Shared with the locator probe, which starts the
    /// demo itself so it can describe the automation tree before a driver is trusted to find anything in it.
    /// </summary>
    internal const string RelativeExecutablePath =
        @"Examples\Transition\Jalium\Demo\bin\Debug\net10.0-windows\Demo.exe";

    public override string Platform => PlatformName;

    /// <summary>The demo writes <c>v=1</c>; the suites check it before trusting any other field.</summary>
    public override int PayloadVersion => 1;

    protected override string ExecutablePath => AtConfig.DemoExecutable(RelativeExecutablePath);

    protected override string ReadyAutomationId => "over.state";

    protected override string PayloadAutomationId => "over.state";
}
