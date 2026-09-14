using System;
using System.Threading;
using System.Threading.Tasks;

namespace VeloxDev.AI;

/// <summary>
/// What an owner of tools wants done around every call to them: which thread the call runs on, whether
/// it may run at all, and what to do once it has.
/// <para>
/// The point of the seam is that a tool's <i>behaviour</i> belongs to whoever assembled it, not to the
/// tool. A subsystem used on its own supplies only <see cref="UIContext"/> — its tools are marshalled and
/// nothing else. The same subsystem composed into a larger agent is handed the larger agent's policy, so
/// its tools are still counted, still reported, and still mark state dirty. Neither the subsystem nor the
/// wrapper needs to know which of the two it is in.
/// </para>
/// <para>
/// Every hook is optional; an empty policy means "run the tool as called, on this thread, and leave it
/// alone afterwards".
/// </para>
/// </summary>
public sealed class AgentToolPolicy
{
    /// <summary>
    /// Resolves the thread every tool call is marshalled onto, or <c>null</c> to run on the caller's
    /// thread.
    /// <para>
    /// A delegate rather than a value because the context is decided by the host, and a host is free to
    /// register it after the policy — or after the agent — was built. It is read once per call.
    /// </para>
    /// <para>
    /// When it resolves to a context, the <i>whole</i> call moves — including everything a tool does
    /// after an <c>await</c>. See <c>TrackedAIFunction</c> for why that matters and what it costs.
    /// </para>
    /// </summary>
    public Func<SynchronizationContext?>? MarshalTo { get; set; }

    /// <summary>
    /// Consulted before the tool runs. Return a message to refuse the call — it becomes the tool's error
    /// result, and the tool is not invoked — or <c>null</c> to allow it. Call budgets are enforced here.
    /// </summary>
    public Func<string, string?>? Refuse { get; set; }

    /// <summary>
    /// Invoked after a tool completes successfully, with the tool's name and its result rendered as text.
    /// Runs on the marshalled thread, and is awaited, so call counting, host callbacks and dirty marking
    /// all happen before the call is handed back. A tool that throws does not reach this hook.
    /// </summary>
    public Func<string, string, Task>? AfterCall { get; set; }
}
