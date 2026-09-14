using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.AI;

namespace VeloxDev.AI.MCP;

/// <summary>
/// Contributes a <see cref="McpScope"/>'s current state to an agent invocation: what the model is told
/// about the servers, the tools that manage them, and the tools of every connected server.
/// <para>
/// This is what makes the MCP subsystem usable on its own. Attach it to any agent and the model gains
/// MCP; nothing about the workflow layer is involved.
/// </para>
/// <para>
/// The render is cached on the scope's version: loading, adding or unloading a server advances it, so a
/// change reaches the model on the next turn without rebuilding the agent. MCP has no language dimension,
/// so the version alone is the whole key.
/// </para>
/// </summary>
public sealed class McpAgentContextProvider : AIContextProvider
{
    private readonly McpScope _scope;
    private readonly AgentToolPolicy _policy;
    private readonly string[] _stateKeys;
    private readonly object _gate = new();

    private long? _renderedFor;
    private string? _instructions;
    private IReadOnlyList<AITool>? _tools;

    /// <summary>Creates a provider over <paramref name="scope"/>.</summary>
    /// <param name="scope">The MCP servers to render and expose.</param>
    /// <param name="policy">
    /// How the contributed tools behave. Omit for standalone use — a thread-only policy is derived from
    /// the scope's own synchronization context.
    /// </param>
    public McpAgentContextProvider(McpScope scope, AgentToolPolicy? policy = null)
    {
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _policy = policy ?? new AgentToolPolicy { MarshalTo = () => scope.UIContext };

        // Keyed by the scope, so two providers over one scope collide loudly at agent construction
        // instead of silently contributing everything twice.
        _stateKeys = [$"{nameof(McpAgentContextProvider)}:{scope.InstanceId}"];
    }

    /// <summary>
    /// The toolkit for the current render.
    /// <para>
    /// Built per render rather than once: it captures the host's registered configurations and reads the
    /// self-service level, and a host is free to change either after this provider was constructed.
    /// </para>
    /// </summary>
    private McpAgentToolkit CreateToolkit() => new(_scope, _scope.RegisteredServers);

    /// <inheritdoc />
    public override IReadOnlyList<string> StateKeys => _stateKeys;

    /// <inheritdoc />
    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context, CancellationToken cancellationToken)
        => new(BuildContext());

    /// <summary>
    /// Renders the current MCP contribution, reusing the previous render while the scope's version is
    /// unchanged. Internal rather than private so the rendering contract can be tested without standing up
    /// a chat client.
    /// </summary>
    internal AIContext BuildContext()
    {
        var version = _scope.Version;

        lock (_gate)
        {
            if (_renderedFor != version)
            {
                var toolkit = CreateToolkit();
                _instructions = BuildInstructions(toolkit);
                _tools = BuildTools(toolkit);
                _renderedFor = version;
            }

            // Instructions are transient per invocation and the tools are version-cached instances, so
            // both are handed back on every call — what is cached is the rendering, not the sending.
            return new AIContext
            {
                Instructions = _instructions,
                Tools = _tools,
            };
        }
    }

    /// <summary>
    /// The management tools, plus the tools of every connected server — all wrapped so they obey the
    /// policy. The built-in tools are not included: they are the workflow layer's, and a host that wants
    /// both composes both providers.
    /// </summary>
    private IReadOnlyList<AITool> BuildTools(McpAgentToolkit toolkit)
    {
        var tools = new List<AITool>(toolkit.CreateTools(_policy));

        // Server tools are AIFunctions (McpClientTool derives from it), so they wrap the same way. A tool
        // that is not falls through unwrapped rather than being dropped.
        foreach (var tool in _scope.LoadedTools)
            tools.Add(tool is AIFunction function ? new TrackedAIFunction(function, _policy) : tool);

        return tools;
    }

    private string? BuildInstructions(McpAgentToolkit toolkit)
    {
        var inventory = _scope.BuildInventoryBlock();
        var description = toolkit.BuildPromptContext();

        // The description is always worth contributing — it is how the model learns these tools exist and
        // what the host lets it do with them. The inventory only once a server has been registered.
        var text = new StringBuilder();
        text.AppendLine(description.TrimEnd());
        if (!string.IsNullOrWhiteSpace(inventory))
        {
            text.AppendLine();
            text.AppendLine(inventory.TrimEnd());
        }
        return text.ToString();
    }
}
