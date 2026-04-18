using Demo.ViewModels.Workflow.Helper;
using System.ComponentModel;
using VeloxDev.AI;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels;

[AgentContext(AgentLanguages.Chinese, "枚举选择器节点，根据枚举类型动态生成输出口，将输入路由到与 SelectedValue 匹配的输出口。默认大小为 280×380。节点固定包含一个输入口 InputSlot（接收端，prop='InputSlot'），必须将上游节点连接至此口才能触发路由。创建时通过 CreateAndConfigureNode 的 enumSlotProperty='OutputSlots' + enumTypeName 一步完成配置，创建后立即连接 InputSlot。若要修改已有节点的枚举类型，直接调用 SetEnumSlotCollection(nodeIndex,'OutputSlots',newEnumTypeName)，无需删除重建节点；注意：切换枚举类型会销毁旧输出口上的所有连接，需重新连接输出口。")]
[AgentContext(AgentLanguages.English, "Enum selector node that dynamically generates output slots from an enum type and routes input to the slot matching SelectedValue. Default size: 280×380. The node always has exactly one input slot: prop='InputSlot' (receiver) — you MUST connect an upstream node to InputSlot, otherwise no routing occurs. Create with CreateAndConfigureNode using enumSlotProperty='OutputSlots' and enumTypeName, then immediately wire InputSlot. To change the enum type on an EXISTING node call SetEnumSlotCollection(nodeIndex,'OutputSlots',newEnumTypeName) — do NOT delete and recreate; note that switching enum type destroys all existing output-slot connections, which must be rewired.")]
[WorkflowBuilder.Node<EnumSelectorHelper>(workSemaphore: 1)]
public partial class EnumSelectorNodeViewModel
{
    public EnumSelectorNodeViewModel()
    {
        InitializeWorkflow();
        InputSlot = new();
        OutputSlots.SetSelector(typeof(NetworkRequestMethod));
    }

    [AgentContext(AgentLanguages.Chinese, "输入口（接收端）。节点创建后必须将上游节点的输出口连接到此口，否则路由不会触发。使用 prop='InputSlot' 引用此口。")]
    [AgentContext(AgentLanguages.English, "Input slot (receiver). After creating the node you MUST connect an upstream output slot to this slot; routing only fires when data arrives here. Reference it with prop='InputSlot'.")]
    [VeloxProperty] public partial SlotViewModel InputSlot { get; set; }

    [AgentContext(AgentLanguages.Chinese, "枚举驱动的输出口集合，通过 enumTypeName 配置选择器类型以生成输出口。允许类型：NetworkRequestMethod、SlotChannel")]
    [VeloxProperty]
    [SlotSelectors(typeof(NetworkRequestMethod), typeof(SlotChannel))]
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
    [VeloxProperty] private object? selectedValue;

    [VeloxProperty] private string lastRouted = "-";

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
}
