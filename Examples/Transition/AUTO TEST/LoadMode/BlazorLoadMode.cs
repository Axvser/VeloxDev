namespace VeloxDev.AT.LoadMode;

/// <summary>
/// The Blazor demo's three load-mode boxes: what a test watches, and what they read at rest.
/// </summary>
/// <remarks>
/// The three animations are <c>Animation0/1/2</c> on <c>Box0/1/2</c> — translate X and opacity, a delayed rotate and
/// scale, then a stitched move/recolour — with their own colours. The initial values are transcribed from
/// <c>CreateReset</c> and the demo's <c>Box0Color</c>/<c>Box1Color</c>/<c>Box2Color</c> constants, which together are
/// the demo's own declaration of "what rest means"; comparing a reset against what the reset itself produced would
/// say nothing.
/// <para>
/// One deliberate difference from the WPF table: the colour field is <c>color</c> rather than <c>fill</c>, because the
/// Razor adapter writes a CSS colour string into <c>BoxModel.Color</c> and there is no brush object to name. The value
/// is that string verbatim — the reset writes the literal back, so the comparison is exact rather than parsed.
/// </para>
/// </remarks>
internal static class BlazorLoadMode
{
    internal const string Platform = "Blazor";

    internal static LoadModeEntry Entry { get; } = new(Platform, new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["r0.x"] = "0",
        ["r0.rotate"] = "0",
        ["r0.scale"] = "1",
        ["r0.color"] = "#00bcd4",
        ["r0.opacity"] = "1",
        ["r1.x"] = "0",
        ["r1.rotate"] = "0",
        ["r1.scale"] = "1",
        ["r1.color"] = "#66bb6a",
        ["r1.opacity"] = "1",
        ["r2.x"] = "0",
        ["r2.rotate"] = "0",
        ["r2.scale"] = "1",
        ["r2.color"] = "#ab47bc",
        ["r2.opacity"] = "1",
    });
}
