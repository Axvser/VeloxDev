namespace VeloxDev.AI.Skills;

/// <summary>
/// Where a skill came from. This drives its injection mode (see <see cref="SkillScope"/>):
/// <see cref="Embedded"/> skills are architectural prompt text and are always injected in full;
/// <see cref="File"/> skills follow the Agent Skills convention and are advertised only, with their
/// body loaded on demand.
/// </summary>
public enum SkillSourceKind
{
    /// <summary>Shipped inside the library assembly under <c>Resources/{System}/{lang}/Skills/</c>.</summary>
    Embedded = 0,

    /// <summary>Discovered on disk under a skill root directory, as <c>&lt;root&gt;/&lt;name&gt;/SKILL.md</c>.</summary>
    File = 1,
}
