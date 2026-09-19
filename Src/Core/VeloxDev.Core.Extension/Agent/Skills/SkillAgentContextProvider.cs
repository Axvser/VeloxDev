using Microsoft.Agents.AI;
using VeloxDev.AI.Pipelines;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.AI;

namespace VeloxDev.AI.Skills;

/// <summary>
/// Contributes a <see cref="SkillScope"/>'s current state to an agent invocation: the enabled skills'
/// text, and the tools that list, load, unload and read them.
/// <para>
/// This is what makes the skill subsystem usable on its own. Attach it to any agent and the model gains
/// skills; nothing about the workflow layer is involved.
/// </para>
/// <para>
/// The render is cached on the scope's version <i>and</i> its prompt language — the language is not part
/// of the version, so a host that changes it would otherwise keep the corpus it first rendered.
/// </para>
/// </summary>
public sealed class SkillAgentContextProvider : AIContextProvider
{
    private readonly SkillScope _scope;
    private readonly ToolPipeline _toolPipeline;
    private readonly AgentPipeline? _pipeline;
    private readonly SkillAgentToolkit _toolkit;
    private readonly string[] _stateKeys;
    private readonly object _gate = new();

    private (long Version, AgentLanguages Language)? _renderedFor;
    private string? _instructions;
    private IReadOnlyList<AITool>? _tools;

    /// <summary>Creates a provider over <paramref name="scope"/>.</summary>
    /// <param name="scope">The skill set to render.</param>
    /// <param name="policy">
    /// How the contributed tools behave. Omit for standalone use — a thread-only policy is derived from
    /// the scope's own synchronization context.
    /// </param>
    public SkillAgentContextProvider(SkillScope scope, ToolPipeline? tools = null, AgentPipeline? pipeline = null)
    {
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _toolPipeline = tools ?? new ToolPipeline(marshalTo: () => scope.UIContext);
        _pipeline = pipeline;
        _toolkit = new SkillAgentToolkit(scope) { Language = scope.PromptLanguage };

        // Keyed by the scope, so two providers over one scope collide loudly at agent construction
        // instead of silently contributing everything twice.
        _stateKeys = [$"{nameof(SkillAgentContextProvider)}:{scope.InstanceId}"];
    }

    /// <inheritdoc />
    public override IReadOnlyList<string> StateKeys => _stateKeys;

    /// <inheritdoc />
    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context, CancellationToken cancellationToken)
        => new(BuildContext());

    /// <summary>
    /// Renders the current skill contribution, reusing the previous render while nothing observable has
    /// changed. Internal rather than private so the rendering contract can be tested without standing up
    /// a chat client.
    /// </summary>
    internal AIContext BuildContext()
    {
        var language = _scope.PromptLanguage;
        var key = (_scope.Version, language);

        lock (_gate)
        {
            if (_renderedFor != key)
            {
                // Skill text is read in the same language the prompt is written in, so the toolkit has to
                // follow the scope rather than the language it was constructed with.
                _toolkit.Language = language;

                _instructions = BuildInstructions(language);
                _tools = [.. _toolkit.CreateTools(_toolPipeline, _pipeline)];
                _renderedFor = key;
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

    private string? BuildInstructions(AgentLanguages language)
    {
        var embedded = _scope.BuildEmbeddedBlock(language);
        var advertised = _scope.BuildAdvertisement(language);

        // Both empty means no skill is enabled: contribute nothing rather than an empty heading.
        if (string.IsNullOrWhiteSpace(embedded) && string.IsNullOrWhiteSpace(advertised)) return null;

        var text = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(embedded))
        {
            text.AppendLine(embedded.TrimEnd());
            text.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(advertised))
        {
            text.AppendLine(advertised.TrimEnd());
            text.AppendLine();
        }
        return text.ToString();
    }
}
