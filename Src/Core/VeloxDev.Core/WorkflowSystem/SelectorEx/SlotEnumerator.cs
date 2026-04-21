using System.Collections;
using System.Collections.ObjectModel;
using System.Diagnostics;
using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

public partial class SlotEnumerator<TSlot> : IEnumerable<TSlot>
    where TSlot : IWorkflowSlotViewModel, new()
{
    public SlotEnumerator()
    {
        Items = [];
    }

    [VeloxProperty] private IWorkflowNodeViewModel? _parent;
    [VeloxProperty] private string selectorTypeName = string.Empty;
    [VeloxProperty] private Dictionary<object, TSlot> conditionMap = [];
    [VeloxProperty] private ObservableCollection<ConditionalSlot<TSlot>> items = [];

    partial void OnItemAddedToItems(IEnumerable<ConditionalSlot<TSlot>> items)
    {
        if (Parent is null) return;

        foreach (var item in items)
        {
            Parent.CreateSlotCommand.Execute(item.Slot);
        }
    }

    partial void OnItemRemovedFromItems(IEnumerable<ConditionalSlot<TSlot>> items)
    {
        foreach (var item in items)
        {
            item.Slot.DeleteCommand.Execute(null);
        }
    }

    public void Install(IWorkflowNodeViewModel parent)
    {
        Parent = parent;
    }

    public void Uninstall()
    {
        Parent = null;
        conditionMap.Clear();
        for (int i = Items.Count - 1; i >= 0; i--)
            Items.RemoveAt(i);
    }

    public bool TrySelect(object value, out TSlot? slot)
    {
        return conditionMap.TryGetValue(value, out slot);
    }

    public int Count => Items.Count;

    public TSlot this[int index] => Items[index].Slot;

    public IEnumerator<TSlot> GetEnumerator()
    {
        foreach (var item in Items)
            yield return item.Slot;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private Type? _selectorType;

    public Type? SelectorType => _selectorType;

    public void SetSelector(Type selectorType)
    {
        if (Parent is null)
        {
            Debug.Fail("Parent must be set before configuring selector type.");
            return;
        }

        if (!selectorType.IsEnum && selectorType != typeof(bool))
        {
            Debug.Fail("Provided type must be an enum or bool.");
            return;
        }

        _selectorType = selectorType;
        SelectorTypeName = selectorType.FullName ?? selectorType.Name;
        conditionMap.Clear();
        for (int i = Items.Count - 1; i >= 0; i--)
            Items.RemoveAt(i);

        var rawValues = selectorType == typeof(bool)
            ? [false, true]
            : Enumerable.Cast<object>(Enum.GetValues(selectorType)).ToArray();

        foreach (var value in rawValues)
        {
            var slot = new TSlot();
            conditionMap[value] = slot;
            var conditional = new ConditionalSlot<TSlot>
            {
                Name = value.ToString() ?? string.Empty,
                Value = value,
                Slot = slot
            };
            Items.Add(conditional);
        }
    }
}
