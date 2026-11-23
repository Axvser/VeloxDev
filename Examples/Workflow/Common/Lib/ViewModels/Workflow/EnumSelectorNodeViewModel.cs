using Demo.ViewModels.Workflow.Helper;
using System.ComponentModel;
using VeloxDev.AI;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels;

[WorkflowBuilder.Node<EnumSelectorHelper>(workSemaphore: 1)]
[DefaultSize(280, 380)]
public partial class EnumSelectorNodeViewModel : ICompileTimeRouter, ICompileTimeAware
{
    public EnumSelectorNodeViewModel()
    {
        InitializeWorkflow();
        OutputSlots.SetSelector(typeof(VoltageRange));
    }

    [VeloxProperty] public partial SlotViewModel InputSlot { get; set; }

    [VeloxProperty]
    [SlotSelectors(typeof(VoltageRange))]
    public partial SlotEnumerator<SlotViewModel> OutputSlots { get; set; }

    partial void OnOutputSlotsChanged(SlotEnumerator<SlotViewModel>? oldValue, SlotEnumerator<SlotViewModel>? newValue)
    {
        oldValue?.PropertyChanged -= OnOutputSlotsPropertyChanged;
        newValue?.PropertyChanged += OnOutputSlotsPropertyChanged;
        OnPropertyChanged(nameof(EnumType));
        OnPropertyChanged(nameof(EnumValues));
        OnPropertyChanged(nameof(SelectedValue));
    }

    private void OnOutputSlotsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // SelectorTypeName fires after SelectorType (SlotEnumerator sets the type first),
        // so by the time this fires, EnumValues already reads the new selector type.
        if (e.PropertyName == nameof(SlotEnumerator<>.SelectorTypeName))
        {
            OnPropertyChanged(nameof(EnumType));
            OnPropertyChanged(nameof(EnumValues));
        }
        // CurrentValue is now owned by the enumerator; sync it here to the node's SelectedValue binding.
        // UI frameworks (WPF/Avalonia) drop a re-bound selection in the same frame the ItemsSource swaps arrays:
        // item containers are not yet generated for the new source, the string match fails → it shows as empty.
        // Defer one frame so ItemsSource lands and generates items first, then re-bind the selected value.
        if (e.PropertyName == nameof(SlotEnumerator<>.CurrentValue))
        {
            var sc = SynchronizationContext.Current;
            if (sc is not null)
                sc.Post(_ => OnPropertyChanged(nameof(SelectedValue)), null);
            else
                OnPropertyChanged(nameof(SelectedValue));
        }
    }

    [VeloxProperty] private string title = "Enum Selector";

    [VeloxProperty] private bool autoBroadcast = true;

    public object? SelectedValue
    {
        get => OutputSlots?.CurrentValue;
        set
        {
            // 卡片被复用/重新实例化时，ComboBox 的 SelectedItem 会因 ItemsSource 换了一份而复位，并经 TwoWay
            // 把 null 写回来源 —— 那是控件自己的复位，不是用户清空选择。忽略它，再把真值推回目标；否则
            // Static 模式在编译时拿不到分支键，整条下游都会被剪成 Order = -1。
            if (OutputSlots is null) return;
            if (value is null)
            {
                OnPropertyChanged(nameof(SelectedValue));
                return;
            }
            OutputSlots.CurrentValue = value;
        }
    }

    [VeloxProperty] private string lastRouted = "-";

    // Execution sequence number (hand-written; the generator does not cover this yet)
    private int lastExecutionOrder;
    public int LastExecutionOrder
    {
        get => lastExecutionOrder;
        set
        {
            if (lastExecutionOrder == value) return;
            lastExecutionOrder = value;
            OnPropertyChanged(nameof(LastExecutionOrder));
            OnPropertyChanged(nameof(HasExecutionOrder));
            OnPropertyChanged(nameof(ExecutionOrderText));
        }
    }
    public bool HasExecutionOrder => LastExecutionOrder > 0 || IsCompileStopped;
    public string ExecutionOrderText => IsCompileStopped ? "⊘" : LastExecutionOrder > 0 ? $"#{LastExecutionOrder}" : "-";

    public bool HasInputSlot => _inputSlot is not null;

    public Type? EnumType => OutputSlots?.SelectorType;

    public string[] EnumValues
    {
        get
        {
            var t = EnumType;
            if (t == null) return [];
            if (t == typeof(bool)) return ["False", "True"];
            return Enum.GetNames(t);
        }
    }

    public SlotViewModel? GetSlotForValue(object value)
    {
        var key = OutputSlots?.NormalizeSelectorValue(value);
        return key is not null && OutputSlots?.TrySelect(key, out var slot) == true ? slot : null;
    }

    public string GetSlotLabel(int index)
    {
        var items = OutputSlots?.Items;
        if (items == null || index < 0 || index >= items.Count) return "?";
        return items[index].Slot?.ToString() ?? "?";
    }

    /// <summary>Compile-time identity injected by the compiler (Order = -1 means absolute stop).</summary>
    public ICompileContext? CompileContext { get; private set; }

    /// <summary>Whether the node is in the compile-time absolute stop state (unselected static branch / terminated).</summary>
    public bool IsCompileStopped => CompileContext is { Order: -1 };

    /// <inheritdoc/>
    public void AttachCompileTimeContext(ICompileContext context)
    {
        CompileContext = context;
        LastExecutionOrder = context.Order >= 0 ? context.Order + 1 : 0;
        OnPropertyChanged(nameof(CompileContext));
        OnPropertyChanged(nameof(IsCompileStopped));
        OnPropertyChanged(nameof(HasExecutionOrder));
        OnPropertyChanged(nameof(ExecutionOrderText));
    }

    /// <summary>
    /// Compile mode: Static returns only the currently selected branch at compile time; Dynamic returns all branches (key decided at runtime).
    /// The dictionary returned by <see cref="GetRouteTable"/> differs by mode.
    /// </summary>
    [VeloxProperty] private RouterCompileMode _compileMode = RouterCompileMode.Dynamic;

    /// <summary>Options for the compile-mode dropdown.</summary>
    public RouterCompileMode[] CompileModeOptions => [RouterCompileMode.Static, RouterCompileMode.Dynamic];

    private static readonly string[] CompileModeNamesCache = [nameof(RouterCompileMode.Static), nameof(RouterCompileMode.Dynamic)];

    /// <summary>String form of the compile-mode options, for dropdowns whose SelectedItem is bound two-way.</summary>
    public string[] CompileModeNames => CompileModeNamesCache;

    /// <summary>
    /// String-form compile mode for two-way dropdowns.
    /// </summary>
    /// <remarks>
    /// A ComboBox resets its selection when its ItemsSource is swapped (recycled card, new DataContext) and writes
    /// null back through the two-way SelectedItem binding. Bound straight to <see cref="CompileMode"/>, that null
    /// converts to <c>default</c> = <c>Static</c> — the selector then compiles with no branch key and prunes every
    /// downstream node. This property drops the null instead and pushes the real value back to the control.
    /// </remarks>
    public string CompileModeName
    {
        get => CompileMode.ToString();
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                OnPropertyChanged(nameof(CompileModeName));
                return;
            }

            if (Enum.TryParse<RouterCompileMode>(value, out var mode) && mode != CompileMode)
            {
                CompileMode = mode;
            }
        }
    }

    partial void OnCompileModeChanged(RouterCompileMode oldValue, RouterCompileMode newValue)
        => OnPropertyChanged(nameof(CompileModeName));

    /// <summary>
    /// Unified route-key entry point:
    /// - Static: the key is decided by the currently selected enum value (decidable at compile time);
    /// - Dynamic: a null compile-time payload returns null → IsDynamic; at runtime reads the shared "selector.value" field, else falls back to the currently selected value.
    /// </summary>
    /// <inheritdoc/>
    public Task<object?> ResolveRouteKey(object? payload)
    {
        if (CompileMode == RouterCompileMode.Dynamic && payload is null)
            return Task.FromResult<object?>(null);

        if (payload is IRuntimeContext ctx)
        {
            // Explicit shared variable wins (legacy stateless broadcast).
            if (ctx.TryGet("selector.value", out var v) && v is string s)
                return Task.FromResult(OutputSlots is not null ? OutputSlots.NormalizeSelectorValue(s) : s);

            // Otherwise, when the payload carries per-member 0/1 flags (e.g. a python node emitting High/Low/Zero),
            // route by whichever flag is set — lets the voltage demo's computed grade drive the branch.
            if (OutputSlots is not null && ctx.Data is IDictionary<string, object?> data)
            {
                foreach (var item in OutputSlots.Items)
                {
                    var key = item.Value?.ToString();
                    if (key is not null && data.TryGetValue(key, out var flag) && IsTruthyFlag(flag))
                        return Task.FromResult(OutputSlots.NormalizeSelectorValue(item.Value!));
                }
            }
        }

        return Task.FromResult(OutputSlots is not null
            ? OutputSlots.NormalizeSelectorValue(OutputSlots.CurrentValue)
            : null);
    }

    /// <summary>Whether a payload field is a nonzero 0/1 style flag (long/int/bool/numeric string).</summary>
    private static bool IsTruthyFlag(object? flag) => flag switch
    {
        long l => l != 0,
        int i => i != 0,
        bool b => b,
        string s => long.TryParse(s, out var n) && n != 0,
        _ => false,
    };

    /// <summary>Compile-time route table (changes with mode): Static contains only the currently selected branch; Dynamic contains all branches (preserving 1:N fan-out).</summary>
    /// <inheritdoc/>
    public Task<IReadOnlyDictionary<object, IReadOnlyList<INodeComponent>>> GetRouteTable()
    {
        var dict = new Dictionary<object, List<INodeComponent>>();
        if (OutputSlots is null)
            return Task.FromResult(EmptyRouteTable());

        if (CompileMode == RouterCompileMode.Static)
        {
            // Static: return only the currently selected branch (register as terminal branch if it has no downstream)
            var key = OutputSlots.NormalizeSelectorValue(OutputSlots.CurrentValue);
            var slot = key is not null && OutputSlots.TrySelect(key, out var s) ? s : null;
            if (slot is not null)
            {
                if (slot.Targets.Count == 0)
                {
                    if (key is not null && !dict.ContainsKey(key)) dict[key] = [];
                }
                else
                {
                    foreach (var target in slot.Targets)
                        if (target.Parent is not null)
                            AddTarget(dict, key!, target.Parent);
                }
            }
        }
        else
        {
            // Dynamic: all branches (terminal branches without downstream are registered as empty lists)
            foreach (var item in OutputSlots.Items)
            {
                var slot = item.Slot;
                if (item.Value is null || slot is null) continue;
                if (slot.Targets.Count == 0)
                {
                    if (!dict.ContainsKey(item.Value)) dict[item.Value] = [];
                    continue;
                }
                foreach (var target in slot.Targets)
                    if (target.Parent is not null)
                        AddTarget(dict, item.Value, target.Parent);
            }
        }

        return Task.FromResult<IReadOnlyDictionary<object, IReadOnlyList<INodeComponent>>>(
            dict.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<INodeComponent>)kv.Value.AsReadOnly()));
    }

    private static void AddTarget(Dictionary<object, List<INodeComponent>> dict, object key,
        INodeComponent target)
    {
        if (!dict.TryGetValue(key, out var list))
            dict[key] = list = [];
        if (!list.Contains(target))
            list.Add(target);
    }

    private static IReadOnlyDictionary<object, IReadOnlyList<INodeComponent>> EmptyRouteTable()
        => new Dictionary<object, IReadOnlyList<INodeComponent>>();
}
