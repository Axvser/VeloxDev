using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.AI.Pipelines;
using VeloxDev.AI.Workflow;

namespace VeloxDev.AI.SubAgents;

/// <summary>
/// Contributes the sub-agent subsystem to an agent invocation: what the model is told about dispatching
/// children, the tools that manage them, and — for an agent that was itself spawned — the facts of its own
/// dispatch.
/// <para>
/// The render is cached on the scope's version, which advances whenever the roster or any row changes, so a
/// child finishing reaches the model on the next turn without rebuilding the agent. There is no language
/// dimension: the subsystem's own text is written once in both languages rather than following the workflow
/// prompt language, because a sub-agent's briefing is read by the model and not by the user.
/// </para>
/// </summary>
public sealed class SubAgentAgentContextProvider : AIContextProvider
{
    private readonly SubAgentScope _scope;
    private readonly ToolPipeline _toolPipeline;
    private readonly AgentPipeline? _pipeline;
    private readonly string[] _stateKeys;
    private readonly object _gate = new();

    private long? _renderedFor;
    private string? _instructions;
    private IList<AITool>? _tools;

    /// <summary>Creates a provider over <paramref name="scope"/>.</summary>
    /// <param name="scope">The sub-agent subsystem to render and expose.</param>
    /// <param name="tools">
    /// How the contributed tools behave. Omit for standalone use — a thread-only policy is derived from the
    /// parent scope's own synchronization context.
    /// </param>
    /// <param name="pipeline">The chain the host's events travel on, if any.</param>
    public SubAgentAgentContextProvider(SubAgentScope scope, ToolPipeline? tools = null, AgentPipeline? pipeline = null)
    {
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _toolPipeline = tools ?? new ToolPipeline(marshalTo: () => scope.Parent?.UIContext);
        _pipeline = pipeline;

        // Keyed by this scope instance, so two providers over one scope collide loudly at agent
        // construction instead of silently contributing everything twice. A Guid rather than the workflow
        // scope's StateDiscriminator, which is derived from the tree: a parent and its child live on the
        // same tree, and would therefore collide on exactly the pair that must not.
        _stateKeys = [$"{nameof(SubAgentAgentContextProvider)}:{scope.InstanceId}"];
    }

    /// <inheritdoc />
    public override IReadOnlyList<string> StateKeys => _stateKeys;

    /// <inheritdoc />
    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context, CancellationToken cancellationToken)
        => new(BuildContext());

    /// <summary>
    /// Renders the current contribution, reusing the previous render while the scope's version is unchanged.
    /// Internal rather than private so the rendering contract can be tested without standing up a chat
    /// client.
    /// </summary>
    internal AIContext BuildContext()
    {
        // A subsystem nobody attached has no host to narrow against and no roster to contribute. Returning
        // an empty context rather than throwing keeps a host that built the provider before attaching it
        // working — the same tolerance the MCP provider shows for a scope with no servers.
        if (_scope.Parent is not { } host)
            return new AIContext();

        var version = _scope.Version;

        lock (_gate)
        {
            if (_renderedFor != version)
            {
                var toolkit = new SubAgentAgentToolkit(_scope, host);
                _instructions = BuildInstructions(toolkit);
                _tools = toolkit.CreateTools(_toolPipeline, _pipeline);
                _renderedFor = version;
            }

            // Instructions are transient per invocation and the tools are version-cached instances, so both
            // are handed back on every call — what is cached is the rendering, not the sending.
            return new AIContext
            {
                Instructions = _instructions,
                Tools = _tools,
            };
        }
    }

    private static string? BuildInstructions(SubAgentAgentToolkit toolkit)
    {
        var standing = toolkit.BuildPromptContext();
        var live = toolkit.BuildRosterBlock();

        var text = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(standing)) text.AppendLine(standing.TrimEnd());
        if (!string.IsNullOrWhiteSpace(live))
        {
            if (text.Length > 0) text.AppendLine();
            text.AppendLine(live.TrimEnd());
        }

        return text.Length == 0 ? null : text.ToString();
    }
}
