using Demo.ViewModels.Workflow.Helper;
using System.Collections.ObjectModel;
using VeloxDev.AI;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels;

[AgentContext(AgentLanguages.Chinese, "枚举选择器节点，根据枚举类型动态生成输出口，将输入路由到与 SelectedValue 匹配的输出口。默认大小为 280*380。创建后需通过 SetEnumSlotCollection 工具设置枚举类型（如 'Demo.ViewModels.NetworkRequestMethod'）以生成输出口")]
[AgentContext(AgentLanguages.English, "Enum selector node that dynamically generates output slots from an enum type and routes input to the slot matching SelectedValue. Default size: 280×380. After creation, use SetEnumSlotCollection tool with the enum full type name (e.g. 'Demo.ViewModels.NetworkRequestMethod') to populate output slots")]
[WorkflowBuilder.Node<EnumSelectorHelper>(workSemaphore: 1)]
public partial class EnumSelectorNodeViewModel
{
    private Type? _enumType;
    private object[] _enumValues = [];

    public EnumSelectorNodeViewModel()
    {
        InputSlot = new SlotViewModel();
        OutputSlots = [];
        InitializeWorkflow();
    }

    [AgentContext(AgentLanguages.Chinese, "输入口")]
    [VeloxProperty] public partial SlotViewModel InputSlot { get; set; }

    [VeloxProperty]
    [EnumSlotCollection]
    public partial ObservableCollection<SlotViewModel> OutputSlots { get; set; }

    [AgentContext(AgentLanguages.Chinese, "标题")]
    [VeloxProperty] private string title = "Enum Selector";

    [AgentContext(AgentLanguages.Chinese, "当前选中的枚举值，决定路由到哪个输出口")]
    [VeloxProperty] private object? selectedValue;

    [VeloxProperty] private string lastRouted = "-";

    public bool HasInputSlot => _inputSlot is not null;

    [SlotsEnumType(nameof(OutputSlots), typeof(NetworkRequestMethod), typeof(SlotChannel))]
    public Type? EnumType
    {
        get => _enumType;
        set
        {
            if (_enumType == value) return;
            _enumType = value;
            _enumValues = ResolveEnumValues(value);
            OnPropertyChanged(nameof(EnumType));
            OnPropertyChanged(nameof(EnumValues));
            RebuildOutputSlots();
        }
    }

    public object[] EnumValues => _enumValues;

    public void RebuildOutputSlots()
    {
        if (OutputSlots is not null)
        {
            while (OutputSlots.Count > 0)
                OutputSlots.RemoveAt(OutputSlots.Count - 1);
        }
        else
        {
            OutputSlots = [];
        }

        foreach (var _ in _enumValues)
        {
            var slot = CreateWorkflowSlot<SlotViewModel>();
            slot.Channel = SlotChannel.MultipleTargets;
            OutputSlots.Add(slot);
        }
    }

    public SlotViewModel? GetSlotForValue(object value)
    {
        if (OutputSlots is null) return null;
        var index = Array.IndexOf(_enumValues, value);
        if (index >= 0 && index < OutputSlots.Count)
            return OutputSlots[index];
        return null;
    }

    public string GetSlotLabel(int index)
    {
        if (index >= 0 && index < _enumValues.Length)
            return _enumValues[index].ToString() ?? "?";
        return "?";
    }

    private static object[] ResolveEnumValues(Type? enumType)
    {
        if (enumType is null || !enumType.IsEnum) return [];
        var arr = Enum.GetValues(enumType);
        var result = new object[arr.Length];
        arr.CopyTo(result, 0);
        return result;
    }
}
