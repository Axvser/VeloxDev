namespace VeloxDev.AT.LoadMode;

/// <summary>
/// The WPF demo's three load-mode shapes: what a test watches, and what they read at rest.
/// </summary>
/// <remarks>
/// The three animations are <c>Animation0/1/2</c> on <c>Rec0/1/2</c> — translate X, rotate, transform-group — with
/// their own fills and opacities. The initial values are transcribed from <c>CreateResetRec0/1/2</c>, which is the
/// demo's own declaration of "what rest means".
/// </remarks>
internal static class WpfLoadMode
{
    internal const string Platform = "WPF";

    internal static LoadModeEntry Entry { get; } = new(Platform, new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["r0.x"] = "0",
        ["r0.fill"] = "#00ffff",
        ["r0.opacity"] = "1",
        ["r1.x"] = "0",
        ["r1.fill"] = "#00ff00",
        ["r1.opacity"] = "1",
        ["r2.x"] = "0",
        ["r2.fill"] = "LinearGradientBrush",
        ["r2.opacity"] = "1",
    });
}
