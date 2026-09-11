using VeloxDev.AT.Engine;

namespace VeloxDev.AT.Drivers;

/// <summary>
/// Drives the WinForms transition demo through UI Automation.
/// </summary>
/// <remarks>
/// The demo's observation surface is built at runtime in <c>Form1.Overshoot.cs</c>, not in the designer, so the
/// controls exist before the window is shown and readiness is purely the readout timer ticking. It is the one platform
/// whose token is the control's own <c>Name</c>: WinForms reports <c>Owner.Name</c> as the automation id, so there is no
/// attached property to set.
/// </remarks>
internal sealed class WinFormsDemoDriver : DemoDriverBase
{
    /// <summary>The name this platform is registered and filtered under.</summary>
    internal const string PlatformName = "WinForms";

    public override string Platform => PlatformName;

    /// <summary>The demo writes <c>v=1</c>; the suites check it before trusting any other field.</summary>
    public override int PayloadVersion => 1;

    protected override string ExecutablePath => AtConfig.DemoExecutable(
        @"Examples\Transition\WinForms\Demo\bin\Debug\net9.0-windows\Demo.exe");

    protected override string ReadyAutomationId => "over.state";

    protected override string PayloadAutomationId => "over.state";
}
