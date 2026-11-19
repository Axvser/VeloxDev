using VeloxDev.AT.Engine;

namespace VeloxDev.AT.Drivers;

/// <summary>
/// Drives the Blazor transition demo through a headless browser.
/// </summary>
/// <remarks>
/// The demo's observation surface is the <c>over.*</c> block of <c>Home.razor</c> and the payload its readout timer
/// builds in <c>Home.razor.cs</c>. Controls carry a <c>data-at</c> token instead of an id, so the tokens below are read
/// as CSS attribute selectors — the demo deliberately has no ids and none are to be added.
/// <para>
/// The readout interval here is 50ms, unlike the 40ms the desktop demos use, and it is the demo's own number.
/// </para>
/// </remarks>
internal sealed class BlazorDemoDriver : DemoDriverBase
{
    /// <summary>The name this platform is registered and filtered under.</summary>
    internal const string PlatformName = "Blazor";

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
}
