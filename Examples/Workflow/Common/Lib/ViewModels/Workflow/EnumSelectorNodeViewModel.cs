using Demo.ViewModels.Workflow.Helper;
using System.ComponentModel;
using VeloxDev.AI;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels;

[AgentContext(AgentLanguages.Chinese, "枚举选择器节点，可将输入按枚举成员路由到多个执行路径。默认大小为 280×380。")]
[AgentContext(AgentLanguages.English, "Enum selector node that routes input to multiple execution paths based on enum members. Default size: 280×380.")]
[WorkflowBuilder.Node<EnumSelectorHelper>(workSemaphore: 1)]
public partial class EnumSelectorNodeViewModel : ICompileTimeRouter, ICompileTimeAware
{
    public EnumSelectorNodeViewModel()
    {
        InitializeWorkflow();
        OutputSlots.SetSelector(typeof(NetworkRequestMethod));
    }

    [AgentContext(AgentLanguages.Chinese, "输入口（接收端）")]
    [AgentContext(AgentLanguages.English, "Input slot (receiver)")]
    [VeloxProperty] public partial SlotViewModel InputSlot { get; set; }

    [AgentContext(AgentLanguages.Chinese, "输出口（发送端）")]
    [AgentContext(AgentLanguages.English, "Output slot (sender). Supports enum types (NetworkRequestMethod, VoltageRange, ModelProtocol) " +
        "and the instance-driven CustomRouteSelector. " +
        "For CustomRouteSelector pass its JSON to 'selectorTypeOrJson' and 'Demo.ViewModels.CustomRouteSelector' to 'nonEnumTypeName' when calling SetEnumSlotCollection.")]
    [VeloxProperty]
    [SlotSelectors(typeof(NetworkRequestMethod), typeof(VoltageRange), typeof(ModelProtocol), typeof(CustomRouteSelector))]
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

    [AgentContext(AgentLanguages.Chinese, "标题")]
    [VeloxProperty] private string title = "Enum Selector";

    [AgentContext(AgentLanguages.Chinese, "是否自动广播给下游节点")]
    [AgentContext(AgentLanguages.English, "When true, the node automatically forwards the result to all connected downstream nodes after execution.")]
    [VeloxProperty] private bool autoBroadcast = true;

    [AgentContext(AgentLanguages.Chinese, "当前选中的枚举值，决定路由到哪个输出口")]
    [AgentContext(AgentLanguages.English, "Currently selected enum value. Determines which output slot receives the routed input. Set to the desired enum member name (string) or its underlying integer value.")]
    public object? SelectedValue
    {
        get => OutputSlots?.CurrentValue;
        set { if (OutputSlots is not null) OutputSlots.CurrentValue = value; }
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
    [AgentContext(AgentLanguages.Chinese, "编译模式：Static 编译期锁定当前选中分支（未选中分支被剪除，其下游节点 Order = -1 绝对停止）；Dynamic 运行期按数据负载重新选分支（全部分支存活，编译期 payload 为 null 时 ResolveRouteKey 返回 null）。通过 PatchNodeProperties 设置，如 {\"CompileMode\":\"Static\"}。")]
    [AgentContext(AgentLanguages.English, "Compile mode: Static locks the currently selected branch at compile time (unselected branches are pruned; their downstream nodes get Order = -1 / absolute stop); Dynamic re-selects the branch at runtime from the data payload (all branches stay alive; ResolveRouteKey returns null for a null compile-time payload). Set via PatchNodeProperties, e.g. {\"CompileMode\":\"Static\"}.")]
    [VeloxProperty] private RouterCompileMode _compileMode = RouterCompileMode.Dynamic;

    /// <summary>Options for the compile-mode dropdown.</summary>
    public RouterCompileMode[] CompileModeOptions => [RouterCompileMode.Static, RouterCompileMode.Dynamic];

    /// <summary>
    /// Unified route-key entry point:
    /// - Static: the key is decided by the currently selected enum value (decidable at compile time);
    /// - Dynamic: a null compile-time payload returns null → IsDynamic; at runtime reads the shared "selector.value" field, else falls back to the currently selected value.
    /// </summary>
    public Task<object?> ResolveRouteKey(object? payload)
    {
        if (CompileMode == RouterCompileMode.Dynamic && payload is null)
            return Task.FromResult<object?>(null);

        if (payload is IRuntimeContext ctx && ctx.TryGet("selector.value", out var v) && v is string s)
            return Task.FromResult(OutputSlots is not null ? OutputSlots.NormalizeSelectorValue(s) : s);

        return Task.FromResult(OutputSlots is not null
            ? OutputSlots.NormalizeSelectorValue(OutputSlots.CurrentValue)
            : null);
    }

    /// <summary>Compile-time route table (changes with mode): Static contains only the currently selected branch; Dynamic contains all branches (preserving 1:N fan-out).</summary>
    public Task<IReadOnlyDictionary<object, IReadOnlyList<IWorkflowNodeViewModel>>> GetRouteTable()
    {
        var dict = new Dictionary<object, List<IWorkflowNodeViewModel>>();
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

        return Task.FromResult<IReadOnlyDictionary<object, IReadOnlyList<IWorkflowNodeViewModel>>>(
            dict.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<IWorkflowNodeViewModel>)kv.Value.AsReadOnly()));
    }

    private static void AddTarget(Dictionary<object, List<IWorkflowNodeViewModel>> dict, object key,
        IWorkflowNodeViewModel target)
    {
        if (!dict.TryGetValue(key, out var list))
            dict[key] = list = [];
        if (!list.Contains(target))
            list.Add(target);
    }

    private static IReadOnlyDictionary<object, IReadOnlyList<IWorkflowNodeViewModel>> EmptyRouteTable()
        => new Dictionary<object, IReadOnlyList<IWorkflowNodeViewModel>>();
}
