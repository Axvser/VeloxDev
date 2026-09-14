using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VeloxDev.AI;

namespace VeloxDev.AI.Skills;

/// <summary>
/// Discovers the prompt documents embedded in an assembly under
/// <c>Resources/{System}/{lang}/Skills/{Name}.md</c> — the architectural prompt text a package ships
/// for its own agent.
/// <para>
/// Unlike <see cref="FileSkillSource"/>, the same skill normally exists once per supported language, so
/// <see cref="Discover"/> yields one descriptor per (name, language). Names are taken from YAML
/// frontmatter when a file has it and derived from the file name otherwise, so documents that predate
/// the skill convention keep working.
/// </para>
/// <para>
/// Skills discovered here have no per-skill resources: the <c>References/</c> corpus is shared by all of
/// them and keeps its own dedicated injection path.
/// </para>
/// </summary>
public sealed class EmbeddedSkillSource : ISkillSource
{
    /// <summary>
    /// The languages embedded resources are stored under. <see cref="AgentEmbeddedResources"/> maps every
    /// language other than Chinese to the English directory, so only these two have distinct text.
    /// </summary>
    private static readonly AgentLanguages[] SupportedLanguages =
        [AgentLanguages.English, AgentLanguages.Chinese];

    private readonly string _system;
    private readonly Dictionary<string, List<AgentLanguages>> _skillLanguages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _skillBaseNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a source over the embedded skills of <paramref name="system"/> (e.g. <c>"Workflow"</c>).</summary>
    /// <param name="system">Resource-system folder name, matching <c>Resources/{system}/…</c>.</param>
    public EmbeddedSkillSource(string system)
    {
        if (string.IsNullOrWhiteSpace(system))
            throw new ArgumentException("A resource system name is required.", nameof(system));
        _system = system;
    }

    /// <inheritdoc />
    public SkillSourceKind Kind => SkillSourceKind.Embedded;

    /// <inheritdoc />
    public IReadOnlyList<SkillDescriptor> Discover()
    {
        _skillLanguages.Clear();
        _skillBaseNames.Clear();

        var found = new List<SkillDescriptor>();
        foreach (var language in SupportedLanguages)
        {
            foreach (var baseName in AgentEmbeddedResources.ListSkillsExact(_system, language))
            {
                var text = AgentEmbeddedResources.ReadSkill(_system, baseName, language);
                if (string.IsNullOrWhiteSpace(text)) continue;

                string name;
                string description;
                string? error = null;

                if (text!.TrimStart((char)0xFEFF).StartsWith("---", StringComparison.Ordinal))
                {
                    // Normalized file: frontmatter is authoritative. A malformed header is reported
                    // against the file-derived name so the skill stays identifiable.
                    if (!SkillFrontmatter.TryParse(text, out var fmName, out var fmDescription, out _, out var fmError))
                    {
                        name = fmName ?? ToKebabCase(baseName);
                        description = fmDescription ?? DeriveDescription(text);
                        error = fmError;
                    }
                    else
                    {
                        name = fmName!;
                        description = fmDescription!;
                    }
                }
                else
                {
                    // Pre-convention document: derive both from what is there.
                    name = ToKebabCase(baseName);
                    description = DeriveDescription(text);
                }

                if (!_skillLanguages.TryGetValue(name, out var languages))
                    _skillLanguages[name] = languages = [];
                if (!languages.Contains(language)) languages.Add(language);

                // Prefer a frontmatter-carrying file as the base name for body reads; otherwise keep the
                // first seen (the per-language content is what differs, not the file name).
                if (!_skillBaseNames.ContainsKey(name)) _skillBaseNames[name] = baseName;

                found.Add(new SkillDescriptor
                {
                    Name = name,
                    Description = description,
                    Source = SkillSourceKind.Embedded,
                    Path = $"Resources/{_system}/{AgentEmbeddedResources.ToLanguageCode(language)}/Skills/{baseName}.md",
                    Language = language,
                    Error = error,
                });
            }
        }

        return found;
    }

    /// <inheritdoc />
    public string? ReadBody(string skillName, AgentLanguages language)
    {
        if (_skillBaseNames.Count == 0) Discover();
        if (!_skillBaseNames.TryGetValue(skillName, out var baseName)) return null;

        var text = AgentEmbeddedResources.ReadSkill(_system, baseName, language);
        if (string.IsNullOrWhiteSpace(text)) return null;

        // Always take the parsed body rather than the raw text: the parse strips the frontmatter block
        // whenever it is present, even if the header itself is invalid, and the advertisement already
        // carries name and description.
        SkillFrontmatter.TryParse(text!, out _, out _, out var body, out _);
        return body;
    }

    /// <summary>Embedded skills carry no per-skill resources; the shared References corpus is separate.</summary>
    public string? ReadResource(string skillName, string relativePath, AgentLanguages language) => null;

    /// <summary>
    /// Turns a document file name into a skill name: <c>SlotEnumerator</c> → <c>slot-enumerator</c>.
    /// The result satisfies the kebab-case rule the Agent Skills convention requires.
    /// </summary>
    internal static string ToKebabCase(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var sb = new StringBuilder(value.Length + 4);
        for (int i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Derives a one-line description for a document that has no frontmatter: its first heading, with the
    /// decorative emoji and the leading "Skill:" label stripped. Falls back to the first non-empty line.
    /// </summary>
    internal static string DeriveDescription(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimStart((char)0xFEFF);
            if (line.Length == 0) continue;
            if (!line.StartsWith("#", StringComparison.Ordinal))
            {
                // Skip anything before the first heading (e.g. a stray rule line).
                if (line == "---") continue;
                return line;
            }

            var heading = line.TrimStart('#').Trim();
            heading = heading.Replace("🧭", string.Empty).Replace("🛡️", string.Empty)
                             .Replace("💡", string.Empty).Replace("⚠", string.Empty)
                             .Trim();
            foreach (var prefix in new[] { "Skill:", "Skill —", "Skill -" })
            {
                if (heading.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    heading = heading.Substring(prefix.Length).Trim();
                    break;
                }
            }
            if (heading.Length > 0) return heading;
        }
        return string.Empty;
    }
}
