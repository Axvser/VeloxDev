namespace VeloxDev.AI.Safety;

/// <summary>
/// How much the Agent may do before it has to ask, as one named mode rather than a dial setting.
/// </summary>
/// <remarks>
/// <para>
/// A mode is a <b>state the user can name and switch</b>, and every refusal says which one is in force and
/// what to switch to. That is the whole difference from the numbered level this replaces: a number could only
/// be described ("level 3"), so a model that hit it could say nothing better than "the host must change a
/// setting" — and the user had no switch to reach for.
/// </para>
/// <para>
/// Modes separate along the axes the actions actually differ in — reading, editing the graph, running it, and
/// changing what the session is allowed to reach — not along a single trust percentage. Editing the graph is
/// undoable through the command stack; running node business code is not; installing a server changes the
/// machine. <see cref="AutoEdit"/> exists at that first boundary.
/// </para>
/// <para>
/// A mode is the <i>coarse</i> layer. <see cref="AgentPermissionRule"/> entries sit beneath it and can be
/// stricter than any mode: a <c>Deny</c> rule holds even in <see cref="Bypass"/>.
/// </para>
/// <para>
/// Two ways in, and neither widens the Agent's reach. The host sets the mode outright
/// (<c>WithPermissionMode</c> / <c>SetPermissionMode</c>) and may do so at any moment — the gate reads it per
/// call, so the very next call is judged by the new mode, and the prompt catches up on the next turn. The
/// Agent has exactly two transitions of its own, both of which ask first: into <see cref="Plan"/> and back out
/// to the mode it left. There is deliberately no tool that moves it to a wider mode.
/// </para>
/// </remarks>
public enum AgentPermissionMode
{
    /// <summary>
    /// Read and propose only. Edits, execution and changes to what the session may reach are refused outright
    /// rather than offered — the mode is for surveying a graph and saying what should change. Asking the user
    /// something is still allowed: that is how a plan gets agreed to.
    /// </summary>
    Plan = 0,

    /// <summary>
    /// Every action that is not a read is put to the user first. The default, and the safest mode that can
    /// still get work done.
    /// </summary>
    Manual = 1,

    /// <summary>
    /// Graph edits run without asking; running node code, and anything that changes what the session may
    /// reach, still ask. The boundary is that an edit is a command on the undo stack while running a node is
    /// not.
    /// </summary>
    AutoEdit = 2,

    /// <summary>
    /// Reads, edits and execution all run; only changing what the session may reach (adding or reconfiguring
    /// an MCP server, say) still asks. For a host that trusts the conversation with its machine but not with
    /// its reach.
    /// </summary>
    Auto = 3,

    /// <summary>
    /// Nothing is asked. Explicit <c>Deny</c> rules still hold — this mode turns off the questions, not the
    /// host's stated prohibitions.
    /// </summary>
    Bypass = 4,
}
