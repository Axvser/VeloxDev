using Demo.ViewModels.Workflow.Helper;
using System.ComponentModel;
using VeloxDev.AI;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.Compilation;

namespace Demo.ViewModels;

[AgentContext(AgentLanguages.Chinese, "枚举选择器节点，可将输入按枚举成员路由到多个执行路径。默认大小为 280×380。")]
[AgentContext(AgentLanguages.English, "Enum selector node that routes input to multiple execution paths based on enum members. Default size: 280×380.")]
[WorkflowBuilder.Node<EnumSelectorHelper>(workSemaphore: 1)]
public partial class EnumSelectorNodeViewModel : ICompileTimeRouter, ICompileTimeNotifier
{
    public EnumSelectorNodeViewModel()
    {
        InitializeWorkflow();
        OutputSlots.SetSelector(typeof(NetworkRequestMethod));
    }

    /// <summary>
    /// 编译期回调：编译瞬间获知自己的编译身份。
    /// 正常编译（选中分支）写入顺序（+1 对齐运行时 1-based）；被略过时保持 0。
    /// </summary>
    public void OnCompiled(CompiledItem item)
    {
        IsCompileSkipped = item.IsSkipped;
        LastExecutionOrder = item.IsSkipped ? 0 : item.Order + 1;
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
        // SelectorTypeName 在 SelectorType 之后触发（SlotEnumerator 先设置类型），
        // 因此触发时 EnumValues 读到的已是新的选择器类型。
        if (e.PropertyName == nameof(SlotEnumerator<>.SelectorTypeName))
        {
            OnPropertyChanged(nameof(EnumType));
            OnPropertyChanged(nameof(EnumValues));
        }
        // CurrentValue 现由枚举器持有，这里同步到节点的 SelectedValue 绑定。
        // UI 框架（WPF/Avalonia）的 ComboBox 在 ItemsSource 换新数组的同一帧内会丢弃
        // 已重绑的选中项：条目容器尚未按新源生成，字符串匹配失败 → 显示为空。
        // 延迟一帧，让 ItemsSource 先落地并生成条目，再重绑选中项。
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

    [AgentContext(AgentLanguages.Chinese, "当前选中的枚举值，决定路由到哪个输出口")]
    [AgentContext(AgentLanguages.English, "Currently selected enum value. Determines which output slot receives the routed input. Set to the desired enum member name (string) or its underlying integer value.")]
    public object? SelectedValue
    {
        get => OutputSlots?.CurrentValue;
        set { if (OutputSlots is not null) OutputSlots.CurrentValue = value; }
    }

    [VeloxProperty] private string lastRouted = "-";

    // 执行序列号（手动实现，生成器暂未覆盖）
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
    public bool HasExecutionOrder => LastExecutionOrder > 0;
    public string ExecutionOrderText => LastExecutionOrder > 0 ? $"#{LastExecutionOrder}" : "-";

    /// <summary>本次编译中该节点是否因属于未选中条件分支而被略过（编译期判定）。</summary>
    public bool IsCompileSkipped { get; private set; }

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
        => OutputSlots?.TrySelect(OutputSlots.NormalizeSelectorValue(value), out var slot) == true ? slot : null;

    public string GetSlotLabel(int index)
    {
        var items = OutputSlots?.Items;
        if (items == null || index < 0 || index >= items.Count) return "?";
        return items[index].Slot?.ToString() ?? "?";
    }

    public object? GetCurrentRouteKey()
        => OutputSlots is not null ? OutputSlots.NormalizeSelectorValue(OutputSlots.CurrentValue) : null;

    /// <summary>
    /// 编译时路由表：枚举值 → 对应的下游节点列表。
    /// 单个分支可能扇出到多个目标（如 C→3 且 C→4），必须保留全部目标；
    /// 旧的 1:1 赋值会让后写的目标覆盖先写的，导致丢失分支路径。
    /// </summary>
    public IReadOnlyDictionary<object, IReadOnlyList<IWorkflowNodeViewModel>> GetRouteTable()
    {
        var dict = new Dictionary<object, List<IWorkflowNodeViewModel>>();
        if (OutputSlots is null) return EmptyRouteTable();

        foreach (var item in OutputSlots.Items)
        {
            var slot = item.Slot;
            if (item.Value is null || slot.Targets.Count == 0) continue;

            if (!dict.TryGetValue(item.Value, out var list))
                dict[item.Value] = list = [];

            foreach (var target in slot.Targets)
                if (target.Parent is not null && !list.Contains(target.Parent))
                    list.Add(target.Parent);
        }
        return ToReadOnly(dict);
    }

    private static IReadOnlyDictionary<object, IReadOnlyList<IWorkflowNodeViewModel>> EmptyRouteTable()
        => new Dictionary<object, IReadOnlyList<IWorkflowNodeViewModel>>();

    private static IReadOnlyDictionary<object, IReadOnlyList<IWorkflowNodeViewModel>> ToReadOnly(
        Dictionary<object, List<IWorkflowNodeViewModel>> dict)
        => dict.ToDictionary(kv => kv.Key,
            kv => (IReadOnlyList<IWorkflowNodeViewModel>)kv.Value.AsReadOnly());
}
