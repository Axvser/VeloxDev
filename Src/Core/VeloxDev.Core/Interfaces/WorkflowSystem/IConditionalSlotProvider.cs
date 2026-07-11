using System.Collections.ObjectModel;

namespace VeloxDev.WorkflowSystem;

public interface IConditionalSlotProvider<TSlot> : IEnumerable<TSlot>
    where TSlot : IWorkflowSlotViewModel, new()
{
    public IWorkflowNodeViewModel? Parent { get; set; }
    public string SelectorTypeName { get; set; }
    public ObservableCollection<ConditionalSlot<TSlot>> Items { get; set; }

    public bool TrySelect(object value, out TSlot? slot);
    public void SetSelector(object? selector);
    public void Install(IWorkflowNodeViewModel parent, string memberName);
    public void Uninstall();
}
