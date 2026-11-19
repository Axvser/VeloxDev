using VeloxDev.AT.Engine;

namespace VeloxDev.AT.Drivers;

/// <summary>
/// Drives the WinUI transition demo through UI Automation.
/// </summary>
/// <remarks>
/// The observation surface is described in <c>MainWindow.xaml</c> and <c>MainWindow.xaml.cs</c>, reached by the
/// language-independent automation ids XAML sets with <c>AutomationProperties.AutomationId</c>.
/// <para>
/// The demo is unpackaged by design (<c>WindowsPackageType=None</c>), so it is a plain executable under
/// <c>bin\&lt;Configuration&gt;\&lt;tfm&gt;\win-x64\</c> rather than something reached through a package. Launching it
/// is therefore the same as any other desktop process, and no MSIX activation is involved.
/// </para>
/// </remarks>
internal sealed class WinUIDemoDriver : DemoDriverBase
{
    /// <summary>The name this platform is registered and filtered under.</summary>
    internal const string PlatformName = "WinUI";

    public override string Platform => PlatformName;

    /// <summary>The demo writes <c>v=1</c>; the suites check it before trusting any other field.</summary>
    public override int PayloadVersion => 1;

    /// <summary>
    /// The unpackaged demo's executable. The path carries both the target framework and the runtime identifier because
    /// <c>RuntimeIdentifier</c> defaults to <c>win-x64</c> for this project.
    /// </summary>
    protected override string ExecutablePath => AtConfig.DemoExecutable(
        @"Examples\Transition\WinUI\Demo\bin\Debug\net8.0-windows10.0.19041.0\win-x64\Demo.exe");

    protected override string ReadyAutomationId => "over.state";

    protected override string PayloadAutomationId => "over.state";
}
