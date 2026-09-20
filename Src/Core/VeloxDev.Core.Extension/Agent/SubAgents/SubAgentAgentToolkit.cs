using Microsoft.Extensions.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VeloxDev.AI.Pipelines;
using VeloxDev.AI.Workflow;

namespace VeloxDev.AI.SubAgents;

/// <summary>
/// The Agent-facing view of <see cref="SubAgentScope"/>: dispatch a background sub-agent, wait for it, read
/// what it concluded, survey the roster, and cancel one.
/// <para>
/// Reach the model through <see cref="SubAgentScope.CreateContextProvider"/>, which contributes these tools
/// already wrapped so they obey the composing host's policy. Use <see cref="CreateTools(ToolPipeline, AgentPipeline)"/>
/// directly only when assembling providers by hand; the parameterless <see cref="CreateTools()"/> returns
/// them unwrapped, with no marshalling or accounting.
/// </para>
/// <para>
/// All five are asynchronous in shape even where the work is immediate. That is the contract rather than an
/// implementation detail: <c>SpawnSubAgent</c> returns a handle and runs the child in the background, so a
/// caller never waits on a child inside a tool call, and a host reading the schema sees dispatch-and-poll
/// rather than call-and-block.
/// </para>
/// </summary>
public sealed class SubAgentAgentToolkit(SubAgentScope scope, WorkflowAgentScope host)
{
    private readonly SubAgentScope _scope = scope ?? throw new ArgumentNullException(nameof(scope));
    private readonly WorkflowAgentScope _host = host ?? throw new ArgumentNullException(nameof(host));

    /// <summary>
    /// The names of the tools this toolkit registers. Exposed so that a host composing several tool sources
    /// can classify them without repeating the literals — all five are read-only with respect to the
    /// workflow graph, so a workflow host must keep them out of its mutation budget and out of its dirty
    /// marking. The workflow toolkit reads them from here for exactly that.
    /// </summary>
    public static readonly string[] ToolNames =
        ["SpawnSubAgent", "WaitSubAgents", "GetSubAgentResult", "ListSubAgents", "CancelSubAgent"];

    /// <summary>
    /// Every tool this toolkit can offer, ignoring the host's switches. This is what a host UI enumerates to
    /// show the switchable surface; <see cref="CreateTools(ToolPipeline, AgentPipeline)"/> is what the model
    /// is shown.
    /// </summary>
    public IList<AITool> CreateAllTools()
    {
        return
        [
            AIFunctionFactory.Create(SpawnSubAgent, ToolNames[0]),
            AIFunctionFactory.Create(WaitSubAgents, ToolNames[1]),
            AIFunctionFactory.Create(GetSubAgentResult, ToolNames[2]),
            AIFunctionFactory.Create(ListSubAgents, ToolNames[3]),
            AIFunctionFactory.Create(CancelSubAgent, ToolNames[4]),
        ];
    }

    /// <summary>Creates the tools, unwrapped, omitting the ones the host switched off.</summary>
    public IList<AITool> CreateTools()
        => [.. CreateAllTools().Where(tool => _host.IsToolEnabled(tool.Name))];

    /// <summary>
    /// Creates the tools wrapped so that every call obeys <paramref name="tools"/> — marshalled onto the
    /// host's thread, gated against the same budget as every other call, and reported afterwards. This is
    /// what the context provider contributes.
    /// </summary>
    public IList<AITool> CreateTools(ToolPipeline tools, AgentPipeline? pipeline = null)
    {
        if (tools is null) throw new ArgumentNullException(nameof(tools));
        return [.. CreateTools().Select(tool =>
            tool is AIFunction function ? (AITool)new TrackedAIFunction(function, tools, pipeline) : tool)];
    }

    // ────────────────────────── the tools ──────────────────────────

    [Description("Dispatches a background sub-agent to carry out one task and returns immediately with its id — the agent runs while you carry on. "
        + "Its abilities are a narrowed subset of yours, and whatever you ask for that you do not have is refused and listed in the reply under \"dropped\": read that list, because the agent will not tell you. "
        + "Omit a capability argument to inherit the sensible default: the tool set defaults to your read-only tools, and every budget defaults to as much as you have left. "
        + "Name tools explicitly to grant anything that changes the graph. Afterwards use WaitSubAgents to collect its report.")]
    private Task<string> SpawnSubAgent(
        [Description("What the sub-agent must do. Write it as a complete, self-contained instruction — it cannot ask you questions.")] string task,
        [Description("A short display name, e.g. \"node-counter\". Omitted, one is generated.")] string? name = null,
        [Description("The exact tools the sub-agent may use. Omitted, it inherits your read-only tools only. An empty list grants no tools. Naming one you do not have, or one the host switched off, is refused and reported.")] string[]? allowedTools = null,
        [Description("The most tool calls the sub-agent and anything it dispatches may make in total. Omitted, up to as much of your remaining budget as can be granted.")] int? maxToolCalls = null,
        [Description("A separate cap on its read-only calls. Omitted, inherits yours.")] int? maxReadToolCalls = null,
        [Description("A separate cap on its calls that change the graph. Omitted, inherits yours.")] int? maxWriteToolCalls = null,
        [Description("Whether it may run node business code. Refused unless you have that ability yourself. Default false.")] bool? allowNodeExecution = null,
        [Description("Generic node commands it may run, e.g. \"ReceiveCommand\". Refused for any command you are not allowlisted for. Default none.")] string[]? allowedGenericCommands = null,
        [Description("Whether its graph edits should mark the graph dirty. Omitted, follows your setting.")] bool? autoMarkDirty = null,
        [Description("Free-form context for the sub-agent, appended to what it is told about itself.")] string? notes = null)
        => Task.FromResult(Spawn(new SubAgentRequest
        {
            Task = task,
            Name = name,
            AllowedTools = allowedTools,
            MaxToolCalls = maxToolCalls,
            MaxReadToolCalls = maxReadToolCalls,
            MaxWriteToolCalls = maxWriteToolCalls,
            AllowNodeExecution = allowNodeExecution,
            AllowedGenericCommands = allowedGenericCommands,
            AutoMarkDirty = autoMarkDirty,
            Notes = notes,
        }));

    private string Spawn(SubAgentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Task))
            return Refused("A sub-agent needs a task. Say what it should do and dispatch it again.");

        var id = _scope.TrySpawn(request, out var refusal);
        if (id is null || refusal is not null)
            return Refused(refusal ?? "The sub-agent was not dispatched.");

        var row = _scope.Snapshot.FirstOrDefault(s => s.Id == id);
        return new JObject
        {
            ["status"] = "ok",
            ["id"] = id,
            ["name"] = row?.Name ?? string.Empty,
            ["depth"] = row?.Depth ?? 0,
            ["maxToolCalls"] = row?.MaxToolCalls,
            ["grantedToolCount"] = row?.GrantedToolCount ?? 0,
            ["dropped"] = new JArray(row?.DroppedRequests ?? []),
            ["message"] = (row?.DroppedRequests.Count ?? 0) > 0
                ? "Dispatched, but not with everything you asked for — read \"dropped\". Call WaitSubAgents to collect its report."
                : "Dispatched. Call WaitSubAgents to collect its report.",
        }.ToString(Formatting.None);
    }

    [Description("Waits for sub-agents you dispatched to finish, then returns each one's state and the report of every one that completed. "
        + "Name specific ids to wait for those, or omit them to wait for every one still running. Returns early if the timeout elapses, marking which are still going. "
        + "Prefer one long wait over repeated short ones — each call costs you a tool call, and the sub-agents cost you none while they run.")]
    private async Task<string> WaitSubAgents(
        [Description("The ids to wait for, as returned by SpawnSubAgent. Omitted, every sub-agent still running is waited for.")] string[]? ids = null,
        [Description("How long to wait before returning anyway, in milliseconds. Default 60000. Use a longer value rather than polling in a loop.")] int? timeoutMs = null)
    {
        var timeout = Math.Max(0, timeoutMs ?? 60_000);
        var (rows, timedOut) = await _scope.WaitAsync(ids, timeout).ConfigureAwait(false);

        var agents = new JArray();
        foreach (var row in rows)
        {
            var agent = new JObject
            {
                ["id"] = row.Id,
                ["name"] = row.Name,
                ["depth"] = row.Depth,
                ["state"] = row.State.ToString(),
                ["stateText"] = row.StateText,
                ["callCount"] = row.CallCount,
            };

            if (row.Result is { Length: > 0 } result)
            {
                var (text, truncated) = Truncate(result, 4000);
                agent["result"] = text;
                if (truncated) agent["truncated"] = true;
            }
            if (row.Error is { Length: > 0 } error) agent["error"] = error;
            if (row.DroppedRequests.Count > 0) agent["dropped"] = new JArray(row.DroppedRequests);

            agents.Add(agent);
        }

        return new JObject
        {
            ["status"] = "ok",
            ["timedOut"] = timedOut,
            ["agents"] = agents,
            ["message"] = rows.Count == 0
                ? "No sub-agent matched. Nothing is running that you dispatched."
                : timedOut
                    ? "The timeout elapsed before every sub-agent finished; the ones still running are marked as such. Wait again to collect them."
                    : "Every sub-agent you waited for has finished.",
        }.ToString(Formatting.None);
    }

    [Description("Reads one sub-agent's current state and, once it has finished, its full report — untruncated, unlike the preview WaitSubAgents returns. "
        + "Use it to re-read a sub-agent you already collected, or to check on one you did not wait for.")]
    private Task<string> GetSubAgentResult(
        [Description("The id returned by SpawnSubAgent.")] string id)
        => Task.FromResult(Describe(_scope.GetResult(id), id));

    [Description("Lists the sub-agents you dispatched, with their state, depth, how many tool calls each has made, and a short preview of the task. "
        + "You see only the sub-agents you dispatched yourself — not those of other agents, and not the ones that dispatched you. Pure query.")]
    private Task<string> ListSubAgents()
    {
        var rows = _scope.List();
        var agents = new JArray();
        foreach (var row in rows)
        {
            var (task, _) = Truncate(row.Task, 120);
            agents.Add(new JObject
            {
                ["id"] = row.Id,
                ["name"] = row.Name,
                ["depth"] = row.Depth,
                ["state"] = row.State.ToString(),
                ["stateText"] = row.StateText,
                ["callCount"] = row.CallCount,
                ["task"] = task,
            });
        }

        return Task.FromResult(new JObject
        {
            ["status"] = "ok",
            ["count"] = rows.Count,
            ["running"] = rows.Count(r => r.IsRunning),
            ["agents"] = agents,
        }.ToString(Formatting.None));
    }

    [Description("Stops a sub-agent you dispatched. Its work up to that point is discarded and it reports as Cancelled rather than as a failure — a cancellation is not an error. "
        + "Use it when a sub-agent has become irrelevant or is taking longer than the task is worth.")]
    private Task<string> CancelSubAgent(
        [Description("The id returned by SpawnSubAgent.")] string id)
        => Task.FromResult(Describe(_scope.Cancel(id), id));

    // ────────────────────────── rendering ──────────────────────────

    /// <summary>
    /// The standing text that explains the subsystem to the model: what a sub-agent is, what the model keeps
    /// while one runs, and that its abilities can only be narrowed. Built from the tools the host actually
    /// offers, so a switched-off tool is not advertised.
    /// </summary>
    internal string BuildPromptContext()
    {
        var offered = new HashSet<string>(CreateTools().Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
        if (offered.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("## 子代理 / Sub-agents");
        sb.AppendLine();

        if (offered.Contains("SpawnSubAgent"))
        {
            sb.AppendLine("You may dispatch background sub-agents to work in parallel or to keep a long side task out of your own context.");
            sb.AppendLine("A sub-agent gets a narrowed subset of your abilities — never more — and anything you ask for beyond that is refused and listed in the dispatch reply under \"dropped\".");
            sb.AppendLine("It cannot ask you questions, so give it a complete instruction. Omitting a capability argument inherits your default: read-only tools, and as much budget as you have left.");
            sb.AppendLine("Dispatch, then collect with WaitSubAgents — do not poll in a loop.");
        }

        if (offered.Contains("ListSubAgents"))
            sb.AppendLine("You see only the sub-agents you dispatched yourself, never another agent's.");

        if (!_scope.CanSpawn)
            sb.AppendLine($"You are at the depth limit for sub-agents (depth {_scope.Depth}), so you cannot dispatch one — carry the work out yourself.");

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// The live block: what the model's own children are doing right now, and — for a spawned agent — the
    /// facts of its own dispatch that no provider of its own could otherwise know.
    /// </summary>
    internal string BuildRosterBlock()
    {
        var sb = new StringBuilder();

        var briefing = _scope.Briefing;
        if (briefing is not null) AppendBriefing(sb, briefing);

        var rows = _scope.Snapshot;
        if (rows.Count > 0)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine("### 你的子代理 / Your sub-agents");
            foreach (var row in rows)
            {
                var (task, _) = Truncate(row.Task, 100);
                sb.Append("- ").Append(row.Name)
                  .Append(" [").Append(row.Id[..8]).Append("] ")
                  .Append(row.StateText)
                  .Append(" · ").Append(row.CallCount).Append(" 次调用")
                  .Append(" · ").Append(task);

                if (row.Result is { Length: > 0 } result)
                {
                    var (preview, _) = Truncate(result.Replace('\n', ' '), 200);
                    sb.AppendLine().Append("  → ").Append(preview);
                }
                else if (row.Error is { Length: > 0 } error)
                {
                    sb.AppendLine().Append("  ! ").Append(error);
                }
                else
                {
                    sb.AppendLine();
                }
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static void AppendBriefing(StringBuilder sb, ChildBriefing briefing)
    {
        sb.AppendLine("## 关于你自己 / About you");
        sb.AppendLine();
        sb.AppendLine($"You are a background sub-agent at depth {briefing.Depth}.");
        if (briefing.MaxToolCalls is { } budget)
            sb.AppendLine($"You and anything you dispatch may make at most {budget} tool calls in total.");
        if (!string.IsNullOrWhiteSpace(briefing.Notes))
            sb.AppendLine($"The agent that dispatched you added: {briefing.Notes!.Trim()}");

        if (briefing.Dropped.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("It asked for these on your behalf and did not get them — you do not have them:");
            foreach (var line in briefing.Dropped) sb.Append("- ").AppendLine(line);
        }

        if (!briefing.CanSpawn)
            sb.AppendLine().AppendLine("You cannot dispatch sub-agents of your own: you are at the depth limit. Do the work yourself.");
    }

    /// <summary>One child, described for the two tools that report a single handle.</summary>
    private static string Describe(SubAgentSummary? row, string id)
    {
        if (row is null)
            return Refused($"No sub-agent with id '{id}' was dispatched by you. Call ListSubAgents to see the ones you dispatched.");

        var result = new JObject
        {
            ["status"] = "ok",
            ["id"] = row.Id,
            ["name"] = row.Name,
            ["depth"] = row.Depth,
            ["state"] = row.State.ToString(),
            ["stateText"] = row.StateText,
            ["callCount"] = row.CallCount,
            ["maxToolCalls"] = row.MaxToolCalls,
            ["grantedToolCount"] = row.GrantedToolCount,
        };

        if (row.DroppedRequests.Count > 0) result["dropped"] = new JArray(row.DroppedRequests);
        if (row.Result is { Length: > 0 }) result["result"] = row.Result;
        if (row.Error is { Length: > 0 }) result["error"] = row.Error;

        return result.ToString(Formatting.None);
    }

    /// <summary>
    /// Cuts a payload to fit a caller's context, saying whether it did. The marker is put inside the text
    /// rather than only in a flag: a model reading the value alone must be able to tell a complete report
    /// from one that was cut, or it will treat a truncated conclusion as the whole answer.
    /// </summary>
    private static (string Text, bool Truncated) Truncate(string text, int limit)
        => text.Length <= limit
            ? (text, false)
            : (text[..limit] + $"\n…[truncated: {text.Length - limit} more characters — call GetSubAgentResult for the whole report]", true);

    private static string Refused(string message)
        => JsonConvert.SerializeObject(new { status = "refused", message }, Formatting.None);
}
