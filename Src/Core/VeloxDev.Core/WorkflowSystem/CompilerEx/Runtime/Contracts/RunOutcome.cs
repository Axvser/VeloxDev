namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// How a compiled run ended — the precise reading of <see cref="IRuntimeContext.Status"/>, which has to share the
/// one word <c>"Stopped"</c> between a failure and a cancellation.
/// </summary>
/// <remarks>
/// <para>
/// The mapping the engine writes, over the two members that already existed:
/// </para>
/// <list type="bullet">
///   <item><description>
///   <see cref="Completed"/> — <c>Status == "Completed"</c>: every entry was walked. A branch that legitimately
///   ends the run early (a terminal route key) is a completion, not a failure.
///   </description></item>
///   <item><description><see cref="Cancelled"/> — the host's token ended it (<c>Status == "Stopped"</c>, <c>EndedWithError == false</c>).</description></item>
///   <item><description><see cref="Failed"/> — a node reported an error and the flow ended (<c>Status == "Stopped"</c>, <c>EndedWithError == true</c>).</description></item>
/// </list>
/// <para>
/// <b>There is deliberately no <c>Stopped</c> member</b>: every path that produces that status string is one of the
/// two below it, so an enum member for it would be unreachable.
/// </para>
/// </remarks>
public enum RunOutcome
{
    /// <summary>The run has not ended — it never started, or it is still going.</summary>
    Unknown = 0,

    /// <summary>Every entry was walked to the end without an error.</summary>
    Completed,

    /// <summary>The host cancelled it.</summary>
    Cancelled,

    /// <summary>A failure ended the flow early.</summary>
    Failed,
}
