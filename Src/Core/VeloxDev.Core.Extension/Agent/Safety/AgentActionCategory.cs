namespace VeloxDev.AI.Safety;

/// <summary>
/// What a tool <i>does</i>, which is what a permission mode is a policy about.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not <see cref="VeloxDev.AI.Workflow.Functions.WorkflowToolCategory"/>. That one is a
/// <b>surface filter</b> — a flags enum the host passes to <c>ProvideTools</c> to choose which groups are sent
/// to the model at all, tested bitwise and never consulted at call time. This one is a <b>classification</b>,
/// compared for equality and read when a call is about to run. Overloading one enum would make the same value
/// mean "was it sent" and "may it run", and the two answers differ: a tool can be sent and still be denied by
/// the mode.
/// </para>
/// <para>
/// A tool declares its category once, on the same line that registers it, so the declaration and the tool
/// cannot drift apart.
/// </para>
/// </remarks>
public enum AgentActionCategory
{
    /// <summary>Inspection: listing, detailing, searching, type schemas, analytics, snapshots. Cannot change anything.</summary>
    Read = 0,

    /// <summary>
    /// Changing the graph: creating, deleting, moving, connecting, patching. Undoable, because every one of
    /// them goes through the command stack.
    /// </summary>
    Edit = 1,

    /// <summary>
    /// Running something: node business code, a compiled chain, an allowlisted command, a spawned sub-agent.
    /// Not undoable — the side effects are whatever the code does.
    /// </summary>
    Execute = 2,

    /// <summary>
    /// Changing what the session may reach: adding, loading or reconfiguring an MCP server, switching
    /// capabilities. The only category whose effects outlive the conversation.
    /// </summary>
    Curate = 3,

    /// <summary>
    /// The asking mechanism itself — selecting an option, confirming an operation, asking for a larger call
    /// budget. Never put to the user, in any mode: a mode that made the Agent unable to ask would leave it
    /// able only to refuse.
    /// </summary>
    Interact = 4,
}
