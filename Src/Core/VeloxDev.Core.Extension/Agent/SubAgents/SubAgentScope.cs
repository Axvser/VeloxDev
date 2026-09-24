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
using VeloxDev.AI.MCP;
using VeloxDev.AI.Pipelines;
using VeloxDev.AI.Skills;
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
    /// The exact tools the child may use, or <c>null</c> to inherit every tool the parent currently offers.
    /// An empty array is a request for no tools at all, which is a different thing from omitting it.
    /// </summary>
    public string[]? AllowedTools { get; set; }

    /// <summary>
    /// The skills the child may read, or <c>null</c> to inherit every skill the parent has switched on.
    /// An empty array grants none — and its skill tools go with them, since a <c>load_skill</c> with no
    /// skills behind it is a tool that can only fail.
    /// </summary>
    public string[]? AllowedSkills { get; set; }

    /// <summary>
    /// The MCP servers the child may use, or <c>null</c> to inherit every server the parent has connected
    /// and switched on — the same default <see cref="AllowedSkills"/> has. An empty array grants none, which
    /// is a different thing from omitting it.
    /// </summary>
    public string[]? AllowedMcpServers { get; set; }

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

    /// <summary>The skills this child was given, as the names it can pass to <c>load_skill</c>.</summary>
    public IReadOnlyList<string> GrantedSkills { get; set; } = [];

    /// <summary>The MCP servers this child was given, as the names it reaches their tools through.</summary>
    public IReadOnlyList<string> GrantedMcpServers { get; set; } = [];

    /// <summary>
    /// Whether the spawn that produced this child asked for less than its parent had.
    /// <para>
    /// It gates whether the child is read the roster of what it was given. Naming that roster is worth its
    /// length exactly when the set is smaller than the parent's — a child handed two of nine skills cannot
    /// find out which two any other way — and is pure repetition when it is not, because the child's own
    /// skill and MCP providers already describe what they contribute.
    /// </para>
    /// </summary>
    public bool Narrowed { get; set; }
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
        + "what you found or did. You hold what the agent that dispatched you handed down — by default "
        + "everything it had — and the tools you actually hold are the whole of what you may use; do not "
        + "attempt to obtain others.";

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
            TokensUsed = e.Row.TokensUsed,
            InputTokens = e.Row.InputTokens,
            OutputTokens = e.Row.OutputTokens,
            StartedAt = e.Row.StartedAt,
            FinishedAt = e.Row.FinishedAt,
            GrantedToolCount = e.Row.GrantedTools.Count,
            GrantedSkillCount = e.Row.GrantedSkills.Count,
            GrantedMcpServerCount = e.Row.GrantedMcpServers.Count,
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

        // ── the tool surface: the parent's own, handed down as it stands ──
        // What may be asked for is what the parent currently offers, and it is also what the child gets when
        // the request says nothing: omitting a field means *inherit*, and inheriting means the whole of it.
        // Naming tools is the one way to take anything away. The only subtraction is the parent's own
        // switches — a tool the parent itself had turned off is not a capability the parent has, so it is not
        // one it can hand down.
        var parentToolkit = parent.CreateToolkit();
        var everyName = parentToolkit.CreateAllTools().Select(t => t.Name).ToList();

        // The skill tools are part of that surface even though they are absent from the toolkit: they come
        // from the skill subsystem's own provider. Leaving them out would drop `load_skill` from every grant
        // as "not available to this agent", and would leave the skill tools switched on for a child that was
        // granted no skills — a tool the model can call and that can only fail.
        if (parent.Skills is not null) everyName.AddRange(SkillAgentToolkit.ToolNames);

        // The five management tools, and unlike the skill line above this one is not behind a check on the
        // parent: `child.WithSubAgents(grand)` below is unconditional, so every child reaches these five
        // whether or not its parent had a subsystem attached. A name that is reachable has to be in this
        // list, or the switch-off loop further down cannot take it away — which is exactly what was wrong.
        // Absent from this list the axis ran inverted: a spawn that named its tools switched all five off,
        // while a spawn that named none left them all on. So the model could delegate only when it had not
        // stopped to think about what it was granting, and never by asking — and asking was answered with
        // "not available to this agent", which is also false.
        everyName.AddRange(SubAgentAgentToolkit.ToolNames);

        // And the third source, MCP, which was missing from this list entirely. Its tools are reachable by a
        // child through its own MCP provider, so a name absent from here could not be granted by name either
        // — asking for one was answered with "not available to this agent, or switched off by the host",
        // which is precisely what it was not. `LoadedTools` is already the set this wants: connected,
        // switched on, and with the individually disabled tools taken out.
        var mcpNames = new HashSet<string>(
            parent.Mcp is { } mcpSource ? mcpSource.LoadedTools.Select(t => t.Name) : Enumerable.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
        everyName.AddRange(mcpNames);
        everyName = [.. everyName.Distinct(StringComparer.OrdinalIgnoreCase)];

        var available = everyName.Where(parent.IsToolEnabled).ToList();

        List<string> grantedTools;
        if (request.AllowedTools is { } wanted)
        {
            grantedTools = [.. wanted
                .Where(name => available.Contains(name, StringComparer.OrdinalIgnoreCase))
                .Select(name => Canonical(name, available))];
            foreach (var name in wanted.Where(name => !available.Contains(name, StringComparer.OrdinalIgnoreCase)))
                dropped.Add($"{name}: not available to this agent, or switched off by the host");
        }
        else
        {
            grantedTools = [.. available];
        }

        // ── the knowledge surface: the same default as the tools ──
        var grantedSkills = ResolveSkills(parent, request, dropped);
        var grantedServers = ResolveMcpServers(parent, request, dropped);

        // Nothing to load is nothing to hold the loader for. Without this the inherited surface would still
        // list `load_skill` — it is part of the parent's surface, so it comes back through the branch above
        // — and the child would come away told it holds a tool that does not exist on its scope.
        if (grantedSkills.Count == 0 && parent.Skills is not null)
        {
            foreach (var name in SkillAgentToolkit.ToolNames)
                if (grantedTools.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) > 0)
                    dropped.Add($"{name}: no skills were granted to it, so there is nothing for this tool to operate on");
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
        // parent's whole surface, not over the part its own switches happen to allow. That whole surface
        // includes the names the child gets from its own providers, which is what lets this one loop be the
        // only place a workflow tool is taken away. MCP names are skipped here, and only they: an MCP tool's
        // switch is keyed `server/tool` on its own scope rather than by tool name on this one, so a name
        // passed to `WithToolEnabled` here would look like a removal and take nothing away. That source is
        // narrowed where its key lives, in the granted view built below.
        foreach (var name in everyName)
            if (!mcpNames.Contains(name) && !grantedTools.Contains(name, StringComparer.OrdinalIgnoreCase))
                child.WithToolEnabled(name, false);

        // ── the host's interaction configuration, handed down with the two tools that need it ──
        // RequestSelection and RequestConfirmation are offered only when the level is above zero AND a
        // handler is registered on that very scope (WorkflowAgentToolkit.CreateAllTools). A child scope is a
        // fresh one over the same tree, so leaving this out would put two names in the grant list that the
        // child does not have and can never call — a permission the model can see and never use, which is
        // worse than no permission. The level travels with them because level zero is what makes the reset
        // tool refuse to ask: a child holding the reset tool under level zero holds a tool that only fails.
        // A host that registered no handlers hands down nothing, and then neither tool reaches the child
        // either — which is the point.
        parent.GrantInteractionTo(child);

        // ── the two sources that carry their own providers ──
        // A tool name cannot express these: skills and MCP servers are contributed by their own context
        // providers, out of their own data layers. So the child is given a *view* of each — one that owns
        // nothing its parent owns, so disposing it cannot tear down a connection or a skill folder the
        // parent is still using. The view is not a narrowing: it carries the parent's enabled set unless the
        // request named a smaller one, and for MCP it takes the named tools away at the key that source
        // actually switches on.
        if (grantedSkills.Count > 0 && parent.Skills is { } parentSkills)
        {
            // Before WithSkills, which re-applies the scope's default language to whatever it attaches.
            // A skill's text is read back in the language it was rendered in, so setting this afterwards
            // would leave a Chinese session's child reading English documents.
            child.WithPromptLanguage(parent.PromptLanguage);
            child.WithSkills(parentSkills.CreateNarrowed(grantedSkills));
        }
        if (grantedServers.Count > 0 && parent.Mcp is { } parentMcp)
            child.WithMcps(McpScope.CreateGrantedView(parentMcp, grantedServers, grantedTools));

        // Custom tools, in the groups they were registered in — so the child gets the guidance belonging to
        // the tools it actually holds, and not the guidance for the ones it does not.
        parent.GrantCustomToolsTo(child, grantedTools);

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
                GrantedSkills = grantedSkills,
                GrantedMcpServers = grantedServers,
                // The request said something about what the child may have, which is what makes the roster
                // worth reading to it. A silent spawn inherits the parent's set, and the child's own skill and
                // MCP providers already describe that — repeating it every turn is the whole of what this
                // flag exists to avoid.
                Narrowed = request.AllowedTools is not null
                    || request.AllowedSkills is not null
                    || request.AllowedMcpServers is not null,
            },
        };
        grand.InheritFrom(this);
        child.WithSubAgents(grand);

        var row = new SubAgentStatusViewModel(id, Describe(request, Children.Count + 1), SelfId, childDepth, request.Task)
        {
            MaxToolCalls = granted,
            Transcript = transcript,
        };
        row.SetGrantedTools(grantedTools.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
        row.SetGrantedSkills(grantedSkills.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
        row.SetGrantedMcpServers(grantedServers.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
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

    /// <summary>
    /// The row's display title: the one the spawn asked for, or a numbered stand-in.
    /// <para>
    /// Per parent rather than the id's first eight characters. This string is what the host's panel prints
    /// beside the lamp, and an identifier there tells the person watching nothing that the position in the
    /// tree had not already said, while taking the width a title would have used.
    /// </para>
    /// </summary>
    private static string Describe(SubAgentRequest request, int ordinal)
        => string.IsNullOrWhiteSpace(request.Name) ? $"子代理 {ordinal}" : request.Name!;

    /// <summary>
    /// The skills a spawn asks for: what it named, or every skill the parent has switched on.
    /// <para>
    /// The same default <see cref="ResolveMcpServers"/> has, and for the same reason: a skill the parent has
    /// switched on is already in its own prompt, so handing it down hands down nothing the parent was not
    /// already told. What has to be asked for is the opposite — a *smaller* set, because a child given nine
    /// skills it does not need carries nine documents' worth of prompt it will never read.
    /// </para>
    /// </summary>
    private static List<string> ResolveSkills(
        WorkflowAgentScope parent, SubAgentRequest request, List<string> dropped)
    {
        var grantable = parent.Skills?.GrantableNames ?? [];
        if (request.AllowedSkills is not { } wanted) return [.. grantable];

        var granted = new List<string>();
        foreach (var name in wanted)
        {
            if (grantable.Contains(name, StringComparer.OrdinalIgnoreCase)) granted.Add(Canonical(name, grantable));
            else dropped.Add(parent.Skills is null
                ? $"{name}: this agent has no skills attached"
                : $"{name}: not a skill this agent has switched on");
        }
        return granted;
    }

    /// <summary>
    /// The MCP servers a spawn asks for: what it named, or every server the parent has connected and switched
    /// on.
    /// <para>
    /// Inheriting rather than refusing is what makes a dispatched agent usable. A child is dispatched to do
    /// work, and the servers its parent can reach are part of what it needs to do that work; making every
    /// spawn restate the whole server list is how a spawn comes back with a tree of agents that cannot open
    /// the tools they were dispatched for. What the child must not reach is the parent's servers that are
    /// switched off — that is the intersection <see cref="McpScope.GrantableNames"/> already draws.
    /// </para>
    /// </summary>
    private static List<string> ResolveMcpServers(
        WorkflowAgentScope parent, SubAgentRequest request, List<string> dropped)
    {
        var grantable = parent.Mcp?.GrantableNames ?? [];
        if (request.AllowedMcpServers is not { } wanted) return [.. grantable];

        var granted = new List<string>();
        foreach (var name in wanted)
        {
            if (grantable.Contains(name, StringComparer.OrdinalIgnoreCase)) granted.Add(Canonical(name, grantable));
            else dropped.Add(parent.Mcp is null
                ? $"{name}: this agent has no MCP servers attached"
                : $"{name}: not an MCP server this agent has connected and switched on");
        }
        return granted;
    }

    /// <summary>
    /// The canonical spelling of <paramref name="name"/> among <paramref name="candidates"/>, or the name
    /// as given when it is not there.
    /// <para>
    /// Every match in this file is case-insensitive, but the grant list is read by people and by the model
    /// that asked. Echoing back the model's spelling of <c>Load_Skill</c> would put a second spelling of a
    /// name this library owns into the row, the briefing and the spawn's own result.
    /// </para>
    /// </summary>
    private static string Canonical(string name, IEnumerable<string> candidates)
        => candidates.FirstOrDefault(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase)) ?? name;

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
            // StartedAt before State, and never the other way round: State is a bindable property, so
            // assigning it publishes the row — and a consumer reading on that notification would otherwise
            // see a child that is running with no start time.
            entry.Row.StartedAt = DateTimeOffset.Now;
            entry.Row.State = SubAgentState.Running;
        }).ConfigureAwait(false);

        try
        {
            var agent = _agentFactory(entry.Scope);
            var response = await agent.RunAsync(request.Task, cancellationToken: entry.Cancellation.Token).ConfigureAwait(false);
            await OnRosterThread(() => Finish(entry, response.Text, response.Usage, null, cancelled: false)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await OnRosterThread(() => Finish(entry, null, null, null, cancelled: true)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await OnRosterThread(() => Finish(entry, null, null, ex, cancelled: false)).ConfigureAwait(false);
        }
    }

    // 结束一个孩子的运行。
    // 每个分支都把 State 放在最后、在它宣告的载荷之后：State 是可绑定属性，赋值即重发名册并通知面板，
    // 于是恰好在这一刻读取的消费者（面板就是这么做的）否则会拿到「已完成但没有结论」的行。State 是信号，
    // 比它宣告的东西先到的信号，是对它所属那一行的谎言。
    //
    // token 只在成功分支写：只有那条路径拿得到 response。被取消或抛异常的孩子的消耗已经无从取得，
    // 留 null 让面板显示「未计量」，而不是一个它没测过的 0。
    private void Finish(SubAgentEntry entry, string? result, UsageDetails? usage, Exception? error, bool cancelled)
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
            entry.Row.Error = error.Message;
            entry.Row.State = SubAgentState.Failed;
            return;
        }

        entry.Row.TokensUsed = usage?.TotalTokenCount;
        entry.Row.InputTokens = usage?.InputTokenCount;
        entry.Row.OutputTokens = usage?.OutputTokenCount;

        entry.Row.Result = result ?? string.Empty;
        entry.Row.State = SubAgentState.Completed;
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
