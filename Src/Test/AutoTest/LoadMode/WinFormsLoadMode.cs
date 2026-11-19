namespace VeloxDev.AT.LoadMode;

/// <summary>
/// The WinForms demo's three load-mode shapes: what a test watches, and what they read at rest.
/// </summary>
/// <remarks>
/// The three animations are <c>Animation0/1/2</c> on <c>panel1/2/3</c> — move, enlarge, move-and-resize — with their
/// own back colours. The initial values are transcribed from <c>CreateReset1/2/3</c>, which is the demo's own
/// declaration of "what rest means".
/// <para>
/// The positions are the panels' rest offsets **within their row's stage** — the case list moves each panel into a
/// stage of its own, so the rest offset sits near that stage's origin rather than out in a full-size form. The
/// numbers move whenever the demo's geometry does; the transcription above is what keeps them honest.
/// </para>
/// <para>
/// The properties are the ones this adapter can actually interpolate, so the field names are this platform's own and
/// not the other demos': a WinForms control has no transform to read, and its colour is a
/// <c>System.Drawing.Color</c> rather than a brush, so <c>color</c> stands where the WPF table has <c>fill</c>.
/// <c>r0.parent</c> is there because
/// <c>Animation0</c> animates a nested path — the target's parent's background — which no other field would show.
/// </para>
/// </remarks>
internal static class WinFormsLoadMode
{
    internal const string Platform = "WinForms";

    internal static LoadModeEntry Entry { get; } = new(Platform, new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // panel1 — Animation0 slides it 200 to the right and paints it orange, plus its stage's own background.
        ["r0.x"] = "6",
        ["r0.y"] = "6",
        ["r0.w"] = "80",
        ["r0.h"] = "60",
        ["r0.color"] = "#ff0000",
        ["r0.parent"] = "#ffffff",

        // panel2 — Animation1 enlarges it to 150x96 and paints it light green; it never moves.
        ["r1.x"] = "6",
        ["r1.y"] = "6",
        ["r1.w"] = "80",
        ["r1.h"] = "60",
        ["r1.color"] = "#008000",

        // panel3 — Animation2 moves it to (170,6), resizes it to 120x96 and repaints it twice, then brings it back.
        ["r2.x"] = "6",
        ["r2.y"] = "6",
        ["r2.w"] = "80",
        ["r2.h"] = "60",
        ["r2.color"] = "#0000ff",
    });
}
