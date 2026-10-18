using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace VeloxDev.AI;

/// <summary>
/// Provides access to Markdown files embedded inside the <c>VeloxDev.Core.Extension</c> assembly
/// under the canonical directory hierarchy:
/// <code>
///   Resources/{System}/{LangCode}/Skills/{Name}.md
///   Resources/{System}/{LangCode}/References/{Name}.md
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
/// <b>Adding new files</b>: drop a <c>.md</c> file into the corresponding folder inside
/// <c>Resources/{System}/{LangCode}/</c> — the <c>.csproj</c> glob picks it up automatically.
/// </para>
/// </summary>
public static class AgentEmbeddedResources
{
    private static readonly Assembly _assembly = typeof(AgentEmbeddedResources).Assembly;

    // The default namespace prefix inserted by MSBuild when embedding resources.
    // Matches the assembly name, which equals the project's <RootNamespace>.
    private const string Prefix = "VeloxDev.Core.Extension.";

    // ── Language helpers ─────────────────────────────────────────────────────

    private static string ToLangCode(AgentLanguages language)
        => language == AgentLanguages.Chinese ? "zh" : "en";

    /// <summary>
    /// The folder code an <see cref="AgentLanguages"/> maps to inside <c>Resources/{System}/</c>:
    /// <c>"zh"</c> or <c>"en"</c>. Only these two directories are embedded, so every other language
    /// resolves to <c>"en"</c>.
    /// </summary>
    public static string ToLanguageCode(AgentLanguages language) => ToLangCode(language);

    // ── Skills ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the content of <c>Resources/{system}/{lang}/Skills/{name}.md</c>.
    /// Falls back to the English variant when the requested language is not found.
    /// Returns <c>null</c> if neither variant exists.
    /// </summary>
    public static string? ReadSkill(string system, string name, AgentLanguages language = AgentLanguages.English)
        => ReadWithFallback(system, "Skills", name, language);

    /// <summary>
    /// Returns the base names of all skill files for the given system and language.
    /// </summary>
    public static IEnumerable<string> ListSkills(string system, AgentLanguages language = AgentLanguages.English)
        => ListCategory(system, "Skills", language);

    /// <summary>
    /// Returns the base names of the skill files that exist in <b>exactly</b> the given language, with no
    /// English fallback. Use this to attribute a skill to the language it is actually written in —
    /// <see cref="ListSkills"/> merges the requested language with English, so it cannot tell a
    /// translated file from a fallback one.
    /// </summary>
    public static IEnumerable<string> ListSkillsExact(string system, AgentLanguages language)
        => ListCategoryExact(system, "Skills", language);

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
    /// Reads the content of <c>Resources/{system}/{lang}/Safety/{name}.md</c>.
    /// Falls back to the English variant when the requested language is not found.
    /// Returns <c>null</c> if neither variant exists.
    /// </summary>
    public static string? ReadSafety(string system, string name, AgentLanguages language = AgentLanguages.English)
        => ReadWithFallback(system, "Safety", name, language);

    // ── Core helpers ─────────────────────────────────────────────────────────

    // Resource path pattern: Resources.{system}.{lang}.{category}.{name}.md
    private static string? ReadWithFallback(string system, string category, string name, AgentLanguages language)
    {
        var lang = ToLangCode(language);
        return Read($"Resources.{system}.{lang}.{category}.{name}.md")
            ?? (lang != "en" ? Read($"Resources.{system}.en.{category}.{name}.md") : null);
    }

    /// <summary>
    /// Concatenates all files in <paramref name="category"/> for the given system and language.
    /// Falls back to English for any file that lacks a localized variant.
    /// </summary>
    private static string ReadAll(string system, string category, AgentLanguages language)
    {
        var sb = new System.Text.StringBuilder();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Scan both the requested language and the "en" fallback directory
        var lang = ToLangCode(language);
        foreach (var langCode in lang == "en" ? new[] { "en" } : new[] { lang, "en" })
        {
            var scanPrefix = $"{Prefix}Resources.{system}.{langCode}.{category}.";
            foreach (var resourceName in _assembly.GetManifestResourceNames())
            {
                if (!resourceName.StartsWith(scanPrefix, StringComparison.Ordinal)) continue;
                var relative = resourceName.Substring(scanPrefix.Length); // e.g. "SlotEnumerator.md"
                if (!relative.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) continue;
                var baseName = relative.Substring(0, relative.Length - 3); // strip .md
                if (!seen.Add(baseName)) continue;

                var content = ReadWithFallback(system, category, baseName, language);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    sb.AppendLine(content);
                    sb.AppendLine();
                }
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Lists base names (without .md) for <c>Resources/{system}/{lang}/{category}/</c>.
    /// Falls back to "en" entries when the requested language has no files of its own.
    /// </summary>
    private static IEnumerable<string> ListCategory(string system, string category, AgentLanguages language)    {
        var lang = ToLangCode(language);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var langCode in lang == "en" ? new[] { "en" } : new[] { lang, "en" })
        {
            var scanPrefix = $"{Prefix}Resources.{system}.{langCode}.{category}.";
            foreach (var name in _assembly.GetManifestResourceNames())
            {
                if (!name.StartsWith(scanPrefix, StringComparison.Ordinal)) continue;
                var relative = name.Substring(scanPrefix.Length);
                var baseName = relative.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                    ? relative.Substring(0, relative.Length - 3)
                    : relative;
                if (seen.Add(baseName))
                    yield return baseName;
            }
        }
    }

    /// <summary>
    /// Lists base names (without .md) for <c>Resources/{system}/{lang}/{category}/</c> for one language
    /// only, applying no fallback.
    /// </summary>
    private static IEnumerable<string> ListCategoryExact(string system, string category, AgentLanguages language)
    {
        var langCode = ToLangCode(language);
        var scanPrefix = $"{Prefix}Resources.{system}.{langCode}.{category}.";
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
