namespace VeloxDev.AT.LoadMode;

/// <summary>
/// The Jalium demo's three load-mode shapes: what a test watches, and what they read at rest.
/// </summary>
/// <remarks>
/// The initial values are transcribed from the demo's own <c>Reset()</c> — the only place it declares what rest is.
/// Note the fills are formatted upper-case here where WPF's are lower-case, because the two demos' <c>Describe</c>
/// helpers differ; the suite compares them exactly, as it must, since a colour is not a number.
/// </remarks>
internal static class JaliumLoadMode
{
    internal const string Platform = "Jalium";

    internal static LoadModeEntry Entry { get; } = new(Platform, new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["r0.x"] = "0",
        ["r0.fill"] = "#00FFFF",
        ["r1.x"] = "0",
        ["r1.fill"] = "#00FF00",
        ["r2.x"] = "0",
        ["r2.fill"] = "#FFA500",
    });
}
