using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using VeloxDev.AI.Pipelines;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.AI;
using VeloxDev.AI.MCP;
using VeloxDev.AI.Skills;
using VeloxDev.AI.Workflow.Functions;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.AI.Workflow;

public class WorkflowAgentScope(IWorkflowTreeViewModel tree) : IAgentToolCallNotifier
{
    public IWorkflowTreeViewModel Tree { get; } = tree;

    public int? MaxToolCalls { get; private set; }

    public bool AutoMarkDirty { get; private set; }

    public event EventHandler<AgentToolCallEventArgs>? ToolCalled;

    private const string SystemName = "Workflow";

    internal static readonly Type[] FrameworkEnums =
        [typeof(SlotChannel), typeof(SlotState), typeof(RouterCompileMode)];

    internal static bool IsFrameworkEnum(Type t) => FrameworkEnums.Contains(t);

    private static readonly Type[] FrameworkInterfaces =
        [typeof(IWorkflowTreeViewModel), typeof(IWorkflowNodeViewModel), typeof(IWorkflowSlotViewModel), typeof(IWorkflowLinkViewModel), typeof(IWorkflowViewModel)];

    private static readonly Type[] FrameworkComponents =
        [typeof(TreeDefaultViewModel), typeof(NodeDefaultViewModel), typeof(SlotDefaultViewModel), typeof(LinkDefaultViewModel)];

    internal static readonly Type[] FrameworkData =
        [typeof(Anchor), typeof(Offset), typeof(Size),
         typeof(IAccessContext), typeof(ITaskContext), typeof(TaskContext),
         // Compiler contexts: rendered as data so the Agent understands the dataflow access edge gate,
         // compile identity (Order/ChainIndex/Offset) and the runtime session contract during compiled runs.
         typeof(ICompileContext), typeof(IRuntimeContext)];

    private readonly Dictionary<AgentLanguages, HashSet<Type>> CustomerEnums = [];
    private readonly Dictionary<AgentLanguages, HashSet<Type>> CustomerInterfaces = [];
    private readonly Dictionary<AgentLanguages, HashSet<Type>> CustomerComponents = [];

    private readonly Dictionary<AgentLanguages, HashSet<Type>> CustomerData = [];

    /// <summary>
    /// Tracks types already discovered by <see cref="WithAutoDiscovery"/> across all languages.
    /// Prevents the same type from being registered in multiple language slots.
    /// </summary>
    private readonly HashSet<Type> _globallyDiscoveredTypes = [];

    private readonly List<AITool> _customTools = [];
    private readonly List<AITool> _queryOnlyCustomTools = [];
    private readonly HashSet<string> _queryOnlyCustomToolNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly StringBuilder _customToolPrompt = new();

    private AgentLanguages _defaultLanguage = AgentLanguages.English;
    private AgentLanguages? _outputLanguage;

    /// <summary>
    /// Mutation-capable tools registered by the developer via <see cref="WithTools"/>.
    /// </summary>
    internal IReadOnlyList<AITool> CustomTools => _customTools;

    /// <summary>
    /// Read-only tools registered by the developer via <see cref="WithQueryTools"/>.
    /// They are never auto-marked dirty, even when <see cref="WithAutoMarkDirty"/> is enabled.
    /// </summary>
    internal IReadOnlyList<AITool> QueryOnlyCustomTools => _queryOnlyCustomTools;

    /// <summary>
    /// Whether a tool name was registered as read-only via <see cref="WithQueryTools"/>.
    /// </summary>
    internal bool IsQueryOnlyCustomTool(string toolName)
        => _queryOnlyCustomToolNames.Contains(toolName);

    /// <summary>
    /// Distinct assemblies of all types registered via <see cref="WithComponents"/>, <see cref="WithEnums"/>,
    /// <see cref="WithInterfaces"/>, <see cref="WithData"/>, or <see cref="WithAutoDiscovery"/>. Used by
    /// ListCreatableTypes to surface creatable types from registered libraries even before any node
    /// instance exists in the tree.
    /// </summary>
    internal IEnumerable<Assembly> CustomerAssemblies
    {
        get
        {
            var seen = new HashSet<Assembly>();
            foreach (var set in CustomerComponents.Values) foreach (var t in set) seen.Add(t.Assembly);
            foreach (var set in CustomerEnums.Values) foreach (var t in set) seen.Add(t.Assembly);
            foreach (var set in CustomerInterfaces.Values) foreach (var t in set) seen.Add(t.Assembly);
            foreach (var set in CustomerData.Values) foreach (var t in set) seen.Add(t.Assembly);
            return seen;
        }
    }

    /// <summary>
    /// Sets the global default language used when a per-call <c>language</c> argument is <c>null</c>.
    /// Call this once at the start of the fluent chain before any <c>With*</c> registration.
    /// </summary>
    public WorkflowAgentScope WithPromptLanguage(AgentLanguages language)
    {
        _defaultLanguage = language;
        // Propagate to anything already attached: the skill subsystem renders in the prompt language, and
        // the order of WithSkills and this call is the host's business.
        Skills?.WithPromptLanguage(language);
        BumpVersion();
        return this;
    }

    private AgentLanguages Resolve(AgentLanguages? language) => language ?? _defaultLanguage;

    /// <summary>
    /// Sets the language the LLM should use when generating its responses.
    /// This is independent of the prompt/documentation language set by <see cref="WithPromptLanguage"/>.
    /// When not configured the LLM will respond in whatever language it deems appropriate.
    /// </summary>
    public WorkflowAgentScope WithOutputLanguage(AgentLanguages language)
    {
        _outputLanguage = language;
        BumpVersion();
        return this;
    }

    public WorkflowAgentScope WithMaxToolCalls(int maxCalls)
    {
        MaxToolCalls = maxCalls;
        BumpVersion();
        return this;
    }

    internal int? MaxReadToolCalls { get; private set; }
    internal int? MaxWriteToolCalls { get; private set; }

    /// <summary>
    /// Sets a separate cap on read-only (query) tool calls. Independent of <see cref="WithMaxToolCalls"/>
    /// and <see cref="WithMaxWriteToolCalls"/>. Use to stop token-heavy queries (ListNodes, GetFullTopology, …)
    /// from consuming the mutation budget.
    /// </summary>
    public WorkflowAgentScope WithMaxReadToolCalls(int maxCalls)
    {
        MaxReadToolCalls = maxCalls;
        BumpVersion();
        return this;
    }

    /// <summary>
    /// Sets a separate cap on mutation (non-query) tool calls. Independent of <see cref="WithMaxToolCalls"/>
    /// and <see cref="WithMaxReadToolCalls"/>. Use to bound how many graph edits an Agent may make in a turn.
    /// </summary>
    public WorkflowAgentScope WithMaxWriteToolCalls(int maxCalls)
    {
        MaxWriteToolCalls = maxCalls;
        BumpVersion();
        return this;
    }

    /// <summary>
    /// Registers mutation-capable custom <see cref="AITool"/> instances merged into the tool list
    /// returned by <see cref="ProvideTools"/>. Each tool is wrapped with the same tracked wrapper as the
    /// built-in tools, so it receives UI-thread marshalling (<see cref="WithSynchronizationContext"/>),
    /// <see cref="WithMaxToolCalls"/> accounting, the <see cref="WithToolCallCallback"/> notification, and
    /// (when <see cref="WithAutoMarkDirty"/> is enabled) automatic dirty marking. A tool that is not an
    /// <c>AIFunction</c> is added as-is and gets none of that — wrap it with the SDK's
    /// <c>ToAIFunction()</c>/<c>AsAIFunction()</c> to opt in. MCP client tools are <c>AIFunction</c>s and
    /// are therefore wrapped by default; attaching an <see cref="McpScope"/> via
    /// <see cref="WithMcps"/> is what brings them in without registering each one by hand.
    /// Use <paramref name="promptContext"/> to inject instructions into the system prompt describing
    /// when and how to use these tools; pass <c>null</c> if the tool metadata is self-explanatory.
    /// </summary>
    /// <param name="promptContext">Optional prompt text describing the custom tools.</param>
    /// <param name="tools">One or more <see cref="AITool"/> instances.</param>
    public WorkflowAgentScope WithTools(string? promptContext, params AITool[] tools)
    {
        _customTools.AddRange(tools);
        AppendCustomToolPrompt(promptContext);
        BumpVersion();
        return this;
    }

    /// <summary>
    /// Registers read-only custom <see cref="AITool"/> instances. They behave exactly like
    /// <see cref="WithTools"/> tools, except they never trigger automatic dirty marking (mirroring the
    /// built-in <c>QueryToolNames</c> set). Use for custom query / introspection tools.
    /// </summary>
    /// <param name="promptContext">Optional prompt text describing the custom tools.</param>
    /// <param name="tools">One or more <see cref="AITool"/> instances.</param>
    public WorkflowAgentScope WithQueryTools(string? promptContext, params AITool[] tools)
    {
        foreach (var tool in tools ?? [])
        {
            _queryOnlyCustomTools.Add(tool);
            if (!string.IsNullOrEmpty(tool.Name))
                _queryOnlyCustomToolNames.Add(tool.Name);
        }
        AppendCustomToolPrompt(promptContext);
        BumpVersion();
        return this;
    }

    private void AppendCustomToolPrompt(string? promptContext)
    {
        if (!string.IsNullOrWhiteSpace(promptContext))
            _customToolPrompt.AppendLine(promptContext);
    }

    /// <summary>
    /// Configures whether every mutation tool call automatically marks the workflow tree as dirty.
    /// When enabled (<c>true</c>), the framework marks dirty after every mutation tool call and the
    /// Agent can rely on framework-managed dirty marking — it does not need to call <c>MarkDirty</c>.
    /// When disabled (<c>false</c>, the default), the framework does not auto-mark; the injected prompt
    /// (CommandReference.md) instead instructs the Agent to call <c>MarkDirty</c> exactly once at the
    /// end of a mutation task. Pure query tools never trigger auto dirty marking.
    /// </summary>
    public WorkflowAgentScope WithAutoMarkDirty(bool enabled = false)
    {
        AutoMarkDirty = enabled;
        // The embedded CommandReference.md documents both modes side by side and cannot say which one is
        // live, so the capability envelope is what resolves it — and that needs the version to move.
        BumpVersion();
        return this;
    }

    // ── Capability gates (enforced in code, not just prompt) ───────────────

    /// <summary>
    /// Whether the node-execution tools (<c>ExecuteNode</c>, <c>ExecuteNodes</c>,
    /// <c>BroadcastNode</c>, <c>ReverseBroadcastNode</c>) are allowed.
    /// These tools run arbitrary node business code, so they are opt-in.
    /// Default: <c>false</c> (denied). Security is enforced here, not in prompt prose.
    /// </summary>
    public WorkflowAgentScope WithAllowNodeExecution(bool enabled = false)
    {
        AllowNodeExecution = enabled;
        BumpVersion();
        return this;
    }

    internal bool AllowNodeExecution { get; private set; }

    private readonly HashSet<string> _allowedGenericCommands = new(StringComparer.OrdinalIgnoreCase);

    // Locked for the same reason _disabledTools is: allowlisted from the host's UI thread, consulted from
    // the agent's invocation thread. The set is small and the lock is uncontended, so the cost is nil
    // next to the tool call it guards.
    private readonly object _genericCommandLock = new();

    /// <summary>
    /// Allowlists command names (e.g. <c>"ReceiveCommand"</c>) for the generic tools
    /// <c>ExecuteCommandOnNode</c> and <c>ExecuteCommandById</c>. The <c>"Command"</c> suffix is optional.
    /// When this is never called, generic command execution is disabled entirely (secure default).
    /// Calling it once restricts those tools to exactly the listed commands.
    /// </summary>
    public WorkflowAgentScope WithAllowedGenericCommands(params string[] commandNames)
    {
        lock (_genericCommandLock)
        {
            foreach (var c in commandNames ?? [])
            {
                var name = c.EndsWith("Command", StringComparison.OrdinalIgnoreCase) ? c : c + "Command";
                _allowedGenericCommands.Add(name);
            }
        }
        BumpVersion();
        return this;
    }

    internal bool IsGenericCommandAllowed(string commandName)
    {
        var name = commandName.EndsWith("Command", StringComparison.OrdinalIgnoreCase)
            ? commandName
            : commandName + "Command";
        lock (_genericCommandLock)
            return _allowedGenericCommands.Contains(name);
    }

    /// <summary>
    /// Whether any command was allowlisted at all. The generic command tools are registered either way, so
    /// a host UI needs this to tell "this tool is live" from "this tool will refuse whatever the model asks
    /// it" — which the tool's own description cannot say per call.
    /// </summary>
    internal bool HasAllowedGenericCommands
    {
        get { lock (_genericCommandLock) return _allowedGenericCommands.Count > 0; }
    }

    /// <summary>A snapshot of the allowlisted generic command names, in no particular order.</summary>
    internal IReadOnlyList<string> AllowedGenericCommands
    {
        get { lock (_genericCommandLock) return [.. _allowedGenericCommands]; }
    }

    // ── Per-tool switches ───────────────────────────────────────────────────

    /// <summary>
    /// Names switched off via <see cref="WithToolEnabled"/>/<see cref="SetToolEnabled"/>. Everything is on
    /// by default, so an empty set is the unchanged behaviour.
    /// </summary>
    private readonly HashSet<string> _disabledTools = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _toolSwitchLock = new();

    /// <summary>
    /// Switches one tool on or off by name — built-in or developer-registered. A disabled tool is not
    /// offered to the model at all from the next invocation on: the tool set is rendered per turn, so the
    /// change needs no agent rebuild.
    /// <para>
    /// This is a narrower instrument than <see cref="WithAllowNodeExecution"/> or
    /// <see cref="WithAllowedGenericCommands"/>, which gate a capability in code. Use it to trim the tool
    /// surface for token cost or tool-selection accuracy, or to take one tool out of play while a
    /// subsystem stays wired up.
    /// </para>
    /// </summary>
    /// <param name="toolName">Tool name as it reaches the model, e.g. <c>ListNodes</c>.</param>
    /// <param name="enabled">Whether the tool should be offered.</param>
    public WorkflowAgentScope WithToolEnabled(string toolName, bool enabled = true)
    {
        // Bump only on a real move, so Changed keeps meaning "something changed" — a dashboard following
        // this scope rebuilds on that event, and a no-op registration would be pure noise.
        if (ApplyToolEnabled(toolName, enabled)) BumpVersion();
        return this;
    }

    /// <summary>
    /// Switches one tool on or off after the scope was configured (the runtime counterpart of
    /// <see cref="WithToolEnabled"/>), and advances <see cref="Version"/> so a context provider re-renders
    /// its cached tool set. Returns whether the switch actually moved.
    /// </summary>
    /// <param name="toolName">Tool name as it reaches the model, e.g. <c>ListNodes</c>.</param>
    /// <param name="enabled">Whether the tool should be offered.</param>
    public bool SetToolEnabled(string toolName, bool enabled)
    {
        var changed = ApplyToolEnabled(toolName, enabled);
        if (changed) BumpVersion();
        return changed;
    }

    private bool ApplyToolEnabled(string toolName, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(toolName)) return false;
        // Locked: switched from the host's UI thread, read from the agent's invocation thread via
        // BuildDynamicTools. Same treatment the sibling scopes give their own switch sets.
        lock (_toolSwitchLock)
            return enabled ? _disabledTools.Remove(toolName) : _disabledTools.Add(toolName);
    }

    /// <summary>Whether the named tool is currently offered to the model.</summary>
    public bool IsToolEnabled(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName)) return true;
        lock (_toolSwitchLock)
            return !_disabledTools.Contains(toolName);
    }

    /// <summary>A snapshot of the switched-off tool names.</summary>
    public IReadOnlyList<string> DisabledToolNames
    {
        get { lock (_toolSwitchLock) return [.. _disabledTools]; }
    }

    /// <summary>
    /// Raised whenever something the Agent is shown changes — a <c>With*</c> call, a
    /// <see cref="SetToolEnabled"/> switch, an attached subsystem. Lets a bindable panel follow the scope
    /// instead of only its own edits.
    /// <para>
    /// <b>Raised on whichever thread made the change, with no marshalling.</b> A scope is normally
    /// configured on the UI thread, but nothing enforces that — a subscriber that touches bound state must
    /// post to its own <see cref="SynchronizationContext"/>.
    /// </para>
    /// </summary>
    public event EventHandler? Changed;

    // ── UI-thread marshalling ───────────────────────────────────────────────

    internal SynchronizationContext? UIContext { get; private set; }

    /// <summary>
    /// Registers the UI <see cref="SynchronizationContext"/> (e.g. the WPF/Avalonia dispatcher
    /// context) so every tool call is marshalled onto the UI thread. Workflow components are
    /// UI-bound (<c>ObservableCollection</c> + <c>INotifyPropertyChanged</c>), so mutations MUST
    /// happen on the thread that owns the binding. Call this during setup while on the UI thread:
    /// <c>.WithSynchronizationContext(SynchronizationContext.Current)</c>. When <c>null</c> (default),
    /// tools run on whatever thread the chat client invokes them on.
    /// </summary>
    public WorkflowAgentScope WithSynchronizationContext(SynchronizationContext? context)
    {
        UIContext = context;
        // Propagate to anything already attached: the order of WithSkills / WithMcps and this call is
        // the host's business, and a skill list left unbound would throw when a UI binds it.
        Skills?.WithSynchronizationContext(context);
        Mcp?.WithSynchronizationContext(context);
        return this;
    }

    private Func<AgentToolCallEventArgs, Task>? _toolCallHandler;

    /// <summary>
    /// Registers an asynchronous handler invoked after every Agent tool call.
    /// The handler receives an <see cref="AgentToolCallEventArgs"/> with the tool name,
    /// result, and cumulative call count. Replaces any previously registered handler.
    /// </summary>
    public WorkflowAgentScope WithToolCallCallback(Func<AgentToolCallEventArgs, Task> handler)
    {
        _toolCallHandler = handler;
        return this;
    }

    // ── Interactive handlers ────────────────────────────────────────────────

    /// <summary>
    /// Carries the result of a user interaction involving selection and/or free text.
    /// Returned by the host UI layer and consumed by <c>WorkflowAgentToolkit.RequestSelection</c>.
    /// </summary>
    public sealed class SelectionResult
    {
        /// <summary>For single-select: the chosen option. <c>null</c> if cancelled.</summary>
        public string? SelectedOption { get; set; }

        /// <summary>For multi-select: the chosen options. Empty if none selected.</summary>
        public IReadOnlyList<string> SelectedOptions { get; set; } = [];

        /// <summary>Free-text response typed by the user. <c>null</c> or empty if not provided.</summary>
        public string? FreeTextResponse { get; set; }

        /// <summary>
        /// Creates a single-select result.
        /// </summary>
        public static SelectionResult Single(string? option) => new() { SelectedOption = option };

        /// <summary>
        /// Creates a multi-select result.
        /// </summary>
        public static SelectionResult Multi(IReadOnlyList<string> options, string? freeText = null)
            => new() { SelectedOptions = options ?? [], FreeTextResponse = freeText };

        /// <summary>
        /// Creates a free-text-only result (no predefined options selected).
        /// </summary>
        public static SelectionResult FreeText(string text) => new() { FreeTextResponse = text };
    }

    // Low-level Func delegates consumed by WorkflowAgentToolkit.
    // null means the tool is not available (tool won't be registered).
    // Args: prompt, options, freeTextPrompt, allowMultiSelect → result
    internal Func<string, string[], string, bool, Task<SelectionResult?>>? SelectionHandler { get; private set; }
    internal Func<string, string, Task<AgentConfirmationResult>>? ConfirmationHandler { get; private set; }

    /// <summary>
    /// Backing set for "always allow in this session" confirmations.
    /// Keyed by the <c>operationKey</c> the Agent supplies.
    /// </summary>
    private readonly HashSet<string> _sessionAllowedOperations = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Controls how aggressively the Agent uses <c>RequestSelection</c> and <c>RequestConfirmation</c>.
    /// <list type="bullet">
    ///   <item><b>0 — Silent</b>: Never interact. Act autonomously on best-guess; skip both tools entirely.</item>
    ///   <item><b>1 — Cautious (default)</b>: Ask only when intent is genuinely ambiguous or the action is bulk/destructive.</item>
    ///   <item><b>2 — Balanced</b>: Ask whenever there are multiple plausible paths OR the action touches ≥ 2 nodes/links.</item>
    ///   <item><b>3 — Strict</b>: Ask before every mutation that is not a pure single-node creation. Gate all destructive actions unconditionally.</item>
    /// </list>
    /// Valid range: 0–3. Values outside this range are clamped.
    /// </summary>
    private int _interactionSafety = 1;

    /// <summary>
    /// Custom prompt text per safety level (1–3). Level 0 is always the built-in silent rule.
    /// Key = level (1/2/3), Value = full body text to embed in the "Interaction Safety Policy" section.
    /// When a level has no entry the built-in default text is used.
    /// </summary>
    private readonly Dictionary<int, string> _safetyPromptOverrides = [];

    /// <summary>
    /// Sets the interaction safety level (0–3) that governs how often the Agent pauses
    /// to ask the user via <c>RequestSelection</c> or <c>RequestConfirmation</c>.
    /// Higher values make the Agent more conservative and user-driven.
    /// </summary>
    public WorkflowAgentScope WithInteractionSafety(int level)
    {
        _interactionSafety = Math.Max(0, Math.Min(3, level));
        BumpVersion();
        return this;
    }

    /// <summary>
    /// Overrides the prompt body text injected into the system prompt for the specified safety level (1–3).
    /// Level 0 always uses the built-in silent rule and cannot be overridden.
    /// The <paramref name="promptBody"/> replaces the entire body of the
    /// "Interaction Safety Policy" section for that level; the heading and footer are still generated automatically.
    /// Call multiple times to configure several levels independently.
    /// </summary>
    /// <param name="level">Safety level to override (1, 2, or 3).</param>
    /// <param name="promptBody">Full body text for that level, written in the language your Agent understands.</param>
    public WorkflowAgentScope WithInteractionSafetyPrompt(int level, string promptBody)
    {
        if (level < 1 || level > 3) return this;
        _safetyPromptOverrides[level] = promptBody ?? string.Empty;
        BumpVersion();
        return this;
    }

    /// <summary>
    /// Registers an asynchronous handler for the <c>RequestSelection</c> tool.
    /// The handler receives an <see cref="AgentSelectionEventArgs"/> describing the prompt and options,
    /// and must set <see cref="AgentSelectionEventArgs.SelectedOption"/> (single-select),
    /// <see cref="AgentSelectionEventArgs.SelectedOptions"/> (multi-select), and/or
    /// <see cref="AgentSelectionEventArgs.FreeTextResponse"/> before completing.
    /// When <c>null</c>, the <c>RequestSelection</c> tool is not registered.
    /// </summary>
    public WorkflowAgentScope WithSelectionHandler(Func<AgentSelectionEventArgs, Task> handler)
    {
        SelectionHandler = handler is null ? null : async (prompt, options, freeTextPrompt, allowMultiSelect) =>
        {
            var args = new AgentSelectionEventArgs(prompt, options)
            {
                AllowMultiSelect = allowMultiSelect,
                FreeTextPrompt = freeTextPrompt,
            };
            await handler(args);
            return new SelectionResult
            {
                SelectedOption = args.SelectedOption,
                SelectedOptions = args.SelectedOptions ?? [],
                FreeTextResponse = args.FreeTextResponse,
            };
        };
        BumpVersion();
        return this;
    }

    /// <summary>
    /// Registers an asynchronous handler for the <c>RequestConfirmation</c> tool.
    /// The handler receives an <see cref="AgentConfirmationEventArgs"/> describing the operation,
    /// and must set <see cref="AgentConfirmationEventArgs.Result"/> before completing.
    /// When <c>null</c>, the <c>RequestConfirmation</c> tool is not registered.
    /// </summary>
    public WorkflowAgentScope WithConfirmationHandler(Func<AgentConfirmationEventArgs, Task> handler)
    {
        ConfirmationHandler = handler is null ? null : async (key, desc) =>
        {
            var args = new AgentConfirmationEventArgs(key, desc);
            await handler(args);
            return args.Result;
        };
        BumpVersion();
        return this;
    }

    /// <summary>
    /// Called by <c>WorkflowAgentToolkit.RequestConfirmation</c>.
    /// Returns <c>true</c> when the operation is allowed (either session-wide or one-time).
    /// Persists session-wide approvals automatically.
    /// </summary>
    internal async Task<bool> ResolveConfirmationAsync(string operationKey, string description)
    {
        if (_sessionAllowedOperations.Contains(operationKey))
            return true;

        if (ConfirmationHandler is null)
            return false;

        var result = await ConfirmationHandler(operationKey, description);
        if (result == AgentConfirmationResult.AllowAlways)
            _sessionAllowedOperations.Add(operationKey);

        return result != AgentConfirmationResult.Deny;
    }

    internal async Task RaiseToolCalledAsync(string toolName, string result, int callCount)
    {
        var args = new AgentToolCallEventArgs(toolName, result, callCount);
        ToolCalled?.Invoke(this, args);
        if (_toolCallHandler is not null)
            await _toolCallHandler(args);
    }

    // ── Interaction safety prompt ───────────────────────────────────────────

    /// <summary>
    /// Whether interaction tools (RequestSelection / RequestConfirmation) are allowed.
    /// When <c>false</c> (level 0), no interaction tools are registered and no safety
    /// policy is emitted, guaranteeing the Agent cannot call them.
    /// </summary>
    internal bool IsInteractionAllowed => _interactionSafety > 0;

    private string BuildInteractionSafetyPrompt(AgentLanguages language)
    {
        if (_interactionSafety == 0)
            return string.Empty; // No tools registered → no policy needed

        var sb = new StringBuilder();
        sb.AppendLine("## Interaction Safety Policy");
        sb.AppendLine();

        // Non-level-0 (1–3): load shared gate + per-level rules
        {
            // ── Shared gate (levels 1–3): loaded from embedded Safety/Shared.md ──
            var shared = AgentEmbeddedResources.ReadSafety(SystemName, "Shared", language);
            if (!string.IsNullOrWhiteSpace(shared))
                sb.AppendLine(shared!.TrimEnd());

            sb.AppendLine();

            // ── Per-level rules: loaded from embedded Safety/Level{n}.md ────────
            var levelFile = AgentEmbeddedResources.ReadSafety(SystemName, $"Level{_interactionSafety}", language);
            if (!string.IsNullOrWhiteSpace(levelFile))
                sb.AppendLine(levelFile!.TrimEnd());

            // ── Host-supplied additive overrides ─────────────────────────────────
            if (_safetyPromptOverrides.TryGetValue(_interactionSafety, out var custom) && !string.IsNullOrWhiteSpace(custom))
            {
                sb.AppendLine();
                if (language == AgentLanguages.Chinese)
                    sb.AppendLine("#### 宿主自定义附加规则（优先级高于上述所有默认规则）");
                else
                    sb.AppendLine("#### Host-Configured Additional Rules (take priority over all defaults above)");
                sb.AppendLine(custom.TrimEnd());
            }
        }

        sb.AppendLine();
        if (language == AgentLanguages.Chinese)
            sb.AppendLine($"> 当前安全挡位：**第 {_interactionSafety} 挡**（由宿主通过 `WithInteractionSafety({_interactionSafety})` 设置）。");
        else
            sb.AppendLine($"> Active safety level: **{_interactionSafety}** (set by the host via `WithInteractionSafety({_interactionSafety})`).");
        return sb.ToString();
    }

    /// <summary>
    /// A global, language-agnostic recovery protocol injected into both prompt modes.
    /// Gives the Agent a deterministic procedure when a tool returns error/rejected,
    /// and tells it that some tools may be disabled by host policy.
    /// </summary>
    private string BuildFailureHandlingProtocol(AgentLanguages language)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Failure Handling Protocol (apply on every error/rejection)");
        sb.AppendLine();
        sb.AppendLine("When a tool returns `error` or `rejected`:");
        sb.AppendLine("1. Read `message`, `reasons`, `hint`, and `preferredAlternative` — they name the cause and the recommended next tool.");
        sb.AppendLine("2. Do NOT blindly retry the same call with the same arguments.");
        sb.AppendLine("3. Verify current state first (`ListNodes`, `GetNodeDetail`, `GetComponentContext`) before any retry — indices and IDs shift after structural changes.");
        sb.AppendLine("4. If a `hint` or `preferredAlternative` names another tool, switch to it.");
        sb.AppendLine("5. If you still cannot make progress after two attempts, stop and ask the user via `RequestConfirmation`, or report the blocker plainly in your reply. Do not loop silently.");
        sb.AppendLine();
        sb.AppendLine("### When a call limit is reached");
        sb.AppendLine();
        sb.AppendLine("A refusal that names a **limit** is a hard stop, not a retryable error. Every tool call is accepted or refused; once the budget is spent, none of them work until it is reopened.");
        sb.AppendLine();
        sb.AppendLine("1. **Do not retry the refused tool**, and do not switch to another tool hoping it is exempt — they all go through the same budget.");
        sb.AppendLine("2. **Do not tell the user you will continue.** You cannot widen your own budget, and saying otherwise is a promise you cannot keep.");
        sb.AppendLine("3. Call **`ResetToolCallLimit`**. It puts the question to the user and waits for their answer; only their agreement reopens the budget.");
        sb.AppendLine("4. If they decline, stop and report: what you completed, what is still outstanding, and that the limit is why you stopped. Let them raise or reset it themselves.");
        sb.AppendLine();
        sb.AppendLine("> Some tools may be **disabled by host policy** (e.g. `ExecuteNode`, `ExecuteCommandOnNode`, `ExecuteCommandById`). If a tool returns a `disabled by host policy` error, do not work around it — report it and let the user enable it if needed.");
        return sb.ToString();
    }

    public WorkflowAgentScope WithEnums(Type[] enums, AgentLanguages? language = null)
    {
        var lang = Resolve(language);
        if (CustomerEnums.TryGetValue(lang, out var set))
        {
            foreach (var item in enums) set.Add(item);
        }
        else
        {
            CustomerEnums[lang] = [.. enums];
        }
        BumpVersion();
        return this;
    }

    public WorkflowAgentScope WithInterfaces(Type[] interfaces, AgentLanguages? language = null)
    {
        var lang = Resolve(language);
        if (CustomerInterfaces.TryGetValue(lang, out var set))
        {
            foreach (var item in interfaces) set.Add(item);
        }
        else
        {
            CustomerInterfaces[lang] = [.. interfaces];
        }
        BumpVersion();
        return this;
    }

    public WorkflowAgentScope WithComponents(Type[] components, AgentLanguages? language = null)
    {
        var lang = Resolve(language);
        if (CustomerComponents.TryGetValue(lang, out var set))
        {
            foreach (var item in components) set.Add(item);
        }
        else
        {
            CustomerComponents[lang] = [.. components];
        }
        BumpVersion();
        return this;
    }

    /// <summary>
    /// Registers value-object / data types (e.g. custom Anchor-like structs, size records)
    /// so the Agent understands their structure as plain data, not as interactive components.
    /// </summary>
    public WorkflowAgentScope WithData(Type[] dataTypes, AgentLanguages? language = null)
    {
        var lang = Resolve(language);
        if (CustomerData.TryGetValue(lang, out var set))
        {
            foreach (var item in dataTypes) set.Add(item);
        }
        else
        {
            CustomerData[lang] = [.. dataTypes];
        }
        BumpVersion();
        return this;
    }

    /// <summary>
    /// Scans <paramref name="assembly"/> and automatically registers all workflow-related types,
    /// then deeply inspects each discovered component to infer additional related types.
    /// <list type="bullet">
    ///   <item>Concrete workflow component classes → <see cref="WithComponents"/>.</item>
    ///   <item>Enum types referenced by <c>[SlotSelectors]</c> or any property/field/parameter → <see cref="WithEnums"/>.</item>
    ///   <item>Interface types used as property/field types on any component → <see cref="WithInterfaces"/>.</item>
    ///   <item>Custom parameter types referenced by <c>[AgentCommandParameter]</c> on any method → <see cref="WithData"/>.</item>
    ///   <item>Non-workflow classes/structs decorated with <c>[AgentContext]</c> → <see cref="WithData"/>.</item>
    ///   <item>Non-primitive value-object structs found on component properties → <see cref="WithData"/>.</item>
    /// </list>
    /// Already-registered types and framework built-in types are never re-added.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <param name="language">Override language for this call; if <c>null</c> the global default set by <see cref="WithPromptLanguage"/> is used.</param>
    public WorkflowAgentScope WithAutoDiscovery(Assembly assembly, AgentLanguages? language = null)
    {
        if (assembly is null) throw new ArgumentNullException(nameof(assembly));
        var lang = Resolve(language);

        var workflowBase = typeof(IWorkflowViewModel);
        var nodeBase     = typeof(IWorkflowNodeViewModel);
        var slotBase     = typeof(IWorkflowSlotViewModel);
        var linkBase     = typeof(IWorkflowLinkViewModel);
        var treeBase     = typeof(IWorkflowTreeViewModel);

        // Pass 1: register components and [AgentContext] data types.
        // Only types actually registered here are marked in _globallyDiscoveredTypes.
        // Enums / interfaces / data referenced by component members are marked by TryRegister*
        // during Pass 2 — if Pass 1 marked every concrete type, Pass 2's "already registered?"
        // guard would reject them all and no member-inferred type would ever register.
        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface) continue;
            if (_globallyDiscoveredTypes.Contains(type)) continue; // already registered by a prior call

            bool isWorkflowComponent = nodeBase.IsAssignableFrom(type)
                || slotBase.IsAssignableFrom(type)
                || linkBase.IsAssignableFrom(type)
                || treeBase.IsAssignableFrom(type);

            if (isWorkflowComponent)
            {
                // Framework built-ins (e.g. NodeDefaultViewModel when scanning VeloxDev.Core)
                // are never "customer" components — skip them so they do not pollute the
                // customer context or get deep-scanned as if they were host-authored.
                if (!IsFrameworkBuiltin(type))
                {
                    _globallyDiscoveredTypes.Add(type);
                    WithComponents([type], lang);
                }
            }
            else if (type.IsEnum && type.GetCustomAttributes<AgentContextAttribute>().Any())
            {
                // [AgentContext]-annotated enums are rendered by GetEnumContext — never as data
                // (GetDataContext would present an enum's members as properties). Register them
                // as enums here so their documentation surfaces even if no component member
                // references them. Unannotated enums are discovered during Pass 2 member scanning.
                if (!IsFrameworkBuiltin(type))
                {
                    _globallyDiscoveredTypes.Add(type);
                    WithEnums([type], lang);
                }
            }
            else if (type.GetCustomAttributes<AgentContextAttribute>().Any() && !IsFrameworkBuiltin(type))
            {
                _globallyDiscoveredTypes.Add(type);
                WithData([type], lang);
            }
        }

        // Pass 2: deep-scan every registered component to infer Enums / Interfaces / Data
        var registeredComponents = CustomerComponents.TryGetValue(lang, out var cs) ? cs : (IEnumerable<Type>)[];
        foreach (var type in registeredComponents.ToArray())
            ScanComponentMembers(type, lang, workflowBase);

        BumpVersion();
        return this;
    }

    /// <summary>
    /// Scans <paramref name="assemblyName"/> and automatically registers all workflow-related types.
    /// </summary>
    /// <param name="assemblyName">Simple name of the assembly to scan (e.g. <c>"Lib"</c>).</param>
    /// <param name="language">Override language for this call; if <c>null</c> the global default set by <see cref="WithPromptLanguage"/> is used.</param>
    public WorkflowAgentScope WithAutoDiscovery(string assemblyName, AgentLanguages? language = null)
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == assemblyName);
        return assembly == null
            ? throw new ArgumentException($"Assembly '{assemblyName}' not found in current AppDomain.", nameof(assemblyName))
            : WithAutoDiscovery(assembly, language);
    }

    private void ScanComponentMembers(Type type, AgentLanguages lang, Type workflowBase)
    {
        const BindingFlags allInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        // --- Properties ---
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // Prefer attributes declared on implemented interfaces when present (interface attributes are authoritative).
            SlotSelectorsAttribute? selAttr = prop.GetCustomAttribute<SlotSelectorsAttribute>();
            AgentCommandParameterAttribute? paramAttr = prop.GetCustomAttribute<AgentCommandParameterAttribute>();

            if (selAttr == null || paramAttr == null)
            {
                foreach (var iface in type.GetInterfaces())
                {
                    var ip = iface.GetProperty(prop.Name);
                    if (ip == null) continue;
                    selAttr ??= ip.GetCustomAttribute<SlotSelectorsAttribute>();
                    paramAttr ??= ip.GetCustomAttribute<AgentCommandParameterAttribute>();
                    if (selAttr != null && paramAttr != null) break;
                }
            }

            CollectSlotSelectorTypes(selAttr, lang);
            if (paramAttr?.ParameterType is { } ppt)
            {
                TryRegisterData(ppt, lang, workflowBase);
                RegisterGenericTypeArguments(ppt, lang, workflowBase);
            }
            TryRegisterMemberType(prop.PropertyType, lang, workflowBase);
            RegisterGenericTypeArguments(prop.PropertyType, lang, workflowBase);
        }

        // --- Fields (backing fields of [VeloxProperty]; attributes allowed there too) ---
        foreach (var field in type.GetFields(allInstance))
        {
            CollectSlotSelectorTypes(field.GetCustomAttribute<SlotSelectorsAttribute>(), lang);
            if (field.GetCustomAttribute<AgentCommandParameterAttribute>()?.ParameterType is { } fpt)
            {
                TryRegisterData(fpt, lang, workflowBase);
                RegisterGenericTypeArguments(fpt, lang, workflowBase);
            }
            TryRegisterMemberType(field.FieldType, lang, workflowBase);
            RegisterGenericTypeArguments(field.FieldType, lang, workflowBase);
        }

        // --- Methods: [AgentCommandParameter] + declared parameter types ---
        foreach (var method in type.GetMethods(allInstance))
        {
            if (method.GetCustomAttribute<AgentCommandParameterAttribute>()?.ParameterType is { } mpt)
            {
                TryRegisterData(mpt, lang, workflowBase);
                RegisterGenericTypeArguments(mpt, lang, workflowBase);
            }
            foreach (var p in method.GetParameters())
            {
                TryRegisterMemberType(p.ParameterType, lang, workflowBase);
                RegisterGenericTypeArguments(p.ParameterType, lang, workflowBase);
            }
        }
    }

    private void CollectSlotSelectorTypes(SlotSelectorsAttribute? sel, AgentLanguages lang)
    {
        if (sel == null) return;
        foreach (var et in sel.AllowedEnumTypes)
            TryRegisterEnum(et, lang);
        // String-based constructor: AllowedEnumTypes is empty; resolve names best-effort
        if (sel.AllowedEnumTypes.Length == 0)
        {
            foreach (var name in sel.AllowedEnumTypeNames)
            {
                if (string.IsNullOrEmpty(name)) continue;
                var resolved = Type.GetType(name, throwOnError: false);
                if (resolved != null)
                    TryRegisterEnum(resolved, lang);
            }
        }
    }

    private void TryRegisterMemberType(Type type, AgentLanguages lang, Type workflowBase)
    {
        type = UnwrapGeneric(type);

        if (type == null || type == typeof(object) || type == typeof(string) || type.IsPrimitive)
            return;

        if (workflowBase.IsAssignableFrom(type))
            return;   // workflow components already handled in Pass 1

        if (IsFrameworkBuiltin(type))
            return;

        if (type.IsEnum)      { TryRegisterEnum(type, lang);               return; }
        if (type.IsInterface) { TryRegisterInterface(type, lang);           return; }
        if (type.IsValueType) { TryRegisterData(type, lang, workflowBase); return; }

        // Reference class carrying [AgentContext] → Data
        if (type.GetCustomAttributes<AgentContextAttribute>().Any())
            TryRegisterData(type, lang, workflowBase);
    }

    private static Type UnwrapGeneric(Type type)
    {
        if (!type.IsGenericType) return type;

        var def  = type.GetGenericTypeDefinition();
        var args = type.GetGenericArguments();

        if (args.Length != 1) return type;

        var defName = def.FullName ?? string.Empty;
        if (def == typeof(Nullable<>)
            || defName == "System.Collections.Generic.IEnumerable`1"
            || defName == "System.Collections.Generic.IList`1"
            || defName == "System.Collections.Generic.ICollection`1"
            || defName == "System.Collections.Generic.IReadOnlyList`1"
            || defName == "System.Collections.Generic.IReadOnlyCollection`1"
            || defName == "System.Collections.Generic.List`1"
            || defName == "System.Threading.Tasks.Task`1"
            || defName == "System.Threading.Tasks.ValueTask`1")
        {
            return UnwrapGeneric(args[0]);
        }

        return type;
    }

    private static bool IsFrameworkBuiltin(Type type)
    {
        if (FrameworkEnums.Contains(type)) return true;
        if (FrameworkData.Contains(type)) return true;
        foreach (var fi in FrameworkInterfaces) if (fi == type) return true;
        foreach (var fc in FrameworkComponents) if (fc == type) return true;
        var ns = type.Namespace ?? string.Empty;
        return ns.StartsWith("System")
            || ns.StartsWith("Microsoft")
            || ns.StartsWith("VeloxDev.WorkflowSystem")
            // Framework MVVM plumbing (VeloxDev.MVVM: IVeloxCommand, base view models, …) must
            // never surface as a customer interface when a component's command properties are
            // deep-scanned.
            || ns.StartsWith("VeloxDev.MVVM")
            // Compiler/engine plumbing under VeloxDev.Core.WorkflowSystem (e.g. CompilerEx enums
            // like RouterCompileMode, RuntimeContext, CompileContext) is framework-internal — it
            // must never surface in the Agent's customer context. Note this is a distinct prefix
            // from VeloxDev.WorkflowSystem (the public component namespace) — both are excluded.
            || ns.StartsWith("VeloxDev.Core.WorkflowSystem");
    }

    /// <summary>
    /// Recursively extracts all generic type arguments from a type and registers them.
    /// Handles multi-parameter generics (e.g. Dictionary&lt;string, MyEnum&gt;) that
    /// <see cref="UnwrapGeneric"/> cannot fully unwrap.
    /// </summary>
    private void RegisterGenericTypeArguments(Type type, AgentLanguages lang, Type workflowBase)
    {
        if (!type.IsGenericType) return;
        foreach (var arg in type.GetGenericArguments())
        {
            TryRegisterMemberType(arg, lang, workflowBase);
            // Recurse in case the argument itself is generic (e.g. List&lt;Dictionary&lt;string, MyEnum&gt;&gt;)
            RegisterGenericTypeArguments(arg, lang, workflowBase);
        }
    }

    private void TryRegisterEnum(Type type, AgentLanguages lang)
    {
        if (!type.IsEnum || IsFrameworkBuiltin(type)) return;
        if (CustomerEnums.TryGetValue(lang, out var set) && set.Contains(type)) return;
        if (!_globallyDiscoveredTypes.Add(type)) return; // already registered under a different language
        WithEnums([type], lang);
    }

    private void TryRegisterInterface(Type type, AgentLanguages lang)
    {
        if (!type.IsInterface || IsFrameworkBuiltin(type)) return;
        if (CustomerInterfaces.TryGetValue(lang, out var set) && set.Contains(type)) return;
        if (!_globallyDiscoveredTypes.Add(type)) return; // already registered under a different language
        WithInterfaces([type], lang);
    }

    private void TryRegisterData(Type type, AgentLanguages lang, Type workflowBase)
    {
        if (type.IsPrimitive || type == typeof(string) || type == typeof(object)) return;
        if (type.IsEnum || type.IsInterface || IsFrameworkBuiltin(type)) return;
        if (workflowBase.IsAssignableFrom(type)) return;
        if (CustomerComponents.TryGetValue(lang, out var cs) && cs.Contains(type)) return;
        if (CustomerData.TryGetValue(lang, out var ds) && ds.Contains(type)) return;
        if (!_globallyDiscoveredTypes.Add(type)) return; // already registered under a different language
        WithData([type], lang);
    }

    /// <summary>
    /// Provides full context using the global default language set by <see cref="WithPromptLanguage"/>.
    /// </summary>
    public string ProvideAllContexts() => ProvideAllContexts(_defaultLanguage);

    public string ProvideAllContexts(AgentLanguages language)
    {
        var result = new StringBuilder();

        result.AppendLine("# Workflow Agent Context");
        result.AppendLine();
        result.AppendLine("> Agent can learn about the structure of the Workflow Framework and how to Takeover a workflow system with Takeover Protocol.");
        result.AppendLine("> Agent can read source code from https://github.com/Axvser/VeloxDev");
        result.AppendLine();

        // ── Built-in References ──
        result.AppendLine(AgentEmbeddedResources.ReadAllReferences(SystemName, language).TrimEnd());
        result.AppendLine();

        result.AppendLine("## Framework Context");
        result.AppendLine();
        result.AppendLine(ProvideFrameworkContext(language));
        result.AppendLine();
        result.AppendLine("## Framework Data Types");
        result.AppendLine();
        result.AppendLine(ProvideFrameworkDataContext(language));
        result.AppendLine();
        result.AppendLine("## Customer Context");
        result.AppendLine();
        result.AppendLine(ProvideCustomerContext(language));
        result.AppendLine();
        result.AppendLine("## Customer Data Types");
        result.AppendLine();
        result.AppendLine(ProvideCustomerDataContext(language));
        result.AppendLine();

        // ── Failure Handling Protocol ──
        result.AppendLine(BuildFailureHandlingProtocol(language));

        // ── Custom Tools ──
        AppendCustomToolsSection(result);

        // ── Interaction Safety Policy ──
        result.AppendLine(BuildInteractionSafetyPrompt(language));

        // ── Built-in Skills ──
        AppendEmbeddedSkills(result, language);
        AppendOutputLanguageDirective(result);
        CaptureSkeletonReceipt(language);
        return result.ToString();
    }

    /// <summary>
    /// Provides a minimal system prompt for progressive context disclosure.
    /// Only includes a brief overview and instructs the Agent to use
    /// GetWorkflowSummary / GetComponentContext / ListComponentCommands
    /// to discover details on demand, reducing initial token overhead.
    /// </summary>
    /// <summary>Provides a progressive context prompt using the global default language set by <see cref="WithPromptLanguage"/>.</summary>
    public string ProvideProgressiveContextPrompt() => ProvideProgressiveContextPrompt(_defaultLanguage);

    public string ProvideProgressiveContextPrompt(AgentLanguages language)
    {
        var result = new StringBuilder();

        result.AppendLine("# Workflow Agent Context (Progressive)");
        result.AppendLine();
        result.AppendLine("> You are an Agent that fully operates a visual workflow editor via tools.");
        result.AppendLine();

        // ── Global Behavioral Constraints ──
        result.AppendLine("## Critical Behavioral Constraints");
        result.AppendLine();
        result.AppendLine("These constraints apply to ALL operations.");
        result.AppendLine();
        result.AppendLine("### 0. How the framework reports results — read this first");
        result.AppendLine("Nearly every tool returns a JSON object with a `status` field: `ok`, `error`, or `rejected`.");
        result.AppendLine("- `ok` — the operation succeeded.");
        result.AppendLine("- `error` — the operation failed; read `message` for the cause.");
        result.AppendLine("- `rejected` — a connection was refused; read `reasons`, `hint`, and `preferredAlternative`, which tell you exactly what to do next.");
        result.AppendLine("Treat `error`/`rejected` as **actionable feedback**, never as a dead end.");
        result.AppendLine("A few operations on **unmounted** components are silent no-ops (no error, no effect). If an operation seems to do nothing, first call `ListNodes` / `GetNodeDetail` to verify the component is mounted, then retry. See the Failure Handling Protocol below.");
        result.AppendLine();
        result.AppendLine("### 1. Mount-before-operate");
        result.AppendLine("Nodes MUST be added to the Tree (via `CreateNode`) before any operation on their internals.");
        result.AppendLine("Operating on an unmounted node (`Parent == null`) results in " + (language == AgentLanguages.Chinese ? "**静默无操作**——框架不报错，操作不生效" : "**silent no-op** — the framework returns without error and the operation has no effect") + ".");
        result.AppendLine("Always use a `nodeIndex` or `runtimeId` obtained from `ListNodes` / `CreateNode` to reference nodes.");
        result.AppendLine();
        result.AppendLine("### 2. Delete cascading (do NOT pre-clean)");
        result.AppendLine("Deleting a node (`DeleteNode`) triggers `StandardDelete` which performs an **atomic 4-phase cascade** wrapped in a single undoable action:");
        result.AppendLine("**Phase 1** — Collect all valid links (both endpoints in the same Tree) via `LinksMap` lookup. Deduplicates them.");
        result.AppendLine("**Phase 2** — Remove every link: delete from `LinksMap`, `Links`, `Sender.Targets`, `Receiver.Sources`, set `IsVisible = false`.");
        result.AppendLine("**Phase 3** — Null every slot's `Parent`, then remove node from `tree.Nodes` and null `node.Parent`.");
        result.AppendLine("**Phase 4** — Batch-update affected slot states via `UpdateState()`.");
        result.AppendLine("You do NOT need to call `DisconnectSlots` or `DeleteSlot` before `DeleteNode`. The cascade is complete and atomic.");
        result.AppendLine();
        result.AppendLine("Deleting a slot (`DeleteSlot`) triggers `StandardDelete` which:");
        result.AppendLine("**1** — Collects all links touching this slot (both as sender and as receiver) via `LinksMap`.");
        result.AppendLine("**2** — Delegates to each link's `link.GetHelper().Delete()` (which removes from all 6 collections and calls `UpdateState()` on both endpoints).");
        result.AppendLine("**3** — Removes the slot from `parent.Slots` and nulls `slot.Parent`. (Undoable when the node is in a Tree; direct removal otherwise.)");
        result.AppendLine("You do NOT need to manually disconnect slot connections before `DeleteSlot`.");
        result.AppendLine();
        result.AppendLine("### 3. Connection auto-dedup");
        result.AppendLine("When connecting two nodes that already share a same-direction connection, the framework silently replaces the old connection.");
        result.AppendLine("You do NOT need to call `DisconnectSlots` before `ConnectSlots` for the same node pair.");
        result.AppendLine();
        result.AppendLine("### 4. Reference integrity");
        result.AppendLine("Node indices shift after `CreateNode`, `DeleteNode`, `DeleteSlot`. Always refresh after structural changes.");
        result.AppendLine("Runtime IDs are stable for the lifetime of a component, except SlotEnumerator slots which are rebuilt on selector change.");
        result.AppendLine();
        result.AppendLine("### 5. Property patching — pick the tool by intent");
        result.AppendLine("`PatchNodeProperties` / `PatchComponentById` are **rejecting** patchers: for every property they refuse they return a `rejected`/`skipped` entry with the exact `reason`. Use the table instead of guessing:");
        result.AppendLine();
        result.AppendLine("| Intent | Use this tool | If it rejects / errors |");
        result.AppendLine("|---|---|---|");
        result.AppendLine("| Position / anchor | `SetNodePosition` | call `GetNodeDetail` to read the current Layer, then retry |");
        result.AppendLine("| Size | `ResizeNode` | ensure width/height > 0; a zero size makes a node invisible |");
        result.AppendLine("| Slot channel | `SetSlotChannel` | read the slot's current channel via `GetNodeDetail` |");
        result.AppendLine("| SlotEnumerator selector | `SetEnumSlotCollection` | verify the type is in `allowedSelectorTypes`; never patch it directly |");
        result.AppendLine("| Add / remove collection slot | `AddSlotToCollection` / `RemoveSlotFromCollection` | — |");
        result.AppendLine("| Plain custom property | `PatchNodeProperties` | read the `reason`; if it says `command-backed`, use the dedicated tool it names |");
        result.AppendLine();
        result.AppendLine("The patcher **always rejects by code** (do not try to work around it): `Parent`, `Nodes`, `Links`, `LinksMap`, `Slots`, `Targets`, `Sources`, `State`, `VirtualLink`, `RuntimeId`, `Helper`, slot-typed properties, and `[SlotSelectors]`-marked properties.");
        result.AppendLine();
        result.AppendLine("### 6. Prefer mutate in-place; fall back to delete+recreate when needed");
        result.AppendLine("ALWAYS prefer in-place mutation over delete+recreate. Deleting a node destroys its identity, all slots, and all connections.");
        result.AppendLine("The following changes are all possible **without** deleting the node:");
        result.AppendLine("- Change properties → `PatchNodeProperties` / `PatchComponentById`");
        result.AppendLine("- Change SlotEnumerator selector type → `SetEnumSlotCollection` (on the existing node)");
        result.AppendLine("- Add/remove slots → `AddSlotToCollection` / `RemoveSlotFromCollection` / `CreateSlotOnNode`");
        result.AppendLine("- Resize → `ResizeNode`");
        result.AppendLine("- Reposition → `SetNodePosition` / `MoveNode`");
        result.AppendLine("- Reconnect → `DisconnectSlots` then `ConnectSlots` (or `ConnectByProperty`); reconnecting is a two-step command sequence, never one bundled call");
        result.AppendLine("Only use `DeleteNode` when the user explicitly asks to remove a node, or when in-place mutation is genuinely impossible (e.g. the node type itself must change). If a patch is rejected, first try the suggested alternative tool; if that also fails, delete+recreate is acceptable as a last resort.");
        result.AppendLine();
        result.AppendLine(BuildFailureHandlingProtocol(language));
        result.AppendLine();

        // ── Built-in References ──
        result.AppendLine(AgentEmbeddedResources.ReadAllReferences(SystemName, language).TrimEnd());
        result.AppendLine();

        result.AppendLine("## Registered Component Types");
        result.AppendLine();
        foreach (var t in FrameworkInterfaces)
            result.AppendLine($"- `{t.FullName}` (framework interface)");
        foreach (var t in FrameworkComponents)
            result.AppendLine($"- `{t.FullName}` (framework base class)");
        foreach (var t in FrameworkData)
            result.AppendLine($"- `{t.FullName}` (framework data)");
        foreach (var kvp in CustomerEnums)
        {
            foreach (var t in kvp.Value)
                result.AppendLine($"- `{t.FullName}` (customer enum)");
        }
        foreach (var kvp in CustomerInterfaces)
        {
            foreach (var t in kvp.Value)
                result.AppendLine($"- `{t.FullName}` (customer interface)");
        }
        foreach (var kvp in CustomerComponents)
        {
            foreach (var t in kvp.Value)
                result.AppendLine($"- `{t.FullName}` (customer component)");
        }
        foreach (var kvp in CustomerData)
        {
            foreach (var t in kvp.Value)
                result.AppendLine($"- `{t.FullName}` (customer data)");
        }

        result.AppendLine();
        result.AppendLine("## Registered Component Summaries (one line each)");
        result.AppendLine();
        result.AppendLine("One-line summaries only. The full property/command tables are intentionally NOT preloaded to keep the prompt small and accurate.");
        result.AppendLine();
        result.AppendLine("**MANDATE:** the FIRST time you operate on a type this session, call **GetComponentContext** with its full type name (e.g. `GetComponentContext(\"Demo.ViewModels.HttpNodeViewModel\")`) to load its complete property/command table. Never guess a type's shape from its name or from its one-line summary.");
        result.AppendLine();
        AppendPreloadedComponentSummaries(result, language);
        result.AppendLine();
        result.AppendLine("> Before operating on any type: `GetComponentContext(\"<Full.Type.Name>\")` first.");
        result.AppendLine();

        // ── Custom Tools ──
        AppendCustomToolsSection(result);

        // ── Interaction Safety Policy ──
        result.AppendLine(BuildInteractionSafetyPrompt(language));

        // ── Built-in Skills ──
        AppendEmbeddedSkills(result, language);
        AppendOutputLanguageDirective(result);
        CaptureSkeletonReceipt(language);
        return result.ToString();
    }

    /// <summary>
    /// Injects the <see cref="WithTools"/> / <see cref="WithQueryTools"/> <c>promptContext</c> text
    /// (if any) as a "Custom Tools" section so the Agent knows when and how to use them.
    /// </summary>
    private void AppendCustomToolsSection(StringBuilder result)
    {
        if (_customToolPrompt.Length == 0) return;
        result.AppendLine("## Custom Tools");
        result.AppendLine();
        result.AppendLine(_customToolPrompt.ToString().TrimEnd());
        result.AppendLine();
    }

    private void AppendOutputLanguageDirective(StringBuilder result)
    {
        if (_outputLanguage is null) return;
        var displayName = _outputLanguage.Value.GetDisplayName();
        var langCode = _outputLanguage.Value.ToLanguageCode();
        result.AppendLine();
        result.AppendLine("## Output Language");
        result.AppendLine();
        result.AppendLine($"> **Always use {displayName} ({langCode}) for ALL output**, including:");
        result.AppendLine($"> - Every conversational reply to the user.");
        result.AppendLine($"> - The `prompt` argument of `RequestSelection` and the `description` argument of `RequestConfirmation`.");
        result.AppendLine($"> - Any human-readable text embedded inside tool call arguments.");
        result.AppendLine($"> This rule overrides any language implied by the source material or documentation.");
    }

    public string ProvideFrameworkContext(AgentLanguages language = AgentLanguages.English)
    {
        var result = new StringBuilder();

        foreach (var framework in FrameworkEnums)
        {
            result.AppendLine(AgentContextCollector.GetEnumContext(framework, language));
        }
        foreach (var framework in FrameworkInterfaces)
        {
            result.AppendLine(AgentContextCollector.GetInterfaceContext(framework, language));
        }
        foreach (var framework in FrameworkComponents)
        {
            result.AppendLine(AgentContextCollector.GetClassContext(framework, language));
        }

        return result.ToString();
    }

    public string ProvideCustomerContext(AgentLanguages language = AgentLanguages.English)
    {
        var result = new StringBuilder();

        foreach (var kvp in CustomerEnums)
        {
            foreach (var framework in kvp.Value)
            {
                result.AppendLine(AgentContextCollector.GetEnumContext(framework, kvp.Key));
            }
        }
        foreach (var kvp in CustomerInterfaces)
        {
            foreach (var framework in kvp.Value)
            {
                result.AppendLine(AgentContextCollector.GetInterfaceContext(framework, kvp.Key));
            }
        }
        foreach (var kvp in CustomerComponents)
        {
            foreach (var framework in kvp.Value)
            {
                result.AppendLine(AgentContextCollector.GetClassContext(framework, kvp.Key));
            }
        }

        return result.ToString();
    }

    private WorkflowAgentToolkit? _toolkit;

    /// <summary>
    /// The scope's <see cref="WorkflowAgentToolkit"/>, providing MAF-compatible <see cref="AITool"/>
    /// instances for full operational control over the scoped tree.
    /// <para>
    /// There is exactly one toolkit per scope, created on first use. It owns mutable state — the call
    /// counters the budgets are enforced against and the state tracker behind
    /// <c>GetChangesSinceSnapshot</c> — so handing out a fresh instance per call would silently give
    /// each caller its own budget and its own snapshot history.
    /// </para>
    /// </summary>
    public WorkflowAgentToolkit CreateToolkit() => _toolkit ??= new WorkflowAgentToolkit(this);

    /// <summary>
    /// Convenience method: returns all tools ready for use with <c>ChatOptions.Tools</c> or
    /// <c>AsAIAgent(tools: ...)</c>.
    /// <para>
    /// Prefer <see cref="CreateContextProvider"/> when the tool set must follow the scope's state: this
    /// snapshot is computed once and does not pick up later registrations.
    /// </para>
    /// </summary>
    public IList<AITool> ProvideTools() => CreateToolkit().CreateTools();

    /// <summary>
    /// Convenience method: returns only the tools in the given
    /// <see cref="WorkflowToolCategory"/> flags. Use this to shrink the tool surface exposed to
    /// the LLM (lower token cost, better tool-selection accuracy). Custom tools registered via
    /// <see cref="WithTools"/> are always included.
    /// </summary>
    public IList<AITool> ProvideTools(WorkflowToolCategory categories) => CreateToolkit().CreateTools(categories);

    // ── Dynamic context (per-invocation prompt + tool surface) ──────────────

    private long _version;

    /// <summary>
    /// Monotonic version of everything this scope can be configured with. Every setter that changes what
    /// the Agent is shown advances it, so a context provider caching on it re-renders exactly when
    /// something actually changed.
    /// </summary>
    public long Version => Interlocked.Read(ref _version);

    /// <summary>
    /// Attached skill scope, or <c>null</c> when skills are not under dynamic management. Set by
    /// <see cref="WithSkills(string)"/> or <see cref="WithSkills(SkillScope)"/>.
    /// <para>
    /// Whichever side is attached second owns the embedded corpus: a skeleton rendered before this call
    /// already carries it, so the subsystem contributes the difference instead of repeating it. The
    /// documented host order — configure everything, <i>then</i> build the skeleton — puts the corpus here.
    /// </para>
    /// </summary>
    public SkillScope? Skills { get; private set; }

    /// <summary>
    /// Attached MCP scope, or <c>null</c> when no MCP servers are wired. Set by
    /// <see cref="WithMcps"/>. Its loaded tools join the Agent's tool set on every turn, wrapped like
    /// the built-in tools so they take part in call accounting, UI-thread marshalling and dirty marking.
    /// </summary>
    public McpScope? Mcp { get; private set; }

    private readonly List<Func<WorkflowAgentScope, AIContextProvider>> _contextProviderFactories = [];

    /// <summary>
    /// Identifies this scope for the context provider's session-state key. Two providers of the same type
    /// attached to one agent must not share a key, and a scope's tree is what makes it distinct.
    /// </summary>
    internal string StateDiscriminator { get; } =
        tree is IWorkflowIdentifiable identifiable && !string.IsNullOrEmpty(identifiable.RuntimeId)
            ? identifiable.RuntimeId
            : Guid.NewGuid().ToString("N");

    /// <summary>
    /// Brings skills under dynamic management: they are discovered from <paramref name="rootPath"/> — a
    /// directory of Agent Skills folders, resolved against the application base directory when relative —
    /// and can be switched on or off at any point, by the host or by the Agent.
    /// <para>
    /// Calling this also moves the library's own embedded prompt documents into the same skill list, so
    /// they become individually switchable too. They stay injected in full while enabled: the switchable
    /// form does not thin them out.
    /// </para>
    /// </summary>
    /// <param name="rootPath">Root directory containing the skill folders.</param>
    public WorkflowAgentScope WithSkills(string rootPath)
    {
        var skills = Skills ??= new SkillScope().WithSource(new EmbeddedSkillSource(SystemName));
        // Bind the skill list to the same thread the components are bound to, so a host can bind
        // Status.Skills directly.
        skills.WithSynchronizationContext(UIContext);
        // The skill subsystem renders in whatever language it is told, and the scope owns that choice.
        skills.WithPromptLanguage(_defaultLanguage);
        skills.WithSkillRoot(rootPath);
        skills.Refresh();
        return AttachSkillProvider();
    }

    /// <summary>
    /// Attaches an already-built <see cref="SkillScope"/>. Use it to share one skill data layer between
    /// scopes, or to supply custom <see cref="ISkillSource"/> instances only.
    /// </summary>
    public WorkflowAgentScope WithSkills(SkillScope skills)
    {
        Skills = skills ?? throw new ArgumentNullException(nameof(skills));
        Skills.WithSynchronizationContext(UIContext);
        Skills.WithPromptLanguage(_defaultLanguage);
        return AttachSkillProvider();
    }

    /// <summary>
    /// Builds the skill subsystem's provider and wires the corpus's ownership.
    /// <para>
    /// A host may have rendered the static skeleton before calling <c>WithSkills</c>, in which case that
    /// skeleton already carries the embedded corpus and the skill subsystem must not carry it again — the
    /// model would read all seven documents twice. The skeleton is a string the host has already taken, so
    /// there is nothing to re-render; the subsystem is told what the prompt already says and contributes
    /// the difference instead (which skills have since been switched off).
    /// </para>
    /// </summary>
    private WorkflowAgentScope AttachSkillProvider()
    {
        _skillProvider = Skills!.CreateContextProvider(SharedTools, Pipeline, _embeddedSkillsFrozenIntoPrompt);
        BumpVersion();
        return this;
    }

    /// <summary>
    /// Whether any prompt this scope built has carried the embedded skill corpus. Sticky: the string was
    /// handed to a host, and re-rendering cannot un-say it.
    /// </summary>
    private bool _embeddedSkillsFrozenIntoPrompt;

    private AIContextProvider? _skillProvider;
    private AIContextProvider? _mcpProvider;

    /// <summary>
    /// The tool seam every source this scope composes is given, so a tool from MCP or a skill is gated,
    /// counted and reported exactly like a built-in one.
    /// <para>
    /// One shared instance, not a copy per source: separate seams would mean separate call counters, and
    /// the budgets would stop being budgets. It reads the scope live, so a <c>With*</c> call made after a
    /// subsystem was attached still governs that subsystem's tools.
    /// </para>
    /// </summary>
    private ToolPipeline SharedTools => CreateToolkit().Tools;

    // ── Event pipeline ──────────────────────────────────────────────────────

    private AgentPipeline? _pipeline;
    private AgentTranscript? _transcript;

    /// <summary>
    /// The conversation this scope reports into, once <see cref="WithTranscript"/> has attached one.
    /// A host binds this instead of writing its own run loop.
    /// </summary>
    public AgentTranscript? Transcript => _transcript;

    /// <summary>
    /// The scope's event chain: every tool call, every fragment of text, every piece of reasoning, and the
    /// turn boundaries. Composed on first use and shared, so a host subscribes once.
    /// <para>
    /// Attach the pipeline to the agent with <c>agent.AsBuilder().UseAgentPipeline(scope.Pipeline).Build()</c>
    /// — the framework's own middleware slot, which is what lets a host keep calling
    /// <c>RunAsync</c> / <c>RunStreamingAsync</c> unchanged.
    /// </para>
    /// </summary>
    public AgentPipeline Pipeline
    {
        get
        {
            if (_pipeline is not null) return _pipeline;

            var pipeline = new AgentPipeline();

            // The text stage is always present, even with no conversation attached yet: this getter is
            // reached by anything that composes a subsystem, so a host that attaches its transcript
            // afterwards would otherwise end up with a chain that can never feed it. The stage asks for the
            // transcript per event and no-ops while there is none.
            //
            // Text first, then tools: they handle disjoint events, so the order is only about which a
            // reader of the chain meets first.
            pipeline.Use(new TextPipeline(() => _transcript, () => UIContext));
            pipeline.Use(SharedTools);
            pipeline.Use(CreateToolkit().CreateAccountingStage());

            _pipeline = pipeline;
            return _pipeline;
        }
    }

    /// <summary>
    /// Attaches a conversation for the scope to report into, and wires the stages that maintain it.
    /// <para>
    /// One call replaces the run loop a host would otherwise write: after this,
    /// <see cref="AgentTranscript.Entries"/> is the conversation, in order, with the model's reasoning kept
    /// beside its answers rather than folded into them.
    /// </para>
    /// </summary>
    public WorkflowAgentScope WithTranscript(AgentTranscript transcript)
    {
        if (transcript is null) throw new ArgumentNullException(nameof(transcript));
        if (ReferenceEquals(_transcript, transcript)) return this;

        if (_transcript is not null)
            throw new InvalidOperationException(
                "This scope already reports into a transcript. Attach one scope per conversation, or clear the existing transcript.");

        _transcript = transcript;
        return this;
    }

    /// <summary>
    /// The Agent-facing skill tools, bound to this scope's skill set and prompt language. The language
    /// is taken from <see cref="WithPromptLanguage"/> so a skill's text is read in the same language the
    /// rest of the prompt is written in.
    /// </summary>
    /// <exception cref="InvalidOperationException">No skill scope is attached — call <see cref="WithSkills(string)"/> first.</exception>
    public SkillAgentToolkit CreateSkillToolkit()
    {
        if (Skills is null)
            throw new InvalidOperationException($"{nameof(WithSkills)} must be called before {nameof(CreateSkillToolkit)}().");
        return new SkillAgentToolkit(Skills) { Language = _defaultLanguage };
    }

    /// <summary>The language the Agent's prompt is written in, set by <see cref="WithPromptLanguage"/>.</summary>
    public AgentLanguages PromptLanguage => _defaultLanguage;

    /// <summary>
    /// Attaches the MCP scope whose servers this Agent may use. Loaded server tools become part of the
    /// per-turn tool set, so loading or unloading a server takes effect on the next turn without
    /// rebuilding the agent.
    /// </summary>
    public WorkflowAgentScope WithMcps(McpScope mcp)
    {
        Mcp = mcp ?? throw new ArgumentNullException(nameof(mcp));

        // MCP self-service gates itself against the same confirmation handler the workflow tools use, so
        // an approval is configured once. The delegate resolves lazily, which keeps registration order in
        // the fluent chain irrelevant. A handler set directly on the MCP scope is replaced by this.
        mcp.WithConfirmationHandler(ResolveConfirmationAsync);
        // Same UI thread as the components: the MCP status list is meant to be bound by the host.
        mcp.WithSynchronizationContext(UIContext);
        // Composed with this scope's policy, so MCP-sourced tools join the same budgets and callbacks.
        // Without it the subsystem would fall back to marshalling only, and its calls would go uncounted.
        _mcpProvider = mcp.CreateContextProvider(SharedTools, Pipeline);

        BumpVersion();
        return this;
    }

    // ── 框架自带的能力 provider ─────────────────────────────────────────────
    // 这三个是 Agent Framework 自己提供的 AIContextProvider，本模块只是把它们接进组合里，
    // 不复制任何实现。挂上之后它们的工具直接进模型，不经 ToolPipeline。

    private AIContextProvider? _todoProvider;
    private AIContextProvider? _agentModeProvider;
    private AIContextProvider? _compactionProvider;

    /// <summary>
    /// The todo list the Agent keeps its own work in, once <see cref="WithTodoTracking"/> has attached it;
    /// <c>null</c> otherwise. Bind it to show the plan the model is working to.
    /// </summary>
    public TodoProvider? Todo { get; private set; }

    /// <summary>
    /// The operating mode the Agent is in, once <see cref="WithAgentModes"/> has attached it; <c>null</c>
    /// otherwise. A host reads and sets the mode through this to build a mode switch the user can drive.
    /// </summary>
    public AgentModeProvider? AgentMode { get; private set; }

    /// <summary>
    /// Puts the Agent Framework's todo list into play: the model gains its <c>todos_*</c> tools and is told
    /// on every turn what is still outstanding, so a task that outlives one context window keeps its plan.
    /// <para>
    /// Those tools come from the framework's own provider and are deliberately <b>not</b> wrapped the way
    /// the workflow tools are. They neither read nor write the tree, so there is nothing to marshal to the
    /// host thread, nothing to charge to a call budget and nothing to mark dirty — the same treatment
    /// <c>ResetToolCallLimit</c> gets, for the same reason.
    /// </para>
    /// </summary>
    /// <param name="options">Framework options for the todo provider; defaults are used when <c>null</c>.</param>
    /// <seealso cref="Todo"/>
    public WorkflowAgentScope WithTodoTracking(TodoProviderOptions? options = null)
    {
        Todo = new TodoProvider(options ?? new TodoProviderOptions());
        _todoProvider = Todo;
        BumpVersion();
        return this;
    }

    /// <summary>
    /// Puts the Agent Framework's operating modes into play: the model gains the <c>mode_set</c> and
    /// <c>mode_get</c> tools and is told how to behave in each mode. Which modes exist, and which one the
    /// Agent starts in, is the host's choice — see <see cref="AgentModeProviderOptions.Modes"/>.
    /// </summary>
    /// <param name="options">The modes and their instructions. Required: there is no useful default set.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
    /// <seealso cref="AgentMode"/>
    public WorkflowAgentScope WithAgentModes(AgentModeProviderOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));

        AgentMode = new AgentModeProvider(options);
        _agentModeProvider = AgentMode;
        BumpVersion();
        return this;
    }

    /// <summary>
    /// Bounds how much conversation the model carries. Once the history approaches the model's input budget
    /// the oldest tool results are summarised up, and past the next threshold the oldest turns are dropped —
    /// so a long editing session degrades instead of failing outright. Nothing happens while it fits.
    /// <para>
    /// The two numbers are facts about the host's model, not about this scope: the strategy divides the
    /// difference to decide when to act, so pass the real context window and output cap rather than a guess.
    /// </para>
    /// </summary>
    /// <param name="maxContextWindowTokens">The model's total context window, in tokens.</param>
    /// <param name="maxOutputTokens">The most the model may generate per response, in tokens.</param>
    public WorkflowAgentScope WithContextCompaction(int maxContextWindowTokens, int maxOutputTokens)
    {
        // 整个 Microsoft.Agents.AI.Compaction 命名空间都标着 [Experimental]（MAAI001）。把引用圈在这一处是为了
        // 让诊断只落在本库内：宿主用上面两个整数配置，自己永远不必写出那个实验类型名。
#pragma warning disable MAAI001
        _compactionProvider = new CompactionProvider(
            new ContextWindowCompactionStrategy(
                maxContextWindowTokens,
                maxOutputTokens,
                ContextWindowCompactionStrategy.DefaultToolEvictionThreshold,
                ContextWindowCompactionStrategy.DefaultTruncationThreshold),
            $"{nameof(CompactionProvider)}:{StateDiscriminator}",
            null);
#pragma warning restore MAAI001
        BumpVersion();
        return this;
    }

    /// <summary>
    /// Adds a factory for an additional context provider. Factories run once, from
    /// <see cref="CreateContextProviders"/>, in registration order; the framework chains the resulting
    /// providers so each sees the context built by the previous one.
    /// <para>
    /// Use this to contribute prompt text or tools of your own alongside the scope's. Do not contribute
    /// tool names the scope already provides — the framework unions tool lists without deduplicating.
    /// </para>
    /// </summary>
    /// <param name="factory">Receives this scope and returns a provider.</param>
    public WorkflowAgentScope WithContextProvider(Func<WorkflowAgentScope, AIContextProvider> factory)
    {
        if (factory is null) throw new ArgumentNullException(nameof(factory));
        _contextProviderFactories.Add(factory);
        return this;
    }

    /// <summary>
    /// Creates the scope's context provider — the object that renders the current skills, prompt text and
    /// full tool set on every invocation. Attach it through
    /// <c>ChatClientAgentOptions.AIContextProviders</c>.
    /// </summary>
    public AIContextProvider CreateContextProvider() => new WorkflowAgentContextProvider(this);

    /// <summary>
    /// Creates every context provider to attach: compaction when the host asked for it, then this scope's
    /// own, then one per attached subsystem, then one per factory registered with
    /// <see cref="WithContextProvider"/>. Pass the result to
    /// <c>ChatClientAgentOptions.AIContextProviders</c>.
    /// <para>
    /// The order is fixed rather than the order the host attached things in, because the framework
    /// concatenates what providers contribute — so the sequence of <c>WithSkills</c> and
    /// <c>WithMcps</c> calls would otherwise decide how the prompt reads.
    /// </para>
    /// </summary>
    public IReadOnlyList<AIContextProvider> CreateContextProviders()
    {
        var providers = new List<AIContextProvider>();

        // 压缩排最前：它重写的是消息历史，排在它之后的切片应该看到裁剪过的版本，而不是完整的。
        if (_compactionProvider is not null) providers.Add(_compactionProvider);

        providers.Add(CreateContextProvider());
        if (_skillProvider is not null) providers.Add(_skillProvider);
        if (_mcpProvider is not null) providers.Add(_mcpProvider);
        // 框架自带的行为脚手架排在上下文来源之后，与宿主挂载的先后无关：提示读起来始终是「先上下文、后指令」。
        if (_todoProvider is not null) providers.Add(_todoProvider);
        if (_agentModeProvider is not null) providers.Add(_agentModeProvider);
        foreach (var factory in _contextProviderFactories) providers.Add(factory(this));
        return providers;
    }

    // ── 能力包络：骨架之后每轮补的那一块 ──────────────────────────────────────
    //
    // 骨架（ProvideProgressiveContextPrompt）是「调用那一刻的快照」，宿主把它冻进 ChatOptions.Instructions
    // 之后就不再变。而本 scope 的 Version 会在二十多处配置变更时自增，那些变更全都只喂给那个冻住的字符串。
    // 于是模型会拿着过期的自我描述工作：宿主在 agent 造好之后调 WithInteractionSafety(1)，模型仍按旧挡位行事。
    //
    // 这一块补的就是那个差。它只讲「现在能做什么」，不讲框架是什么（骨架的事）、也不讲图长什么样
    // （ListNodes 渐进披露的事）。它必须由一个专用廉价渲染器产出 —— 骨架一次分配 870 KB，是框架每轮
    // 基线的十九倍，任何情况下都不能每轮跑。

    private readonly StringBuilder _envelopeScratch = new();
    private readonly object _envelopeGate = new();
    private long _envelopeKey = -1;
    private string? _envelope;

    /// <summary>
    /// What the skeleton was rendered from, or <c>null</c> until a skeleton is built. The envelope repairs
    /// only what has drifted since, which is what keeps a section from being said twice: the skeleton
    /// already carries the safety policy, the registered-type catalogue and the custom-tool guidance, and
    /// repeating any of them verbatim costs tokens and attention without adding a fact.
    /// </summary>
    private SkeletonReceipt? _skeletonReceipt;

    /// <summary>
    /// The state a built prompt captured. Every field records something that only ever grows or moves on a
    /// configuration change, so comparing a field against the live value answers "has this section gone
    /// stale?" without holding on to the 57 KB of text itself.
    /// </summary>
    private sealed class SkeletonReceipt
    {
        /// <summary>The language the skeleton was written in — the envelope's prose follows it.</summary>
        public AgentLanguages Language;

        /// <summary>The interaction-safety level whose policy the skeleton spelled out.</summary>
        public int SafetyLevel;

        /// <summary>Length of that level's host override, or -1 when it had none.</summary>
        public int SafetyOverrideLength;

        /// <summary>The output language the skeleton directed, or -1 when none was set.</summary>
        public int OutputLanguage;

        /// <summary>How much custom-tool guidance the skeleton carried.</summary>
        public int CustomToolPromptLength;

        /// <summary>The registered-type full names the skeleton listed, per category.</summary>
        public HashSet<string> Enums = [];
        public HashSet<string> Interfaces = [];
        public HashSet<string> Components = [];
        public HashSet<string> Data = [];
    }

    /// <summary>
    /// Records what the prompt just built carried, so the envelope can tell what has drifted.
    /// <para>
    /// This assumes the string this call returned is the one the model is given — which is how both callers
    /// are documented to be used (<c>ChatOptions.Instructions</c>). Rendering a skeleton for any other
    /// purpose (a preview pane, a log, a different agent) makes the envelope treat it as already delivered
    /// and stay quiet about the sections it contained. Attach the skeleton to the agent you are describing,
    /// or accept the envelope will consider its content stated.
    /// </para>
    /// </summary>
    private void CaptureSkeletonReceipt(AgentLanguages language)
    {
        _skeletonReceipt = new SkeletonReceipt
        {
            Language = language,
            SafetyLevel = _interactionSafety,
            SafetyOverrideLength = OverrideLengthFor(_interactionSafety),
            OutputLanguage = _outputLanguage.HasValue ? (int)_outputLanguage.Value : -1,
            CustomToolPromptLength = _customToolPrompt.Length,
            Enums = CollectTypeNames(CustomerEnums),
            Interfaces = CollectTypeNames(CustomerInterfaces),
            Components = CollectTypeNames(CustomerComponents),
            Data = CollectTypeNames(CustomerData),
        };
    }

    private static HashSet<string> CollectTypeNames(Dictionary<AgentLanguages, HashSet<Type>> source)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var set in source.Values)
            foreach (var t in set)
                names.Add(t.FullName ?? t.Name);
        return names;
    }

    private int OverrideLengthFor(int level)
        => _safetyPromptOverrides.TryGetValue(level, out var body) ? (body?.Length ?? 0) : -1;

    /// <summary>
    /// The key a per-turn render is cached against: <see cref="Version"/> plus the current budget-usage
    /// band. The band is the one fact that moves without a version bump — every tool call consumes one —
    /// and it is part of what the envelope says, so folding it in is what lets the provider hand back a
    /// single cached context while the model still hears about a budget that is filling up.
    /// </summary>
    internal long ContextKey => unchecked((Version << 3) | (uint)UsageBand());

    /// <summary>
    /// How close this run is to any of its call budgets, as 0–5: 0 while there is headroom, 4 once past 80%
    /// of a cap, 5 once a cap is spent. Bands rather than raw counts because a raw count changes on every
    /// tool call, which would make the rendered text differ every turn and put a string on the per-turn
    /// path for no new information — a warning is worth acting on, a counter ticking up is not.
    /// </summary>
    private int UsageBand()
    {
        var (toolCalls, readCalls, writeCalls) = CreateToolkit().CallUsage;
        return Math.Max(
            BandFor(toolCalls, MaxToolCalls),
            Math.Max(BandFor(readCalls, MaxReadToolCalls), BandFor(writeCalls, MaxWriteToolCalls)));
    }

    /// <summary>0–5 for one budget: 0 with headroom, 4 past 80% of the cap, 5 once the cap is spent.</summary>
    private static int BandFor(int used, int? cap)
    {
        if (!cap.HasValue || cap.Value <= 0) return 0;
        var percent = (int)((long)used * 100 / cap.Value);
        return Math.Min(5, percent / 20);
    }

    /// <summary>
    /// The prompt text this scope contributes on every invocation — the capability envelope.
    /// <para>
    /// Not the skeleton: that belongs to the host, in <c>ChatOptions.Instructions</c>, built once. This is
    /// only the part of the model's self-description that can change while it is running.
    /// </para>
    /// </summary>
    internal string? BuildDynamicInstructions()
    {
        var key = ContextKey;
        if (_envelopeKey == key) return _envelope;

        lock (_envelopeGate)
        {
            if (_envelopeKey != key)
            {
                _envelope = RenderEnvelope();
                _envelopeKey = key;
            }
            return _envelope;
        }
    }

    private string RenderEnvelope()
    {
        var sb = _envelopeScratch;
        sb.Clear();

        var language = _skeletonReceipt?.Language ?? _defaultLanguage;
        var chinese = language == AgentLanguages.Chinese;

        sb.AppendLine(chinese ? "## 当前能力状态（每轮实时渲染）" : "## Current Capability State (rendered live, every turn)");
        sb.AppendLine();
        sb.AppendLine(chinese
            ? "> 上面那段提示词是启动时的快照。以下是**现在**的实际情况；若两者冲突，以这里为准。"
            : "> The prompt above was a snapshot taken at startup. Below is how things stand **now**; where the two disagree, this section wins.");
        sb.AppendLine();

        AppendGates(sb, language);
        AppendToolSwitches(sb, language);
        AppendBudgets(sb, chinese);

        var receipt = _skeletonReceipt;
        if (receipt is null)
        {
            // No skeleton was ever built, so nothing above has stated any of this and there is nothing to
            // supersede — every section is emitted plainly.
            AppendInteractionSafetyPolicy(sb, language, supersedesLevel: null);
            AppendOutputLanguageDirective(sb, language, supersedes: false);
            AppendCustomToolGuidance(sb, language, fromLength: 0);
            AppendRegisteredTypeDelta(sb, language, new SkeletonReceipt());
        }
        else
        {
            if (receipt.SafetyLevel != _interactionSafety
                || receipt.SafetyOverrideLength != OverrideLengthFor(_interactionSafety))
                AppendInteractionSafetyPolicy(sb, language, supersedesLevel: receipt.SafetyLevel);

            var outputLanguage = _outputLanguage.HasValue ? (int)_outputLanguage.Value : -1;
            if (receipt.OutputLanguage != outputLanguage)
                AppendOutputLanguageDirective(sb, language, supersedes: receipt.OutputLanguage >= 0);

            if (receipt.CustomToolPromptLength != _customToolPrompt.Length)
                AppendCustomToolGuidance(sb, language, fromLength: receipt.CustomToolPromptLength);

            AppendRegisteredTypeDelta(sb, language, receipt);
        }

        var text = sb.ToString();
        sb.Clear(); // Hand the buffer back empty; this instance is reused on the next render.
        return text;
    }

    private void AppendGates(StringBuilder sb, AgentLanguages language)
    {
        var chinese = language == AgentLanguages.Chinese;
        sb.AppendLine(chinese ? "### 当前生效的闸门" : "### Gates in force");
        sb.AppendLine();
        sb.AppendLine(chinese
            ? $"- 运行节点业务代码（`ExecuteNode` / `ExecuteNodes` / `BroadcastNode` / `ReverseBroadcastNode`）：**{(AllowNodeExecution ? "允许" : "禁止")}**"
            : $"- Running node business code (`ExecuteNode` / `ExecuteNodes` / `BroadcastNode` / `ReverseBroadcastNode`): **{(AllowNodeExecution ? "ALLOWED" : "DENIED")}**");

        sb.AppendLine(chinese
            ? $"- 自动标脏：**{(AutoMarkDirty ? "开启 —— 每次变更调用后框架自动标脏，不要调用 `MarkDirty`" : "关闭 —— 变更任务结束时你自己调用一次 `MarkDirty`")}**"
            : $"- Automatic dirty marking: **{(AutoMarkDirty ? "ON — the framework marks dirty after every mutating call; do not call `MarkDirty`" : "OFF — call `MarkDirty` yourself, exactly once, at the end of a mutation task")}**");

        var commands = AllowedGenericCommands;
        var generic = chinese
            ? "- 泛型命令（`ExecuteCommandOnNode` / `ExecuteCommandById`）："
            : "- Generic commands (`ExecuteCommandOnNode` / `ExecuteCommandById`): ";
        sb.AppendLine(generic + (commands.Count > 0
            ? (chinese ? $"仅限 {string.Join("、", commands.OrderBy(c => c, StringComparer.Ordinal))}"
                       : $"restricted to {string.Join(", ", commands.OrderBy(c => c, StringComparer.Ordinal))}")
            : (chinese ? "**完全禁用**（未加入任何白名单）" : "**disabled entirely** (nothing is allowlisted)")));
        sb.AppendLine();
    }

    private void AppendToolSwitches(StringBuilder sb, AgentLanguages language)
    {
        var chinese = language == AgentLanguages.Chinese;
        var disabled = DisabledToolNames;

        sb.AppendLine(chinese ? "### 被宿主关掉的工具" : "### Tools switched off by the host");
        sb.AppendLine();
        if (disabled.Count == 0)
        {
            sb.AppendLine(chinese
                ? "全部可用 —— 没有任何工具被关闭。"
                : "None — every registered tool is available.");
        }
        else
        {
            sb.AppendLine(chinese
                ? "下列工具**不会被提供**给你。不要尝试调用它们，也不要假设它们失败是暂时的："
                : "These are **not offered** to you. Do not attempt to call them, and do not treat their absence as temporary:");
            foreach (var name in disabled.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                sb.AppendLine($"- `{name}`");
        }
        sb.AppendLine();
    }

    private void AppendBudgets(StringBuilder sb, bool chinese)
    {
        var (toolCalls, readCalls, writeCalls) = CreateToolkit().CallUsage;

        sb.AppendLine(chinese ? "### 调用预算" : "### Call budgets");
        sb.AppendLine();
        AppendBudgetLine(sb, chinese, chinese ? "工具调用" : "Tool calls", toolCalls, MaxToolCalls);
        AppendBudgetLine(sb, chinese, chinese ? "只读（查询）调用" : "Read-only (query) calls", readCalls, MaxReadToolCalls);
        AppendBudgetLine(sb, chinese, chinese ? "变更调用" : "Mutating calls", writeCalls, MaxWriteToolCalls);
        sb.AppendLine();
    }

    /// <summary>
    /// One budget line. The usage figure appears only once the band is non-zero — below that the model has
    /// nothing to act on, and leaving it out is what keeps an idle turn from rendering different text.
    /// </summary>
    private static void AppendBudgetLine(StringBuilder sb, bool chinese, string label, int used, int? cap)
    {
        if (!cap.HasValue)
        {
            sb.AppendLine(chinese ? $"- {label}：无上限" : $"- {label}: no cap");
            return;
        }

        var percent = cap.Value > 0 ? (int)((long)used * 100 / cap.Value) : 0;
        if (percent < 80)
        {
            sb.AppendLine(chinese ? $"- {label}：上限 {cap.Value}" : $"- {label}: cap {cap.Value}");
            return;
        }

        var state = percent >= 100
            ? (chinese ? "**已用尽 —— 在重置前不会再接受任何调用**" : "**spent — no further calls are accepted until it is reset**")
            : (chinese ? "**接近上限**" : "**near the limit**");
        sb.AppendLine(chinese
            ? $"- {label}：{used}/{cap.Value}，{state}"
            : $"- {label}: {used}/{cap.Value}, {state}");
    }

    private void AppendInteractionSafetyPolicy(StringBuilder sb, AgentLanguages language, int? supersedesLevel)
    {
        var chinese = language == AgentLanguages.Chinese;

        // Level 0 means no policy at all, and saying so is not the same as saying nothing: a skeleton
        // written at level 1+ still carries a policy the model will keep obeying unless it is told the
        // policy was withdrawn. An empty "the following replaces it" would be worse than silence.
        if (_interactionSafety == 0)
        {
            sb.AppendLine(chinese
                ? "### 交互安全策略已撤销（取代上文）"
                : "### Interaction safety policy withdrawn (replaces the section above)");
            sb.AppendLine();
            sb.AppendLine(chinese
                ? "> 上文那份交互安全策略**不再适用** —— 宿主已撤销它。"
                : "> The interaction safety policy above **no longer applies** — the host has withdrawn it.");
            sb.AppendLine();
            return;
        }

        if (supersedesLevel.HasValue)
        {
            sb.AppendLine(chinese
                ? $"### 安全策略已变更（取代上文为第 {supersedesLevel.Value} 挡渲染的那一份）"
                : $"### Interaction safety policy changed (replaces the one above, written for level {supersedesLevel.Value})");
            sb.AppendLine();
            sb.AppendLine(chinese
                ? "> 上文那份策略已经失效，**以下取代它**。"
                : "> The policy above no longer applies — **the following replaces it**.");
            sb.AppendLine();
        }

        sb.AppendLine(BuildInteractionSafetyPrompt(language));
    }

    private void AppendOutputLanguageDirective(StringBuilder sb, AgentLanguages language, bool supersedes)
    {
        if (_outputLanguage is null) return;

        var chinese = language == AgentLanguages.Chinese;
        if (supersedes)
        {
            sb.AppendLine(chinese
                ? "### 输出语言已变更（取代上文的 Output Language 小节）"
                : "### Output language changed (replaces the Output Language section above)");
            sb.AppendLine();
        }
        AppendOutputLanguageDirective(sb);
        sb.AppendLine();
    }

    private void AppendCustomToolGuidance(StringBuilder sb, AgentLanguages language, int fromLength)
    {
        if (_customToolPrompt.Length <= fromLength) return;

        var chinese = language == AgentLanguages.Chinese;
        sb.AppendLine(chinese
            ? "### 新注册工具的使用说明（上文 Custom Tools 小节之后新增的部分）"
            : "### Guidance for tools registered since startup (the part after the Custom Tools section above)");
        sb.AppendLine();
        sb.AppendLine(_customToolPrompt.ToString(fromLength, _customToolPrompt.Length - fromLength).TrimEnd());
        sb.AppendLine();
    }

    /// <summary>
    /// Types registered after the skeleton was built. The catalogue in the skeleton names every type the
    /// model may meet, and a type registered later would otherwise be invisible until the agent stumbles on
    /// it — <c>GetComponentContext</c> answers 404 for a type it was never told about.
    /// </summary>
    private void AppendRegisteredTypeDelta(StringBuilder sb, AgentLanguages language, SkeletonReceipt receipt)
    {
        var added = new List<string>();
        CollectAdded(added, CustomerEnums, receipt.Enums, language, "enum");
        CollectAdded(added, CustomerInterfaces, receipt.Interfaces, language, "interface");
        CollectAdded(added, CustomerComponents, receipt.Components, language, "component");
        CollectAdded(added, CustomerData, receipt.Data, language, "data");
        if (added.Count == 0) return;

        sb.AppendLine(language == AgentLanguages.Chinese
            ? "### 启动后新注册的类型"
            : "### Types registered since startup");
        sb.AppendLine();
        sb.AppendLine(language == AgentLanguages.Chinese
            ? "> 上文清单是启动时的快照。以下类型在那之后加入，现在同样可用。"
            : "> The catalogue above was a startup snapshot. These were added afterwards and are available now.");
        sb.AppendLine();
        foreach (var line in added) sb.AppendLine(line);
        sb.AppendLine();
    }

    private static void CollectAdded(
        List<string> into, Dictionary<AgentLanguages, HashSet<Type>> source,
        HashSet<string> alreadyListed, AgentLanguages language, string kind)
    {
        var label = language == AgentLanguages.Chinese ? "客户" : "customer";
        foreach (var set in source.Values)
            foreach (var t in set)
            {
                var name = t.FullName ?? t.Name;
                if (alreadyListed.Contains(name)) continue;
                into.Add($"- `{name}` ({label} {kind})");
            }
    }

    /// <summary>
    /// The tools this scope offers on every invocation: the built-in workflow tools and the
    /// developer-registered ones, all wrapped so they obey the shared policy.
    /// <para>
    /// A subsystem's tools are <b>not</b> included. Each subsystem contributes its own, which is what
    /// keeps the subsystems usable on their own and keeps this list from having to know about them.
    /// </para>
    /// </summary>
    internal IReadOnlyList<AITool> BuildDynamicTools()
        => [.. CreateToolkit().CreateTools()];

    /// <summary>Advances <see cref="Version"/>, invalidating any provider render cached against it.</summary>
    private void BumpVersion()
    {
        Interlocked.Increment(ref _version);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Appends the library's embedded skill corpus to a statically built prompt — unless skills are under
    /// dynamic management, where the skill subsystem's own provider renders them per turn and appending
    /// them here as well would duplicate the whole corpus.
    /// <para>
    /// Which of the two happens is decided here, by whether a skill scope was attached <i>at this moment</i>
    /// — but the string this went into is one a host freezes into the agent's instructions, so the decision
    /// is not reversible by a later <c>WithSkills</c>. That case is recorded rather than ignored; see
    /// <see cref="AttachSkillProvider"/>.
    /// </para>
    /// </summary>
    private void AppendEmbeddedSkills(StringBuilder result, AgentLanguages language)
    {
        if (Skills is not null) return;
        _embeddedSkillsFrozenIntoPrompt = true;
        result.AppendLine(AgentEmbeddedResources.ReadAllSkills(SystemName, language).TrimEnd());
        result.AppendLine();
    }

    private void AppendPreloadedComponentSummaries(StringBuilder result, AgentLanguages language)
    {
        void AppendSummary(Type t, AgentLanguages lang)
        {
            var classContexts = AgentContextCollector.GetAgentContext(t, lang);
            var summary = classContexts.Length > 0 ? string.Join(" | ", classContexts) : "*no developer instructions*";
            result.AppendLine($"- `{t.FullName}` — {summary}");
        }

        foreach (var kvp in CustomerEnums)
            foreach (var t in kvp.Value) AppendSummary(t, kvp.Key);
        foreach (var kvp in CustomerInterfaces)
            foreach (var t in kvp.Value) AppendSummary(t, kvp.Key);
        foreach (var kvp in CustomerComponents)
            foreach (var t in kvp.Value) AppendSummary(t, kvp.Key);
        foreach (var kvp in CustomerData)
            foreach (var t in kvp.Value) AppendSummary(t, kvp.Key);
    }

    /// <summary>
    /// All mode: full context for framework-built-in data types (Anchor, Offset, Size).
    /// </summary>
    public string ProvideFrameworkDataContext(AgentLanguages language = AgentLanguages.English)
    {
        var result = new StringBuilder();
        foreach (var t in FrameworkData)
            result.AppendLine(AgentContextCollector.GetDataContext(t, language));
        return result.ToString();
    }

    /// <summary>
    /// All mode: full context for developer-registered data types.
    /// </summary>
    public string ProvideCustomerDataContext(AgentLanguages language = AgentLanguages.English)
    {
        var result = new StringBuilder();
        foreach (var kvp in CustomerData)
            foreach (var t in kvp.Value)
                result.AppendLine(AgentContextCollector.GetDataContext(t, kvp.Key));
        return result.ToString();
    }
}
