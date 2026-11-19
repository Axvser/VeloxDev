namespace VeloxDev.AT.LoadMode;

/// <summary>
/// The Avalonia demo's three load-mode shapes: what a test watches, and what they read at rest.
/// </summary>
/// <remarks>
/// The three animations are <c>Animation0/1/2</c> on <c>Rec0/1/2</c> — translate X plus a gradient fill, a
/// transform group, and a translate/rotate-3D/scale group — each with its own fill. The initial values are
/// transcribed from <c>CreateResetRec0/1/2</c>, which is the demo's own declaration of "what rest means";
/// <c>Rec2</c>'s fill is a <c>LinearGradientBrush</c> (the <c>Bs1</c> resource rebuilt in code), so it is reported
/// by type name rather than as a colour, exactly as WPF's is.
/// </remarks>
internal static class AvaloniaLoadMode
{
    internal const string Platform = "Avalonia";

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
