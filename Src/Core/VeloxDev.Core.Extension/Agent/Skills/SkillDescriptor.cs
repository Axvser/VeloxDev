using VeloxDev.AI;

namespace VeloxDev.AI.Skills;

/// <summary>
/// What a source knows about a skill before its body is read: the frontmatter the model is advertised,
/// plus enough provenance to report it and to find the body again.
/// <para>
/// A source may yield several descriptors for the same <see cref="Name"/> — one per language — because
/// a skill is toggled as one concept while its text is rendered per prompt language.
/// </para>
/// </summary>
public sealed class SkillDescriptor
{
    /// <summary>Skill name. Must match the parent directory name for file skills (Agent Skills convention).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>One-line description used to match a request against the skill.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Which loader produced this descriptor.</summary>
    public SkillSourceKind Source { get; set; } = SkillSourceKind.Embedded;

    /// <summary>File path (file skills) or embedded resource name (embedded skills). Reporting only.</summary>
    public string? Path { get; set; }

    /// <summary>
    /// Language this descriptor's text is written in. <c>null</c> for language-neutral skills
    /// (file skills carry no language dimension).
    /// </summary>
    public AgentLanguages? Language { get; set; }

    /// <summary>
    /// Number of bundled resource files this skill ships (the <c>references/</c> tree for file skills).
    /// These are deliberately not read during discovery.
    /// </summary>
    public int ResourceCount { get; set; }

    /// <summary>
    /// Why this skill is unusable, or <c>null</c> when it is fine. A malformed skill is reported rather
    /// than thrown or dropped: one broken folder must not abort discovery of the others, and it must not
    /// disappear silently either.
    /// </summary>
    public string? Error { get; set; }
}
