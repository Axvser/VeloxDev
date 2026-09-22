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
        + "Use it for work whose material you do not want in your own context: anything long, or anything that reads a great deal and concludes briefly. "
        + "The sub-agent gets everything you have unless you narrow it, and whatever you ask for that you do not have is refused and listed in the reply under \"dropped\": read that list, because the agent will not tell you. "
        + "Omit a capability argument to inherit it — tools, skills and MCP servers all default to the ones you have, and every budget to as much as you have left. Name them explicitly to grant less. "
        + "Afterwards use WaitSubAgents to collect its report.")]
    private Task<string> SpawnSubAgent(
        [Description("What the sub-agent must do. Write it as a complete, self-contained instruction — it cannot ask you anything, so whatever it needs must be in the task itself.")] string task,
        [Description("A short title for this task, as the user should see it on the host's sub-agent panel. Not an identifier: name what the sub-agent is doing, in a few words, in the language the user is writing in — \"统计节点数\", not \"node-counter\" and not a sentence. Omitted, one is generated.")] string? name = null,
        [Description("The exact tools the sub-agent may use. Omitted, it inherits every tool you currently have. An empty list grants no tools. Naming one you do not have, or one the host switched off, is refused and reported.")] string[]? allowedTools = null,
        [Description("The skills the sub-agent may read, by the names ListSkills reports. Omitted, it inherits the skills you have switched on. An empty list grants none, and its skill tools go with them — there would be nothing left for them to load.")] string[]? allowedSkills = null,
        [Description("The MCP servers the sub-agent may use, by the names ListMcpServers reports. Omitted, it inherits the servers you have connected and switched on. Each granted server arrives with only the tools you yourself have switched on, and the sub-agent can use it but cannot load, unload or add one.")] string[]? allowedMcpServers = null,
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
            AllowedSkills = allowedSkills,
            AllowedMcpServers = allowedMcpServers,
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
            ["grantedSkillCount"] = row?.GrantedSkillCount ?? 0,
            ["grantedMcpServerCount"] = row?.GrantedMcpServerCount ?? 0,
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
    /// while one runs, when delegating is required rather than merely allowed, and what a spawn hands down.
    /// Built from the tools the host actually offers, so a switched-off tool is not advertised.
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
            // The mandate leads, and it is written as a rule about kinds of work rather than as a permission.
            // "You may dispatch" answers a question the model is not asking — it states that nothing forbids
            // delegating, which it would have assumed anyway. What it does not supply is when delegating is
            // *required*, and without that a model does the work itself: that costs one turn, is never wrong,
            // and never has to be reasoned about — unless the cost of doing it is named. So the rule is stated
            // with the kind of work it covers, and the reason is the reason the model will act on.
            //
            // The escape clause used to read "only when it is one call you already know how to make", and that
            // is what a model reads as its licence: every read is a call it already knows how to make, so the
            // exception swallowed the rule. Measured against a six-chapter corpus it made all six calls in its
            // own context and dispatched nothing (SubAgentLiveTests). The threshold is now about the *work*
            // rather than the difficulty of each step — anything meant to gather material goes to a child, and
            // what stays is the single value read off one call.
            sb.AppendLine("**Before you read anything, ask whether it belongs in your context.** Work whose purpose is to gather material rather than to act — a web search, reading a document, surveying several files, working through a library — costs a great deal and ends in something short, so it must be dispatched to a sub-agent rather than done by you. This holds however easy each call looks: a lookup you already know how to make is still material you will never use again, and reading it yourself spends the context you need for the task you were actually given. Do such work yourself only when the entire answer is one value read off a single call and quoted as it stands.");
            sb.AppendLine("Two things make it worth it here. Whatever the sub-agent reads stays with it: its tool calls and its reasoning never reach you, and you get its final reply and nothing else. And it runs while you do not wait — dispatch several when the work divides, and carry on with something else meanwhile.");
            sb.AppendLine("What it gets is everything you have, unless you narrow it: name tools, skills or MCP servers to grant less, and omit them to hand down the lot. Anything you ask for that you do not hold is refused and listed in the dispatch reply under \"dropped\"; read that list, because the agent will not tell you.");
            // The title is the one argument whose consumer is neither the model nor the child but the person
            // watching the panel, so nothing the model can observe would tell it that "node-counter" is the
            // wrong register. Left to the schema alone, that argument goes unfilled — and an unfilled one is
            // silent, not an error, which is the failure mode this whole paragraph exists to pre-empt.
            sb.AppendLine("Title each one with `name`: it is what the user reads on the panel, so it wants the work in a few words rather than a sentence or an identifier. Omitted, the child is listed under a generated number instead.");
            // "It cannot ask you questions" was true until the child started inheriting the host's interaction
            // configuration (SubAgentScope.cs:514). It still cannot ask *you* — nothing it says reaches you but
            // its final reply — but it may now put a question to the user directly. Leaving the old sentence
            // would have been the same defect the grant list is careful about: prose that promises a tool is
            // absent while the tool is on the child's surface.
            sb.AppendLine("It cannot ask you anything, and nothing it says reaches you but its final reply, so give it a complete instruction — one that can be finished without your judgement. (It may put a question to the user directly if the host allows that; it will never put one to you.) A sub-agent may dispatch sub-agents of its own on the same terms and with what it was given.");
            sb.AppendLine("Budgets default to as much of yours as can be granted, and every sub-agent spends from the same allowance you do — the tree has one pot, not one per agent.");
            sb.AppendLine("Then collect with WaitSubAgents — do not poll in a loop, and do not do the delegated work yourself as well.");
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
        if (briefing is not null) AppendBriefing(sb, briefing, MayDispatch);

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

    private static void AppendBriefing(StringBuilder sb, ChildBriefing briefing, bool mayDispatch)
    {
        sb.AppendLine("## 关于你自己 / About you");
        sb.AppendLine();
        sb.AppendLine($"You are a background sub-agent at depth {briefing.Depth}.");
        if (briefing.MaxToolCalls is { } budget)
            sb.AppendLine($"You and anything you dispatch may make at most {budget} tool calls in total.");
        if (!string.IsNullOrWhiteSpace(briefing.Notes))
            sb.AppendLine($"The agent that dispatched you added: {briefing.Notes!.Trim()}");

        // Skills and servers are named here only when the spawn narrowed them. They are then part of what
        // this spawn granted rather than of the host's standing configuration — a child given two of its
        // parent's nine skills has no other way to learn which two — and when nothing was narrowed the
        // child's own skill and MCP providers already say what they contribute, so reading out the roster
        // again is pure length on every turn.
        if (briefing.Narrowed && briefing.GrantedSkills.Count > 0)
            sb.AppendLine().AppendLine("Skills you may read, and no others: " + string.Join(", ", briefing.GrantedSkills) + ".");
        if (briefing.Narrowed && briefing.GrantedMcpServers.Count > 0)
            sb.AppendLine().AppendLine("MCP servers you may use: " + string.Join(", ", briefing.GrantedMcpServers) + ".");

        if (briefing.Dropped.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("It asked for these on your behalf and did not get them — you do not have them:");
            foreach (var line in briefing.Dropped) sb.Append("- ").AppendLine(line);
        }

        if (!briefing.CanSpawn)
            sb.AppendLine().AppendLine("You cannot dispatch sub-agents of your own: you are at the depth limit. Do the work yourself.");
        else if (mayDispatch)
            // The affirmative half of the line above, and it was missing: a child that was never told it could
            // delegate does not go looking for the tool, and the tool being present is not the same as the
            // model reaching for it. Gated on the tool actually being offered, because the two ways of not
            // being able to dispatch — the depth limit and a whitelist that left it out — must not read as one.
            sb.AppendLine().AppendLine("You may dispatch sub-agents of your own with the same tools, when part of your task is separable from the rest. Anything you dispatch spends from the same budget you were given above.");
    }

    /// <summary>
    /// Whether this scope's own model is offered the dispatch tool. Read off the switch the toolkit filters by,
    /// so this and <see cref="CreateTools()"/> cannot disagree about what the model can see.
    /// </summary>
    private bool MayDispatch => _host.IsToolEnabled(ToolNames[0]);

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
            ["grantedSkillCount"] = row.GrantedSkillCount,
            ["grantedMcpServerCount"] = row.GrantedMcpServerCount,
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
