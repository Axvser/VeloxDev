namespace VeloxDev.AT.LoadMode;

/// <summary>
/// The MAUI demo's three load-mode shapes: what a test watches, and what they read at rest.
/// </summary>
/// <remarks>
/// The three animations are <c>Animation0/1/2</c> on <c>Rec0/1/2</c> — translate, rotate, and a composite of rotation,
/// translation and scale. MAUI has no <c>Transform</c> collection to report: where WPF reads a
/// <c>TranslateTransform</c> out of the render tree, this adapter's animation writes the shape's own
/// <c>TranslationX/TranslationY</c>, <c>RotationX/RotationY</c> and <c>Scale</c>, so those are the numerics.
/// <para>
/// The declared values are transcribed from the demo's own declarations of "what rest means", never from what a reset
/// happened to produce: <c>CreateRec0/1/2Reset</c> for every property the animations touch, and the <c>OnAppearing</c>
/// initialisation for the rest. The two agree by construction — an animation can only move a property its own reset
/// restores, and every other property is written once at startup and never again — which is why reporting all five
/// numerics for all three targets is sound rather than circular, and inert ones are kept deliberately so the three
/// targets read alike instead of only the one that moves that particular property.
/// </para>
/// <para>
/// Fills are described the way the demo describes them: a solid colour as <c>#rrggbb</c> in the uppercase the payload
/// writes, anything else by its type name. The two gradient brushes are therefore <c>LinearGradientBrush</c> — their
/// start and end points are animated on Rec0, and not distinguishing them from Rec2's is the point: the expectation
/// says "still a gradient", and the animation's own progress is what the payload's t-fields report.
/// </para>
/// </remarks>
internal static class MauiLoadMode
{
    internal const string Platform = "MAUI";

    internal static LoadModeEntry Entry { get; } = new(Platform, new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Rec0: Animation0 translates X and moves a gradient brush's start/end points.
        ["r0.x"] = "0",
        ["r0.y"] = "0",
        ["r0.rotx"] = "0",
        ["r0.roty"] = "0",
        ["r0.scale"] = "1",
        ["r0.fill"] = "LinearGradientBrush",
        ["r0.opacity"] = "1",

        // Rec1: Animation1 rotates X, after a two-second await.
        ["r1.x"] = "0",
        ["r1.y"] = "0",
        ["r1.rotx"] = "0",
        ["r1.roty"] = "0",
        ["r1.scale"] = "1",
        ["r1.fill"] = "#00FF00",
        ["r1.opacity"] = "1",

        // Rec2: Animation2 rotates X and Y, translates X and Y, scales, then recolours.
        ["r2.x"] = "0",
        ["r2.y"] = "0",
        ["r2.rotx"] = "0",
        ["r2.roty"] = "0",
        ["r2.scale"] = "1",
        ["r2.fill"] = "LinearGradientBrush",
        ["r2.opacity"] = "1",
    });
}
