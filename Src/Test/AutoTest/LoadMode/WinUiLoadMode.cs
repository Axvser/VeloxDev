namespace VeloxDev.AT.LoadMode;

/// <summary>
/// The WinUI demo's three load-mode shapes: what a test watches, and what they read at rest.
/// </summary>
/// <remarks>
/// Same field shape as <see cref="WpfLoadMode"/> — one translate X, one fill description and one opacity per shape —
/// so a reader comparing two platforms is comparing the same three things. What differs is which of them the
/// animation actually writes: WinUI's <c>Animation0/1/2</c> drive a <c>TranslateTransform</c> X on Rec0, a
/// <c>RotateTransform</c> on Rec1 and a <c>PlaneProjection</c> on Rec2, so only <c>r0.x</c> moves; the other two
/// report their fill instead, which changes type when the animation swaps the brush.
/// <para>
/// The values are transcribed from <c>CreateRec0Reset/1/2</c> plus the brush constructors they share — the demo's own
/// declaration of "what rest means" — and not from what a reset happens to produce at runtime. The fills are written
/// the way the demo's own <c>Describe</c> writes them: <c>#rrggbb</c> uppercase for a solid colour, the type name
/// otherwise.
/// </para>
/// </remarks>
internal static class WinUiLoadMode
{
    internal const string Platform = "WinUI";

    internal static LoadModeEntry Entry { get; } = new(Platform, new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["r0.x"] = "0",
        ["r0.fill"] = "LinearGradientBrush",
        ["r0.opacity"] = "1",
        ["r1.x"] = "0",
        ["r1.fill"] = "#00FF00",
        ["r1.opacity"] = "1",
        ["r2.x"] = "0",
        ["r2.fill"] = "LinearGradientBrush",
        ["r2.opacity"] = "1",
    });
}
