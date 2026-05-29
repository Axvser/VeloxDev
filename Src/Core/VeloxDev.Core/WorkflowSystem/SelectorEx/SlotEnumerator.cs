using System.Collections;
using System.Collections.ObjectModel;
using System.Diagnostics;
using VeloxDev.DynamicTheme;
using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

public partial class SlotEnumerator<TSlot> : IConditionalSlotProvider<TSlot>
    where TSlot : IWorkflowSlotViewModel, new()
{
    public SlotEnumerator()
    {
        Items = [];
    }

    [VeloxProperty] private IWorkflowNodeViewModel? _parent;
    [VeloxProperty] private string selectorTypeName = string.Empty;
    [VeloxProperty] private Dictionary<object, TSlot> conditionMap = [];

    private bool _isDeduplicating = false;

    [VeloxProperty] public partial Type? SelectorType { get; protected set; }
    [VeloxProperty] public partial ObservableCollection<ConditionalSlot<TSlot>> Items { get; set; }
    public int Count => Items.Count;
    public TSlot this[int index] => Items[index].Slot;

    partial void OnItemAddedToItems(IEnumerable<ConditionalSlot<TSlot>> items)
    {
        List<ConditionalSlot<TSlot>>? stale = null;

        foreach (var item in items)
        {
            var normalizedValue = NormalizeValue(item.Value);

            if (normalizedValue is not null && !ReferenceEquals(normalizedValue, item.Value))
                item.Value = normalizedValue;

            if (normalizedValue is not null && conditionMap.ContainsKey(normalizedValue))
            {
                var staleEntry = Items.FirstOrDefault(
                    c => c != item && Equals(c.Value, normalizedValue));

                if (staleEntry is not null)
                {
                    stale ??= [];
                    stale.Add(staleEntry);
                }

                conditionMap[normalizedValue] = item.Slot;
            }
            else
            {
                if (normalizedValue is not null)
                    conditionMap[normalizedValue] = item.Slot;
            }

            Parent?.CreateSlotCommand.Execute(item.Slot);
        }

        if (stale is not null)
        {
            _isDeduplicating = true;
            try
            {
                foreach (var s in stale)
                    Items.Remove(s);
            }
            finally
            {
                _isDeduplicating = false;
            }
        }
    }

    private object? NormalizeValue(object? value)
    {
        if (value is null) return null;

        Type? targetType = null;
        foreach (var key in conditionMap.Keys)
        {
            targetType = key.GetType();
            break;
        }

        targetType ??= SelectorType;

        if (targetType is null) return value;
        if (value.GetType() == targetType) return value;

        try
        {
            if (targetType.IsEnum)
                return Enum.ToObject(targetType, value);

            return Convert.ChangeType(value, targetType);
        }
        catch
        {
            return value;
        }
    }

    partial void OnItemRemovedFromItems(IEnumerable<ConditionalSlot<TSlot>> items)
    {
        foreach (var item in items)
        {
            if (_isDeduplicating)
                continue;

            if (item.Value is not null)
                conditionMap.Remove(item.Value);

            item.Slot.DeleteCommand.Execute(null);
        }
    }

    partial void OnSelectorTypeNameChanged(string oldValue, string newValue)
    {
        if (!string.IsNullOrEmpty(newValue))
        {
            SelectorType = Type.GetType(newValue);
        }
        else
        {
            SelectorType = null;
        }
    }

    public bool TrySelect(object value, out TSlot? slot)
    {
        return conditionMap.TryGetValue(value, out slot);
    }

    public void SetSelector(object? selector)
    {
        if (Parent is null)
        {
            Debug.Fail("Parent must be set before configuring selector type.");
            return;
        }

        // ISlotProvider path: instance-driven slot list (not enum/bool)
        if (selector is ISlotProvider provider)
        {
            var definitions = provider.GetSlots().ToArray();
            var providerTypeName = provider.GetType().FullName ?? provider.GetType().Name;

            SelectorType = provider.GetType();
            SelectorTypeName = providerTypeName;
            conditionMap.Clear();
            for (int i = Items.Count - 1; i >= 0; i--)
                Items.RemoveAt(i);

            foreach (var def in definitions)
            {
                var slot = new TSlot();
                var label = string.IsNullOrEmpty(def.Label) ? def.Value?.ToString() ?? string.Empty : def.Label;
                var conditional = new ConditionalSlot<TSlot>
                {
                    Name = label,
                    Value = def.Value,
                    Slot = slot
                };
                Items.Add(conditional);
            }
            return;
        }

        Type? selectorType = selector switch
        {
            Type t => t,
            string s => Type.GetType(s),
            _ => null
        };

        if (selectorType is null)
        {
            Debug.Fail($"SetSelector: cannot resolve a Type from '{selector}'. Pass a Type, a fully-qualified type name string, or an ISlotProvider instance.");
            return;
        }

        if (!selectorType.IsEnum && selectorType != typeof(bool))
        {
            Debug.Fail("Provided type must be an enum or bool. For custom slot lists implement ISlotProvider and pass an instance.");
            return;
        }

        var typeFullName = selectorType.FullName ?? selectorType.Name;
        if (SelectorTypeName == typeFullName)
            return;

        var rawValues = (selectorType == typeof(bool)
            ? [false, true]
            : Enumerable.Cast<object>(Enum.GetValues(selectorType)))
            .Where(v => !ConditionMap.ContainsKey(v))
            .ToArray();

        SelectorType = selectorType;
        SelectorTypeName = typeFullName;
        conditionMap.Clear();
        for (int i = Items.Count - 1; i >= 0; i--)
            Items.RemoveAt(i);

        foreach (var value in rawValues)
        {
            var slot = new TSlot();
            var conditional = new ConditionalSlot<TSlot>
            {
                Name = value.ToString() ?? string.Empty,
                Value = value,
                Slot = slot
            };
            Items.Add(conditional);
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

    public IEnumerator<TSlot> GetEnumerator()
    {
        foreach (var item in Items)
            yield return item.Slot;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
