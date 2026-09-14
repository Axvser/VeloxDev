using System.Collections.Generic;
using VeloxDev.AI;

namespace VeloxDev.AI.Skills;

/// <summary>
/// The seam that lets <see cref="SkillScope"/> treat embedded prompt documents and on-disk Agent Skills
/// directories uniformly.
/// <para>
/// Discovery is side-effect free, and the two sources differ in what it costs. A file skill's
/// <c>SKILL.md</c> is read in full — the frontmatter cannot be parsed without it — while its bundled
/// resources are only <i>enumerated</i> to be counted, never opened. What discovery deliberately never
/// does is produce a skill's text; that is <see cref="ReadBody"/>'s job, and it is what keeps a large
/// skill from costing anything until the model asks for it.
/// </para>
/// </summary>
public interface ISkillSource
{
    /// <summary>Which loader this is — decides the skill's injection mode.</summary>
    SkillSourceKind Kind { get; }

    /// <summary>
    /// Enumerates the skills this source can serve. May yield the same <see cref="SkillDescriptor.Name"/>
    /// more than once when the text exists in several languages.
    /// </summary>
    IReadOnlyList<SkillDescriptor> Discover();

    /// <summary>Reads a skill's full text for the requested language. Returns <c>null</c> when unavailable.</summary>
    /// <param name="skillName">Name from <see cref="Discover"/>.</param>
    /// <param name="language">Prompt language the text is needed in.</param>
    string? ReadBody(string skillName, AgentLanguages language);

    /// <summary>Reads one bundled resource of a skill, addressed relative to the skill directory.</summary>
    /// <param name="skillName">Name from <see cref="Discover"/>.</param>
    /// <param name="relativePath">Path inside the skill, e.g. <c>references/model.md</c>.</param>
    /// <param name="language">Prompt language the text is needed in.</param>
    string? ReadResource(string skillName, string relativePath, AgentLanguages language);
}
