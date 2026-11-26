using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace VeloxDev.AI;

/// <summary>
/// Provides access to Markdown files embedded inside the <c>VeloxDev.Core.Extension</c> assembly
/// under the canonical directory hierarchy:
/// <code>
///   Resources/{System}/en/Skills/{Name}.md
///   Resources/{System}/en/References/{Name}.md
///   Resources/{System}/Scripts/{Name}.*
/// </code>
///
/// <list type="bullet">
///   <item><c>Skills/</c>   — prompt fragments that describe how to use a specific feature.</item>
///   <item><c>References/</c> — factual reference material (coordinate system, channel types, …).</item>
///   <item><c>Scripts/</c>  — language-neutral instruction scripts injected verbatim.</item>
/// </list>
///
/// <para>
/// <b>English only.</b> The assembly ships one prompt corpus, under <c>en/</c>; there is no Chinese
/// variant to fall back from. Language parameters remain on the public methods for source compatibility
/// and do not change which file is read.
/// </para>
///
/// <para>
/// <b>Adding new files</b>: drop a <c>.md</c> file into the corresponding folder inside
/// <c>Resources/{System}/en/</c> — the <c>.csproj</c> glob picks it up automatically.
/// </para>
/// </summary>
public static class AgentEmbeddedResources
{
    private static readonly Assembly _assembly = typeof(AgentEmbeddedResources).Assembly;

    // The default namespace prefix inserted by MSBuild when embedding resources.
    // Matches the assembly name, which equals the project's <RootNamespace>.
    private const string Prefix = "VeloxDev.Core.Extension.";

    // ── Language helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// The folder code embedded resources live under. The assembly ships English prompt text only, so
    /// every language resolves to <c>"en"</c>.
    /// </summary>
    public static string ToLanguageCode(AgentLanguages language) => "en";

    // ── Skills ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the content of <c>Resources/{system}/en/Skills/{name}.md</c>.
    /// Returns <c>null</c> when it does not exist.
    /// </summary>
    public static string? ReadSkill(string system, string name, AgentLanguages language = AgentLanguages.English)
        => ReadCategoryFile(system, "Skills", name);

    /// <summary>
    /// Returns the base names of all skill files for the given system and language.
    /// </summary>
    public static IEnumerable<string> ListSkills(string system, AgentLanguages language = AgentLanguages.English)
        => ListCategory(system, "Skills", language);

    /// <summary>
    /// Returns the base names of the skill files that exist in the embedded corpus. The corpus has one
    /// language, so this answers the same set as <see cref="ListSkills"/>; the method remains for callers
    /// that attributed a skill to the language it was written in.
    /// </summary>
    public static IEnumerable<string> ListSkillsExact(string system, AgentLanguages language)
        => ListCategory(system, "Skills", language);

    /// <summary>
    /// Concatenates all skill files for the given system and language, separated by blank lines.
    /// </summary>
    public static string ReadAllSkills(string system, AgentLanguages language = AgentLanguages.English)
        => ReadAll(system, "Skills", language);

    // ── References ───────────────────────────────────────────────────────────

    /// <summary>
    /// Concatenates all reference files for the given system and language, separated by blank lines.
    /// </summary>
    public static string ReadAllReferences(string system, AgentLanguages language = AgentLanguages.English)
        => ReadAll(system, "References", language);

    // ── Safety ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the content of <c>Resources/{system}/en/Safety/{name}.md</c>.
    /// Returns <c>null</c> when it does not exist.
    /// </summary>
    public static string? ReadSafety(string system, string name, AgentLanguages language = AgentLanguages.English)
        => ReadCategoryFile(system, "Safety", name);

    // ── Core helpers ─────────────────────────────────────────────────────────

    // Resource path pattern: Resources.{system}.en.{category}.{name}.md
    private static string? ReadCategoryFile(string system, string category, string name)
        => Read($"Resources.{system}.en.{category}.{name}.md");

    /// <summary>Concatenates every embedded file in <paramref name="category"/>, separated by blank lines.</summary>
    private static string ReadAll(string system, string category, AgentLanguages language)
    {
        var sb = new System.Text.StringBuilder();
        var scanPrefix = $"{Prefix}Resources.{system}.en.{category}.";

        foreach (var resourceName in _assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(scanPrefix, StringComparison.Ordinal)) continue;
            var relative = resourceName.Substring(scanPrefix.Length); // e.g. "SlotEnumerator.md"
            if (!relative.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) continue;

            var content = Read(resourceName.Substring(Prefix.Length));
            if (!string.IsNullOrWhiteSpace(content))
            {
                sb.AppendLine(content);
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    /// <summary>Lists base names (without .md) of the embedded files in <paramref name="category"/>.</summary>
    private static IEnumerable<string> ListCategory(string system, string category, AgentLanguages language)
    {
        var scanPrefix = $"{Prefix}Resources.{system}.en.{category}.";
        foreach (var name in _assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(scanPrefix, StringComparison.Ordinal)) continue;
            var relative = name.Substring(scanPrefix.Length);
            yield return relative.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                ? relative.Substring(0, relative.Length - 3)
                : relative;
        }
    }

    /// <summary>
    /// Lists file names for <c>Resources/{system}/Scripts/</c> (language-neutral).
    /// </summary>
    /// <summary>
    /// Reads an embedded resource by its path relative to the assembly prefix.
    /// Returns <c>null</c> if not found.
    /// </summary>
    private static string? Read(string relativeName)
    {
        var fullName = Prefix + relativeName;
        using var stream = _assembly.GetManifestResourceStream(fullName);
        if (stream == null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
