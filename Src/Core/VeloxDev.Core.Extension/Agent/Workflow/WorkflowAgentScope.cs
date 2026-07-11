using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using VeloxDev.AI;
using VeloxDev.AI.Workflow.Functions;
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
        [typeof(SlotChannel), typeof(SlotState)];

    internal static bool IsFrameworkEnum(Type t) => FrameworkEnums.Contains(t);

    private static readonly Type[] FrameworkInterfaces =
        [typeof(IWorkflowTreeViewModel), typeof(IWorkflowNodeViewModel), typeof(IWorkflowSlotViewModel), typeof(IWorkflowLinkViewModel), typeof(IWorkflowViewModel)];

    private static readonly Type[] FrameworkComponents =
        [typeof(TreeViewModelBase), typeof(NodeViewModelBase), typeof(SlotViewModelBase), typeof(LinkViewModelBase)];

    internal static readonly Type[] FrameworkData =
        [typeof(Anchor), typeof(Offset), typeof(Size)];

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
    private readonly StringBuilder _customToolPrompt = new();

    private AgentLanguages _defaultLanguage = AgentLanguages.English;
    private AgentLanguages? _outputLanguage;

    /// <summary>
    /// Tools registered by the developer via <see cref="WithTools"/>.
    /// </summary>
    internal IReadOnlyList<AITool> CustomTools => _customTools;

    /// <summary>
    /// Sets the global default language used when a per-call <c>language</c> argument is <c>null</c>.
    /// Call this once at the start of the fluent chain before any <c>With*</c> registration.
    /// </summary>
    public WorkflowAgentScope WithPromptLanguage(AgentLanguages language)
    {
        _defaultLanguage = language;
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
        return this;
    }

    public WorkflowAgentScope WithMaxToolCalls(int maxCalls)
    {
        MaxToolCalls = maxCalls;
        return this;
    }

    /// <summary>
    /// Registers custom <see cref="AITool"/> instances that will be merged into the
    /// tool list returned by <see cref="ProvideTools"/>. Use <paramref name="promptContext"/> to inject
    /// additional instructions into the system prompt so the Agent knows when and how to use them.
    /// Pass <c>null</c> if the tool metadata (name + description) is self-explanatory.
    /// </summary>
    /// <param name="promptContext">Optional prompt text describing custom tools.</param>
    /// <param name="tools">One or more <see cref="AITool"/> instances.</param>
    public WorkflowAgentScope WithTools(string? promptContext, params AITool[] tools)
    {
        _customTools.AddRange(tools);
        if (!string.IsNullOrWhiteSpace(promptContext))
        {
            _customToolPrompt.AppendLine(promptContext);
        }
        return this;
    }

    /// <summary>
    /// Configures whether every mutation tool call automatically marks the workflow tree as dirty.
    /// When enabled, the Agent may rely on framework-managed dirty marking.
    /// When disabled (the default), the framework does not auto-mark dirty and the Agent is not instructed to call <c>MarkDirty</c>.
    /// </summary>
    public WorkflowAgentScope WithAutoMarkDirty(bool enabled = false)
    {
        AutoMarkDirty = enabled;
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

        // Pass 1: register components and [AgentContext] data types
        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface) continue;
            if (!_globallyDiscoveredTypes.Add(type)) continue; // already discovered by a prior call

            bool isWorkflowComponent = nodeBase.IsAssignableFrom(type)
                || slotBase.IsAssignableFrom(type)
                || linkBase.IsAssignableFrom(type)
                || treeBase.IsAssignableFrom(type);

            if (isWorkflowComponent)
                WithComponents([type], lang);
            else if (type.GetCustomAttributes<AgentContextAttribute>().Any())
                WithData([type], lang);
        }

        // Pass 2: deep-scan every registered component to infer Enums / Interfaces / Data
        var registeredComponents = CustomerComponents.TryGetValue(lang, out var cs) ? cs : (IEnumerable<Type>)[];
        foreach (var type in registeredComponents.ToArray())
            ScanComponentMembers(type, lang, workflowBase);

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
        return ns.StartsWith("System") || ns.StartsWith("Microsoft") || ns.StartsWith("VeloxDev.WorkflowSystem");
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

        // ── Interaction Safety Policy ──
        result.AppendLine(BuildInteractionSafetyPrompt(language));

        // ── Built-in Skills ──
        result.AppendLine(AgentEmbeddedResources.ReadAllSkills(SystemName, language).TrimEnd());
        result.AppendLine();
        AppendOutputLanguageDirective(result);
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
        result.AppendLine("## ⚠ Critical Behavioral Constraints");
        result.AppendLine();
        result.AppendLine("These constraints apply to ALL operations. Violations cause SILENT failures (no error, no effect).");
        result.AppendLine();
        result.AppendLine("### 1. Mount-before-operate");
        result.AppendLine("Nodes MUST be added to the Tree (via `CreateNode` / `CloneNodes`) before any operation on their internals.");
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
        result.AppendLine("Node indices shift after `CreateNode`, `DeleteNode`, `DeleteSlot`, `CloneNodes`. Always refresh after structural changes.");
        result.AppendLine("Runtime IDs are stable for the lifetime of a component, except SlotEnumerator slots which are rebuilt on selector change.");
        result.AppendLine();
        result.AppendLine("### 5. Patch restrictions");
        result.AppendLine("NEVER patch: `Parent`, `Nodes`, `Links`, `Slots`, `Targets`, `Sources`, `State`, `RuntimeId`, `Helper`.");
        result.AppendLine("Properties backed by commands (Anchor → SetAnchorCommand) must use dedicated tools (`SetNodePosition`, `ResizeNode`, `SetSlotChannel`).");
        result.AppendLine("Slot-typed properties are auto-created by the source generator — do NOT assign or patch them.");
        result.AppendLine("`[SlotSelectors]`-marked properties must use `SetEnumSlotCollection`, not `PatchNodeProperties`.");
        result.AppendLine();
        result.AppendLine("### 6. Prefer mutate in-place; fall back to delete+recreate when needed");
        result.AppendLine("ALWAYS prefer in-place mutation over delete+recreate. Deleting a node destroys its identity, all slots, and all connections.");
        result.AppendLine("The following changes are all possible **without** deleting the node:");
        result.AppendLine("- Change properties → `PatchNodeProperties` / `PatchComponentById`");
        result.AppendLine("- Change SlotEnumerator selector type → `SetEnumSlotCollection` (on the existing node)");
        result.AppendLine("- Add/remove slots → `AddSlotToCollection` / `RemoveSlotFromCollection` / `CreateSlotOnNode`");
        result.AppendLine("- Resize → `ResizeNode`");
        result.AppendLine("- Reposition → `SetNodePosition` / `MoveNode`");
        result.AppendLine("- Reconnect → `ConnectSlots` / `DisconnectSlots` / `ReplaceConnection`");
        result.AppendLine("Only use `DeleteNode` / `DeleteNodes` when the user explicitly asks to remove a node, or when in-place mutation is genuinely impossible (e.g. the node type itself must change). If a patch is rejected, first try the suggested alternative tool; if that also fails, delete+recreate is acceptable as a last resort.");
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
        result.AppendLine("## 📋 Pre-loaded Component Descriptions (from [AgentContext])");
        result.AppendLine();
        result.AppendLine("The following developer instructions are **AUTHORITATIVE** for each customer component type.");
        result.AppendLine("They define intended usage and semantics. Treat them as ground truth.");
        result.AppendLine("Call **GetComponentContext** with any type name to retrieve the full property/command table on demand.");
        result.AppendLine();
        AppendPreloadedComponentSummaries(result, language);
        result.AppendLine();
        result.AppendLine("> Call **GetComponentContext** with any type name above to get full documentation including all properties and commands.");
        result.AppendLine();

        // ── Interaction Safety Policy ──
        result.AppendLine(BuildInteractionSafetyPrompt(language));

        // ── Built-in Skills ──
        result.AppendLine(AgentEmbeddedResources.ReadAllSkills(SystemName, language).TrimEnd());
        result.AppendLine();
        AppendOutputLanguageDirective(result);
        return result.ToString();
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

    /// <summary>
    /// Creates a <see cref="WorkflowAgentToolkit"/> that provides MAF-compatible
    /// <see cref="AITool"/> instances for full operational control over the scoped tree.
    /// </summary>
    public WorkflowAgentToolkit CreateToolkit() => new(this);

    /// <summary>
    /// Convenience method: creates the toolkit and returns all tools ready for use
    /// with <c>ChatOptions.Tools</c> or <c>AsAIAgent(tools: ...)</c>.
    /// </summary>
    public IList<AITool> ProvideTools() => CreateToolkit().CreateTools();

    private void AppendPreloadedComponentSummaries(StringBuilder result, AgentLanguages language)
    {
        void AppendSummary(Type t, AgentLanguages lang)
        {
            var classContexts = AgentContextCollector.GetAgentContext(t, lang);
            result.AppendLine($"### `{t.FullName}`");
            result.AppendLine();
            if (classContexts.Length > 0)
            {
                result.AppendLine("**Developer Instructions:**");
                foreach (var ctx in classContexts)
                    result.AppendLine($"- {ctx}");
            }
            else
            {
                result.AppendLine("*(no developer instructions)*");
            }
            result.AppendLine();
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
