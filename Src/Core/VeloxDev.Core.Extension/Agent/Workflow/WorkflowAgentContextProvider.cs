using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VeloxDev.AI.Workflow;

/// <summary>
/// Supplies the Agent's per-invocation context from a <see cref="WorkflowAgentScope"/>, so that
/// everything the scope can be configured with — skills, the built-in tool set, developer-registered
/// tools, and the tools of every connected MCP server — reflects its current state on every turn
/// instead of being frozen when the agent was built.
/// <para>
/// <b>This provider is the sole source of tools.</b> The Agent Framework unions the tools a provider
/// contributes with whatever <c>ChatOptions.Tools</c> carries, and that union does not deduplicate by
/// name — supplying the same tool through both channels sends it to the model twice. Hosts that attach
/// this provider must therefore leave <c>ChatOptions.Tools</c> empty.
/// </para>
/// <para>
/// Instructions contributed here are appended to the agent's own instructions and are transient: they
/// apply to the invocation that requested them and are not retained. The cached render is therefore
/// handed back on every call — what is cached is the rendering work, never the decision to send.
/// </para>
/// </summary>
public sealed class WorkflowAgentContextProvider : AIContextProvider
{
    private readonly WorkflowAgentScope _scope;
    private readonly string[] _stateKeys;
    private readonly object _gate = new();

    private long? _renderedFor;
    private string? _instructions;
    private IReadOnlyList<AITool>? _tools;

    /// <summary>Creates a provider bound to <paramref name="scope"/>.</summary>
    /// <param name="scope">The scope whose current state each invocation is rendered from.</param>
    public WorkflowAgentContextProvider(WorkflowAgentScope scope)
    {
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));

        // The base implementation keys state by the concrete type name, and the Agent Framework throws
        // when two providers attached to one agent share a key. Two trees — each with its own scope and
        // provider — are an ordinary arrangement, so the key has to be per scope.
        _stateKeys = [$"{nameof(WorkflowAgentContextProvider)}:{_scope.StateDiscriminator}"];
    }

    /// <inheritdoc />
    public override IReadOnlyList<string> StateKeys => _stateKeys;

    /// <inheritdoc />
    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context, CancellationToken cancellationToken)
        => new(BuildContext());

    /// <summary>
    /// Renders the current context, reusing the previous render while the scope's version is unchanged.
    /// Internal rather than private so the rendering contract can be tested without standing up a chat
    /// client; the framework only ever reaches it through <see cref="ProvideAIContextAsync"/>.
    /// </summary>
    internal AIContext BuildContext()
    {
        // This scope's own version only. Skills and MCP are rendered by their own providers now, so their
        // versions cannot change anything this provider produces — keying on them would re-render a slice
        // that has not moved.
        var key = _scope.Version;

        lock (_gate)
        {
            if (_renderedFor != key)
            {
                _instructions = _scope.BuildDynamicInstructions();
                _tools = _scope.BuildDynamicTools();
                _renderedFor = key;
            }

            // Never return null — the framework dereferences the result. An AIContext with nothing set
            // is the documented no-op: it contributes nothing without clearing what came before.
            return new AIContext
            {
                Instructions = _instructions,
                Tools = _tools,
            };
        }
    }
}
