namespace VeloxDev.AI.Skills;

/// <summary>
/// Lifecycle state of a discovered skill. Mirrors <c>McpServerStatus</c>, minus the
/// Installing/Connecting split — reading a skill's text has no multi-stage handshake to report.
/// </summary>
public enum SkillState
{
    /// <summary>Not discovered yet.</summary>
    NotStarted = 0,

    /// <summary>Being read from its source.</summary>
    Loading = 1,

    /// <summary>Discovered and readable. Whether it reaches the prompt is <see cref="SkillStatusViewModel.IsEnabled"/>.</summary>
    Ready = 2,

    /// <summary>Discovery or read failed (see <see cref="SkillStatusViewModel.Error"/>).</summary>
    Error = 3,
}
