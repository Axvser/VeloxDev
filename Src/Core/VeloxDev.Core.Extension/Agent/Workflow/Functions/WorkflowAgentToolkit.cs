using Microsoft.Extensions.AI;
using VeloxDev.AI.Pipelines;
using VeloxDev.Serialization;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.AI.Skills;
using VeloxDev.AI.SubAgents;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;

namespace VeloxDev.AI.Workflow.Functions;

/// <summary>
/// Provides MAF-compatible <see cref="AITool"/> instances that give an Agent
/// full operational control over a single <see cref="IWorkflowTreeViewModel"/>.
/// All JSON output uses <see cref="VeloxJsonFormat.Compact"/> to minimize token consumption.
/// </summary>
public sealed class WorkflowAgentToolkit
{
    private readonly WorkflowAgentScope _scope;
    private readonly WorkflowStateTracker _tracker;
    private readonly ToolCallLedger _ledger;
    private IWorkflowTreeViewModel Tree => _scope.Tree;

    /// <summary>
    /// Creates the toolkit for a scope that owns the session's tool-call allowance.
    /// </summary>
    public WorkflowAgentToolkit(WorkflowAgentScope scope) : this(scope, null)
    {
    }

    /// <summary>
    /// Creates the toolkit for a scope whose allowance is a share of another scope's.
    /// </summary>
    /// <param name="scope">The scope being served.</param>
    /// <param name="outerLedger">
    /// The ledger of the scope that spawned <paramref name="scope"/>, or <c>null</c> when nobody did. Every
    /// call counted here is counted there too, which is what makes <paramref name="scope"/>'s caps a
    /// narrowing of its parent's rather than a second budget beside it.
    /// </param>
    internal WorkflowAgentToolkit(WorkflowAgentScope scope, ToolCallLedger? outerLedger)
    {
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _tracker = new WorkflowStateTracker(_scope.Tree);
        _ledger = new ToolCallLedger(_scope, outerLedger);
    }

    /// <summary>
    /// This scope's place in the session's call accounting — handed to a spawned sub-agent's toolkit as its
    /// <paramref name="outerLedger"/>.
    /// </summary>
    internal ToolCallLedger Ledger => _ledger;

    /// <summary>
    /// Creates the AI tools for workflow operations within the scoped tree, optionally restricted
    /// to the given <see cref="WorkflowToolCategory"/> flags. Every tool is wrapped with
    /// <see cref="TrackedAIFunction"/> so that tracking is invoked after each call.
    /// Developer-registered custom tools are always included regardless of <paramref name="categories"/>.
    /// <para>
    /// Tools switched off via <see cref="WorkflowAgentScope.SetToolEnabled"/> are omitted, whichever
    /// category is asked for — this method is the single point that decides what reaches the model, so a
    /// switch cannot be honoured in one path and ignored in another.
    /// </para>
    /// </summary>
    public IList<AITool> CreateTools(WorkflowToolCategory categories = WorkflowToolCategory.All)
        => [.. CreateAllTools(categories).Where(t => _scope.IsToolEnabled(t.Name))];

    /// <summary>
    /// Every tool this toolkit can offer — the built-ins for <paramref name="categories"/>, the custom
    /// ones, and no per-tool filtering. This is what a host UI enumerates to show the switchable surface;
    /// <see cref="CreateTools(WorkflowToolCategory)"/> is what the model is shown.
    /// </summary>
    internal IList<AITool> CreateAllTools(WorkflowToolCategory categories = WorkflowToolCategory.All)
    {
        AITool T(Delegate method, string name)
            => new TrackedAIFunction(AIFunctionFactory.Create(method, name), Tools, _scope.Pipeline);

        var tools = new List<AITool>();

        void Add(WorkflowToolCategory category, params AITool[] items)
        {
            if ((categories & category) == category)
                tools.AddRange(items);
        }

        // ── Query (read-only inspection) ──
        Add(WorkflowToolCategory.Query,
            T(ListNodes, nameof(ListNodes)),
            T(GetNodeDetail, nameof(GetNodeDetail)),
            T(GetNodeDetailById, nameof(GetNodeDetailById)),
            T(ListConnections, nameof(ListConnections)),
            T(GetTypeSchema, nameof(GetTypeSchema)),
            T(GetWorkflowSummary, nameof(GetWorkflowSummary)),
            T(GetComponentContext, nameof(GetComponentContext)),
            T(ListComponentCommands, nameof(ListComponentCommands)),
            T(FindNodes, nameof(FindNodes)),
            T(ResolveSlotId, nameof(ResolveSlotId)),
            T(ListSlotProperties, nameof(ListSlotProperties)),
            T(GetEnumSlotByValue, nameof(GetEnumSlotByValue)),
            T(GetLinkDetail, nameof(GetLinkDetail)),
            T(ListCreatableTypes, nameof(ListCreatableTypes)),
            T(ValidateWorkflow, nameof(ValidateWorkflow)),
            T(GetFullTopology, nameof(GetFullTopology)),
            T(CompileWorkflow, nameof(CompileWorkflow)),
            T(CompileNodeResult, nameof(CompileNodeResult)),
            T(GetCompileStatus, nameof(GetCompileStatus)),
            T(GetExecutionLog, nameof(GetExecutionLog)));

        // ── State tracking / diff / dirty ──
        Add(WorkflowToolCategory.State,
            T(TakeSnapshot, nameof(TakeSnapshot)),
            T(GetChangesSinceSnapshot, nameof(GetChangesSinceSnapshot)),
            T(MarkDirty, nameof(MarkDirty)));

        // ── Structural mutation (each tool executes exactly one component command — no bundled
        // multi-step gestures, so the framework's undo/redo stack stays the source of truth) ──
        Add(WorkflowToolCategory.Mutation,
            T(MoveNode, nameof(MoveNode)),
            T(SetNodePosition, nameof(SetNodePosition)),
            T(ResizeNode, nameof(ResizeNode)),
            T(DeleteNode, nameof(DeleteNode)),
            T(DeleteSlot, nameof(DeleteSlot)),
            T(ConnectSlots, nameof(ConnectSlots)),
            T(ConnectSlotsById, nameof(ConnectSlotsById)),
            T(ConnectByProperty, nameof(ConnectByProperty)),
            T(DisconnectSlots, nameof(DisconnectSlots)),
            T(DisconnectSlotsById, nameof(DisconnectSlotsById)),
            T(SetSlotChannel, nameof(SetSlotChannel)),
            T(SetEnumSlotChannel, nameof(SetEnumSlotChannel)),
            T(ConnectEnumSlot, nameof(ConnectEnumSlot)),
            T(PatchNodeProperties, nameof(PatchNodeProperties)),
            T(PatchComponentById, nameof(PatchComponentById)),
            T(CreateNode, nameof(CreateNode)),
            T(CreateSlotOnNode, nameof(CreateSlotOnNode)),
            T(AddSlotToCollection, nameof(AddSlotToCollection)),
            T(RemoveSlotFromCollection, nameof(RemoveSlotFromCollection)),
            T(SetEnumSlotCollection, nameof(SetEnumSlotCollection)),
            T(Undo, nameof(Undo)),
            T(Redo, nameof(Redo)),
            T(ClearHistory, nameof(ClearHistory)));

        // ── Node execution (gated by WithAllowNodeExecution) ──
        Add(WorkflowToolCategory.Execution,
            T(ExecuteNode, nameof(ExecuteNode)),
            T(ExecuteNodes, nameof(ExecuteNodes)),
            T(BroadcastNode, nameof(BroadcastNode)),
            T(ReverseBroadcastNode, nameof(ReverseBroadcastNode)),
            // Chain-level entry: drives the compiled graph with the execution engine
            // (the demo's Run path). Distinct from ExecuteNode (node-level EXEC).
            T(RunCompiledWorkflow, nameof(RunCompiledWorkflow)),
            // Terminal/result entry: compute a single node's result from its ancestor cone.
            T(GetNodeResult, nameof(GetNodeResult)),
            // The same run, but handed back as a handle so the Agent can hold / let go / stop / follow it.
            T(StartCompiledWorkflow, nameof(StartCompiledWorkflow)),
            T(ContinueCompiledWorkflow, nameof(ContinueCompiledWorkflow)),
            T(GetCompiledRunStatus, nameof(GetCompiledRunStatus)),
            T(PauseCompiledRun, nameof(PauseCompiledRun)),
            T(ResumeCompiledRun, nameof(ResumeCompiledRun)),
            T(StopCompiledRun, nameof(StopCompiledRun)));

        // ── Generic command execution (gated by WithAllowedGenericCommands) ──
        Add(WorkflowToolCategory.Command,
            T(ExecuteCommandOnNode, nameof(ExecuteCommandOnNode)),
            T(ExecuteCommandById, nameof(ExecuteCommandById)));

        // ── Graph traversal ──
        Add(WorkflowToolCategory.Graph,
            T(SearchForward, nameof(SearchForward)),
            T(SearchReverse, nameof(SearchReverse)),
            T(SearchAllRelative, nameof(SearchAllRelative)),
            T(IsConnected, nameof(IsConnected)),
            T(FindPath, nameof(FindPath)));

        // ── Layout ──
        // No bundled layout tools: aligning/distributing/auto-arranging multiple nodes is
        // performed node-by-node via MoveNode / SetNodePosition (each one SetAnchorCommand).

        // ── Analytics ──
        Add(WorkflowToolCategory.Analytics,
            T(GetNodeStatistics, nameof(GetNodeStatistics)));

        // ── Composite ──
        // No composite/bundled tools: every operation is a single component-command step so the
        // undo/redo stack (owned by Core) is never bypassed or double-submitted.

        // ── Interaction (only registered when handlers are configured AND level > 0) ──
        if (_scope.IsInteractionAllowed)
        {
            if (_scope.SelectionHandler != null)
                Add(WorkflowToolCategory.Interaction, T(RequestSelection, nameof(RequestSelection)));
            if (_scope.ConfirmationHandler != null)
                Add(WorkflowToolCategory.Interaction, T(RequestConfirmation, nameof(RequestConfirmation)));
        }

        // Merge developer-registered custom tools (always included). AIFunction-typed tools are
        // wrapped with TrackedAIFunction so they get the same UI-thread marshalling, MaxToolCalls
        // accounting, ToolCalled callback and auto-dirty handling as the built-in tools. Non-AIFunction
        // tools (e.g. raw MCP client tools) are added as-is.
        // Always offered, whatever categories were asked for: this is the way out of a budget the host set,
        // and it would be useless if it disappeared exactly when the budget ran out.
        tools.Add(T(ResetToolCallLimit, ResetBudgetToolName));

        foreach (var tool in _scope.CustomTools)
            tools.Add(WrapTool(tool));
        foreach (var tool in _scope.QueryOnlyCustomTools)
            tools.Add(WrapTool(tool));

        // Deliberately unfiltered — the switches are applied by CreateTools, which is what the model is
        // shown. Filtering here as well would hide a switched-off tool from a host UI that needs to list it
        // in order to switch it back on.
        return tools;
    }

    /// <summary>
    /// Wraps an <c>AIFunction</c> with <see cref="TrackedAIFunction"/> so it receives the same tracking
    /// (UI marshal, call counting, callback, auto-dirty) as the built-in tools — used for both
    /// developer-registered tools and tools sourced from outside, such as a connected MCP server.
    /// Non-<c>AIFunction</c> tools are returned unchanged.
    /// </summary>
    internal AITool WrapTool(AITool tool)
        => tool is AIFunction fn ? new TrackedAIFunction(fn, Tools, _scope.Pipeline) : tool;

    private ToolPipeline? _tools;

    /// <summary>
    /// This toolkit's tool seam: marshal onto the scope's UI context, and enforce the three call budgets
    /// before a call runs.
    /// <para>
    /// <b>One instance per scope, shared with everything the scope composes.</b> The subsystem context
    /// providers are handed this same reference, so a tool sourced from MCP or a skill is gated by the same
    /// budgets as a built-in one. Its hooks read the scope live, so <c>With*</c> calls made <i>after</i>
    /// the subsystems were attached still reach their tools.
    /// </para>
    /// <para>
    /// Built lazily so it can read <see cref="WorkflowAgentScope.UIContext"/>, which a host is free to
    /// register after this toolkit exists. It holds no reference to the scope's pipeline — the gates and
    /// the chain are separate, and the toolkit hands both to each wrapper.
    /// </para>
    /// </summary>
    internal ToolPipeline Tools => _tools ??= new ToolPipeline(() => _scope.Transcript, () => _scope.UIContext)
    {
        Refuse = CheckBudget,
        Confirm = ConfirmMutationAsync,
    };

    /// <summary>
    /// The human gate. Asked after the budget gate and before the body, for anything that is not a read-only
    /// query — a read has nothing to approve, and asking about one would train the user to click through
    /// dialogs.
    /// <para>
    /// Lives on this shared policy rather than in each slice, so a single switch reaches the workflow
    /// built-ins, the host's own tools and every MCP server's tools alike, exactly as
    /// <see cref="CheckBudget"/> does. A denial is reported as <see cref="AgentToolOutcome.Refused"/> and
    /// never reaches the body.
    /// </para>
    /// <para>
    /// The key handed to the confirmation handler is the tool name, so a host answering "allow for the
    /// session" approves that tool for the rest of the session rather than one call. Coarser than the
    /// framework's per-argument approval, and the right granularity here: the host is answering "may the
    /// Agent use this capability", not auditing a particular argument list.
    /// </para>
    /// </summary>
    private async ValueTask<string?> ConfirmMutationAsync(string toolName, CancellationToken cancellationToken)
    {
        if (!_scope.ToolApproval) return null;
        if (IsQueryTool(toolName)) return null;

        var description = $"The Agent wants to call the tool '{toolName}'.";
        if (await _scope.ResolveConfirmationAsync(toolName, description))
            return null;

        // Same shape as the budget refusals: say what happened, and close the two ways round it — retrying,
        // and finding another tool that makes the same change.
        return $"'{toolName}' was not approved by the user. The call did not run. "
             + "Do not retry it, and do not look for another tool that makes the same change — "
             + "ask the user what they want instead.";
    }

    /// <summary>
    /// The stage that turns a completed call into conversation state: it counts the call against the three
    /// budgets' counters, raises the scope's callback, and marks the tree dirty for a mutation when the
    /// host asked for that.
    /// <para>
    /// This used to be the policy's <c>AfterCall</c> delegate, which could only ever feed one observer and
    /// never saw a refusal or a failure. Registered by the scope on its pipeline.
    /// </para>
    /// </summary>
    internal IAgentPipelineStage CreateAccountingStage() => new AccountingStage(this);

    private sealed class AccountingStage(WorkflowAgentToolkit owner) : IAgentPipelineStage
    {
        public async ValueTask OnEventAsync(
            AgentEvent agentEvent, Func<AgentEvent, ValueTask> next, CancellationToken cancellationToken)
        {
            // Succeeded only, which is what AfterCall saw: a refused or failed call never ran its body, and
            // counting it would spend budget on something the model did not get. A host that wants those
            // has them on the pipeline.
            if (agentEvent is AgentToolCallCompleted { Outcome: AgentToolOutcome.Succeeded } completed)
                await owner.AccountAsync(completed.ToolName, completed.Result).ConfigureAwait(false);

            await next(agentEvent).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The pre-flight gate: returns the refusal message when a configured call limit is already reached,
    /// or <c>null</c> to let the call through. Runs inside the marshalled block, before the tool body.
    /// </summary>
    private string? CheckBudget(string toolName)
    {
        // A tool the host switched off is refused here, not merely filtered out of the workflow tool list:
        // this hook is shared by every slice the scope composes — MCP's and the skills' providers are given
        // this same policy — so one switch reaches all of them. Filtering alone would only reach the
        // workflow built-ins, and a switch on, say, "ListSkills" would silently do nothing.
        if (!_scope.IsToolEnabled(toolName))
            return $"'{toolName}' is disabled by host policy. Do not try to work around it — use another tool or report it to the user.";

        // The escape hatch survives the gate it exists to open. Refusing it exactly when the budget is spent
        // would leave the session with no way forward at all.
        if (string.Equals(toolName, ResetBudgetToolName, StringComparison.OrdinalIgnoreCase))
            return null;

        // ── The session's shared allowance, when this scope is spending somebody else's ──
        // Asked before this scope's own share, because it is the harder wall and the only one the model
        // cannot reason its way around: when the tree is out, no call of any kind is accepted, so naming a
        // per-kind limit here would explain the refusal with the wrong limit. Skipped at the root, where it
        // would be the same counter measured against the same cap and would report one wall as two.
        var root = _ledger.Root;
        if (!ReferenceEquals(root, _ledger) && root.Owner.MaxToolCalls is { } ceiling)
        {
            var (treeSpent, _, _) = root.Usage;
            if (treeSpent >= ceiling)
                return BudgetRefusal($"The session's tool-call budget ({ceiling}) is spent.");
        }

        var (spent, readCalls, writeCalls) = _ledger.Usage;
        if (_scope.MaxToolCalls.HasValue && spent >= _scope.MaxToolCalls.Value)
            return BudgetRefusal($"Tool call limit ({_scope.MaxToolCalls.Value}) reached.");

        bool isQueryTool = IsQueryTool(toolName);
        if (!isQueryTool && _scope.MaxWriteToolCalls.HasValue && writeCalls >= _scope.MaxWriteToolCalls.Value)
            return BudgetRefusal($"Mutation tool call limit ({_scope.MaxWriteToolCalls.Value}) reached.");
        if (isQueryTool && _scope.MaxReadToolCalls.HasValue && readCalls >= _scope.MaxReadToolCalls.Value)
            return BudgetRefusal($"Query tool call limit ({_scope.MaxReadToolCalls.Value}) reached.");

        return null;
    }

    /// <summary>
    /// Tool names that are purely read-only queries and must never trigger a dirty mark.
    /// Every other tool is treated as a mutation when <see cref="WorkflowAgentScope.AutoMarkDirty"/> is enabled.
    /// </summary>
    private static readonly HashSet<string> QueryToolNames = BuildQueryToolNames();

    /// <summary>
    /// Builds <see cref="QueryToolNames"/>, including the skill tools by name from their own toolkit.
    /// <para>
    /// A skill tool changes what the model is shown and never the workflow graph, so it belongs on this
    /// side regardless of how it reaches the agent — whether the host registered it or the skill context
    /// provider contributed it. Taking the names from <see cref="SkillAgentToolkit.ToolNames"/> rather
    /// than repeating the literals keeps the two in step.
    /// </para>
    /// </summary>
    private static HashSet<string> BuildQueryToolNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ListNodes", "GetNodeDetail", "GetNodeDetailById", "ListConnections", "GetTypeSchema",
            "GetWorkflowSummary", "GetComponentContext",
            "ListComponentCommands", "GetChangesSinceSnapshot", "TakeSnapshot",
            "GetFullTopology", "FindNodes", "ResolveSlotId", "ListSlotProperties",
            "GetEnumSlotByValue", "GetLinkDetail", "GetNodeStatistics", "ListCreatableTypes",
            "ValidateWorkflow", "SearchForward", "SearchReverse", "SearchAllRelative",
            "IsConnected", "FindPath", "RequestSelection", "RequestConfirmation", "ResetToolCallLimit",
            "CompileWorkflow", "CompileNodeResult", "GetCompileStatus", "GetExecutionLog",
        };

        foreach (var name in SkillAgentToolkit.ToolNames)
            names.Add(name);

        foreach (var name in SubAgentAgentToolkit.ToolNames)
            names.Add(name);

        return names;
    }

    /// <summary>
    /// Counts a completed call, raises the scope's callback, and marks the tree dirty when
    /// <see cref="WorkflowAgentScope.AutoMarkDirty"/> is enabled.
    /// </summary>
    private async Task AccountAsync(string toolName, string result)
    {
        // The budget's own tool does not spend the budget — and this is not a nicety. Accounting runs after
        // the tool body, so counting the reset would increment the counter the reset had just zeroed and
        // leave the session refused again: the reset would undo itself.
        if (IsBudgetTool(toolName))
        {
            await _scope.RaiseToolCalledAsync(toolName, result, _ledger.Usage.ToolCalls).ConfigureAwait(false);
            return;
        }

        _ledger.Spend(IsQueryTool(toolName));
        await _scope.RaiseToolCalledAsync(toolName, result, _ledger.Usage.ToolCalls).ConfigureAwait(false);
        if (_scope.AutoMarkDirty && !QueryToolNames.Contains(toolName) && !_scope.IsQueryOnlyCustomTool(toolName))
            Tree.GetHelper().MarkDirty();
    }

    /// <summary>Whether the name belongs to a tool that manages the budget rather than spending it.</summary>
    private static bool IsBudgetTool(string toolName)
        => string.Equals(toolName, ResetBudgetToolName, StringComparison.OrdinalIgnoreCase);

    /// <summary>The tool that can reopen a spent tool-call budget.</summary>
    internal const string ResetBudgetToolName = "ResetToolCallLimit";

    /// <summary>
    /// The operation key the reset asks its confirmation under, so a host UI can present it as one decision
    /// and the user's "always" covers the rest of the session.
    /// </summary>
    internal const string ResetBudgetOperationKey = "extend-tool-call-budget";

    [Description("Asks the user to extend this session's tool-call budget. This is the ONLY way to continue after a call is refused for reaching a limit. Call it as soon as a tool reports that a limit was reached — do not retry the refused tool first, and do not assume the answer is yes. The user must agree; until they do, no further tool calls are accepted. Answers with whether the budget was reopened.")]
    private async Task<string> ResetToolCallLimit(CancellationToken cancellationToken = default)
    {
        var exhausted = DescribeExhaustedLimit();
        if (exhausted is null)
            return Ok("No tool-call limit is currently reached; there is nothing to extend.");

        // Level 0 means the host asked never to be interrupted. There is then no way to obtain the user's
        // agreement, and a budget may only be reopened with it — so this denies rather than asking a
        // question the host said it does not want.
        if (!_scope.IsInteractionAllowed)
            return new VeloxJsonObject
            {
                ["status"] = "denied",
                ["message"] = "The host has switched interaction off for this session, so the user cannot be asked. Stop calling tools and report what remains.",
            }.ToJson();

        // Asking is the whole safety property: the Agent cannot widen its own budget, it can only put the
        // question to the user. With no confirmation handler registered the answer is no — an unanswerable
        // prompt must deny, never silently allow.
        var allowed = await _scope.ResolveConfirmationAsync(
            ResetBudgetOperationKey,
            $"Agent 已达到工具调用上限（{exhausted}）。是否允许重置额度让它继续？").ConfigureAwait(false);

        if (!allowed)
            return new VeloxJsonObject
            {
                ["status"] = "denied",
                ["message"] = "The user did not allow more tool calls. Stop calling tools and report what you have done and what remains.",
            }.ToJson();

        // The whole chain, not this scope's share alone: the user agreed to reopen the budget, and a
        // session whose root allowance is still spent would refuse the very next call — the extension would
        // undo itself. At the root the chain is this scope, so the behaviour is unchanged.
        _ledger.ResetChain();

        return Ok("Tool-call budget reset by the user. You may continue.");
    }

    /// <summary>
    /// How much of each call budget <i>this scope</i> has spent — its own share, not the whole tree's.
    /// <para>
    /// Deliberately local even though a tree-wide count exists: this is what the scope's capability
    /// envelope measures against its own caps, and a spawned scope reporting the session's total would
    /// describe itself as exhausted the moment its parent had spent the cap that governs the two of them
    /// differently. Read without a lock — the counters are only ever moved by <see cref="Interlocked"/>, so
    /// a torn read is not possible and a stale one is harmless.
    /// </para>
    /// <para>
    /// A tuple rather than three properties so a reader gets one consistent-enough picture; the scope's
    /// capability envelope and <see cref="DescribeExhaustedLimit"/> both read it, so the two cannot drift
    /// into describing the same budget differently.
    /// </para>
    /// </summary>
    internal (int ToolCalls, int ReadCalls, int WriteCalls) CallUsage => _ledger.Usage;

    /// <summary>
    /// Describes whichever limit is currently reached, or <c>null</c> when none is. Used both to decide
    /// whether there is anything to extend and to tell the user what exactly ran out.
    /// </summary>
    private string? DescribeExhaustedLimit()
    {
        var (toolCalls, readCalls, writeCalls) = CallUsage;
        var parts = new List<string>();

        if (_scope.MaxToolCalls.HasValue && toolCalls >= _scope.MaxToolCalls.Value)
            parts.Add($"{toolCalls}/{_scope.MaxToolCalls.Value} 次工具调用");
        if (_scope.MaxReadToolCalls.HasValue && readCalls >= _scope.MaxReadToolCalls.Value)
            parts.Add($"{readCalls}/{_scope.MaxReadToolCalls.Value} 次查询");
        if (_scope.MaxWriteToolCalls.HasValue && writeCalls >= _scope.MaxWriteToolCalls.Value)
            parts.Add($"{writeCalls}/{_scope.MaxWriteToolCalls.Value} 次变更");

        return parts.Count == 0 ? null : string.Join("、", parts);
    }

    /// <summary>
    /// A budget refusal, worded for whoever hit it.
    /// <para>
    /// Both kinds of scope are now sent to <see cref="ResetBudgetToolName"/>, because both hold it — a
    /// spawned child is handed the parent's interaction configuration along with the tool, so its reset
    /// reaches the user exactly as the parent's does. What differs is the duty the refusal adds: a child has
    /// a dispatcher suspended on its result, so it is told to report upward as well rather than sit still.
    /// Telling it to call a tool that refuses is what this message used to do, and it taught the model to
    /// retry.
    /// </para>
    /// </summary>
    private string BudgetRefusal(string cause)
    {
        var text = LimitRefusal(cause);
        return ReferenceEquals(_ledger.Root, _ledger)
            ? text
            : text + " You were dispatched by another agent that is waiting on this run: if the task cannot "
                   + "be finished, report what you have done and what remains.";
    }

    /// <summary>
    /// A budget refusal. It names <see cref="ResetBudgetToolName"/> because the model cannot see the limit
    /// itself — only this message — and a refusal with no way out is where a run used to simply stop.
    /// </summary>
    private static string LimitRefusal(string cause)
        => $"{cause} No further tool calls are accepted until the budget is extended. "
         + $"If the task is unfinished, call {ResetBudgetToolName} — it asks the user, and only their agreement reopens the budget. "
         + "Do not retry this call, and do not tell the user you can continue without it.";

    /// <summary>
    /// Whether a tool is a read-only query (a built-in <see cref="QueryToolNames"/> entry or a
    /// registered query-only custom tool).
    /// </summary>
    private bool IsQueryTool(string toolName)
        => QueryToolNames.Contains(toolName) || _scope.IsQueryOnlyCustomTool(toolName);

    // ────────────────────────── Query Functions ──────────────────────────

    [Description("Lists all nodes. Returns compact JSON: [{i,id,t,x,y,l,w,h,slots,...props}]. Use GetNodeDetail for full info.")]
    private string ListNodes()
    {
        var nodes = Tree.Nodes;
        var result = new VeloxJsonArray();
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var obj = new VeloxJsonObject
            {
                ["i"] = i,
                ["id"] = GetComponentId(node),
                ["t"] = node.GetType().Name,
                ["x"] = node.Anchor.Horizontal,
                ["y"] = node.Anchor.Vertical,
                ["l"] = node.Anchor.Layer,
                ["w"] = node.Size.Width,
                ["h"] = node.Size.Height,
                ["slots"] = node.Slots.Count,
            };
            AppendScalarProperties(obj, node);
            result.Add(obj);
        }
        return result.ToJson();
    }

    [Description("Gets full detail of a node by index: properties, slots with connections. Use ListComponentCommands for commands.")]
    private string GetNodeDetail(
        [Description("Zero-based index of the node.")] int nodeIndex)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        return BuildNodeDetailJson(node, nodeIndex);
    }

    [Description("Gets full detail of a node by runtime ID. Stable across add/remove.")]
    private string GetNodeDetailById(
        [Description("Runtime ID of the node.")] string runtimeId)
    {
        var (node, index) = FindNodeById(runtimeId);
        if (node == null) return Error($"Node '{runtimeId}' not found.");
        return BuildNodeDetailJson(node, index);
    }

    private string BuildNodeDetailJson(IWorkflowNodeViewModel node, int nodeIndex)
    {
        var obj = new VeloxJsonObject
        {
            ["i"] = nodeIndex,
            ["id"] = GetComponentId(node),
            ["t"] = node.GetType().Name,
            ["fullType"] = node.GetType().FullName,
            ["x"] = node.Anchor.Horizontal,
            ["y"] = node.Anchor.Vertical,
            ["l"] = node.Anchor.Layer,
            ["w"] = node.Size.Width,
            ["h"] = node.Size.Height,
        };

        AppendScalarProperties(obj, node);

        // Build slot→property name mapping for richer context
        var slotPropertyMap = BuildSlotPropertyMap(node);

        var slotsArr = new VeloxJsonArray();
        for (int s = 0; s < node.Slots.Count; s++)
        {
            var slot = node.Slots[s];
            var slotObj = new VeloxJsonObject
            {
                ["si"] = s,
                ["id"] = GetComponentId(slot),
                ["ch"] = slot.Channel.ToString(),
                ["st"] = slot.State.ToString(),
            };
            if (slotPropertyMap.TryGetValue(slot, out var propName))
                slotObj["prop"] = propName;

            if (slot.Targets.Count > 0)
            {
                var targets = new VeloxJsonArray();
                foreach (var t in slot.Targets)
                {
                    if (t.Parent != null)
                        targets.Add($"{GetComponentId(t.Parent)}:{GetComponentId(t)}");
                }
                slotObj["tgt"] = targets;
            }

            if (slot.Sources.Count > 0)
            {
                var sources = new VeloxJsonArray();
                foreach (var src in slot.Sources)
                {
                    if (src.Parent != null)
                        sources.Add($"{GetComponentId(src.Parent)}:{GetComponentId(src)}");
                }
                slotObj["src"] = sources;
            }

            AppendScalarProperties(slotObj, slot);
            slotsArr.Add(slotObj);
        }
        obj["slots"] = slotsArr;

        return obj.ToJson();
    }

    [Description("Lists all visible connections only (compact, with link ids). GetFullTopology also returns connections alongside full node/slot detail — prefer it for the whole graph; use this only when you need links without node detail.")]
    private string ListConnections()
    {
        var links = Tree.Links;
        var result = new VeloxJsonArray();
        for (int i = 0; i < links.Count; i++)
        {
            var link = links[i];
            if (!link.IsVisible) continue;

            result.Add(new VeloxJsonObject
            {
                ["id"] = GetComponentId(link),
                ["sid"] = link.Sender != null ? GetComponentId(link.Sender) : null,
                ["rid"] = link.Receiver != null ? GetComponentId(link.Receiver) : null,
            });
        }
        return result.ToJson();
    }

    // ────────────────────────── Mutation Functions ──────────────────────────

    [Description("Moves a node by relative offset. Coordinate system: +offsetX = rightward, +offsetY = downward (origin is top-left). Mirrors GUI node-drag exactly: dispatches MoveCommand with an Offset delta, so the delta is applied in view space and scaled to world, and the node's z-order (Anchor.Layer) is preserved. NOT undoable (Core's move has no undo entry).")]
    private async Task<string> MoveNode(
        [Description("Node index.")] int nodeIndex,
        [Description("Horizontal offset px.")] double offsetX,
        [Description("Vertical offset px.")] double offsetY,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        var n = node;
        // MoveCommand, not SetAnchorCommand: `node.Anchor`'s getter returns the value *collapsed* by the
        // canvas Scale, so reading it and writing the sum back as an absolute anchor lands the node at
        // roughly half the intended distance whenever the user is zoomed out — which is exactly when an
        // agent is arranging a large graph. MoveCommand is the path a drag takes, so the offset is
        // interpreted as view-space and converted with the live scale.
        await WaitForCommandAsync(n.MoveCommand, new Offset(offsetX, offsetY), cancellationToken);
        RefreshSlotAnchors(n);
        return Ok($"Moved {nodeIndex} by ({offsetX},{offsetY}).");
    }

    [Description("Sets absolute position of a node. Coordinate system: origin (0,0) is top-left; left (X) increases rightward, top (Y) increases downward. Mirrors GUI node placement: dispatches SetAnchorCommand, which is NOT undoable (Core's move command has no undo entry). Z-order is left alone unless you pass layer.")]
    private async Task<string> SetNodePosition(
        [Description("Node index.")] int nodeIndex,
        [Description("Left px.")] double left,
        [Description("Top px.")] double top,
        [Description("Layer (z-order). Omit to keep the node's current layer — passing a value replaces it.")] int? layer = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        var n = node;
        // An omitted layer keeps the current one. Defaulting it to 0 would silently drop every positioned
        // node to the bottom of the z-order, which reads as a rendering bug rather than a tool result.
        // Safe to read: Anchor.Collapse keeps Layer, only Horizontal/Vertical are scale-dependent.
        var effectiveLayer = layer ?? n.Anchor.Layer;
        await WaitForCommandAsync(n.SetAnchorCommand, new Anchor(left, top, effectiveLayer), cancellationToken);
        RefreshSlotAnchors(n);
        return Ok($"Position {nodeIndex} → ({left},{top},{effectiveLayer}).");
    }

    [Description("Resizes a node. Dispatches SetSizeCommand, which is NOT undoable (mirrors Core's resize semantics).")]
    private async Task<string> ResizeNode(
        [Description("Node index.")] int nodeIndex,
        [Description("Width px.")] double width,
        [Description("Height px.")] double height,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        var n = node;
        var oldSize = new Size(n.Size.Width, n.Size.Height);
        var newSize = new Size(width, height);
        if (oldSize.Width == newSize.Width && oldSize.Height == newSize.Height)
            return Ok($"Resized {nodeIndex} → ({width},{height}).");
        await WaitForCommandAsync(n.SetSizeCommand, newSize, cancellationToken);
        RefreshSlotAnchors(n);
        return Ok($"Resized {nodeIndex} → ({width},{height}).");
    }

    [Description("Deletes a node. Cascade: auto-deletes all child slots and their connections — no need to delete them first.")]
    private async Task<string> DeleteNode(
        [Description("Node index.")] int nodeIndex,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        await WaitForCommandAsync(node.DeleteCommand, null, cancellationToken);
        return Ok($"Node {nodeIndex} deleted.");
    }

    [Description("Deletes a slot and its connections.")]
    private async Task<string> DeleteSlot(
        [Description("Node index.")] int nodeIndex,
        [Description("Slot index within the node.")] int slotIndex,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetSlot(nodeIndex, slotIndex, out var slot, out var error)) return error;
        await WaitForCommandAsync(slot.DeleteCommand, null, cancellationToken);
        return Ok($"Slot [{nodeIndex}][{slotIndex}] deleted.");
    }

    [Description("Prefer ConnectByProperty — slot indices shift on SlotEnumerator nodes. Use only after ListSlotProperties confirms a stable index. Returns the slot→property map so you can switch to property routing.")]
    private async Task<string> ConnectSlots(
        [Description("Sender node index.")] int senderNodeIndex,
        [Description("Sender slot index.")] int senderSlotIndex,
        [Description("Receiver node index.")] int receiverNodeIndex,
        [Description("Receiver slot index.")] int receiverSlotIndex,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetSlot(senderNodeIndex, senderSlotIndex, out var senderSlot, out var error)) return error;
        if (!TryGetSlot(receiverNodeIndex, receiverSlotIndex, out var receiverSlot, out error)) return error;

        // Preflight: resolve property names so failure diagnostics are actionable
        var senderPropMap = BuildSlotPropertyMap(Tree.Nodes[senderNodeIndex]);
        var receiverPropMap = BuildSlotPropertyMap(Tree.Nodes[receiverNodeIndex]);
        senderPropMap.TryGetValue(senderSlot!, out var senderPropHint);
        receiverPropMap.TryGetValue(receiverSlot!, out var receiverPropHint);

        await SendReceiveAsync(senderSlot!, receiverSlot!, cancellationToken);

        bool connected = VerifyConnection(senderSlot!, receiverSlot!);
        if (!connected)
        {
            var rejected = (VeloxJsonObject)VeloxJsonValue.Parse(ConnectionRejected(senderSlot!, receiverSlot!,
                $"[{senderNodeIndex}][{senderSlotIndex}]", $"[{receiverNodeIndex}][{receiverSlotIndex}]"));
            if (senderPropHint != null)
                rejected["senderProperty"] = senderPropHint;
            if (receiverPropHint != null)
                rejected["receiverProperty"] = receiverPropHint;
            rejected["preferredAlternative"] = $"ConnectByProperty senderNode={senderNodeIndex} senderProperty={senderPropHint ?? "?"} receiverNode={receiverNodeIndex} receiverProperty={receiverPropHint ?? "?"}";
            return rejected.ToJson();
        }

        var result = new VeloxJsonObject
        {
            ["status"] = "ok",
            ["message"] = $"Connected [{senderNodeIndex}][{senderSlotIndex}]→[{receiverNodeIndex}][{receiverSlotIndex}].",
        };
        if (senderPropHint != null) result["senderProperty"] = senderPropHint;
        if (receiverPropHint != null) result["receiverProperty"] = receiverPropHint;
        if (senderPropHint != null && receiverPropHint != null)
            result["preferPropertyRoute"] = $"Next time use ConnectByProperty senderNode={senderNodeIndex} senderProperty={senderPropHint} receiverNode={receiverNodeIndex} receiverProperty={receiverPropHint}";
        return result.ToJson();
    }

    [Description("Connects two slots by their runtime IDs. IDs are stable across UI redraws but NOT across SlotEnumerator reconfiguration. Prefer ConnectByProperty for SlotEnumerator and generated slot collections; use this only with IDs obtained after the latest collection configuration. The framework may silently reject: check 'connected' in the response.")]
    private async Task<string> ConnectSlotsById(
        [Description("Runtime ID of the sender slot.")] string senderSlotId,
        [Description("Runtime ID of the receiver slot.")] string receiverSlotId,
        CancellationToken cancellationToken = default)
    {
        if (FindComponentById(senderSlotId) is not IWorkflowSlotViewModel sender) return Error($"Sender slot '{senderSlotId}' not found.");
        if (FindComponentById(receiverSlotId) is not IWorkflowSlotViewModel receiver) return Error($"Receiver slot '{receiverSlotId}' not found.");

        // Preflight: resolve property names for richer diagnostics
        var senderPropHint = sender.Parent != null ? (BuildSlotPropertyMap(sender.Parent).TryGetValue(sender, out var sp) ? sp : null) : null;
        var receiverPropHint = receiver.Parent != null ? (BuildSlotPropertyMap(receiver.Parent).TryGetValue(receiver, out var rp) ? rp : null) : null;

        await SendReceiveAsync(sender, receiver, cancellationToken);

        bool connected = VerifyConnection(sender, receiver);
        if (!connected)
        {
            var rejected = (VeloxJsonObject)VeloxJsonValue.Parse(ConnectionRejected(sender, receiver, senderSlotId, receiverSlotId));
            if (senderPropHint != null) rejected["senderProperty"] = senderPropHint;
            if (receiverPropHint != null) rejected["receiverProperty"] = receiverPropHint;
            return rejected.ToJson();
        }

        var result = new VeloxJsonObject { ["status"] = "ok", ["message"] = $"Connected {senderSlotId}→{receiverSlotId}." };
        if (senderPropHint != null) result["senderProperty"] = senderPropHint;
        if (receiverPropHint != null) result["receiverProperty"] = receiverPropHint;
        return result.ToJson();
    }

    [Description("Removes a connection between two slots by node/slot indices.")]
    private async Task<string> DisconnectSlots(
        [Description("Sender node index.")] int senderNodeIndex,
        [Description("Sender slot index.")] int senderSlotIndex,
        [Description("Receiver node index.")] int receiverNodeIndex,
        [Description("Receiver slot index.")] int receiverSlotIndex,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetSlot(senderNodeIndex, senderSlotIndex, out var senderSlot, out var error)) return error;
        if (!TryGetSlot(receiverNodeIndex, receiverSlotIndex, out var receiverSlot, out error)) return error;

        if (Tree.LinksMap.TryGetValue(senderSlot!, out var dic) && dic.TryGetValue(receiverSlot!, out var link))
        {
            await WaitForCommandAsync(link.DeleteCommand, null, cancellationToken);
            return Ok($"Disconnected [{senderNodeIndex}][{senderSlotIndex}]✕[{receiverNodeIndex}][{receiverSlotIndex}].");
        }
        return Error("No connection found between the specified slots.");
    }

    [Description("Executes ReceiveCommand on a node and WAITS until the node actually completes. Returns 'ok' only after real completion; returns an error if the receive fails. Disabled by default: the host must call WithAllowNodeExecution(true) on the scope.")]
    private async Task<string> ExecuteNode(
        [Description("Node index.")] int nodeIndex,
        [Description("Optional parameter (becomes ITaskContext.Data, nullable).")] string? parameter = null,
        CancellationToken cancellationToken = default)
    {
        if (!_scope.AllowNodeExecution)
            return Error("ExecuteNode is disabled by host policy. The host must enable node execution via WithAllowNodeExecution(true).");
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        try
        {
            await WaitForCommandAsync(node.ReceiveCommand, new TaskContext(data: parameter), cancellationToken);
            return Ok($"Receive on node {nodeIndex} completed.");
        }
        catch (OperationCanceledException)
        {
            return Error($"Receive on node {nodeIndex} was cancelled.");
        }
        catch (Exception ex)
        {
            return Error($"Receive on node {nodeIndex} failed: {ex.Message}");
        }
    }

    [Description("Executes BroadcastCommand on a node to forward data along connections, and waits until the broadcast command completes (downstream dispatch itself is fire-and-forget). Disabled by default: requires WithAllowNodeExecution(true).")]
    private async Task<string> BroadcastNode(
        [Description("Node index.")] int nodeIndex,
        [Description("Optional parameter.")] string? parameter = null,
        CancellationToken cancellationToken = default)
    {
        if (!_scope.AllowNodeExecution)
            return Error("BroadcastNode is disabled by host policy. The host must enable node execution via WithAllowNodeExecution(true).");
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        try
        {
            await WaitForCommandAsync(node.BroadcastCommand, parameter, cancellationToken);
            return Ok($"Broadcast on node {nodeIndex} completed.");
        }
        catch (Exception ex)
        {
            return Error($"Broadcast on node {nodeIndex} failed: {ex.Message}");
        }
    }

    [Description("Undoes the last action.")]
    private string Undo()
    {
        Tree.UndoCommand.Execute(null);
        return Ok("Undo.");
    }

    [Description("Redoes the last undone action.")]
    private string Redo()
    {
        Tree.RedoCommand.Execute(null);
        return Ok("Redo.");
    }

    /// <summary>
    /// Clears the entire undo/redo history. The canvas state is left untouched — only the
    /// recorded mutation trail is dropped. Use after a batch op the user must not undo through
    /// (e.g. ClearCanvas), so the user cannot walk back past the boundary.
    /// </summary>
    [Description("Clears the entire undo/redo history WITHOUT touching the canvas. The workflow state stays as-is; only the recorded mutation trail is dropped. Use after a bulk operation (e.g. clearing the canvas) so undo cannot walk back past the boundary.")]
    private string ClearHistory()
    {
        Tree.GetHelper().ClearHistory();
        return Ok("Undo/redo history cleared (canvas untouched).");
    }

    // ────────────────────────── Introspection Functions ──────────────────────────

    [Description("Gets JSON schema of a .NET type by full name. Returns properties, types, defaults.")]
    private string GetTypeSchema(
        [Description("Fully-qualified type name.")] string fullTypeName)
    {
        var type = TypeIntrospector.ResolveType(fullTypeName);
        if (type == null)
            return Error($"Type '{fullTypeName}' not found.");

        return TypeIntrospector.GetTypeSchema(type);
    }

    [Description("Patches custom properties on a node. Rejects: command-backed props (Anchor,Size), framework-managed props (Parent,Slots,RuntimeId,Helper), and source-gen slot props (InputSlot,OutputSlot etc). Use dedicated tools for those.")]
    private string PatchNodeProperties(
        [Description("Node index.")] int nodeIndex,
        [Description("JSON patch object, e.g. '{\"Title\":\"New\"}'.")] string jsonPatch)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        var result = ComponentPatcher.ApplyPatch(node, jsonPatch);
        RefreshSlotAnchorsIfEnumSlotNode(node);
        return result;
    }

    [Description("Patches custom properties on any component by runtime ID. Same rejection rules as PatchNodeProperties.")]
    private string PatchComponentById(
        [Description("Runtime ID of the component.")] string runtimeId,
        [Description("JSON patch object.")] string jsonPatch)
    {
        var component = FindComponentById(runtimeId);
        if (component == null) return Error($"Component '{runtimeId}' not found.");
        var result = ComponentPatcher.ApplyPatch(component, jsonPatch);
        if (component is IWorkflowNodeViewModel patchedNode)
            RefreshSlotAnchorsIfEnumSlotNode(patchedNode);
        return result;
    }

    // ────────────────────────── Progressive Context Functions ──────────────────────────

    [Description("High-level summary: node/link counts, distinct types, tree ID. Call first to orient.")]
    private string GetWorkflowSummary()
    {
        var nodeTypes = Tree.Nodes.Select(n => n.GetType().Name).Distinct().ToArray();
        var obj = new VeloxJsonObject
        {
            ["treeId"] = GetComponentId(Tree),
            ["treeType"] = Tree.GetType().Name,
            ["nodeCount"] = Tree.Nodes.Count,
            ["linkCount"] = Tree.Links.Count(l => l.IsVisible),
            ["nodeTypes"] = VeloxJsonValue.From(nodeTypes),
        };
        return obj.ToJson();
    }

    [Description("Gets AgentContext docs for a .NET type. Use to learn about properties/commands on demand.")]
    private string GetComponentContext(
        [Description("Fully-qualified type name.")] string fullTypeName,
        [Description("'English' or 'Chinese'.")] string language = "English")
    {
        var lang = language.Contains("Chinese") || language.Contains("chinese")
            ? AgentLanguages.Chinese
            : AgentLanguages.English;

        var type = TypeIntrospector.ResolveType(fullTypeName);
        if (type == null)
            return Error($"Type '{fullTypeName}' not found.");

        if (type.IsEnum) return AgentContextCollector.GetEnumContext(type, lang);
        if (type.IsInterface) return AgentContextCollector.GetInterfaceContext(type, lang);
        return AgentContextCollector.GetClassContext(type, lang);
    }

    [Description("Lists commands on a node: name and parameter type.")]
    private string ListComponentCommands(
        [Description("Node index.")] int nodeIndex)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;

        var cmds = CommandInvoker.DiscoverCommands(node);
        var arr = new VeloxJsonArray();
        foreach (var cmd in cmds)
        {
            arr.Add(new VeloxJsonObject
            {
                ["n"] = cmd.Name,
                ["p"] = cmd.ParameterType?.Name,
            });
        }
        return arr.ToJson();
    }

    // ────────────────────────── State Tracking / Diff Functions ──────────────────────────

    [Description("Takes a state snapshot. Returns version number + summary counts only. Use GetChangesSinceSnapshot for diffs.")]
    private string TakeSnapshot()
    {
        _tracker.TakeSnapshot();
        return new VeloxJsonObject
        {
            ["status"] = "ok",
            ["version"] = _tracker.Version,
            ["nodeCount"] = Tree.Nodes.Count,
            ["linkCount"] = Tree.Links.Count(l => l.IsVisible),
        }.ToJson();
    }

    [Description("Returns diff since last snapshot: added/removed/modified nodes and links only.")]
    private string GetChangesSinceSnapshot()
    {
        return _tracker.GetChangesSinceLastSnapshot();
    }

    [Description("Marks the workflow tree as dirty. Call once at the end of an Agent task after one or more mutations so the view can refresh consistently.")]
    private string MarkDirty()
    {
        Tree.GetHelper().MarkDirty();
        return Ok("Tree marked dirty.");
    }

    // ────────────────────────── Generic Command Execution ──────────────────────────

    [Description("Executes any command on a node by index. Use ListComponentCommands to discover available commands. Disabled by default: the host must allowlist the command via WithAllowedGenericCommands.")]
    private string ExecuteCommandOnNode(
        [Description("Node index.")] int nodeIndex,
        [Description("Command name, e.g. 'ReceiveCommand'. 'Command' suffix optional.")] string commandName,
        [Description("JSON parameter, or null.")] string? jsonParameter = null)
    {
        if (!_scope.IsGenericCommandAllowed(commandName))
            return Error($"Generic command execution is disabled by host policy. The host must allowlist '{commandName}' via WithAllowedGenericCommands.");
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        var result = CommandInvoker.Invoke(node, commandName, jsonParameter);
        RefreshSlotAnchorsIfEnumSlotNode(node);
        return result;
    }

    [Description("Executes any command on a component by runtime ID. Works for nodes, slots, links. Disabled by default: the host must allowlist the command via WithAllowedGenericCommands.")]
    private string ExecuteCommandById(
        [Description("Runtime ID.")] string runtimeId,
        [Description("Command name.")] string commandName,
        [Description("JSON parameter, or null.")] string? jsonParameter = null)
    {
        if (!_scope.IsGenericCommandAllowed(commandName))
            return Error($"Generic command execution is disabled by host policy. The host must allowlist '{commandName}' via WithAllowedGenericCommands.");
        var component = FindComponentById(runtimeId);
        if (component == null)
            return Error($"Component '{runtimeId}' not found.");
        var result = CommandInvoker.Invoke(component, commandName, jsonParameter);
        if (component is IWorkflowNodeViewModel cmdNode)
            RefreshSlotAnchorsIfEnumSlotNode(cmdNode);
        return result;
    }

    [Description("Creates a node (via CreateNodeCommand — never modify the Nodes collection directly). Width/height: 0 reads the type's default ([DefaultSize]) if declared, else 300×260. Position auto-offsets to avoid overlap.")]
    private string CreateNode(
        [Description("Fully-qualified type name.")] string fullTypeName,
        [Description("Left px. Consider existing node positions to avoid overlap.")] double left = 0,
        [Description("Top px. Consider existing node positions to avoid overlap.")] double top = 0,
        [Description("Width px. 0 = type's default size (fallback 300×260). Use GetComponentContext to discover the exact default.")] double width = 0,
        [Description("Height px. 0 = type's default size (fallback 300×260). Use GetComponentContext to discover the exact default.")] double height = 0)
    {
        var type = TypeIntrospector.ResolveType(fullTypeName);
        if (type == null)
            return Error($"Type '{fullTypeName}' not found.");
        if (!typeof(IWorkflowNodeViewModel).IsAssignableFrom(type))
            return Error($"'{fullTypeName}' does not implement IWorkflowNodeViewModel.");

        var nodeAccessor = AIContextTreeRegistry.FindAccessor(type.FullName ?? type.Name);
        if (nodeAccessor is null) return Error($"Type '{fullTypeName}' is not in the agent context tree.");
        if (!nodeAccessor.HasPublicParameterlessConstructor)
            return Error($"'{fullTypeName}' cannot be created with no arguments.");

        // Resolve a non-zero size: the caller's explicit value wins; otherwise read the node's real
        // default baked into the field initializer by the generator ([DefaultSize]), which survives
        // construction. Only fall back to the deterministic 300×260 if the type declares no default at all.
        IWorkflowNodeViewModel node;
        try
        {
            node = (IWorkflowNodeViewModel)nodeAccessor.Create();
            if (width <= 0) width = node.Size.Width > 0 ? node.Size.Width : 300;
            if (height <= 0) height = node.Size.Height > 0 ? node.Size.Height : 260;
        }
        catch (Exception ex)
        {
            return Error($"Failed to create node: {ex.Message}");
        }

        // Auto-offset to avoid overlapping existing nodes using spatial query
        const double padding = 30;
        bool moved = false;
        try
        {
            for (int attempt = 0; attempt < 100; attempt++)
            {
                // Query only nodes that intersect with the candidate region (padded)
                var queryViewport = new Viewport(
                    left - padding, top - padding,
                    width + padding * 2, height + padding * 2);
                var nearby = Tree.QueryNodes(queryViewport);

                bool overlap = false;
                foreach (var existing in nearby)
                {
                    double ex = existing.Anchor.Horizontal;
                    double ew = existing.Size.Width;

                    // Shift right of the overlapping node
                    left = ex + ew + padding;
                    moved = true;
                    overlap = true;
                    break;
                }
                if (!overlap) break;
            }
        }
        catch
        {
            // Spatial map not enabled — fall back to linear scan
            for (int attempt = 0; attempt < 100; attempt++)
            {
                bool overlap = false;
                foreach (var existing in Tree.Nodes)
                {
                    double ex = existing.Anchor.Horizontal;
                    double ey = existing.Anchor.Vertical;
                    double ew = existing.Size.Width;
                    double eh = existing.Size.Height;

                    if (left < ex + ew + padding && left + width + padding > ex &&
                        top < ey + eh + padding && top + height + padding > ey)
                    {
                        overlap = true;
                        left = ex + ew + padding;
                        moved = true;
                        break;
                    }
                }
                if (!overlap) break;
            }
        }

        try
        {
            node.Anchor = new Anchor(left, top, 0);
            // Set size before adding to tree so the first Virtualize call (fired by
            // OnNodesChanged → Nodes.Add) already sees the correct bounds.  If size
            // were set afterwards the node would enter the spatial index with zero-size
            // bounds and miss the viewport check, causing it to never enter VisibleItems.
            node.Size = new Size(width, height);
            Tree.CreateNodeCommand.Execute(node);
            var result = new VeloxJsonObject
            {
                ["status"] = "ok",
                ["id"] = GetComponentId(node),
                ["i"] = IndexOfNode(node),
                ["x"] = left,
                ["y"] = top,
                ["w"] = width,
                ["h"] = height,
            };
            if (moved)
                result["repositioned"] = true;
            return result.ToJson();
        }
        catch (Exception ex)
        {
            return Error($"Failed to create node: {ex.Message}");
        }
    }

    [Description("Creates a dynamic slot on a node via CreateSlotCommand. Only use when the node does NOT already define typed slot properties (e.g. InputSlot/OutputSlot) — those are auto-created by source generator.")]
    private string CreateSlotOnNode(
        [Description("Node index.")] int nodeIndex,
        [Description("Fully-qualified slot type name.")] string fullSlotTypeName,
        [Description("Channel: 'None','OneTarget','OneSource','OneBoth','MultipleTargets','MultipleSources','MultipleBoth'.")] string channel = "OneBoth")
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;

        var type = TypeIntrospector.ResolveType(fullSlotTypeName);
        if (type == null)
            return Error($"Type '{fullSlotTypeName}' not found.");
        if (!typeof(IWorkflowSlotViewModel).IsAssignableFrom(type))
            return Error($"'{fullSlotTypeName}' does not implement IWorkflowSlotViewModel.");

        var slotAccessor = AIContextTreeRegistry.FindAccessor(type.FullName ?? type.Name);
        if (slotAccessor is null) return Error($"Type '{fullSlotTypeName}' is not in the agent context tree.");
        if (!slotAccessor.HasPublicParameterlessConstructor)
            return Error($"'{fullSlotTypeName}' cannot be created with no arguments.");

        try
        {
            var slot = (IWorkflowSlotViewModel)slotAccessor.Create();
            if (Enum.TryParse<SlotChannel>(channel, true, out var ch))
                slot.Channel = ch;
            node.CreateSlotCommand.Execute(slot);
            return new VeloxJsonObject
            {
                ["status"] = "ok",
                ["id"] = GetComponentId(slot),
                ["si"] = node.Slots.IndexOf(slot),
            }.ToJson();
        }
        catch (Exception ex)
        {
            return Error($"Failed to create slot: {ex.Message}");
        }
    }

    // ────────────────────────── Slot Collection Functions ──────────────────────────

    [Description("Lists slot properties on a node type: named single slots, slot collection properties, and SlotEnumerator properties. Shows property name, type, current count, and slot IDs.")]
    private string ListSlotProperties(
        [Description("Node index.")] int nodeIndex)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        var result = new VeloxJsonArray();

        foreach (var prop in PropertiesOf(node))
        {
            if (!prop.CanRead) continue;

            // SlotEnumerator<TSlot>
            if (prop.IsSlotEnumerator)
            {
                if (prop.Get(node) is not IConditionalSlotProvider enumerator) continue;

                var ids = new VeloxJsonArray();
                foreach (var item in enumerator.Slots)
                {
                    if (item.Slot is { } s) ids.Add(GetComponentId(s));
                }

                var entry = new VeloxJsonObject
                {
                    ["name"] = prop.Name,
                    ["collection"] = true,
                    ["slotEnumerator"] = true,
                    ["count"] = ids.Count,
                    ["ids"] = ids,
                    ["currentSelectorType"] = enumerator.SelectorTypeName,
                    ["hint"] = "Use SetEnumSlotCollection to set or change the enum/bool type.",
                };

                // Expose allowed selector types from [SlotSelectors] on the enumerator property itself.
                var allowedNames = GetAllowedEnumTypeDisplayNames(prop.Node);
                if (!string.IsNullOrEmpty(allowedNames))
                    entry["allowedSelectorTypes"] = VeloxJsonValue.From(allowedNames.Split([", "], StringSplitOptions.RemoveEmptyEntries));

                result.Add(entry);
                continue;
            }

            if (prop.HoldsASingleSlot(node))
            {
                var slot = prop.Get(node) as IWorkflowSlotViewModel;
                result.Add(new VeloxJsonObject
                {
                    ["name"] = prop.Name,
                    ["collection"] = false,
                    ["id"] = slot != null ? GetComponentId(slot) : null,
                    ["ch"] = slot?.Channel.ToString(),
                });
            }
            else if (prop.IsSlotCollection)
            {
                var col = prop.Get(node) as IList;
                var ids = new VeloxJsonArray();
                if (col != null)
                {
                    foreach (var item in col)
                    {
                        if (item is IWorkflowSlotViewModel s)
                            ids.Add(GetComponentId(s));
                    }
                }
                var entry = new VeloxJsonObject
                {
                    ["name"] = prop.Name,
                    ["collection"] = true,
                    ["count"] = col?.Count ?? 0,
                    ["ids"] = ids,
                };

                result.Add(entry);
            }
        }
        return result.ToJson();
    }

    [Description("Adds a new slot to a collection property on a node (e.g. OutputSlots). The slot is created via the node's CreateWorkflowSlot infrastructure and registered through the node's CreateSlotCommand (the native slot-mount path).")]
    private string AddSlotToCollection(
        [Description("Node index.")] int nodeIndex,
        [Description("Name of the slot collection property, e.g. 'OutputSlots'.")] string propertyName,
        [Description("Fully-qualified slot type name.")] string fullSlotTypeName,
        [Description("Channel: 'None','OneTarget','OneSource','OneBoth','MultipleTargets','MultipleSources','MultipleBoth'.")] string channel = "MultipleBoth")
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        var prop = FindProperty(node, propertyName);
        if (prop is null) return Error($"Property '{propertyName}' not found on {AgentTypeNames.SimpleOf(node)}.");
        // 枚举器也带 IsSlotCollection 标志（它确实是槽的集合），但它的条目不落在 IList 里 —— 它的槽由选择器
        // 决定，增删要走 SetEnumSlotCollection。先把它分出去，否则下面那句「集合是空的」会把模型引到错的方向。
        if (prop.Value.IsSlotEnumerator)
            return Error($"Property '{propertyName}' is a SlotEnumerator — its slots come from the selector, not from this tool. Use 'SetEnumSlotCollection' to rebuild them.");
        if (!prop.Value.IsSlotCollection)
            return Error($"Property '{propertyName}' is not a slot collection.");

        if (prop.Value.Get(node) is not IList col) return Error($"Collection '{propertyName}' is null.");

        var slotType = TypeIntrospector.ResolveType(fullSlotTypeName);
        if (slotType == null) return Error($"Type '{fullSlotTypeName}' not found.");
        if (!typeof(IWorkflowSlotViewModel).IsAssignableFrom(slotType))
            return Error($"'{fullSlotTypeName}' does not implement IWorkflowSlotViewModel.");

        var slotAccessor = AIContextTreeRegistry.FindAccessor(slotType.FullName ?? slotType.Name);
        if (slotAccessor is null) return Error($"Type '{fullSlotTypeName}' is not in the agent context tree.");
        if (!slotAccessor.HasPublicParameterlessConstructor)
            return Error($"Type '{fullSlotTypeName}' cannot be created with no arguments.");

        try
        {
            var slot = (IWorkflowSlotViewModel)slotAccessor.Create();
            if (Enum.TryParse<SlotChannel>(channel, true, out var ch))
                slot.Channel = ch;

            // CreateSlotCommand mounts the slot into the node and its slot collections (the same
            // path a human triggers by adding a slot via the GUI). It produces the framework's
            // undo entry — the toolkit never Submit()s its own gesture.
            node.CreateSlotCommand.Execute(slot);

            return new VeloxJsonObject
            {
                ["status"] = "ok",
                ["id"] = GetComponentId(slot),
                ["count"] = col.Count,
            }.ToJson();
        }
        catch (Exception ex)
        {
            return Error($"Failed to add slot: {ex.Message}");
        }
    }

    [Description("Removes a slot from a collection property on a node by slot runtime ID. Triggers the slot's DeleteCommand (the native slot-removal path).")]
    private string RemoveSlotFromCollection(
        [Description("Node index.")] int nodeIndex,
        [Description("Name of the slot collection property, e.g. 'OutputSlots'.")] string propertyName,
        [Description("Runtime ID of the slot to remove.")] string slotRuntimeId)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        var prop = FindProperty(node, propertyName);
        if (prop is null) return Error($"Property '{propertyName}' not found on {AgentTypeNames.SimpleOf(node)}.");
        // 枚举器也带 IsSlotCollection 标志（它确实是槽的集合），但它的条目不落在 IList 里 —— 它的槽由选择器
        // 决定，增删要走 SetEnumSlotCollection。先把它分出去，否则下面那句「集合是空的」会把模型引到错的方向。
        if (prop.Value.IsSlotEnumerator)
            return Error($"Property '{propertyName}' is a SlotEnumerator — its slots come from the selector, not from this tool. Use 'SetEnumSlotCollection' to rebuild them.");
        if (!prop.Value.IsSlotCollection)
            return Error($"Property '{propertyName}' is not a slot collection.");

        if (prop.Value.Get(node) is not IList col) return Error($"Collection '{propertyName}' is null.");

        for (int i = 0; i < col.Count; i++)
        {
            if (col[i] is IWorkflowSlotViewModel slot && GetComponentId(slot) == slotRuntimeId)
            {
                var capturedSlot = slot;
                capturedSlot.DeleteCommand.Execute(null);
                return Ok($"Removed slot '{slotRuntimeId}' from '{propertyName}'. Count={col.Count}.");
            }
        }
        return Error($"Slot '{slotRuntimeId}' not found in '{propertyName}'.");
    }

    [Description("Sets the selector of a SlotEnumerator on an EXISTING node. enum/bool: pass the type name in 'selectorTypeOrJson' (e.g. 'Demo.NetworkRequestMethod', 'System.Boolean'). Non-enum ISlotProvider: GetTypeSchema(type) first, then pass JSON in 'selectorTypeOrJson' and the type name in 'nonEnumTypeName'. Do NOT delete/recreate the node. New branches are auto re-wired onto the previous branches' downstream (by position); a reused type's connections are restored — do NOT manually rewire.")]
    private string SetEnumSlotCollection(
        [Description("Node index.")] int nodeIndex,
        [Description("Name of the slot collection or SlotEnumerator property, e.g. 'OutputSlots'.")] string propertyName,
        [Description("For enum/bool: fully-qualified type name (e.g. 'Demo.ViewModels.NetworkRequestMethod'). For non-enum ISlotProvider: JSON constructed after calling GetTypeSchema to understand the type structure.")] string selectorTypeOrJson,
        [Description("Only required for non-enum ISlotProvider selectors: the fully-qualified .NET type name. Call GetTypeSchema with this name first to inspect structure before constructing JSON. Leave empty for enum/bool selectors.")] string nonEnumTypeName = "")
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        var prop = FindProperty(node, propertyName);
        if (prop is null) return Error($"Property '{propertyName}' not found on {AgentTypeNames.SimpleOf(node)}.");

        // SlotEnumerator<TSlot> path
        if (prop.Value.IsSlotEnumerator)
        {
            if (prop.Value.Get(node) is not IConditionalSlotProvider enumerator)
                return Error($"SlotEnumerator '{propertyName}' is null.");

            // Determine whether we are in enum/bool mode or arbitrary-object mode
            bool isNonEnum = !string.IsNullOrWhiteSpace(nonEnumTypeName);

            if (isNonEnum)
            {
                // Non-enum path: deserialize JSON → concrete object, pass directly to SetSelector
                var targetType = TypeIntrospector.ResolveType(nonEnumTypeName);
                if (targetType == null) return Error($"Type '{nonEnumTypeName}' not found.");

                // Validate against [SlotSelectors] whitelist when present.
                if (prop.Value.Node.Has(AIContextFlags.HasSlotSelectors) && !IsEnumTypeAllowed(prop.Value.Node, targetType))
                {
                    var allowed = GetAllowedEnumTypeDisplayNames(prop.Value.Node);
                    return Error($"Selector type '{nonEnumTypeName}' is not allowed for '{propertyName}'. Allowed types: {allowed}");
                }

                object? selectorValue;
                try
                {
                    selectorValue = VeloxJsonSerializer.Deserialize(selectorTypeOrJson, targetType);
                }
                catch (Exception ex)
                {
                    // 解析不了的两种原因要分开说：类型名打得开但读写器没编出来，是「加 [Archivable]」，
                    // 而 JSON 写错了是别的问题。合成一句会把模型引到错的方向。
                    return VeloxJsonRegistry.ReaderFor(targetType) is null
                        ? Error($"'{nonEnumTypeName}' has no archive reader, so the host cannot rebuild it from JSON. " +
                                "A type takes part in the archive format only when the generator compiled a reader for it — " +
                                "add [Archivable] to it (or declare it from a type that is already in) and rebuild.")
                        : Error($"Failed to deserialize selector JSON as '{nonEnumTypeName}': {ex.Message}");
                }

                if (selectorValue == null) return Error($"Deserialized selector value is null.");
                if (!IsEnumeratorInstalled(enumerator, node)) return Error($"SlotEnumerator '{propertyName}' is not installed on node {nodeIndex} — mount the node first, then retry.");

                // SetSelector captures the previous state and submits its own undoable
                // WorkflowActionPair internally. Do NOT wrap it in another Submit here —
                // that would create nested undo entries and break Ctrl+Z semantics.
                try
                {
                    InvokeSetSelector(enumerator, selectorValue);
                }
                catch (Exception ex)
                {
                    return Error($"SetSelector failed: {ex.Message}");
                }

                return EnumeratorResult(targetType.FullName!, propertyName, enumerator);
            }

            // Enum/bool path (original behaviour)
            var selectorType = selectorTypeOrJson == "System.Boolean" || selectorTypeOrJson == "bool"
                ? typeof(bool)
                : TypeIntrospector.ResolveType(selectorTypeOrJson);
            if (selectorType == null) return Error($"Type '{selectorTypeOrJson}' not found.");
            if (!selectorType.IsEnum && selectorType != typeof(bool))
                return Error($"'{selectorTypeOrJson}' is not an enum or bool type. If you intended to pass a non-enum selector value, supply the type name in 'nonEnumTypeName' and JSON in 'selectorTypeOrJson'.");

            // Validate against [SlotSelectors] allowed types if present on the enumerator property.
            // Framework-owned enum types (SlotChannel, SlotState, …) are always valid regardless of
            // any developer-specified whitelist — they must never be blocked by [SlotSelectors].
            if (prop.Value.Node.Has(AIContextFlags.HasSlotSelectors) && !WorkflowAgentScope.IsFrameworkEnum(selectorType))
            {
                if (!IsEnumTypeAllowed(prop.Value.Node, selectorType))
                {
                    var allowed = GetAllowedEnumTypeDisplayNames(prop.Value.Node);
                    return Error($"Selector type '{selectorTypeOrJson}' is not allowed for '{propertyName}'. Allowed types: {allowed}");
                }
            }

            if (!IsEnumeratorInstalled(enumerator, node)) return Error($"SlotEnumerator '{propertyName}' is not installed on node {nodeIndex} — mount the node first, then retry.");

            // SetSelector captures the previous state (including old slots/links) and submits
            // its own undoable WorkflowActionPair internally. Call it directly — wrapping it in
            // another Submit created nested undo entries and required an anchor-refresh workaround.
            try
            {
                InvokeSetSelector(enumerator, selectorType);
            }
            catch (Exception ex)
            {
                return Error($"SetSelector failed: {ex.Message}");
            }

            return EnumeratorResult(selectorType.FullName!, propertyName, enumerator);
        }

        return Error($"Property '{propertyName}' is not a SlotEnumerator.");
    }

    /// <summary>
    /// The success shape both selector routes answer with: what was installed, and the slots that came of it.
    /// </summary>
    /// <remarks>
    /// The non-enum route used to answer without the slots — only <c>ok</c> and the type name. A model that had
    /// just handed over a provider therefore had no way to see what its JSON had produced, and the observed
    /// behaviour was a loop: set it, list it, set it again with fewer ports, list it again. Reporting the slots
    /// is what lets the caller stop, and it is what every other mutating tool here already does.
    /// </remarks>
    private static string EnumeratorResult(string selectorTypeName, string propertyName, IConditionalSlotProvider enumerator)
    {
        var slots = new VeloxJsonArray();
        foreach (var item in enumerator.Slots)
        {
            slots.Add(new VeloxJsonObject
            {
                ["id"] = GetComponentId(item.Slot),
                ["label"] = item.Name,
                ["value"] = item.Value?.ToString() ?? string.Empty,
            });
        }

        return new VeloxJsonObject
        {
            // `ok` 是这个工具一直以来的形状；`status` 是其余每个工具的形状，两个都发，
            // 免得只认 `status` 的读法把一次成功当成失败，转头去重试或改问宿主。
            ["status"] = "ok",
            ["ok"] = true,
            ["selectorType"] = selectorTypeName,
            ["property"] = propertyName,
            ["count"] = slots.Count,
            ["slots"] = slots,
        }.ToJson();
    }

    /// <summary>
    /// Builds a reverse map
    /// </summary>
    private static Dictionary<IWorkflowSlotViewModel, string> BuildSlotPropertyMap(IWorkflowNodeViewModel node)
    {
        var map = new Dictionary<IWorkflowSlotViewModel, string>();

        foreach (var prop in PropertiesOf(node))
        {
            if (!prop.CanRead) continue;

            if (prop.HoldsASingleSlot(node))
            {
                if (prop.Get(node) is IWorkflowSlotViewModel slot)
                    map[slot] = prop.Name;
            }
            else if (prop.IsSlotEnumerator)
            {
                if (prop.Get(node) is not IConditionalSlotProvider enumerator) continue;

                for (int i = 0; i < enumerator.Slots.Count; i++)
                {
                    if (enumerator.Slots[i].Slot is { } s) map[s] = $"{prop.Name}[{i}]";
                }
            }
            else if (prop.IsSlotCollection)
            {
                if (prop.Get(node) is IList col)
                {
                    for (int i = 0; i < col.Count; i++)
                    {
                        if (col[i] is IWorkflowSlotViewModel s)
                            map[s] = $"{prop.Name}[{i}]";
                    }
                }
            }
        }
        return map;
    }

    // ────────────────────────── Graph Traversal Functions ──────────────────────────

    [Description("Searches downstream (forward) nodes from a starting node via BFS. Returns compact list of reachable nodes. Optionally filter by type name substring and limit depth.")]
    private string SearchForward(
        [Description("Starting node index.")] int nodeIndex,
        [Description("Optional type name substring filter (case-insensitive). null for all.")] string? typeName = null,
        [Description("Max BFS depth. 0 = unlimited.")] int maxDepth = 0)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        Func<IWorkflowNodeViewModel, bool>? predicate = null;
        if (!string.IsNullOrEmpty(typeName))
            predicate = n => n.GetType().Name.IndexOf(typeName, StringComparison.OrdinalIgnoreCase) >= 0;
        var found = node.SearchForwardNodes(predicate, maxDepth);
        return BuildNodeListResult(found);
    }

    [Description("Searches upstream (reverse) nodes from a starting node via BFS. Returns compact list of reachable nodes.")]
    private string SearchReverse(
        [Description("Starting node index.")] int nodeIndex,
        [Description("Optional type name substring filter (case-insensitive). null for all.")] string? typeName = null,
        [Description("Max BFS depth. 0 = unlimited.")] int maxDepth = 0)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        Func<IWorkflowNodeViewModel, bool>? predicate = null;
        if (!string.IsNullOrEmpty(typeName))
            predicate = n => n.GetType().Name.IndexOf(typeName, StringComparison.OrdinalIgnoreCase) >= 0;
        var found = node.SearchReverseNodes(predicate, maxDepth);
        return BuildNodeListResult(found);
    }

    [Description("Searches both upstream and downstream nodes from a starting node via BFS. Returns compact list of all reachable nodes in both directions.")]
    private string SearchAllRelative(
        [Description("Starting node index.")] int nodeIndex,
        [Description("Optional type name substring filter (case-insensitive). null for all.")] string? typeName = null,
        [Description("Max BFS depth. 0 = unlimited.")] int maxDepth = 0)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        Func<IWorkflowNodeViewModel, bool>? predicate = null;
        if (!string.IsNullOrEmpty(typeName))
            predicate = n => n.GetType().Name.IndexOf(typeName, StringComparison.OrdinalIgnoreCase) >= 0;
        var found = node.SearchAllRelativeNodes(predicate, maxDepth);
        return BuildNodeListResult(found);
    }

    [Description("Checks if two nodes are connected (directly or transitively). Direction: 'forward' (source→target), 'reverse' (target→source), 'any' (either direction).")]
    private string IsConnected(
        [Description("Source node index.")] int sourceNodeIndex,
        [Description("Target node index.")] int targetNodeIndex,
        [Description("Direction: 'forward', 'reverse', or 'any'.")] string direction = "forward")
    {
        if (!TryGetNode(sourceNodeIndex, out var srcNode, out var error)) return error;
        if (!TryGetNode(targetNodeIndex, out var tgtNode, out error)) return error;
        var srcId = GetComponentId(srcNode!);
        var tgtId = GetComponentId(tgtNode!);

        bool connected = false;
        if (direction != "reverse")
        {
            connected = srcNode!.SearchForwardNodes(n => ReferenceEquals(n, tgtNode)).Any();
        }
        if (!connected && direction != "forward")
        {
            connected = srcNode!.SearchReverseNodes(n => ReferenceEquals(n, tgtNode)).Any();
        }

        return new VeloxJsonObject { ["status"] = "ok", ["connected"] = connected, ["direction"] = direction }.ToJson();
    }

    [Description("Finds the shortest forward path between two nodes. Returns ordered list of node IDs/indices from source to target, or empty if no path exists.")]
    private string FindPath(
        [Description("Source node index.")] int sourceNodeIndex,
        [Description("Target node index.")] int targetNodeIndex)
    {
        if (!TryGetNode(sourceNodeIndex, out var srcNode, out var error)) return error;
        if (!TryGetNode(targetNodeIndex, out var tgtNode, out error)) return error;

        // BFS to find shortest path
        var visited = new Dictionary<IWorkflowNodeViewModel, IWorkflowNodeViewModel?>();
        var queue = new Queue<IWorkflowNodeViewModel>();
        visited[srcNode!] = null;
        queue.Enqueue(srcNode!);
        bool found = false;

        while (queue.Count > 0 && !found)
        {
            var current = queue.Dequeue();
            foreach (var slot in current.Slots)
            {
                foreach (var target in slot.Targets)
                {
                    if (target.Parent != null && !visited.ContainsKey(target.Parent))
                    {
                        visited[target.Parent] = current;
                        if (ReferenceEquals(target.Parent, tgtNode))
                        {
                            found = true;
                            break;
                        }
                        queue.Enqueue(target.Parent);
                    }
                }
                if (found) break;
            }
        }

        if (!found)
            return new VeloxJsonObject { ["status"] = "ok", ["found"] = false, ["path"] = new VeloxJsonArray() }.ToJson();

        // Reconstruct path
        var path = new List<VeloxJsonValue>();
        var step = tgtNode!;
        while (step != null)
        {
            path.Add(new VeloxJsonObject { ["i"] = IndexOfNode(step), ["id"] = GetComponentId(step), ["t"] = step.GetType().Name });
            visited.TryGetValue(step, out step!);
        }
        path.Reverse();
        return new VeloxJsonObject { ["status"] = "ok", ["found"] = true, ["length"] = path.Count, ["path"] = VeloxJsonValue.From(path) }.ToJson();
    }

    private string BuildNodeListResult(IEnumerable<IWorkflowNodeViewModel> nodes)
    {
        var arr = new VeloxJsonArray();
        foreach (var n in nodes)
        {
            arr.Add(new VeloxJsonObject
            {
                ["i"] = IndexOfNode(n),
                ["id"] = GetComponentId(n),
                ["t"] = n.GetType().Name,
            });
        }
        return arr.ToJson();
    }

    // ────────────────────────── Reverse Broadcast ──────────────────────────

    [Description("Executes ReverseBroadcastCommand on a node to trigger ReceiveCommand on upstream (source) nodes, and waits until the reverse-broadcast command completes (upstream dispatch itself is fire-and-forget). Disabled by default: requires WithAllowNodeExecution(true).")]
    private async Task<string> ReverseBroadcastNode(
        [Description("Node index.")] int nodeIndex,
        [Description("Optional parameter.")] string? parameter = null,
        CancellationToken cancellationToken = default)
    {
        if (!_scope.AllowNodeExecution)
            return Error("ReverseBroadcastNode is disabled by host policy. The host must enable node execution via WithAllowNodeExecution(true).");
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        try
        {
            await WaitForCommandAsync(node.ReverseBroadcastCommand, parameter, cancellationToken);
            return Ok($"Reverse broadcast on node {nodeIndex} completed.");
        }
        catch (Exception ex)
        {
            return Error($"Reverse broadcast on node {nodeIndex} failed: {ex.Message}");
        }
    }

    // ────────────────────────── Connection Management ──────────────────────────

    [Description("Removes a connection between two slots by their runtime IDs.")]
    private async Task<string> DisconnectSlotsById(
        [Description("Runtime ID of the sender slot.")] string senderSlotId,
        [Description("Runtime ID of the receiver slot.")] string receiverSlotId,
        CancellationToken cancellationToken = default)
    {
        if (FindComponentById(senderSlotId) is not IWorkflowSlotViewModel sender) return Error($"Sender slot '{senderSlotId}' not found.");
        if (FindComponentById(receiverSlotId) is not IWorkflowSlotViewModel receiver) return Error($"Receiver slot '{receiverSlotId}' not found.");

        if (Tree.LinksMap.TryGetValue(sender, out var dic) && dic.TryGetValue(receiver, out var link))
        {
            await WaitForCommandAsync(link.DeleteCommand, null, cancellationToken);
            return Ok($"Disconnected {senderSlotId}→{receiverSlotId}.");
        }
        return Error("No connection found between the specified slots.");
    }



    // ────────────────────────── Slot Channel ──────────────────────────

    [Description("Changes the channel type of a slot. Channels: 'None','OneTarget','OneSource','OneBoth','MultipleTargets','MultipleSources','MultipleBoth'.")]
    private string SetSlotChannel(
        [Description("Node index.")] int nodeIndex,
        [Description("Slot index.")] int slotIndex,
        [Description("New channel type.")] string channel)
    {
        if (!TryGetSlot(nodeIndex, slotIndex, out var slot, out var error)) return error;
        if (!Enum.TryParse<SlotChannel>(channel, true, out var ch))
            return Error($"Invalid channel '{channel}'. Valid: {string.Join(", ", SelectorLabels(typeof(SlotChannel)))}.");
        slot.SetChannelCommand.Execute(ch);
        return Ok($"Slot [{nodeIndex}][{slotIndex}] channel → {ch}.");
    }

    [Description("Gets runtime ID of a slot inside SlotEnumerator by condition value")]
    private string GetEnumSlotByValue(
        [Description("Node index")] int nodeIndex,
        [Description("SlotEnumerator property name")] string propertyName,
        [Description("Condition value: enum name or True/False")] string conditionValue)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        var prop = FindProperty(node, propertyName);
        if (prop is null || !prop.Value.IsSlotEnumerator)
            return Error($"'{propertyName}' is not SlotEnumerator on node [{nodeIndex}]");

        if (prop.Value.Get(node) is not IConditionalSlotProvider enumerator)
            return Error($"SlotEnumerator '{propertyName}' is null");

        if (enumerator.SelectorType is null)
            return Error($"No SelectorType set. Call SetEnumSlotCollection first");

        // 按标签匹配，而不是把名字解析成枚举值再查表：每个条目自己就带着它的选择器值，
        // 而把它从字符串变回枚举需要 `Enum.Parse(Type, …)` —— 那正是这里要绕开的反射。
        IWorkflowSlotViewModel? slot = null;
        foreach (var item in enumerator.Slots)
        {
            if (item.Value is null) continue;
            if (!string.Equals(item.Value.ToString(), conditionValue, StringComparison.OrdinalIgnoreCase)) continue;

            slot = item.Slot;
            break;
        }

        if (slot is null)
            return Error($"'{conditionValue}' not found in SlotEnumerator");

        return new VeloxJsonObject
        {
            ["ok"] = true,
            ["nodeIndex"] = nodeIndex,
            ["property"] = propertyName,
            ["condition"] = conditionValue,
            ["slotId"] = GetComponentId(slot),
            ["channel"] = slot.Channel.ToString()
        }.ToJson();
    }

    [Description("Sets SlotChannel of slot inside SlotEnumerator by condition value")]
    private string SetEnumSlotChannel(
        [Description("Node index")] int nodeIndex,
        [Description("SlotEnumerator property")] string propertyName,
        [Description("Condition value")] string conditionValue,
        [Description("New channel")] string channel)
    {
        var getResult = GetEnumSlotByValue(nodeIndex, propertyName, conditionValue);
        var parsed = (VeloxJsonObject)VeloxJsonValue.Parse(getResult);
        if ((parsed["ok"] as VeloxJsonScalar)?.AsBoolean() != true) return getResult;

        var slotId = (parsed["slotId"] as VeloxJsonScalar)?.Text;
        if (string.IsNullOrEmpty(slotId)) return Error("No slotId returned");
        if (slotId is null || FindComponentById(slotId) is not IWorkflowSlotViewModel slot) return Error($"Slot '{slotId}' not found");
        if (!Enum.TryParse<SlotChannel>(channel, true, out var ch))
            return Error($"Invalid channel '{channel}'");

        slot.SetChannelCommand.Execute(ch);
        RefreshSlotAnchorsIfEnumSlotNode((IWorkflowNodeViewModel)Tree.Nodes[nodeIndex]);
        return Ok($"Slot '{conditionValue}' in {propertyName}[{nodeIndex}] channel set to {ch}");
    }

    [Description("Connects SlotEnumerator slot (by condition) to another slot. The receiver can be a plain slot property/index OR another SlotEnumerator slot — supply receiverCondition to pick the receiver slot by its enum/bool condition value instead of by index.")]
    private async Task<string> ConnectEnumSlot(
        [Description("Sender node index")] int senderNodeIndex,
        [Description("Sender SlotEnumerator property")] string senderProperty,
        [Description("Sender condition value")] string senderCondition,
        [Description("Receiver node index")] int receiverNodeIndex,
        [Description("Receiver slot property or index. When receiverCondition is supplied this must be the SlotEnumerator property name.")] string receiverSlot,
        [Description("Optional: receiver condition value (enum name or True/False). Set this when the receiver slot also lives inside a SlotEnumerator property.")] string? receiverCondition = null,
        CancellationToken cancellationToken = default)
    {
        var senderResult = GetEnumSlotByValue(senderNodeIndex, senderProperty, senderCondition);
        var senderParsed = (VeloxJsonObject)VeloxJsonValue.Parse(senderResult);
        if ((senderParsed["ok"] as VeloxJsonScalar)?.AsBoolean() != true) return senderResult;

        var senderSlotId = (senderParsed["slotId"] as VeloxJsonScalar)?.Text;
        if (string.IsNullOrEmpty(senderSlotId)) return Error("No sender slotId");
        if (senderSlotId is null || FindComponentById(senderSlotId) is not IWorkflowSlotViewModel sender) return Error($"Sender '{senderSlotId}' not found");

        if (!TryGetNode(receiverNodeIndex, out var receiverNode, out var error)) return error;
        IWorkflowSlotViewModel? receiver;

        if (!string.IsNullOrEmpty(receiverCondition))
        {
            // Receiver is also a SlotEnumerator slot — resolve by condition value.
            var receiverResult = GetEnumSlotByValue(receiverNodeIndex, receiverSlot, receiverCondition!);
            var receiverParsed = (VeloxJsonObject)VeloxJsonValue.Parse(receiverResult);
            if ((receiverParsed["ok"] as VeloxJsonScalar)?.AsBoolean() != true) return receiverResult;
            var receiverSlotId = (receiverParsed["slotId"] as VeloxJsonScalar)?.Text;
            if (string.IsNullOrEmpty(receiverSlotId)) return Error("No receiver slotId");
            if (receiverSlotId is null || FindComponentById(receiverSlotId) is not IWorkflowSlotViewModel enumReceiver)
                return Error($"Receiver '{receiverSlotId}' not found");
            receiver = enumReceiver;
        }
        else if (int.TryParse(receiverSlot, out var receiverIndex))
        {
            if (!TryGetSlot(receiverNodeIndex, receiverIndex, out receiver, out error)) return error;
        }
        else
        {
            var prop = FindProperty(receiverNode!, receiverSlot);
            if (prop is null || !prop.Value.HoldsASingleSlot(receiverNode!))
                return Error(prop is not null && prop.Value.IsSlotEnumerator
                    ? $"'{receiverSlot}' is a SlotEnumerator on node [{receiverNodeIndex}] — pick one of its slots by passing receiverCondition (the port or member name), not the property itself."
                    : $"'{receiverSlot}' not a slot on node [{receiverNodeIndex}]");
            receiver = prop.Value.Get(receiverNode!) as IWorkflowSlotViewModel;
            if (receiver == null) return Error($"Slot '{receiverSlot}' is null");
        }

        if (receiver is null) return Error($"Receiver '{receiverSlot}' could not be resolved");
        await SendReceiveAsync(sender, receiver, cancellationToken);

        bool connected = receiver is not null && VerifyConnection(sender, receiver);
        var senderLabel = $"[{senderNodeIndex}].{senderProperty}[{senderCondition}]";
        var receiverLabel = $"[{receiverNodeIndex}].{receiverSlot}";
        if (!connected && receiver is not null)
            return ConnectionRejected(sender, receiver, senderLabel, receiverLabel);
        return Ok($"Connected {senderLabel} to {receiverLabel}");
    }

    // ────────────────────────── Link Inspection ──────────────────────────
    
    [Description("Gets full detail of a link by runtime ID: sender/receiver slots, parent nodes, properties.")]
    private string GetLinkDetail(
        [Description("Runtime ID of the link.")] string linkId)
    {
        if (FindComponentById(linkId) is not IWorkflowLinkViewModel component) return Error($"Link '{linkId}' not found.");

        var obj = new VeloxJsonObject
        {
            ["id"] = linkId,
            ["visible"] = component.IsVisible,
        };

        if (component.Sender != null)
        {
            obj["sender"] = new VeloxJsonObject
            {
                ["slotId"] = GetComponentId(component.Sender),
                ["nodeId"] = component.Sender.Parent != null ? GetComponentId(component.Sender.Parent) : null,
                ["nodeIndex"] = component.Sender.Parent != null ? IndexOfNode(component.Sender.Parent) : -1,
            };
        }
        if (component.Receiver != null)
        {
            obj["receiver"] = new VeloxJsonObject
            {
                ["slotId"] = GetComponentId(component.Receiver),
                ["nodeId"] = component.Receiver.Parent != null ? GetComponentId(component.Receiver.Parent) : null,
                ["nodeIndex"] = component.Receiver.Parent != null ? IndexOfNode(component.Receiver.Parent) : -1,
            };
        }

        AppendScalarProperties(obj, component);
        return obj.ToJson();
    }

    // ────────────────────────── Bulk Operations ──────────────────────────

    [Description("Executes ReceiveCommand on multiple nodes and WAITS for each to complete before returning. Optionally pass a parameter shared by all. Disabled by default: requires WithAllowNodeExecution(true).")]
    private async Task<string> ExecuteNodes(
        [Description("JSON array of node indices, e.g. [0,1,2].")] string nodeIndicesJson,
        [Description("Optional parameter passed to each ReceiveCommand (becomes ITaskContext.Data).")] string? parameter = null,
        CancellationToken cancellationToken = default)
    {
        if (!_scope.AllowNodeExecution)
            return Error("ExecuteNodes is disabled by host policy. The host must enable node execution via WithAllowNodeExecution(true).");
        int[] indices;
        try { indices = [.. ((VeloxJsonArray)VeloxJsonValue.Parse(nodeIndicesJson)).Select(t => ((VeloxJsonScalar)t).AsInt32())]; }
        catch (Exception ex) { return Error($"Invalid JSON array: {ex.Message}"); }

        int completed = 0;
        var errors = new VeloxJsonArray();
        foreach (var idx in indices)
        {
            if (idx < 0 || idx >= Tree.Nodes.Count)
            {
                errors.Add($"Index {idx} out of range.");
                continue;
            }
            try
            {
                await WaitForCommandAsync(Tree.Nodes[idx].ReceiveCommand, new TaskContext(data: parameter), cancellationToken);
                completed++;
            }
            catch (Exception ex)
            {
                errors.Add($"Node {idx}: {ex.Message}");
            }
        }

        var result = new VeloxJsonObject { ["status"] = "ok", ["completed"] = completed };
        if (errors.Count > 0) result["errors"] = errors;
        return result.ToJson();
    }

    // ────────────────────────── Compiled runs the Agent holds ──────────────────────────
    // RunCompiledWorkflow waits for the end; a run the Agent must be able to hold, let go or stop cannot. So a
    // second entry starts one and returns a handle, and these tools act on it. One registry per toolkit, i.e. per
    // scope — a handle means nothing outside the scope that started it.

    private sealed class CompiledRun(string handle, ManualExecutionGate gate)
    {
        public string Handle { get; } = handle;
        /// <summary>
        /// The gate the engine is actually waiting on. Settled once the session is built: a host that brings its
        /// own is adopted, so that pausing the run pauses the gate the run is parked on.
        /// </summary>
        public ManualExecutionGate Gate { get; set; } = gate;
        public RuntimeContext? Context { get; set; }
        public CancellationTokenSource Cts { get; } = new();
        public Task Task { get; set; } = Task.CompletedTask;

        // 引擎在跑的同时 `GetCompiledRunStatus` 会来读这份记录：`List<T>` 两边都用就会互相踩
        // （枚举中被 Append 会抛 "Collection was modified"）。写与读都从这把锁过。
        private readonly object _failuresGate = new();
        private readonly List<ExecutionError> _failures = [];

        /// <summary>What the run recorded, as records — the same failures the session logged, as data.</summary>
        public void AddFailure(ExecutionError error)
        {
            lock (_failuresGate) _failures.Add(error);
        }

        /// <summary>How many failures the run has recorded so far.</summary>
        public int FailureCount
        {
            get { lock (_failuresGate) return _failures.Count; }
        }

        /// <summary>A point-in-time copy of the recorded failures, safe to enumerate while the run is recording.</summary>
        public ExecutionError[] SnapshotFailures()
        {
            lock (_failuresGate) return [.. _failures];
        }

        /// <summary>Set only when the engine itself let an exception escape — a host contract that threw.</summary>
        public Exception? Escaped { get; set; }
    }

    /// <summary>Keeps every failure the run records, and passes it on to the host's own sink when there is one.</summary>
    private sealed class RecordingErrorSink(CompiledRun run, IExecutionErrorSink? downstream) : IExecutionErrorSink
    {
        public async Task OnErrorAsync(ExecutionError error, CancellationToken cancellationToken)
        {
            run.AddFailure(error);
            if (downstream is not null) await downstream.OnErrorAsync(error, cancellationToken);
        }
    }

    private readonly Dictionary<string, CompiledRun> _runs = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _runsGate = new();
    private int _runCounter;

    private string NextHandle() => $"run-{Interlocked.Increment(ref _runCounter)}";

    // 工具操作的门必须就是引擎在等的那把。宿主配了一把我们驱动不了的门时（`DelegateExecutionGate` 之类），
    // 暂停会作用在一把没人等的门上 —— 报 ok 却什么都没停住，比报错更糟，所以这里明说。
    private static bool RunsOnOurGate(CompiledRun run)
        => ReferenceEquals(run.Context?.ExecutionGate, run.Gate);

    private static string GateNotOurs(CompiledRun run)
        => Error($"Run '{run.Handle}' is held by an execution gate this tool cannot drive — the host brought its own. " +
                 "Pause and resume it through the host's controls; StopCompiledRun still works.");

    private bool TryGetRun(string handle, out CompiledRun run, out string error)
    {
        lock (_runsGate)
        {
            if (_runs.TryGetValue(handle, out var found))
            {
                run = found;
                error = string.Empty;
                return true;
            }
        }
        run = null!;
        error = Error($"Unknown run handle '{handle}'. Handles come from StartCompiledWorkflow / ContinueCompiledWorkflow and belong to one scope; list the run you started rather than inventing one.");
        return false;
    }

    /// <summary>
    /// The session a compiled run uses. The order is the contract: the scope's own settings first, then whatever
    /// the host registered with <c>WithSessionConfiguration</c>, then the two things these tools need — and each
    /// of those only fills in what is still unset, so a capability the host configured is never overwritten.
    /// </summary>
    private RuntimeContext NewSession(object? seed, IWorkflowNodeViewModel? target, CompiledRun run)
    {
        var context = new RuntimeContext
        {
            Data = seed,
            Target = target,
            LogWriter = _scope.LogWriter,
        };

        _scope.SessionConfiguration?.Invoke(context);

        // PauseCompiledRun / ResumeCompiledRun 操作的是 run.Gate，所以 run.Gate 必须**就是引擎在等的那把**。
        // 宿主自己的设置在上一行刚跑完：它带了门时，`??=` 会让我们的门整个用不上 ——
        // 两个工具照样报 ok、`isPaused` 也如实反映那把没人等的门，**而运行根本没被停住**。
        if (context.ExecutionGate is ManualExecutionGate hostGate)
        {
            run.Gate = hostGate;                                             // 宿主自带：接过来，工具才操作得动
        }
        else
        {
            context.ExecutionGate ??= run.Gate;                              // 宿主没给：用我们的
        }

        context.CheckpointStore ??= _scope.EffectiveCheckpointStore;          // so ContinueCompiledWorkflow has a place to read
        context.ErrorSink = new RecordingErrorSink(run, context.ErrorSink);
        return context;
    }

    private static VeloxJsonObject FailureJson(ExecutionError failure) => new()
    {
        ["phase"] = failure.Phase.ToString(),
        ["level"] = failure.Level.ToString(),
        ["message"] = failure.Message,
        ["error"] = failure.Error?.Message is { } message ? message : VeloxJsonValue.Null,
        ["attempt"] = failure.Attempt,
        ["order"] = failure.Order,
    };

    /// <summary>The last lines of a session's log — a status answer is not the place to paste a thousand of them.</summary>
    private const int RunStatusLogTail = 40;

    private static VeloxJsonArray LogTail(IEnumerable<string> lines)
    {
        var all = lines.ToList();
        var tail = new VeloxJsonArray();
        foreach (var line in all.Skip(Math.Max(0, all.Count - RunStatusLogTail))) tail.Add(line);
        return tail;
    }

    // ────────────────────────── Chain Execution (Compiler) ──────────────────────────

    /// <summary>
    /// Compiles the sub-graph reachable from a start node (typically a controller) and runs it
    /// through the runtime engine (<see cref="RuntimeEngine"/>), exactly like the demo's Run
    /// button. The engine drives the CHAIN: it injects an <see cref="IRuntimeContext"/> session into
    /// every <see cref="IRuntimeAware"/> node, selects branches via <see cref="ICompileTimeRouter"/>,
    /// and handles redirects — the node's own ReceiveAsync executes in "compiled-step" mode and does
    /// NOT auto-broadcast (the engine owns downstream dispatch). This is the chain-level entry,
    /// distinct from <see cref="ExecuteNode"/> (node-level EXEC via ReceiveCommand).
    /// </summary>
    [Description("Runs the compiled workflow (chain-level execution) from a start node, typically a controller. Compiles the reachable sub-graph, creates a runtime session (IRuntimeContext), and drives the whole chain via the execution engine — the same entry the demo's Run button uses. Nodes execute their ReceiveAsync with an IRuntimeContext (compiled-step semantics; no auto-broadcast — the engine drives the chain). Returns the session outcome: runStatus (Completed/Stopped), outcome (Completed/Cancelled/Failed — the precise reading, since runStatus has to share 'Stopped' between a failure and a cancellation), execution log, final data, attempts, whether it ended with an error, failures (the same failures as records: phase/level/message/attempt/order), and logFile (an absolute path, present only when the host sent the lines to a file — open it with your own file tool to read the whole log rather than the returned excerpt). DIFFERENT from ExecuteNode, which executes a single node via ReceiveCommand (node-level EXEC). Disabled by default: requires WithAllowNodeExecution(true).")]
    private Task<string> RunCompiledWorkflow(
        [Description("Node index of the compile entry point (usually a controller).")] int startNodeIndex,
        [Description("Optional seed payload injected into the runtime session (becomes the session's Data).")] string? seed = null,
        CancellationToken cancellationToken = default)
        => RunCompiledRoleAsync(startNodeIndex, CompileRole.Root, nameof(RunCompiledWorkflow), seed, cancellationToken);

    [Description("Computes a node's RESULT in isolation (Terminal role): discovers the node's ancestor cone — all upstream producers feeding it, traced backward from its input slots — and drives it with the execution engine from the cone's own entry frontier, so no controller/start node is needed. Routers inside the cone KEEP real branch selection: only the branch leading to this node is compiled, so the result exactly matches a normal run that took that branch. IMPORTANT error contract: if a router on the cone actually selects a SIBLING branch at runtime, the target is NOT reached — the tool returns status:error with message naming the target ('... was NOT reached ... No result was produced.') and no data; never treat another branch's final payload as this node's result. To succeed, first point the router at the branch that leads to this node (set CompileMode/Selection via PatchNodeProperties or SetEnumSlotCollection), then retry; or ask for a node that sits on the actually-selected branch. Returns targetReached:true when the node was driven. DIFFERENT from RunCompiledWorkflow (runs the whole chain from a Root/start node) and from ExecuteNode (single-node EXEC via ReceiveCommand). Disabled by default: requires WithAllowNodeExecution(true).")]
    private Task<string> GetNodeResult(
        [Description("Node index whose result to compute (its output becomes the run's final data).")] int nodeIndex,
        [Description("Optional seed payload injected into the runtime session (becomes the session's Data).")] string? seed = null,
        CancellationToken cancellationToken = default)
        => RunCompiledRoleAsync(nodeIndex, CompileRole.Terminal, nameof(GetNodeResult), seed, cancellationToken);

    [Description("Starts a compiled workflow run (chain-level, same compile + engine path as RunCompiledWorkflow) and returns AT ONCE with a handle, instead of waiting for the run to end. Use it when the run may be long or may need holding: the handle is what PauseCompiledRun / ResumeCompiledRun / StopCompiledRun / GetCompiledRunStatus take. The run keeps going on the host's thread while you do other things — status is polled with GetCompiledRunStatus, whose 'outcome' tells you how it ended. Disabled by default: requires WithAllowNodeExecution(true).")]
    private Task<string> StartCompiledWorkflow(
        [Description("Node index of the compile entry point (usually a controller).")] int startNodeIndex,
        [Description("Optional seed payload injected into the runtime session (becomes the session's Data).")] string? seed = null)
        => StartCompiledAsync(startNodeIndex, seed, resume: false, nameof(StartCompiledWorkflow));

    [Description("Starts a compiled run that CARRIES ON from the last place a previous run left in the scope's checkpoint store, instead of starting over: the nodes that place records as done are not driven again and what they produced is restored for the nodes behind them. Returns a handle at once, exactly like StartCompiledWorkflow. Use it after a run was stopped (StopCompiledRun) or ended badly, once whatever it needed has been fixed. Errors when there is no checkpoint to carry on from — run the workflow once first. Disabled by default: requires WithAllowNodeExecution(true).")]
    private Task<string> ContinueCompiledWorkflow(
        [Description("Node index of the compile entry point (usually a controller) — the same graph the checkpoint was taken over.")] int startNodeIndex,
        [Description("Optional seed payload injected into the runtime session.")] string? seed = null)
        => StartCompiledAsync(startNodeIndex, seed, resume: true, nameof(ContinueCompiledWorkflow));

    /// <summary>Shared body of the two background entries: compile, build the session, start driving, return a handle.</summary>
    private async Task<string> StartCompiledAsync(int nodeIndex, string? seed, bool resume, string toolName)
    {
        if (!_scope.AllowNodeExecution)
            return Error($"{toolName} is disabled by host policy. The host must enable node execution via WithAllowNodeExecution(true).");
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;

        try
        {
            ExecutionCheckpoint? place = null;
            if (resume)
            {
                place = await _scope.EffectiveCheckpointStore.LoadAsync(CancellationToken.None);
                if (place is null)
                    return Error("There is no checkpoint to carry on from: nothing has been written yet. Run the workflow once (StartCompiledWorkflow) and stop it mid-way, then continue.");
            }

            var graphs = await new CompilerViewModel().CompileAsync(node, CompileRole.Root);
            if (graphs.Count == 0) return Error("Compile produced no graphs from this start node.");

            var run = new CompiledRun(NextHandle(), new ManualExecutionGate());
            run.Context = NewSession(seed, target: null, run);
            lock (_runsGate) _runs[run.Handle] = run;
            run.Task = DriveAsync(graphs[0], run, place);

            return new VeloxJsonObject
            {
                ["status"] = "ok",
                ["handle"] = run.Handle,
                ["resumed"] = resume,
                ["message"] = resume
                    ? "Carrying on from the last checkpoint. Poll GetCompiledRunStatus with the handle."
                    : "Run started. Poll GetCompiledRunStatus with the handle.",
            }.ToJson();
        }
        catch (Exception ex)
        {
            return Error($"Run failed to start: {ex.Message}");
        }
    }

    /// <summary>The run itself, in the background. An exception the engine lets escape is a host contract that threw.</summary>
    private static async Task DriveAsync(CompiledGraph graph, CompiledRun run, ExecutionCheckpoint? place)
    {
        try
        {
            await new RuntimeEngine().RunAsync(graph, run.Context!, run.Cts.Token, place);
        }
        catch (Exception ex)
        {
            run.Escaped = ex;
        }
    }

    [Description("Reports on a run started by StartCompiledWorkflow / ContinueCompiledWorkflow: isRunning, runStatus, outcome (Completed / Cancelled / Failed — the precise reading, since runStatus shares 'Stopped' between a failure and a cancellation), isPaused, attempts, endedWithError, the final data, failures (the failures the run recorded, as records: phase / level / message / attempt / order), the last lines of the log, and logFile (an absolute path when the host sent the lines to a file — open it with your own file tool for the whole log). Also the way to learn a run has finished: a completed run's outcome stops being 'Unknown'. Pure query.")]
    private string GetCompiledRunStatus(
        [Description("The handle returned when the run was started.")] string handle)
    {
        if (!TryGetRun(handle, out var run, out var error)) return error;

        var context = run.Context!;

        // 只读一次：这一次调用回答的是「结束了吗」，退休的也是同一件事。分两次读会给出自相矛盾的答案
        // —— 「还在跑」的回答配上一具已经被退休的句柄，再问就成了未知句柄。
        // 读任务而不是 context.IsRunning：后者是引擎在后台线程里才置起来的，抢在同一瞬会读到「没在跑」。
        var finished = run.Task.IsCompleted;
        var status = new VeloxJsonObject
        {
            ["status"] = "ok",
            ["handle"] = run.Handle,
            ["isRunning"] = !finished,
            ["runStatus"] = context.Status,
            ["outcome"] = context.Outcome.ToString(),
            ["isPaused"] = run.Gate.IsPaused,
            ["attempts"] = context.Attempt,
            ["endedWithError"] = context.EndedWithError,
            ["data"] = DataJson(context.Data),
            ["failureCount"] = run.FailureCount,
            ["failures"] = VeloxJsonValue.From(run.SnapshotFailures().Select(FailureJson)),
            ["logCount"] = context.Logs.Count,
            ["logs"] = LogTail(context.SnapshotLogs()),
            ["logFile"] = _scope.LogFilePath is { } logPath ? logPath : VeloxJsonValue.Null,
        };
        if (run.Escaped is { } escaped)
            status["escaped"] = escaped.Message;

        // A finished run is dropped once it has been reported: the handle has told its story, and the task and the
        // cancellation source go with it. Asking again afterwards is an unknown handle, which is the honest answer.
        if (finished)
        {
            lock (_runsGate) _runs.Remove(run.Handle);
            run.Cts.Dispose();
        }
        return status.ToJson();
    }

    [Description("Holds a running compiled workflow at its next node boundary: the node being driven finishes, nothing new starts, and runStatus becomes 'Paused'. The same gate the host may have configured itself — this only fills in when the host left it unset. Idempotent. Nothing else about the run changes.")]
    private string PauseCompiledRun(
        [Description("The handle returned when the run was started.")] string handle)
    {
        if (!TryGetRun(handle, out var run, out var error)) return error;
        if (!RunsOnOurGate(run)) return GateNotOurs(run);

        run.Gate.Pause();
        return Ok($"Run '{run.Handle}' is held at its next node boundary (currently {run.Context!.Status}). Release it with ResumeCompiledRun.");
    }

    [Description("Lets a held compiled workflow go again from where it stopped. A no-op when it was not held.")]
    private string ResumeCompiledRun(
        [Description("The handle returned when the run was started.")] string handle)
    {
        if (!TryGetRun(handle, out var run, out var error)) return error;
        if (!RunsOnOurGate(run)) return GateNotOurs(run);

        run.Gate.Resume();
        return Ok($"Run '{run.Handle}' is going again (currently {run.Context!.Status}).");
    }

    [Description("Stops a running compiled workflow: the node being driven finishes, the run ends at that boundary with outcome Cancelled, and the checkpoint it left stays in the scope's store — ContinueCompiledWorkflow can carry on from it. Different from PauseCompiledRun, which holds the run without ending it.")]
    private string StopCompiledRun(
        [Description("The handle returned when the run was started.")] string handle)
    {
        if (!TryGetRun(handle, out var run, out var error)) return error;

        run.Cts.Cancel();
        return Ok($"Run '{run.Handle}' was asked to stop; it ends at the current node boundary.");
    }

    /// <summary>Shared chain-run body for the two compile roles (RunCompiledWorkflow = Root, GetNodeResult = Terminal).</summary>
    private async Task<string> RunCompiledRoleAsync(
        int nodeIndex, CompileRole role, string toolName, string? seed, CancellationToken ct)
    {
        if (!_scope.AllowNodeExecution)
            return Error($"{toolName} is disabled by host policy. The host must enable node execution via WithAllowNodeExecution(true).");
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;

        try
        {
            var compiler = new CompilerViewModel();
            var graphs = await compiler.CompileAsync(node, role);
            if (graphs.Count == 0)
                return Error(role == CompileRole.Terminal
                    ? "Compile produced no graph for this terminal node."
                    : "Compile produced no graphs from this start node.");

            // The same session a background run gets — the scope's log writer, the host's configuration, the
            // checkpoint place, the failure records — apart from a gate, which this entry has no use for: it waits
            // for the end, so there is nothing to hold.
            var run = new CompiledRun(NextHandle(), new ManualExecutionGate());
            var context = NewSession(seed, role == CompileRole.Terminal ? node : null, run);
            await new RuntimeEngine().RunAsync(graphs[0], context, ct);

            // Forward-consistent semantics: a Terminal run only reports a result when the target node was
            // actually driven. If a router on its cone decided on a sibling branch, the target is simply not
            // reached and no value is fabricated.
            if (role == CompileRole.Terminal && !context.TargetReached)
            {
                return Error(
                    $"Target node '{node.GetType().Name}' (id {GetComponentId(node)}) was NOT reached in this run: " +
                    "the router selected a branch that does not lead to it, so its condition was not satisfied. " +
                    "No result was produced.");
            }

            var outcome = new VeloxJsonObject
            {
                ["status"] = "ok",
                ["role"] = role.ToString(),
                ["runStatus"] = context.Status,
                // How it ended, precisely: Status has to share "Stopped" between a failure and a cancellation.
                ["outcome"] = context.Outcome.ToString(),
                ["endedWithError"] = context.EndedWithError,
                ["attempts"] = context.Attempt,
                ["data"] = DataJson(context.Data),
                // The same failures the log carries, as records: phase / level / message / attempt / order.
                ["failures"] = VeloxJsonValue.From(run.SnapshotFailures().Select(FailureJson)),
                ["logs"] = VeloxJsonValue.From(context.Logs),
                // Present only when the host sent the lines to a file; an absolute path the model can open itself.
                ["logFile"] = _scope.LogFilePath is { } logPath ? logPath : VeloxJsonValue.Null,
            };
            // targetReached is meaningful only for Terminal (result) runs; a Root chain run has no target.
            if (role == CompileRole.Terminal)
                outcome["targetReached"] = context.TargetReached;
            return outcome.ToJson();
        }
        catch (OperationCanceledException)
        {
            return Error("Run was cancelled.");
        }
        catch (Exception ex)
        {
            return Error($"Run failed: {ex.Message}");
        }
    }

    // ────────────────────────── Analytics Functions ──────────────────────────

    [Description("Gets statistics for a node: in-degree, out-degree, total connections, connected node IDs, slot utilization. Useful for understanding node importance and connectivity.")]
    private string GetNodeStatistics(
        [Description("Node index.")] int nodeIndex)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error) || node is null) return error;

        int inDegree = 0;
        int outDegree = 0;
        var connectedNodeIds = new HashSet<string>();

        foreach (var slot in node.Slots)
        {
            foreach (var target in slot.Targets)
            {
                outDegree++;
                if (target.Parent != null)
                    connectedNodeIds.Add(GetComponentId(target.Parent));
            }
            foreach (var source in slot.Sources)
            {
                inDegree++;
                if (source.Parent != null)
                    connectedNodeIds.Add(GetComponentId(source.Parent));
            }
        }

        return new VeloxJsonObject
        {
            ["status"] = "ok",
            ["nodeIndex"] = nodeIndex,
            ["id"] = GetComponentId(node),
            ["type"] = node.GetType().Name,
            ["inDegree"] = inDegree,
            ["outDegree"] = outDegree,
            ["totalConnections"] = inDegree + outDegree,
            ["connectedNodes"] = connectedNodeIds.Count,
            ["slotCount"] = node.Slots.Count,
            ["connectedNodeIds"] = VeloxJsonValue.From(connectedNodeIds.ToArray()),
        }.ToJson();
    }

    [Description("Lists all node and slot types that can be created: the concrete workflow component types the context tree carries that have a parameterless constructor.")]
    private string ListCreatableTypes()
    {
        var nodeTypes = new VeloxJsonArray();
        var slotTypes = new VeloxJsonArray();

        // 目录已经按四个组件接口分好了类，这里只要再问一句「能不能无参构造」。
        AppendCreatable("Nodes", nodeTypes);
        AppendCreatable("Slots", slotTypes);

        return new VeloxJsonObject
        {
            ["nodeTypes"] = nodeTypes,
            ["slotTypes"] = slotTypes,
        }.ToJson();

        void AppendCreatable(string kind, VeloxJsonArray into)
        {
            foreach (var root in new[] { AIContextTreeRegistry.FrameworkRoot, AIContextTreeRegistry.CustomerRoot })
            {
                foreach (var name in WorkflowAgentScope.TreeTypeNames($"{root}/Components/{kind}"))
                {
                    var accessor = AIContextTreeRegistry.FindAccessor(name);
                    if (accessor is null || !accessor.HasPublicParameterlessConstructor) continue;

                    into.Add(new VeloxJsonObject
                    {
                        ["fullName"] = name,
                        ["name"] = AgentTypeNames.Simple(name),
                    });
                }
            }
        }
    }

    [Description("Validates the workflow: checks for unconnected slots, nodes without connections, nodes with zero size, and other potential issues. Returns a list of warnings.")]
    private string ValidateWorkflow()
    {
        var warnings = new VeloxJsonArray();

        for (int i = 0; i < Tree.Nodes.Count; i++)
        {
            var node = Tree.Nodes[i];
            var nodeId = GetComponentId(node);

            // Check zero size
            if (node.Size.Width <= 0 || node.Size.Height <= 0)
                warnings.Add(new VeloxJsonObject { ["level"] = "error", ["node"] = i, ["id"] = nodeId, ["msg"] = $"Node has zero/negative size ({node.Size.Width}×{node.Size.Height})." });

            // Check isolated node (no connections at all)
            bool hasAnyConnection = false;
            foreach (var slot in node.Slots)
            {
                if (slot.Targets.Count > 0 || slot.Sources.Count > 0)
                {
                    hasAnyConnection = true;
                    break;
                }
            }
            if (!hasAnyConnection && node.Slots.Count > 0)
                warnings.Add(new VeloxJsonObject { ["level"] = "warn", ["node"] = i, ["id"] = nodeId, ["msg"] = "Node is isolated (has slots but no connections)." });

            // Check node with no slots
            if (node.Slots.Count == 0)
                warnings.Add(new VeloxJsonObject { ["level"] = "info", ["node"] = i, ["id"] = nodeId, ["msg"] = "Node has no slots." });
        }

        // Check for duplicate connections
        var seenLinks = new HashSet<string>();
        foreach (var link in Tree.Links)
        {
            if (!link.IsVisible) continue;
            var key = $"{GetComponentId(link.Sender)}→{GetComponentId(link.Receiver)}";
            if (!seenLinks.Add(key))
                warnings.Add(new VeloxJsonObject { ["level"] = "warn", ["id"] = GetComponentId(link), ["msg"] = $"Duplicate connection: {key}." });
        }

        return new VeloxJsonObject
        {
            ["status"] = "ok",
            ["nodeCount"] = Tree.Nodes.Count,
            ["linkCount"] = Tree.Links.Count(l => l.IsVisible),
            ["warningCount"] = warnings.Count,
            ["warnings"] = warnings,
        }.ToJson();
    }

    // ────────────────────────── Compiler Functions ──────────────────────────

    [Description("Compiles the workflow sub-graph reachable from a start node (typically the controller/entry node) and returns the compiled plan: compiled segments (chain / branch / parallel), routing branches with their options/skipped/terminal flags, fan-out groups, and every compile-aware node's Order / ChainIndex / Offset. Order = -1 means the node is on a pruned static branch — absolute stop (do NOT drive it as part of the live chain). Compiling also attaches compile identity to nodes (updates IsCompileStopped badges). Use GetCompileStatus afterwards to read the identity without recompiling.")]
    private Task<string> CompileWorkflow(
        [Description("Node index of the compile entry point (usually the controller/entry node).")] int startNodeIndex,
        CancellationToken cancellationToken = default)
        => CompileRoleAsync(startNodeIndex, CompileRole.Root, cancellationToken);

    [Description("Reverse-compiles a node's ancestor cone (Terminal role, read-only): the upstream producers feeding the node, traced backward from its input slots; returns the compiled plan that computes just that node's result — compiled segments plus every compile-aware node's Order / ChainIndex / Offset. No controller/start node is needed: the cone's entry frontier is derived automatically. Routers on the cone keep real BranchSegment semantics and only the branch leading to the node is compiled (sibling branches are absent, not Order=-1). If MORE THAN ONE route key of the same router reaches the node, compilation returns an error — a single forward run can only take one branch, so that node has no result; ask for a node on one of those branches instead. Same artifact shape as CompileWorkflow, scoped to the node instead of the whole reachable chain. Compiling attaches compile identity to the cone's nodes. Use GetCompileStatus afterwards to read the identity without recompiling.")]
    private Task<string> CompileNodeResult(
        [Description("Node index whose ancestor cone to compile (its output is what a terminal run would compute).")] int nodeIndex,
        CancellationToken cancellationToken = default)
        => CompileRoleAsync(nodeIndex, CompileRole.Terminal, cancellationToken);

    /// <summary>Shared compile-plan body for the two compile roles (CompileWorkflow = Root, CompileNodeResult = Terminal).</summary>
    private async Task<string> CompileRoleAsync(int nodeIndex, CompileRole role, CancellationToken ct)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        try
        {
            var compiler = new CompilerViewModel();
            var graphs = await compiler.CompileAsync(node, role);

            var entries = new VeloxJsonArray();
            foreach (var g in graphs)
                AppendGraphEntries(entries, g, 0);

            return new VeloxJsonObject
            {
                ["status"] = "ok",
                ["role"] = role.ToString(),
                ["graphCount"] = graphs.Count,
                ["entries"] = entries,
                ["nodeOrders"] = BuildCompileOrders(),
            }.ToJson();
        }
        catch (Exception ex)
        {
            return Error($"Compile failed: {ex.Message}");
        }
    }

    [Description("Returns the current compile identity of every compile-aware node (Order / ChainIndex / Offset, isStopped = Order == -1) WITHOUT recompiling. Call CompileWorkflow / CompileNodeResult first to populate it.")]
    private string GetCompileStatus()
    {
        var orders = BuildCompileOrders();
        return new VeloxJsonObject { ["status"] = "ok", ["compiledNodes"] = orders.Count, ["nodes"] = orders }.ToJson();
    }

    [Description("Returns the tree's aggregate execution log — the chronological record of direct (non-compiler) executions appended by nodes (e.g. '01. EXEC Load Seed'). For the compiler run-session log (with sequence numbers and [Warning] / [Error] markers), use RunCompiledWorkflow's 'logs' field instead. Pure query.")]
    private string GetExecutionLog()
    {
        var logs = new VeloxJsonArray();

        // The tree's execution log is a convention-named public property on the concrete tree view
        // model (e.g. TreeViewModel.ExecutionLog). The context tree is what says whether it is there.
        if (FindProperty(Tree, "ExecutionLog") is { } prop && prop.Get(Tree) is System.Collections.IEnumerable entries)
        {
            foreach (var e in entries)
                if (e is not null) logs.Add(e.ToString());
        }

        return new VeloxJsonObject { ["status"] = "ok", ["entryCount"] = logs.Count, ["entries"] = logs }.ToJson();
    }

    private VeloxJsonArray BuildCompileOrders()
    {
        var arr = new VeloxJsonArray();
        for (int i = 0; i < Tree.Nodes.Count; i++)
        {
            var n = Tree.Nodes[i];
            if (n is ICompileTimeAware aware && aware.CompileContext is { } cc)
            {
                arr.Add(new VeloxJsonObject
                {
                    ["i"] = i,
                    ["id"] = GetComponentId(n),
                    ["t"] = n.GetType().Name,
                    ["order"] = cc.Order,
                    ["chainIndex"] = cc.ChainIndex,
                    ["offset"] = cc.Offset,
                    ["isStopped"] = cc.Order == -1,
                });
            }
        }
        return arr;
    }

    private static void AppendGraphEntries(VeloxJsonArray entries, CompiledGraph graph, int depth)
    {
        foreach (var entry in graph.Entries)
            AppendEntry(entries, entry, depth);
    }

    private static void AppendEntry(VeloxJsonArray entries, CompileSegment entry, int depth)
    {
        var obj = new VeloxJsonObject { ["depth"] = depth };
        switch (entry)
        {
            case ChainSegment exec:
                obj["type"] = "Execute";
                obj["nodes"] = VeloxJsonValue.From(exec.Nodes.Select(n => n.GetType().Name));
                break;
            case BranchSegment branch:
                obj["type"] = "Branch";
                obj["router"] = branch.Router?.GetType().Name;
                obj["isDynamic"] = branch.IsDynamic;
                if (branch.CompileKey is { } ck) obj["compileKey"] = ck.ToString();
                var options = new VeloxJsonArray();
                foreach (var o in branch.Options)
                {
                    options.Add(new VeloxJsonObject
                    {
                        ["key"] = o.Key?.ToString(),
                        ["label"] = o.Label,
                        ["isTerminal"] = o.IsTerminal,
                    });
                    if (o.Graph is not null)
                        AppendGraphEntries(entries, o.Graph, depth + 1);
                }
                obj["options"] = options;
                break;
            case ParallelSegment parallel:
                obj["type"] = "Parallel";
                obj["branches"] = parallel.Branches.Count;
                foreach (var g in parallel.Branches)
                    AppendGraphEntries(entries, g, depth + 1);
                break;
            default:
                obj["type"] = entry.GetType().Name;
                break;
        }
        entries.Add(obj);
    }

    // ────────────────────────── Interaction Tools ──────────────────────────

    [Description("Presents a selection to the user and waits for their answer. Supports single-choice (default) and multi-choice mode. A free-text input field is always shown below the options so the user can type a custom response. Returns a JSON object with status, and depending on mode: 'chosen' (single) or 'chosenList' (multi), plus 'freeText'.")]
    private async Task<string> RequestSelection(
        [Description("A clear, concise prompt describing what the user needs to choose.")] string prompt,
        [Description("JSON array of option strings the user can pick from, e.g. [\"Option A\",\"Option B\"].")] string optionsJson,
        [Description("Label shown above the free-text input field. Provide this in the user's configured output language.")] string freeTextPrompt,
        [Description("When true, the user may select MULTIPLE options (checkboxes). When false (default), the user selects exactly one option (radio-buttons).")] bool allowMultiSelect = false)
    {
        string[] options;
        try { options = [.. ((VeloxJsonArray)VeloxJsonValue.Parse(optionsJson)).Select(t => ((VeloxJsonScalar)t).AsString()!)]; }
        catch (Exception ex) { return Error($"Invalid options JSON: {ex.Message}"); }

        if (options.Length == 0) return Error("No options provided.");
        if (_scope.SelectionHandler == null) return Error("No SelectionHandler registered on WorkflowAgentScope.");

        var result = await _scope.SelectionHandler(prompt, options, freeTextPrompt, allowMultiSelect);
        if (result == null)
            return Error("User rejected the selection.");

        if (allowMultiSelect)
        {
            var selected = result.SelectedOptions?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? [];
            var freeText = result.FreeTextResponse;
            return new VeloxJsonObject
            {
                ["status"] = selected.Count > 0 || !string.IsNullOrWhiteSpace(freeText) ? "ok" : "cancelled",
                ["chosenList"] = VeloxJsonValue.From(selected),
                ["freeText"] = freeText,
            }.ToJson();
        }
        else
        {
            var chosen = result.SelectedOption;
            if (chosen == null && string.IsNullOrWhiteSpace(result.FreeTextResponse))
                return Error("User rejected the selection.");

            return new VeloxJsonObject
            {
                ["status"] = "ok",
                ["chosen"] = chosen ?? result.FreeTextResponse,
                ["freeText"] = result.FreeTextResponse,
            }.ToJson();
        }
    }

    [Description("Requests explicit user confirmation before performing a dangerous or sensitive operation (e.g. deleting nodes, bulk mutations). The user can allow once, allow always for this session, or deny. Do NOT proceed with the operation if this tool returns denied.")]
    private async Task<string> RequestConfirmation(
        [Description("A stable, unique key identifying this operation type, e.g. 'delete-all-nodes'. Used to remember session-wide approvals.")] string operationKey,
        [Description("A human-readable description of what will happen if confirmed.")] string description)
    {
        if (_scope.ConfirmationHandler == null) return Error("No ConfirmationHandler registered on WorkflowAgentScope.");

        var allowed = await _scope.ResolveConfirmationAsync(operationKey, description);
        if (!allowed)
            return new VeloxJsonObject { ["status"] = "denied", ["message"] = "User denied the operation. Do NOT proceed." }.ToJson();

        return new VeloxJsonObject { ["status"] = "ok", ["message"] = "User confirmed. Proceed." }.ToJson();
    }

    // ────────────────────────── Helpers ──────────────────────────

    // [NotNullWhen] 是这套索引工具的地基：调用点清一色写 `if (!TryGetNode(...)) return ...;`，
    // 标了它之后 node 在那条守卫之后就是非空，25 处 `node` 才退得掉。netstandard2.0 没有这个
    // 特性，所以本程序集自带一份 —— 见 Compat/NotNullWhenAttribute.cs。
    private bool TryGetNode(int index, [NotNullWhen(true)] out IWorkflowNodeViewModel? node, out string error)
    {
        node = null;
        error = string.Empty;
        if (index < 0 || index >= Tree.Nodes.Count)
        {
            error = Error($"Node index {index} out of range [0,{Tree.Nodes.Count}).");
            return false;
        }
        node = Tree.Nodes[index];
        return true;
    }

    private bool TryGetSlot(int nodeIndex, int slotIndex, [NotNullWhen(true)] out IWorkflowSlotViewModel? slot, out string error)
    {
        slot = null;
        if (!TryGetNode(nodeIndex, out var node, out error)) return false;
        if (slotIndex < 0 || slotIndex >= node.Slots.Count)
        {
            error = Error($"Slot index {slotIndex} out of range [0,{node.Slots.Count}) on node {nodeIndex}.");
            return false;
        }
        slot = node.Slots[slotIndex];
        return true;
    }

    private int IndexOfNode(IWorkflowNodeViewModel node)
    {
        for (int i = 0; i < Tree.Nodes.Count; i++)
            if (ReferenceEquals(Tree.Nodes[i], node)) return i;
        return -1;
    }

    private (IWorkflowNodeViewModel? node, int index) FindNodeById(string runtimeId)
    {
        // Resolve IDs the same way every other tool does (GetComponentId: the component's RuntimeId,
        // provided by its Helper) so an ID returned by ListNodes/GetNodeDetail always round-trips
        // through the by-id tools.
        for (int i = 0; i < Tree.Nodes.Count; i++)
        {
            if (string.Equals(GetComponentId(Tree.Nodes[i]), runtimeId, StringComparison.Ordinal))
                return (Tree.Nodes[i], i);
        }
        return (null, -1);
    }

    private object? FindComponentById(string runtimeId)
    {
        foreach (var node in Tree.Nodes)
        {
            if (GetComponentId(node) == runtimeId)
                return node;

            var propertySlots = BuildSlotPropertyMap(node).Keys;
            foreach (var slot in node.Slots.Concat(propertySlots).Distinct())
            {
                if (GetComponentId(slot) == runtimeId)
                    return slot;
            }
        }
        foreach (var link in Tree.Links)
        {
            if (GetComponentId(link) == runtimeId)
                return link;
        }
        if (GetComponentId(Tree) == runtimeId)
            return Tree;
        return null;
    }

    private static string GetComponentId(object component)
    {
        // Convention: every workflow component's Helper provides a stable RuntimeId (all default
        // templates implement IWorkflowIdentifiable). Falling back to GetHashCode would yield a value
        // that is neither stable across runs nor meaningful to the Agent — so a missing RuntimeId is
        // an error, not something to paper over.
        if (component is IWorkflowIdentifiable identifiable)
            return identifiable.RuntimeId;
        throw new InvalidOperationException(
            $"'{component.GetType().Name}' does not implement IWorkflowIdentifiable — a stable RuntimeId (provided by the component Helper) is required.");
    }

    /// <summary>
    /// One property of a live component, as the context tree describes it.
    /// </summary>
    /// <remarks>
    /// The accessor is the one belonging to the type that <i>declares</i> the member, not the target's own: the
    /// tree lists a derived type's inherited members through its base link, and only the declaring type's
    /// generated switch has a case for them.
    /// </remarks>
    private readonly struct TreeProperty(string name, AIContextNode node, IAIContextAccessor accessor)
    {
        internal string Name => name;
        internal AIContextNode Node => node;
        internal IAIContextAccessor Accessor => accessor;

        internal bool CanRead => node.Has(AIContextFlags.CanRead);
        internal bool CanWrite => node.Has(AIContextFlags.CanWrite);
        internal bool IsSlotEnumerator => node.Has(AIContextFlags.IsSlotEnumerator);
        internal bool IsSingleSlot => node.Has(AIContextFlags.IsSingleSlot);
        internal bool IsSlotCollection => node.Has(AIContextFlags.IsSlotCollection);

        /// <summary>
        /// Whether this property holds one slot — by the compiled flag, or failing that by what it currently holds.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The flag is computed by the context-tree generator, which cannot see the interface another generator
        /// injects — so a type declared <c>[WorkflowBuilder.Slot&lt;T&gt;]</c> is only recognised as a slot once
        /// that generator knows the rule. A consumer built against an older one gets a property that is plainly a
        /// slot and is not flagged as one, and every name-based path here (listing, the property-to-slot map, the
        /// connect tools) silently skips it.
        /// </para>
        /// <para>
        /// The value cannot lie about this: a property holding an <see cref="IWorkflowSlotViewModel"/> holds a
        /// slot. Reading it costs one cast, and it is what keeps the tools working for a consumer whose generator
        /// predates the flag.
        /// </para>
        /// </remarks>
        internal bool HoldsASingleSlot(object target) => IsSingleSlot || Get(target) is IWorkflowSlotViewModel;

        /// <summary>The declared type, from the accessor's <c>typeof</c> literal.</summary>
        internal Type? Type => accessor.MemberType(name);

        /// <summary>
        /// Reads the member, treating a throwing getter the way the reflective walk did: as nothing to report.
        /// </summary>
        internal object? Get(object target)
        {
            try { return accessor.TryGet(target, name, out var value) ? value : null; }
            catch { return null; }
        }
    }

    /// <summary>Every property the tree records for an object, including the ones it inherits.</summary>
    private static IEnumerable<TreeProperty> PropertiesOf(object target)
    {
        var accessor = AIContextTreeRegistry.FindAccessor(target);
        if (accessor is null) yield break;

        foreach (var node in AIContextDirectory.Shared.MembersAcross(accessor.TypeName, "Properties"))
        {
            var declaring = node.OwnerTypeName is { Length: > 0 } owner
                ? AIContextTreeRegistry.FindAccessor(owner) ?? accessor
                : accessor;

            yield return new TreeProperty(node.Name, node, declaring);
        }
    }

    /// <summary>One property by name, or <see langword="null"/> when the tree records none.</summary>
    private static TreeProperty? FindProperty(object target, string name)
    {
        foreach (var property in PropertiesOf(target))
        {
            if (string.Equals(property.Name, name, StringComparison.Ordinal)) return property;
        }

        return null;
    }

    /// <summary>Renders a compiled run's payload for the model.</summary>
    /// <remarks>
    /// <c>context.Data</c> is whatever the host's own business code put there — an arbitrary object that need
    /// not take part in the archive format. <see cref="VeloxJsonValue.From"/> throws for exactly those, and
    /// letting that escape would turn the whole status call into an error envelope — hiding the status the
    /// Agent asked for, over a payload it only wanted to look at. A payload the format cannot carry is
    /// reported as its text instead, the same fallback <c>AgentObjectToolkit</c> uses for a property value.
    /// </remarks>
    private static VeloxJsonValue DataJson(object? data)
    {
        if (data is null) return VeloxJsonValue.Null;

        try
        {
            return VeloxJsonValue.From(data);
        }
        catch
        {
            return data.ToString();
        }
    }

    private static void AppendScalarProperties(VeloxJsonObject obj, object target)
    {
        foreach (var prop in PropertiesOf(target))
        {
            if (!prop.CanRead) continue;

            var pt = prop.Type;
            if (pt is null) continue;

            if (pt == typeof(string) || pt == typeof(int) || pt == typeof(double) || pt == typeof(bool) ||
                pt == typeof(long) || pt == typeof(float) || pt == typeof(decimal))
            {
                var val = prop.Get(target);
                obj[prop.Name] = val != null ? VeloxJsonValue.From(val) : VeloxJsonValue.Null;
            }
            else if (pt == typeof(Type))
            {
                obj[prop.Name] = (prop.Get(target) as Type)?.FullName;
            }
            else if (pt.IsEnum)
            {
                obj[prop.Name] = prop.Get(target)?.ToString();
            }
        }
    }

    /// <summary>
    /// Checks whether a selector type is allowed by a member's <c>[SlotSelectors]</c> whitelist.
    /// </summary>
    /// <param name="member">The enumerator property, as the tree records it.</param>
    /// <param name="selectorType">The selector type being proposed.</param>
    /// <returns><see langword="true"/> when the member declares no whitelist, or the type is on it.</returns>
    private static bool IsEnumTypeAllowed(AIContextNode member, Type selectorType)
    {
        var fullName = selectorType.FullName ?? selectorType.Name;
        var constrained = false;

        foreach (var reference in member.References)
        {
            if (reference.Kind != AIContextRefKind.SlotSelectorType) continue;

            constrained = true;
            if (string.Equals(reference.DeclaredName, fullName, StringComparison.Ordinal)) return true;
        }

        // 一条白名单都没有 = 不设限。
        return !constrained;
    }

    /// <summary>The labels a selector type's slots carry, in the order the slots are created.</summary>
    private static string[] SelectorLabels(Type selectorType)
    {
        if (selectorType == typeof(bool)) return ["False", "True"];

        var path = AIContextDirectory.Shared.PathFor(selectorType.FullName ?? selectorType.Name);
        if (path is null) return [];

        return [.. AIContextDirectory.Shared.List(path + "/Members").Select(static m => m.Name)];
    }

    /// <summary>All allowed selector type names a member declares, comma-separated.</summary>
    private static string GetAllowedEnumTypeDisplayNames(AIContextNode member)
        => string.Join(", ", member.References
            .Where(static r => r.Kind == AIContextRefKind.SlotSelectorType)
            .Select(static r => r.DeclaredName)
            .Distinct(StringComparer.Ordinal));

    [Description("Finds nodes by type name (substring match) or property value. Returns compact list like ListNodes but filtered. Saves tokens vs. ListNodes + manual filtering.")]
    private string FindNodes(
        [Description("Substring of the node type name to match (case-insensitive). Pass empty string to skip type filter.")] string typeName = "",
        [Description("Optional property name to filter by.")] string? propertyName = null,
        [Description("Optional property value (string) to match.")] string? propertyValue = null)
    {
        var nodes = Tree.Nodes;
        var result = new VeloxJsonArray();
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var nodeTypeName = node.GetType().Name;
            if (!string.IsNullOrEmpty(typeName) &&
                nodeTypeName.IndexOf(typeName, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            if (!string.IsNullOrEmpty(propertyName))
            {
                var prop = FindProperty(node, propertyName!);
                if (prop is null || !prop.Value.CanRead) continue;
                var valStr = prop.Value.Get(node)?.ToString() ?? "";
                if (propertyValue != null && !string.Equals(valStr, propertyValue, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            var obj = new VeloxJsonObject
            {
                ["i"] = i,
                ["id"] = GetComponentId(node),
                ["t"] = nodeTypeName,
            };
            AppendScalarProperties(obj, node);
            result.Add(obj);
        }
        return result.ToJson();
    }

    [Description("Resolves a slot's runtime ID from its owning property name on a node. For collections, specify the index. Avoids needing GetNodeDetail just to get a slot ID.")]
    private string ResolveSlotId(
        [Description("Node index.")] int nodeIndex,
        [Description("Property name of the slot, e.g. 'InputSlot', 'OutputSlots'.")] string propertyName,
        [Description("For collection properties, the zero-based index within the collection. Ignored for single-slot properties.")] int collectionIndex = 0)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        var prop = FindProperty(node, propertyName);
        if (prop is null) return Error($"Property '{propertyName}' not found on {AgentTypeNames.SimpleOf(node)}.");

        if (prop.Value.HoldsASingleSlot(node))
        {
            if (prop.Value.Get(node) is not IWorkflowSlotViewModel slot) return Error($"Slot property '{propertyName}' is null.");
            return new VeloxJsonObject { ["status"] = "ok", ["id"] = GetComponentId(slot), ["prop"] = propertyName }.ToJson();
        }
        else if (prop.Value.IsSlotEnumerator)
        {
            // 枚举器是**另一**种集合：它装的是 ConditionalSlot，本身不是 IList，所以下面那条通用集合分支
            // 读不到它（以前这里没有这条分支，于是它对枚举器一律答「不是槽也不是槽集合」——
            // 而枚举器明明就是槽集合，只是形状不同）。按条件取第 collectionIndex 个。
            if (prop.Value.Get(node) is not IConditionalSlotProvider enumerator)
                return Error($"SlotEnumerator '{propertyName}' is null.");
            if (collectionIndex < 0 || collectionIndex >= enumerator.Slots.Count)
                return Error($"SlotEnumerator '{propertyName}' index {collectionIndex} out of range (count={enumerator.Slots.Count}).");
            return new VeloxJsonObject
            {
                ["status"] = "ok",
                ["id"] = GetComponentId(enumerator.Slots[collectionIndex].Slot),
                ["prop"] = $"{propertyName}[{collectionIndex}]",
                ["index"] = collectionIndex,
                ["label"] = enumerator.Slots[collectionIndex].Name,
            }.ToJson();
        }
        else if (prop.Value.IsSlotCollection)
        {
            if (prop.Value.Get(node) is not IList col || collectionIndex < 0 || collectionIndex >= col.Count)
                return Error($"Collection property '{propertyName}' index {collectionIndex} out of range or null.");
            if (col[collectionIndex] is not IWorkflowSlotViewModel slot2)
                return Error($"Element at [{collectionIndex}] is not a slot.");
            return new VeloxJsonObject { ["status"] = "ok", ["id"] = GetComponentId(slot2), ["prop"] = propertyName, ["index"] = collectionIndex }.ToJson();
        }
        return Error($"Property '{propertyName}' is not a slot or slot collection.");
    }

    // ────────────────────────── Composite Functions (reduce round-trips) ──────────────────────────

    [Description("Connects two slots by property names on their owning nodes. No need to resolve slot IDs first. For collection properties, specify the index. Example: ConnectByProperty(senderNodeIndex: 0, senderProperty: \"OutputSlot\", receiverNodeIndex: 1, receiverProperty: \"InputSlot\").")]
    private async Task<string> ConnectByProperty(
        [Description("Sender node index.")] int senderNodeIndex,
        [Description("Sender slot property name, e.g. 'OutputSlot', 'OutputSlots'.")] string senderProperty,
        [Description("Receiver node index.")] int receiverNodeIndex,
        [Description("Receiver slot property name, e.g. 'InputSlot'.")] string receiverProperty,
        [Description("For sender collection properties, the zero-based index. Default 0.")] int senderCollectionIndex = 0,
        [Description("For receiver collection properties, the zero-based index. Default 0.")] int receiverCollectionIndex = 0,
        CancellationToken cancellationToken = default)
    {
        var senderSlot = ResolveSlotFromProperty(senderNodeIndex, senderProperty, senderCollectionIndex);
        if (senderSlot == null) return Error($"Cannot resolve sender slot: node={senderNodeIndex}, prop={senderProperty}[{senderCollectionIndex}].");
        var receiverSlot = ResolveSlotFromProperty(receiverNodeIndex, receiverProperty, receiverCollectionIndex);
        if (receiverSlot == null) return Error($"Cannot resolve receiver slot: node={receiverNodeIndex}, prop={receiverProperty}[{receiverCollectionIndex}].");

        await SendReceiveAsync(senderSlot, receiverSlot, cancellationToken);

        bool connected = VerifyConnection(senderSlot, receiverSlot);
        if (!connected)
            return ConnectionRejected(senderSlot, receiverSlot,
                $"[{senderNodeIndex}].{senderProperty}", $"[{receiverNodeIndex}].{receiverProperty}");
        return Ok($"Connected [{senderNodeIndex}].{senderProperty}→[{receiverNodeIndex}].{receiverProperty}.");
    }




    [Description("Returns the full topology: all nodes with their slots (including property names and IDs), plus all connections. One call replaces ListNodes + GetNodeDetail×N + ListConnections. Use for complex multi-node operations.")]
    private string GetFullTopology()
    {
        var nodesArr = new VeloxJsonArray();
        for (int i = 0; i < Tree.Nodes.Count; i++)
        {
            var node = Tree.Nodes[i];
            var slotPropertyMap = BuildSlotPropertyMap(node);
            var nodeObj = new VeloxJsonObject
            {
                ["i"] = i,
                ["id"] = GetComponentId(node),
                ["t"] = node.GetType().Name,
            };
            AppendScalarProperties(nodeObj, node);

            var slotsArr = new VeloxJsonArray();
            for (int s = 0; s < node.Slots.Count; s++)
            {
                var slot = node.Slots[s];
                var slotObj = new VeloxJsonObject
                {
                    ["si"] = s,
                    ["id"] = GetComponentId(slot),
                    ["ch"] = slot.Channel.ToString(),
                };
                if (slotPropertyMap.TryGetValue(slot, out var propName))
                    slotObj["prop"] = propName;
                slotsArr.Add(slotObj);
            }
            nodeObj["slots"] = slotsArr;
            nodesArr.Add(nodeObj);
        }

        var linksArr = new VeloxJsonArray();
        foreach (var link in Tree.Links)
        {
            if (!link.IsVisible) continue;
            linksArr.Add(new VeloxJsonObject
            {
                ["id"] = GetComponentId(link),
                ["sid"] = link.Sender != null ? GetComponentId(link.Sender) : null,
                ["rid"] = link.Receiver != null ? GetComponentId(link.Receiver) : null,
            });
        }

        return new VeloxJsonObject
        {
            ["nodes"] = nodesArr,
            ["links"] = linksArr,
        }.ToJson();
    }

    /// <summary>
    /// Resolves a slot instance from node index + property name + optional collection index.
    /// Supports direct slot properties, plain slot collections, and SlotEnumerator (IConditionalSlotProvider) properties.
    /// For SlotEnumerator properties <paramref name="collectionIndex"/> selects the slot by Items[i].Slot.
    /// Returns null if not found.
    /// </summary>
    private IWorkflowSlotViewModel? ResolveSlotFromProperty(int nodeIndex, string propertyName, int collectionIndex = 0)
    {
        if (!TryGetNode(nodeIndex, out var node, out _) || node == null) return null;
        var prop = FindProperty(node, propertyName);
        if (prop is null || !prop.Value.CanRead) return null;

        if (prop.Value.HoldsASingleSlot(node))
            return prop.Value.Get(node) as IWorkflowSlotViewModel;

        if (prop.Value.IsSlotEnumerator)
        {
            if (prop.Value.Get(node) is not IConditionalSlotProvider enumerator) return null;
            if (collectionIndex < 0 || collectionIndex >= enumerator.Slots.Count) return null;

            return enumerator.Slots[collectionIndex].Slot;
        }

        if (prop.Value.IsSlotCollection)
        {
            if (prop.Value.Get(node) is not IList col || collectionIndex < 0 || collectionIndex >= col.Count)
                return null;
            return col[collectionIndex] as IWorkflowSlotViewModel;
        }
        return null;
    }

    /// <summary>
    /// Raises <c>Anchor</c>/<c>Size</c> PropertyChanged so the platform's slot layout behavior
    /// re-syncs slot anchor positions after slots changed on a node with
    /// <see cref="SlotEnumerator{TSlot}"/> properties. Unlike the old ±0.5px move nudge, this is
    /// non-mutating: it produces NO undo entries and leaves the node geometry untouched.
    /// </summary>
    private static void RefreshSlotAnchors(IWorkflowNodeViewModel node)
    {
        node.OnPropertyChanged(nameof(node.Anchor));
        node.OnPropertyChanged(nameof(node.Size));
    }

    private static bool HasSlotEnumerator(IWorkflowNodeViewModel node)
        => PropertiesOf(node).Any(static p => p.IsSlotEnumerator);

    /// <summary>
    /// Verifies a SlotEnumerator is actually installed on the given node before mutating it.
    /// Uninstalled enumerators make <c>SetSelector</c> a silent no-op, so we surface it as an
    /// explicit error instead (matching the framework's no-silent-failures contract).
    /// </summary>
    private static bool IsEnumeratorInstalled(object enumerator, IWorkflowNodeViewModel node)
        => enumerator is IConditionalSlotProvider provider && ReferenceEquals(provider.Parent, node);

    /// <summary>
    /// Switches a SlotEnumerator's selector through Core's non-generic <c>IConditionalSlotProvider</c> view.
    /// </summary>
    /// <remarks>
    /// The enumerator is generic, so a caller holding one as an object cannot name its slot type — the
    /// non-generic interface is what makes this an ordinary call instead of a reflective lookup of
    /// <c>SetSelector</c> on whichever <c>IConditionalSlotProvider&lt;T&gt;</c> the concrete type implements.
    /// </remarks>
    private static void InvokeSetSelector(object enumerator, object? selector)
    {
        if (enumerator is not IConditionalSlotProvider provider)
            throw new InvalidOperationException($"'{AgentTypeNames.SimpleOf(enumerator)}' does not implement IConditionalSlotProvider<T>.");

        provider.SetSelector(selector);
    }

    private static void RefreshSlotAnchorsIfEnumSlotNode(IWorkflowNodeViewModel node)
    {
        if (HasSlotEnumerator(node))
            RefreshSlotAnchors(node);
    }

    /// <summary>
    /// Checks whether a connection was actually established between two slots
    /// by verifying the link exists in <see cref="IWorkflowTreeViewModel.LinksMap"/>.
    /// The framework may silently reject connections due to channel incompatibility,
    /// same-node constraint, or developer-overridden <c>ValidateConnection</c>.
    /// </summary>
    private bool VerifyConnection(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
    {
        return Tree.LinksMap.TryGetValue(sender, out var dic) && dic.ContainsKey(receiver);
    }

    /// <summary>
    /// Builds a structured error response when a connection is rejected by the framework,
    /// including diagnostic hints about the likely rejection reason.
    /// </summary>
    private string ConnectionRejected(
        IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver,
        string senderLabel, string receiverLabel)
    {
        var reasons = new List<string>();
        if (sender.Parent != null && receiver.Parent != null && sender.Parent == receiver.Parent)
            reasons.Add("same-node connection is not allowed");
        if (!sender.Channel.HasFlag(SlotChannel.OneTarget) &&
            !sender.Channel.HasFlag(SlotChannel.MultipleTargets) &&
            !sender.Channel.HasFlag(SlotChannel.OneBoth) &&
            !sender.Channel.HasFlag(SlotChannel.MultipleBoth))
            reasons.Add($"sender channel '{sender.Channel}' cannot send");
        if (!receiver.Channel.HasFlag(SlotChannel.OneSource) &&
            !receiver.Channel.HasFlag(SlotChannel.MultipleSources) &&
            !receiver.Channel.HasFlag(SlotChannel.OneBoth) &&
            !receiver.Channel.HasFlag(SlotChannel.MultipleBoth))
            reasons.Add($"receiver channel '{receiver.Channel}' cannot receive");
        if (reasons.Count == 0)
            reasons.Add("developer ValidateConnection rule or channel capacity limit");

        return new VeloxJsonObject
        {
            ["status"] = "rejected",
            ["message"] = $"Connection {senderLabel}→{receiverLabel} was rejected by the framework.",
            ["reasons"] = VeloxJsonValue.From(reasons),
            ["hint"] = "Do NOT retry the same connection. Check slot channels and ValidateConnection rules, or choose different slots."
        }.ToJson();
    }

    /// <summary>
    /// Dispatches a command and waits until it actually completes. <c>ExecuteAsync</c> is fire-and-forget, so
    /// without this the Agent could never observe when node work really finished.
    /// Throws on failure so the caller can return a structured error.
    /// </summary>
    private static async Task WaitForCommandAsync(IVeloxCommand command, object? parameter, CancellationToken ct)
    {
        var completion = await command.ExecuteAndWaitAsync(parameter, ct).ConfigureAwait(false);

        if (completion.Exception is not null)
        {
            throw completion.Exception;
        }

        if (completion.Outcome == CommandOutcome.Refused)
        {
            // 以前这一支会永久挂起：被 Lock 挡下的调用不发 Exited，等待者永远等不到。
            // 抛出去是把「永远等不到」换成一句能返回给 Agent 的话。
            throw new OperationCanceledException("The command was refused because it is locked.");
        }

        // Canceled 照常返回：被中断的运行是「跑过了，结果是被取消」，不是失败 ——
        // Agent 要能从一次自己停掉的运行里接着往下走。这与旧的 Exited 语义一致。
    }

    /// <summary>
    /// Dispatches the tree's Send then Receive connection commands and awaits both completions, so a
    /// connection tool returns only after the connection is actually created.
    /// </summary>
    private async Task SendReceiveAsync(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver, CancellationToken ct)
        => await Task.WhenAll(
            Tree.SendConnectionCommand.ExecuteAndWaitAsync(sender, ct),
            Tree.ReceiveConnectionCommand.ExecuteAndWaitAsync(receiver, ct)).ConfigureAwait(false);

    /// <summary>
    /// Applies a set of anchor changes as a SINGLE undoable action and waits until the anchors are
    /// actually applied. A human performs a layout gesture once, so the Agent tool must produce exactly
    /// one undo entry for the whole layout. Nodes whose anchor already equals the target are excluded,
    /// so an alignment/layout that doesn't actually move anything does not create a no-op undo entry.
    /// </summary>
    private static string Ok(string message) => new VeloxJsonObject { ["status"] = "ok", ["message"] = message }.ToJson();
    private static string Error(string message) => new VeloxJsonObject { ["status"] = "error", ["message"] = message }.ToJson();
}
