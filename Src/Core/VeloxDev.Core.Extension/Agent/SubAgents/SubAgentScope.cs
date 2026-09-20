using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.AI.Pipelines;
using VeloxDev.AI.Workflow;
using VeloxDev.AI.Workflow.Functions;

namespace VeloxDev.AI.SubAgents;

/// <summary>
/// What one spawn asks for: the task, and the slice of the parent's capabilities the child should get.
/// <para>
/// Every narrowing field is nullable, and a nullable field means <i>inherit</i> rather than <i>none</i> —
/// the model omits what it does not care about, and the server fills it from the parent. There is no way to
/// ask for something the parent does not have: whatever exceeds it is dropped and reported back.
/// </para>
/// </summary>
internal sealed class SubAgentRequest
{
    public string Task { get; set; } = string.Empty;
    public string? Name { get; set; }

    /// <summary>
    /// The exact tools the child may use, or <c>null</c> to inherit the parent's read-only surface.
    /// An empty array is a request for no tools at all, which is a different thing from omitting it.
    /// </summary>
    public string[]? AllowedTools { get; set; }

    public int? MaxToolCalls { get; set; }
    public int? MaxReadToolCalls { get; set; }
    public int? MaxWriteToolCalls { get; set; }
    public bool? AllowNodeExecution { get; set; }
    public string[]? AllowedGenericCommands { get; set; }
    public bool? AutoMarkDirty { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// What a scope tells its own children about themselves: the facts of their own spawn, which no provider
/// of theirs could otherwise know.
/// <para>
/// Held here rather than written into the child's instructions because the child's instructions are the
/// host's — <see cref="SubAgentScope.ForClient"/> writes a fixed preamble, and a host that supplies its own
/// would lose this. Contributed per turn instead, beside the roster, so it survives either choice.
/// </para>
/// </summary>
internal sealed class ChildBriefing
{
    public string Name { get; set; } = string.Empty;
    public int Depth { get; set; }
    public int? MaxToolCalls { get; set; }
    public string? Notes { get; set; }
    public IReadOnlyList<string> Dropped { get; set; } = [];
    public bool CanSpawn { get; set; }
    public int RemainingDepth { get; set; }
}

/// <summary>
/// The sub-agent subsystem: the registry of the agents a scope spawned, the tools that manage them, and the
/// narrowing rules that keep a child inside its parent's capabilities.
/// <para>
/// <b>Dispatch and poll, not call and wait.</b> <see cref="TrySpawn"/> returns a handle as soon as the child
/// exists and runs it in the background; the model then waits, reads a result, or cancels. That shape is
/// forced by where this runs: a spawn happens inside a tool call, on a thread the host's UI owns, and a
/// synchronous child would hold that thread for the child's whole conversation.
/// </para>
/// <para>
/// <b>A child's allowance is a share of its parent's, not a second pot beside it.</b> The child's scope is
/// given the parent's <see cref="ToolCallLedger"/> as its outer ledger, so every call anywhere in the tree
/// is counted at the root, and the root's cap is the tree's cap. What the spawn grants is a <i>sub-limit</i>
/// on the child's own subtree, not a reservation — spawning three children does not divide the pot, it
/// bounds each of them, and the pot itself is enforced at call time.
/// </para>
/// <para>
/// That is also what makes an unbounded depth terminate. A grant is clamped to at most one less than the
/// parent's remaining allowance, so along any root-to-leaf path the grants strictly decrease and each is at
/// least one: the depth of the tree cannot exceed the root's allowance. Note that this buys termination and
/// not practicality — a root allowing 200 calls permits a 199-deep chain. Set
/// <see cref="WithSubAgentDepth"/> as well; the budget is the guarantee, the depth limit is what makes the
/// tree useful.
/// </para>
/// </summary>
public sealed class SubAgentScope : IAsyncDisposable
{
    private readonly Func<WorkflowAgentScope, AIAgent> _agentFactory;
    private readonly string? _instructions;
    private readonly List<SubAgentEntry> _entries = [];
    private readonly object _gate = new();

    private long _version;
    private SynchronizationContext? _ui;
    private WorkflowAgentScope? _parent;
    private bool _disposed;

    /// <summary>
    /// Creates a subsystem that builds each child agent with <paramref name="agentFactory"/>.
    /// <para>
    /// The factory is handed the child's scope and must return a runnable agent over it — normally
    /// <c>scope =&gt; client.AsAIAgent(scope.CreateContextProviders(), preamble).WithPipeline(scope.Pipeline)</c>,
    /// which is exactly what <see cref="ForClient"/> supplies. The library never holds a chat client of its
    /// own: which model a child runs on is the host's decision, and passing a factory is how that decision
    /// stays outside.
    /// </para>
    /// </summary>
    /// <param name="agentFactory">Builds the agent for one child scope.</param>
    /// <param name="instructions">
    /// The child's standing preamble. Omitted, a compact default is used — deliberately not the workflow
    /// skeleton, which is a per-scope megabyte; a child learns what it can do from its own context provider
    /// on every turn instead.
    /// </param>
    public SubAgentScope(Func<WorkflowAgentScope, AIAgent> agentFactory, string? instructions = null)
    {
        _agentFactory = agentFactory ?? throw new ArgumentNullException(nameof(agentFactory));
        _instructions = instructions;
    }

    /// <summary>
    /// A subsystem whose children are agents over <paramref name="client"/>, sharing the host's model.
    /// </summary>
    public static SubAgentScope ForClient(IChatClient client, string? instructions = null)
    {
        if (client is null) throw new ArgumentNullException(nameof(client));
        return new SubAgentScope(scope => client
            .AsAIAgent(scope.CreateContextProviders(), instructions ?? DefaultInstructions)
            .WithPipeline(scope.Pipeline));
    }

    /// <summary>
    /// The preamble a spawned agent is given when the host supplies none. Sibling of the workflow
    /// skeleton's opening, but a few hundred bytes rather than a few hundred kilobytes — a background
    /// child's job is stated by its task, and its capabilities arrive from its own provider each turn.
    /// </summary>
    internal const string DefaultInstructions =
        "You are a sub-agent dispatched in the background by another agent to carry out one task. "
        + "The task is the user message of this conversation; do not ask for clarification, decide what is "
        + "reasonable and proceed. Use the tools you have been given, then answer with a concise report of "
        + "what you found or did. Your abilities are a narrowed subset of the agent that spawned you, and "
        + "the tools you actually hold are the whole of what you may use — do not attempt to obtain others.";

    /// <summary>The child agents this scope has spawned, oldest first, as bindable rows.</summary>
    public ObservableCollection<SubAgentStatusViewModel> Children { get; } = [];

    private volatile SubAgentSummary[] _snapshot = [];

    /// <summary>
    /// Immutable copy of <see cref="Children"/>, republished on the roster's thread whenever a child
    /// changes. Read this — not <see cref="Children"/> — from anywhere else: an agent invocation renders
    /// its prompt on a thread of the framework's choosing, and enumerating an
    /// <see cref="ObservableCollection{T}"/> there races the host's UI.
    /// </summary>
    public IReadOnlyList<SubAgentSummary> Snapshot => _snapshot;

    /// <summary>Monotonic version of the roster and its rows. A provider caches its render on it.</summary>
    public long Version => Interlocked.Read(ref _version);

    /// <summary>
    /// Identifies this subsystem for a context provider's session-state key. The framework throws when two
    /// providers attached to one agent share a key, and the keys default to the provider's type name — so
    /// the discriminator has to live on the scope.
    /// <para>
    /// A <see cref="Guid"/> per instance, not the workflow scope's <c>StateDiscriminator</c>: that one is
    /// derived from the tree, and a parent and its child sit on the same tree, so it would collide on
    /// exactly the pair that must not.
    /// </para>
    /// </summary>
    internal string InstanceId { get; } = Guid.NewGuid().ToString("N");

    /// <summary>Raised whenever <see cref="Version"/> advances.</summary>
    public event EventHandler? Changed;

    private int _spawnBudget = 64;

    /// <summary>
    /// The allowance assumed for a spawn when the parent scope set no <c>WithMaxToolCalls</c>.
    /// <para>
    /// Without it, "the parent's remaining allowance" would be undefined for an uncapped parent and the
    /// descending grant that guarantees termination would not exist. With it the ceiling is always finite,
    /// so the guarantee holds unconditionally — including for the host that never configured a budget.
    /// </para>
    /// </summary>
    public int SpawnBudget => _spawnBudget;

    /// <summary>Sets <see cref="SpawnBudget"/>. Inherited by every scope spawned below this one.</summary>
    public SubAgentScope WithSpawnBudget(int budget)
    {
        _spawnBudget = Math.Max(1, budget);
        BumpVersion();
        return this;
    }

    private int _maxDepth = int.MaxValue;

    /// <summary>
    /// The deepest a spawned agent may sit. A child of the scope this is attached to is depth 1, a
    /// grandchild depth 2, and so on; a scope at or past the limit keeps no ability to spawn.
    /// </summary>
    public int MaxDepth => _maxDepth;

    /// <summary>
    /// Bounds how deep the tree may go. Inherited by every scope spawned below this one.
    /// <para>
    /// The budget already makes the tree terminate; this is what makes it <i>usable</i>. A chain 199 deep
    /// is bounded and useless, so a host that attaches this subsystem should normally set both.
    /// </para>
    /// </summary>
    public SubAgentScope WithSubAgentDepth(int depth)
    {
        _maxDepth = Math.Max(0, depth);
        BumpVersion();
        return this;
    }

    /// <summary>This scope's own depth in its tree: 0 for the scope the host attached it to.</summary>
    internal int Depth { get; set; }

    /// <summary>
    /// Copies the settings a spawned scope inherits from the one that spawned it. Deliberately not the
    /// public <c>With*</c> calls: those clamp their argument and advance a version, and a brand-new scope
    /// with no provider attached yet has nothing to invalidate.
    /// </summary>
    internal void InheritFrom(SubAgentScope parent)
    {
        _spawnBudget = parent._spawnBudget;
        _maxDepth = parent._maxDepth;
    }

    /// <summary>
    /// The id of the row that represents the agent this scope belongs to, or <c>null</c> for the root.
    /// <para>
    /// This is what links the roster to a tree. The records are held per scope — which is what makes a
    /// child unable to see its siblings, since it only ever reads its own — so a row cannot name its parent
    /// by looking one up; the spawn that created this scope writes its own row's id down here, and every
    /// child of this scope then points at it.
    /// </para>
    /// </summary>
    internal string? SelfId { get; set; }

    /// <summary>Whether this scope may still spawn at all, as opposed to merely being allowed to try.</summary>
    internal bool CanSpawn => Depth < MaxDepth;

    /// <summary>What this scope's own spawn told it about itself, when it was a child.</summary>
    internal ChildBriefing? Briefing { get; set; }

    /// <summary>
    /// The UI thread the roster is bound to, or <c>null</c> to write it on whatever thread a spawn or a
    /// completion happens to run on.
    /// </summary>
    public SubAgentScope WithSynchronizationContext(SynchronizationContext? context)
    {
        _ui = context;
        return this;
    }

    /// <summary>The scope this subsystem serves, or <c>null</c> until <c>WithSubAgents</c> attaches it.</summary>
    internal WorkflowAgentScope? Parent => _parent;

    /// <summary>
    /// Binds this subsystem to the scope whose capabilities it narrows against and whose ledger it charges
    /// to. Called by <c>WorkflowAgentScope.WithSubAgents</c>; there is nothing for a host to do with it.
    /// </summary>
    internal void Attach(WorkflowAgentScope parent)
    {
        _parent = parent ?? throw new ArgumentNullException(nameof(parent));
        _ui ??= parent.UIContext;
    }

    /// <summary>
    /// Creates the context provider that contributes the management tools and the roster on every turn.
    /// </summary>
    /// <param name="tools">
    /// The composing host's policy, so a spawn is counted, gated and reported like any other call. Omit for
    /// standalone use — the provider then builds a thread-only policy from this scope's own context.
    /// </param>
    /// <param name="pipeline">The chain the host's events travel on, if any.</param>
    public AIContextProvider CreateContextProvider(ToolPipeline? tools = null, AgentPipeline? pipeline = null)
        => new SubAgentAgentContextProvider(this, tools, pipeline);

    // ────────────────────────── roster ──────────────────────────

    private void BumpVersion()
    {
        Interlocked.Increment(ref _version);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Runs <paramref name="action"/> on the thread the roster is bound to. Inline when there is no context
    /// — which is what lets the tests run without a dispatcher — and awaited when there is one, so a caller
    /// that reads the roster straight afterwards sees the change rather than a queued callback.
    /// </summary>
    private ValueTask OnRosterThread(Action action) => PipelineDispatch.RunAsync(_ui, action);

    private void Republish()
    {
        _snapshot = [.. _entries.Select(e => new SubAgentSummary
        {
            Id = e.Row.Id,
            Name = e.Row.Name,
            ParentId = e.Row.ParentId,
            Depth = e.Row.Depth,
            Task = e.Row.Task,
            State = e.Row.State,
            StateText = e.Row.StateText,
            Result = e.Row.Result,
            Error = e.Row.Error,
            CallCount = e.Row.CallCount,
            MaxToolCalls = e.Row.MaxToolCalls,
            GrantedToolCount = e.Row.GrantedTools.Count,
            DroppedRequests = [.. e.Row.DroppedRequests],
        })];

        BumpVersion();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e) => Republish();

    /// <summary>
    /// Refreshes every row's call count from its child's ledger. Called before a roster render and before a
    /// wait returns, so the number a panel or the model reads is what the child has actually spent rather
    /// than what it had spent when it last changed state.
    /// </summary>
    private void RefreshCallCounts()
    {
        foreach (var entry in _entries)
            entry.Row.CallCount = entry.Scope.CreateToolkit().CallUsage.ToolCalls;
    }

    // ────────────────────────── spawning ──────────────────────────

    /// <summary>
    /// Spawns a child and returns its handle immediately, or returns <c>null</c> with
    /// <paramref name="refusal"/> set when the spawn itself was refused.
    /// </summary>
    /// <remarks>
    /// Must be called on the roster's thread — a tool body already is, since the composing host marshalled
    /// it. The child's configuration happens here and is therefore serialized with every other spawn and
    /// with the panel; only the child's own run is put on the thread pool.
    /// </remarks>
    internal string? TrySpawn(SubAgentRequest request, out string? refusal)
    {
        refusal = null;
        var parent = _parent ?? throw new InvalidOperationException(
            $"This {nameof(SubAgentScope)} is not attached to a scope. Call {nameof(WorkflowAgentScope.WithSubAgents)} on the scope first.");

        if (_disposed)
        {
            refusal = "This session has been disposed; no further sub-agents can be spawned.";
            return null;
        }

        if (!CanSpawn)
        {
            refusal = $"Sub-agents cannot be spawned this deep: the limit is {MaxDepth} and this agent is at depth {Depth}. "
                    + "Carry the task out yourself and report what you did.";
            return null;
        }

        var dropped = new List<string>();

        // ── the allowance: a share of what the parent has left, and never the whole of it ──
        var parentLedger = parent.CreateToolkit().Ledger;
        var remaining = RemainingAllowance(parent, parentLedger);
        var granted = Math.Min(request.MaxToolCalls ?? remaining, remaining - 1);
        if (granted < 1)
        {
            refusal = "No tool-call allowance remains for a sub-agent. The session's budget is spent, or this "
                    + "agent's own share is; report what you have done rather than delegating further.";
            return null;
        }
        if (request.MaxToolCalls is { } askedBudget && askedBudget > granted)
            dropped.Add($"maxToolCalls: asked for {askedBudget}, granted {granted} — the parent has that much left at most");

        // ── the tool surface: the parent's own, narrowed ──
        // Narrowing is against the parent's *potential* surface, not against what its switches currently
        // allow. Both halves matter and they are not the same list: what may be asked for is what the parent
        // currently offers, while what must be switched off is everything the child could otherwise reach —
        // an inherited tool the parent itself had turned off would be a capability the child holds and its
        // parent does not, which is the one thing this subsystem exists to make impossible.
        var parentToolkit = parent.CreateToolkit();
        var everyName = parentToolkit.CreateAllTools()
            .Select(t => t.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var available = everyName
            .Where(parent.IsToolEnabled)
            .Where(name => !NoChildMayHold(name))
            .ToList();

        List<string> grantedTools;
        if (request.AllowedTools is { } wanted)
        {
            grantedTools = [.. wanted.Where(name => available.Contains(name, StringComparer.OrdinalIgnoreCase))];
            foreach (var name in wanted.Where(name => !available.Contains(name, StringComparer.OrdinalIgnoreCase)))
                dropped.Add(NoChildMayHold(name)
                    ? $"{name}: never granted to a sub-agent — {NoChildMayHoldReason}"
                    : $"{name}: not available to this agent, or switched off by the host");
        }
        else
        {
            // Inheriting means inheriting the *read-only* part. A child that must change the graph says so
            // by naming the tools, which keeps the default the one that cannot damage anything.
            grantedTools = [.. available.Where(parentToolkit.IsQueryOnlyTool)];
        }

        // A background child must not put a modal question to the user while its parent's turn is suspended,
        // and there is no UI context to marshal a graph mutation onto. Both are properties of being a
        // background child rather than requests the model gets to make, so they are applied unconditionally
        // — safety enforced in code, not in prompt prose.
        if (parent.UIContext is null)
        {
            var needingUi = grantedTools.Where(name => !parentToolkit.IsQueryOnlyTool(name)).ToList();
            foreach (var name in needingUi)
            {
                grantedTools.Remove(name);
                dropped.Add($"{name}: needs a UI synchronization context, and the host registered none");
            }
        }

        var child = parent.Tree.AsAgentScope();
        var childDepth = Depth + 1;

        child.WithMaxToolCalls(granted);
        ApplyCap(child, request.MaxReadToolCalls, parent.MaxReadToolCalls, "maxReadToolCalls", dropped);
        ApplyCap(child, request.MaxWriteToolCalls, parent.MaxWriteToolCalls, "maxWriteToolCalls", dropped);
        child.WithAllowNodeExecution(RequestableNodeExecution(request, parent, dropped));

        var commands = RequestableCommands(request, parent, dropped);
        if (commands.Count > 0) child.WithAllowedGenericCommands([.. commands]);
        child.WithAutoMarkDirty(RequestableAutoMarkDirty(request, parent, dropped));

        // Everything the child could otherwise reach but was not granted is switched off by name — over the
        // parent's whole surface, not over the part its own switches happen to allow.
        foreach (var name in everyName)
            if (!grantedTools.Contains(name, StringComparer.OrdinalIgnoreCase))
                child.WithToolEnabled(name, false);

        if (request.AllowedTools is not null)
            foreach (var name in SubAgentAgentToolkit.ToolNames)
                if (!request.AllowedTools.Contains(name, StringComparer.OrdinalIgnoreCase))
                    child.WithToolEnabled(name, false);

        // The one gate the child can never open. Its two halves are independent on purpose: the level stops
        // the reset from asking a question, and the switch stops it from being offered or reached at all.
        child.WithInteractionSafety(0);
        child.WithToolEnabled(WorkflowAgentToolkit.ResetBudgetToolName, false);

        // Takes effect before the toolkit exists, so the child's very first call is already charged to the
        // tree rather than to a counter of its own.
        child.ParentLedger = parentLedger;

        // The host's thread, not one of the child's own. A child's graph edits and its parent's are
        // serialized only by passing through the same context — and the child runs on a thread-pool thread,
        // so without this the permission to mutate granted above would be permission to race. Set before
        // WithSubAgents, which forwards this scope's context down to the child's own subsystem.
        child.WithSynchronizationContext(parent.UIContext);

        var transcript = new AgentTranscript();
        child.WithTranscript(transcript);

        // ── the row, then the run ──
        var id = Guid.NewGuid().ToString("N");
        var grand = new SubAgentScope(_agentFactory, _instructions)
        {
            Depth = childDepth,
            // Its children will name this row as their parent, so the tree can be built from rows alone.
            SelfId = id,
            Briefing = new ChildBriefing
            {
                Name = request.Name ?? string.Empty,
                Depth = childDepth,
                MaxToolCalls = granted,
                Notes = request.Notes,
                Dropped = dropped,
                CanSpawn = childDepth < MaxDepth,
                RemainingDepth = Math.Max(0, MaxDepth - childDepth),
            },
        };
        grand.InheritFrom(this);
        child.WithSubAgents(grand);

        var row = new SubAgentStatusViewModel(id, Describe(request, id), SelfId, childDepth, request.Task)
        {
            MaxToolCalls = granted,
            Transcript = transcript,
        };
        row.SetGrantedTools(grantedTools.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
        row.SetDroppedRequests(dropped);

        var cancellation = new CancellationTokenSource();
        var entry = new SubAgentEntry(row, child, grand, cancellation);

        row.PropertyChanged += OnRowChanged;
        _entries.Add(entry);
        Children.Add(row);
        Republish();

        entry.Run = Task.Run(() => RunAsync(entry, request));
        return id;
    }

    private static string Describe(SubAgentRequest request, string id)
        => string.IsNullOrWhiteSpace(request.Name) ? $"agent-{id[..8]}" : request.Name!;

    /// <summary>
    /// Why a tool can never be handed to a spawned agent, whatever the request says. Shared by the predicate
    /// below and by the reasoning it reports, so the two cannot describe different rules.
    /// </summary>
    private const string NoChildMayHoldReason =
        "a background sub-agent must never put a modal question to the user, because the turn of the agent "
        + "that dispatched it is still suspended, and asking the user to widen a budget is the parent's move, not a child's";

    /// <summary>
    /// Tools that are subtracted from what a spawn may match against, rather than switched off after the
    /// grant is reported.
    /// <para>
    /// The distinction is the whole point of the reporting contract: a grant list that named one of these
    /// would be a promise the child cannot keep, and a model that believed it would plan around a capability
    /// that is not there. Switching it off afterwards is not enough — the report has already been made.
    /// </para>
    /// <para>
    /// These are structural rather than policy: a background child's interaction level is zeroed and its
    /// budget tool is switched off unconditionally (see the child assembly below), so neither is a decision
    /// any request can change.
    /// </para>
    /// </summary>
    private static bool NoChildMayHold(string name)
        => string.Equals(name, WorkflowAgentToolkit.ResetBudgetToolName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "RequestConfirmation", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "RequestSelection", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// How much of the session's allowance this scope's subtree may still spend.
    /// <para>
    /// The parent's own cap governs its whole subtree — the accounting walks the chain, so the parent's
    /// usage already includes everything its existing children spent, and a later sibling starts from a
    /// smaller remainder. When the parent set no cap, <see cref="SpawnBudget"/> stands in for it, which is
    /// what keeps the answer finite and the descent that guarantees termination intact.
    /// </para>
    /// </summary>
    private int RemainingAllowance(WorkflowAgentScope parent, ToolCallLedger ledger)
    {
        var remaining = (parent.MaxToolCalls ?? SpawnBudget) - ledger.Usage.ToolCalls;

        // The root's pot is the session's, and a grant that ignored it would promise a child more than the
        // tree can actually deliver — the call-time gate would refuse it later, far from the spawn that
        // lied about it.
        var root = ledger.Root;
        if (!ReferenceEquals(root, ledger) && root.Owner.MaxToolCalls is { } rootCap)
            remaining = Math.Min(remaining, rootCap - root.Usage.ToolCalls);

        return remaining;
    }

    /// <summary>
    /// Grants a read or write cap: the request when the parent has no such cap, the parent's when the
    /// request is silent, and otherwise the smaller of the two — reporting a request the parent could not
    /// honour rather than quietly shrinking it.
    /// </summary>
    private static void ApplyCap(WorkflowAgentScope child, int? requested, int? parentCap, string label, List<string> dropped)
    {
        if (parentCap is not { } cap)
        {
            if (requested is { } want) SetCap(child, label, want);
            return;
        }

        if (requested is not { } asked)
        {
            SetCap(child, label, cap);
            return;
        }

        if (asked > cap) dropped.Add($"{label}: asked for {asked}, the parent allows at most {cap}");
        SetCap(child, label, Math.Min(asked, cap));
    }

    private static void SetCap(WorkflowAgentScope child, string label, int value)
    {
        if (string.Equals(label, "maxReadToolCalls", StringComparison.Ordinal))
            child.WithMaxReadToolCalls(value);
        else
            child.WithMaxWriteToolCalls(value);
    }

    /// <summary>
    /// Whether the child may run node business code. Opt-in on both sides: the parent must already allow it,
    /// because a capability a parent does not have cannot be handed down.
    /// </summary>
    private static bool RequestableNodeExecution(SubAgentRequest request, WorkflowAgentScope parent, List<string> dropped)
    {
        if (request.AllowNodeExecution != true) return false;
        if (parent.AllowNodeExecution) return true;
        dropped.Add("allowNodeExecution: the parent does not allow node execution, so it cannot be granted");
        return false;
    }

    /// <summary>
    /// The generic commands the child may run: the requested names, minus everything the parent was not
    /// itself allowlisted for. Requested and refused are both reported, so the model can tell "you asked
    /// for a command that does not exist" from "the parent cannot hand that one down".
    /// </summary>
    private static IReadOnlyList<string> RequestableCommands(SubAgentRequest request, WorkflowAgentScope parent, List<string> dropped)
    {
        if (request.AllowedGenericCommands is not { Length: > 0 } wanted) return [];

        var granted = new List<string>();
        foreach (var name in wanted)
        {
            if (parent.IsGenericCommandAllowed(name)) granted.Add(name);
            else dropped.Add($"allowedGenericCommands/{name}: the parent is not allowlisted for it");
        }
        return granted;
    }

    /// <summary>
    /// Whether a child that changes the graph should mark it dirty. Inherited from the parent by default,
    /// because a child's edits are the parent's edits — but never granted beyond what the parent itself does.
    /// </summary>
    private static bool RequestableAutoMarkDirty(SubAgentRequest request, WorkflowAgentScope parent, List<string> dropped)
    {
        if (request.AutoMarkDirty != true) return parent.AutoMarkDirty;
        if (parent.AutoMarkDirty) return true;
        dropped.Add("autoMarkDirty: the parent does not mark the graph dirty, so it cannot be granted");
        return false;
    }

    private async Task RunAsync(SubAgentEntry entry, SubAgentRequest request)
    {
        await OnRosterThread(() =>
        {
            entry.Row.State = SubAgentState.Running;
            entry.Row.StartedAt = DateTimeOffset.Now;
        }).ConfigureAwait(false);

        try
        {
            var agent = _agentFactory(entry.Scope);
            var response = await agent.RunAsync(request.Task, cancellationToken: entry.Cancellation.Token).ConfigureAwait(false);
            await OnRosterThread(() => Finish(entry, response.Text, null, cancelled: false)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await OnRosterThread(() => Finish(entry, null, null, cancelled: true)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await OnRosterThread(() => Finish(entry, null, ex, cancelled: false)).ConfigureAwait(false);
        }
    }

    private void Finish(SubAgentEntry entry, string? result, Exception? error, bool cancelled)
    {
        entry.Row.FinishedAt = DateTimeOffset.Now;
        entry.Row.CallCount = entry.Scope.CreateToolkit().CallUsage.ToolCalls;

        if (cancelled)
        {
            entry.Row.State = SubAgentState.Cancelled;
            return;
        }

        if (error is not null)
        {
            entry.Row.State = SubAgentState.Failed;
            entry.Row.Error = error.Message;
            return;
        }

        entry.Row.State = SubAgentState.Completed;
        entry.Row.Result = result ?? string.Empty;
    }

    // ────────────────────────── the operations the tools map to ──────────────────────────

    private SubAgentEntry? Find(string id)
    {
        lock (_gate)
            return _entries.FirstOrDefault(e => string.Equals(e.Row.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The rows the roster should report: the named ones, or every one still running.
    /// <para>
    /// A named id that this scope never issued is skipped rather than refused — see
    /// <see cref="Describe"/>'s note on isolation. The handle table is per-scope, so a model that names a
    /// sibling's child gets nothing back rather than a peek at another branch of the tree.
    /// </para>
    /// </summary>
    private List<SubAgentEntry> Select(string[]? ids)
    {
        lock (_gate)
        {
            if (ids is not { Length: > 0 })
                return [.. _entries.Where(e => e.Row.IsRunning)];

            var wanted = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
            return [.. _entries.Where(e => wanted.Contains(e.Row.Id))];
        }
    }

    /// <summary>Read-only survey of what this scope spawned, for the <c>ListSubAgents</c> tool.</summary>
    internal IReadOnlyList<SubAgentSummary> List()
    {
        RefreshCallCounts();
        Republish();
        return _snapshot;
    }

    /// <summary>One child's current state, or <c>null</c> when this scope never issued that handle.</summary>
    internal SubAgentSummary? GetResult(string id)
    {
        var entry = Find(id);
        if (entry is null) return null;
        entry.Row.CallCount = entry.Scope.CreateToolkit().CallUsage.ToolCalls;
        Republish();
        return _snapshot.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Waits for the named children — or every running one — to finish, or for <paramref name="timeoutMs"/>
    /// to elapse, whichever happens first. Returns whether it timed out along with the rows as they stand.
    /// </summary>
    internal async Task<(IReadOnlyList<SubAgentSummary> Rows, bool TimedOut)> WaitAsync(string[]? ids, int timeoutMs)
    {
        var targets = Select(ids);
        if (targets.Count > 0)
        {
            var all = Task.WhenAll(targets.Select(t => t.Run ?? Task.CompletedTask));
            var finished = await Task.WhenAny(all, Task.Delay(Math.Max(0, timeoutMs))).ConfigureAwait(false);

            // The delay task is the one that lost only when something else completed first; comparing by
            // reference is the only way to tell them apart, since both complete successfully.
            var timedOut = !ReferenceEquals(finished, all);
            return (RowsFor(targets, ids), timedOut);
        }

        return (RowsFor(targets, ids), false);
    }

    private IReadOnlyList<SubAgentSummary> RowsFor(List<SubAgentEntry> targets, string[]? ids)
    {
        RefreshCallCounts();
        Republish();
        var wanted = new HashSet<string>(targets.Select(t => t.Row.Id), StringComparer.OrdinalIgnoreCase);
        return [.. _snapshot.Where(s => wanted.Contains(s.Id))];
    }

    /// <summary>
    /// Cancels a running child. Returns its row, or <c>null</c> when this scope never issued that handle.
    /// </summary>
    internal SubAgentSummary? Cancel(string id)
    {
        var entry = Find(id);
        if (entry is null) return null;

        if (entry.Row.IsRunning)
        {
            // The run observes the token and settles the row itself, so the state reached here is the one
            // the child's own catch sets — a single writer, and a caller that waits afterwards sees it.
            entry.Cancellation.Cancel();
        }
        return GetResult(id);
    }

    /// <summary>
    /// The subsystem of the child a row stands for, or <c>null</c> if this scope never issued that handle.
    /// This is the edge the tree panel walks: rows in, child scopes out, and recurse.
    /// </summary>
    internal SubAgentScope? SubAgentsOf(string id) => Find(id)?.SubAgents;

    /// <summary>
    /// Cancels every child still running and waits for them to settle.
    /// <para>
    /// Awaited rather than fired off: a child's tool calls are marshalled onto the host's UI thread, so a
    /// host that tore its dispatcher down while children were still in flight would have them post into a
    /// pump that no longer exists. The wait is what makes disposal a boundary rather than a race.
    /// </para>
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        SubAgentEntry[] running;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            running = [.. _entries.Where(e => e.Row.IsRunning)];
        }

        foreach (var entry in running)
        {
            try { entry.Cancellation.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        foreach (var entry in running)
        {
            if (entry.Run is { } run)
            {
                try { await run.ConfigureAwait(false); }
                catch { /* the run settles its own row; disposal does not need its outcome */ }
            }
        }

        lock (_gate)
        {
            foreach (var entry in _entries)
            {
                entry.Row.PropertyChanged -= OnRowChanged;
                entry.Cancellation.Dispose();
            }
        }

        Children.Clear();
    }
}

/// <summary>
/// One spawned child: its row, its scope, the subsystem it may spawn with, and the token that stops it.
/// </summary>
internal sealed class SubAgentEntry(
    SubAgentStatusViewModel row,
    WorkflowAgentScope scope,
    SubAgentScope subAgents,
    CancellationTokenSource cancellation)
{
    public SubAgentStatusViewModel Row { get; } = row;
    public WorkflowAgentScope Scope { get; } = scope;
    public SubAgentScope SubAgents { get; } = subAgents;
    public CancellationTokenSource Cancellation { get; } = cancellation;
    public Task? Run { get; set; }
}
