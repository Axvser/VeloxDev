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

    /// <summary>
    /// The last render, or <c>null</c> before the first one. Read and written with <see cref="Volatile"/>
    /// so the whole render crosses threads as one unit — a bare <c>long</c> key beside a bare field would
    /// leave a window where a reader sees the new key and the old payload. A <see cref="Render"/> is never
    /// mutated once published, so a reader either sees all of it or none of it.
    /// </summary>
    private Render? _published;

    /// <summary>One turn's contribution, and the keys it was built for.</summary>
    private sealed class Render
    {
        /// <summary>The <see cref="WorkflowAgentScope.ContextKey"/> this was built for.</summary>
        public long Key;

        /// <summary>The <see cref="WorkflowAgentScope.Version"/> the tool list was built for.</summary>
        public long ToolsVersion;

        public IReadOnlyList<AITool>? Tools;

        public AIContext Context = null!;
    }

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
    /// Renders the current context, reusing the previous render while nothing it depends on has moved.
    /// Internal rather than private so the rendering contract can be tested without standing up a chat
    /// client; the framework only ever reaches it through <see cref="ProvideAIContextAsync"/>.
    /// <para>
    /// On an unchanged turn this takes no lock and allocates nothing — it returns the very same
    /// <see cref="AIContext"/> instance it returned last time. The framework reads that instance and builds
    /// its own from the parts, so handing back one object rather than a fresh three-field copy every turn
    /// is what keeps an idle agent off the GC entirely.
    /// </para>
    /// </summary>
    internal AIContext BuildContext()
    {
        // This scope's own key. Skills and MCP are rendered by their own providers now, so their versions
        // cannot change anything this provider produces — keying on them would re-render a slice that has
        // not moved. The key carries the budget-usage band as well as the version: the band is the one fact
        // that moves without a configuration change, and the envelope states it.
        var key = _scope.ContextKey;

        var published = Volatile.Read(ref _published);
        if (published is not null && published.Key == key) return published.Context;

        lock (_gate)
        {
            published = _published;
            if (published is null || published.Key != key)
            {
                published = BuildRender(key, published);
                Volatile.Write(ref _published, published);
            }

            // Never return null — the framework dereferences the result. An AIContext with nothing set
            // is the documented no-op: it contributes nothing without clearing what came before.
            return published.Context;
        }
    }

    private Render BuildRender(long key, Render? previous)
    {
        var version = _scope.Version;
        var instructions = _scope.BuildDynamicInstructions();

        // The tool list depends on the version alone, so a band-only move keeps the previous one rather
        // than rebuilding sixty-odd wrappers to say the same thing. Note the framework hands this exact
        // array to ChatOptions.Tools when this is the only provider on the agent, so it is shared with the
        // host and must not be mutated in place.
        var tools = previous is not null && previous.ToolsVersion == version
            ? previous.Tools
            : _scope.BuildDynamicTools();

        return new Render
        {
            Key = key,
            ToolsVersion = version,
            Tools = tools,
            Context = new AIContext { Instructions = instructions, Tools = tools },
        };
    }
}
