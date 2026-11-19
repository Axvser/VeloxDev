using VeloxDev.AT.Engine;

namespace VeloxDev.AT.Drivers;

/// <summary>
/// Drives the MAUI transition demo through UI Automation, on its Windows head.
/// </summary>
/// <remarks>
/// The observation surface is described in <c>MainPage.xaml</c> and <c>MainPage.xaml.cs</c>, reached by the
/// <c>AutomationId</c> property the XAML puts on each control — MAUI's own property, not the attached one WPF and
/// WinUI use. A MAUI Windows app is a WinUI 3 application underneath, so the same UI Automation client that drives the
/// WPF demo sees this one too.
/// </remarks>
internal sealed class MauiDemoDriver : DemoDriverBase
{
    /// <summary>The name this platform is registered and filtered under.</summary>
    internal const string PlatformName = "MAUI";

    public override string Platform => PlatformName;

    /// <summary>The demo writes <c>v=1</c>; the suites check it before trusting any other field.</summary>
    public override int PayloadVersion => 1;

    protected override string ExecutablePath => AtConfig.DemoExecutable(
        @"Examples\Transition\MAUI\Demo\bin\Debug\net10.0-windows10.0.19041.0\win-x64\Demo.exe");

    protected override string ReadyAutomationId => "over.state";

    protected override string PayloadAutomationId => "over.state";
}
