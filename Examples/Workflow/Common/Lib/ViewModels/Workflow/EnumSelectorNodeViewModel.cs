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
public partial class EnumSelectorNodeViewModel : ICompileTimeRouter
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
    }

    private void OnOutputSlotsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SlotEnumerator<>.SelectorTypeName))
        {
            OnPropertyChanged(nameof(EnumType));
            OnPropertyChanged(nameof(EnumValues));
        }
    }

    [AgentContext(AgentLanguages.Chinese, "标题")]
    [VeloxProperty] private string title = "Enum Selector";

    [AgentContext(AgentLanguages.Chinese, "当前选中的枚举值，决定路由到哪个输出口")]
    [AgentContext(AgentLanguages.English, "Currently selected enum value. Determines which output slot receives the routed input. Set to the desired enum member name (string) or its underlying integer value.")]
    [VeloxProperty] private object? selectedValue;

    [VeloxProperty] private string lastRouted = "-";

    // WorkResult（生成器 NuGet 暂未生成该属性，手动实现）
    private object? workResult;
    public object? WorkResult
    {
        get => workResult;
        set
        {
            if (Equals(workResult, value)) return;
            workResult = value;
            OnPropertyChanged(nameof(WorkResult));
        }
    }

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

    public bool HasInputSlot => _inputSlot is not null;

    public Type? EnumType => OutputSlots?.SelectorType;

    public object[] EnumValues
    {
        get
        {
            var t = EnumType;
            if (t == null) return [];
            if (t == typeof(bool)) return [false, true];
            var arr = Enum.GetValues(t);
            var result = new object[arr.Length];
            arr.CopyTo(result, 0);
            return result;
        }
    }

    public SlotViewModel? GetSlotForValue(object value)
        => OutputSlots?.TrySelect(value, out var slot) == true ? slot : null;

    public string GetSlotLabel(int index)
    {
        var items = OutputSlots?.Items;
        if (items == null || index < 0 || index >= items.Count) return "?";
        return items[index].Slot?.ToString() ?? "?";
    }

    public object? GetCurrentRouteKey() => SelectedValue;

    /// <summary>
    /// 编译时路由表：枚举值 → 对应的下游节点
    /// </summary>
    public IReadOnlyDictionary<object, IWorkflowNodeViewModel> GetRouteTable()
    {
        var dict = new Dictionary<object, IWorkflowNodeViewModel>();
        if (OutputSlots is null) return dict;

        foreach (var item in OutputSlots.Items)
        {
            var slot = item.Slot;
            foreach (var target in slot.Targets)
            {
                if (target.Parent is not null && item.Value is not null)
                    dict[item.Value] = target.Parent;
            }
        }
        return dict;
    }
}
