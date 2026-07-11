using System.Collections;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
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
    private bool _isApplyingState = false;
    private string _memberName = string.Empty;
    private readonly List<ConditionalSlot<TSlot>> _deferredRemovals = [];

    [VeloxProperty] public partial Type? SelectorType { get; protected set; }
    [VeloxProperty] public partial ObservableCollection<ConditionalSlot<TSlot>> Items { get; set; }
    public int Count { get { FlushDeferredRemovals(); return Items.Count; } }
    public TSlot this[int index] { get { FlushDeferredRemovals(); return Items[index].Slot; } }

    partial void OnItemAddedToItems(IEnumerable<ConditionalSlot<TSlot>> items)
    {
        if (_isApplyingState)
            return;

        foreach (var item in items)
        {
            var normalizedValue = NormalizeValue(item.Value);

            if (normalizedValue is not null && !ReferenceEquals(normalizedValue, item.Value))
                item.Value = normalizedValue;

            if (normalizedValue is not null && conditionMap.ContainsKey(normalizedValue))
            {
                // Defer removal of the old ConditionalSlot from Items to avoid
                // ObservableCollection.CheckReentrancy() when called from within
                // a CollectionChanged event (e.g. JSON deserialization import).
                var staleEntry = Items.FirstOrDefault(
                    c => c != item && Equals(c.Value, normalizedValue));
                if (staleEntry is not null)
                    _deferredRemovals.Add(staleEntry);

                conditionMap[normalizedValue] = item.Slot;
            }
            else
            {
                if (normalizedValue is not null)
                    conditionMap[normalizedValue] = item.Slot;
            }

            Parent?.CreateSlotCommand.Execute(item.Slot);
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
        if (_isApplyingState)
            return;

        foreach (var item in items)
        {
            if (_isDeduplicating)
                continue;

            if (item.Value is not null)
                conditionMap.Remove(item.Value);

            item.Slot.DeleteCommand.Execute(null);
        }
    }

    private void FlushDeferredRemovals()
    {
        if (_deferredRemovals.Count == 0)
            return;

        _isDeduplicating = true;
        try
        {
            foreach (var s in _deferredRemovals)
                Items.Remove(s);
        }
        finally
        {
            _deferredRemovals.Clear();
            _isDeduplicating = false;
        }
    }

    public bool TrySelect(object value, out TSlot? slot)
    {
        return conditionMap.TryGetValue(value, out slot);
    }

    public void SetSelector(object? selector)
    {
        FlushDeferredRemovals();

        if (Parent is null)
        {
            return;
        }

        var oldState = CaptureState();
        List<ConditionalSlot<TSlot>> newItems = [];
        string newTypeName;
        Type? newType;

        if (selector is ISlotProvider provider)
        {
            var definitions = provider.GetSlots().ToArray();
            newType = provider.GetType();
            newTypeName = newType.FullName ?? newType.Name;

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
                newItems.Add(conditional);
            }
        }
        else
        {
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

            var rawValues = selectorType == typeof(bool)
                ? [false, true]
                : Enumerable.Cast<object>(Enum.GetValues(selectorType)).ToArray();

            newType = selectorType;
            newTypeName = typeFullName;

            foreach (var value in rawValues)
            {
                var slot = new TSlot();
                var conditional = new ConditionalSlot<TSlot>
                {
                    Name = value.ToString() ?? string.Empty,
                    Value = value,
                    Slot = slot
                };
                newItems.Add(conditional);
            }
        }

        var newState = new SelectorState(newTypeName, newType, newItems, []);
        var tree = Parent.Parent;
        if (tree is null)
        {
            ApplyDetachedState(newState);
            return;
        }

        tree.GetHelper().Submit(new WorkflowActionPair(
            () => ApplyAttachedState(tree, newState),
            () => ApplyAttachedState(tree, oldState)));
    }

    private SelectorState CaptureState()
    {
        var slots = new HashSet<IWorkflowSlotViewModel>(
            Items.Select(item => (IWorkflowSlotViewModel)item.Slot));
        var links = Parent?.Parent?.Links
            .Where(link => slots.Contains(link.Sender) || slots.Contains(link.Receiver))
            .Distinct()
            .ToArray() ?? [];

        return new SelectorState(SelectorTypeName, SelectorType, [.. Items], links);
    }

    private void ApplyDetachedState(SelectorState state)
    {
        FlushDeferredRemovals();
        SelectorTypeName = state.TypeName;
        SelectorType = state.Type;
        ConditionMap.Clear();
        Items.Clear();
        foreach (var item in state.Items)
            Items.Add(item);
    }

    private void ApplyAttachedState(IWorkflowTreeViewModel tree, SelectorState state)
    {
        FlushDeferredRemovals();

        if (Parent is null)
            return;

        var currentSlots = new HashSet<IWorkflowSlotViewModel>(
            Items.Select(item => (IWorkflowSlotViewModel)item.Slot));
        foreach (var link in tree.Links
            .Where(link => currentSlots.Contains(link.Sender) || currentSlots.Contains(link.Receiver))
            .ToArray())
        {
            RemoveLink(tree, link);
        }

        foreach (var slot in currentSlots)
        {
            Parent.Slots.Remove(slot);
            slot.Parent = null;
        }

        _isApplyingState = true;
        try
        {
            SelectorTypeName = state.TypeName;
            SelectorType = state.Type;
            ConditionMap.Clear();

            Items.Clear();
            foreach (var item in state.Items)
            {
                if (item.Value is not null)
                    ConditionMap[item.Value] = item.Slot;
                Items.Add(item);
            }
        }
        finally
        {
            _isApplyingState = false;
        }

        foreach (var item in state.Items)
        {
            item.Slot.Parent = Parent;
            if (!Parent.Slots.Contains(item.Slot))
                Parent.Slots.Add(item.Slot);
        }

        foreach (var link in state.Links)
            RestoreLink(tree, link);

        // Notify the parent node that the slot collection was reset,
        // so the adapter triggers a full position recalculation.
        //
        // Defer via SynchronizationContext.Post so the notification
        // fires after the UI binding engine has processed the collection
        // changes and generated containers. Firing synchronously would
        // race against container generation, causing adapters to find
        // missing or unmeasured containers and slot anchors falling
        // back to (0,0).
        if (!string.IsNullOrEmpty(_memberName) && Parent is IWorkflowViewModel viewModel)
        {
            var context = SynchronizationContext.Current;
            if (context is not null)
                context.Post(_ => viewModel.OnPropertyChanged(_memberName), null);
            else
                viewModel.OnPropertyChanged(_memberName);
        }
    }

    private static void RemoveLink(IWorkflowTreeViewModel tree, IWorkflowLinkViewModel link)
    {
        var sender = link.Sender;
        var receiver = link.Receiver;

        sender.Targets.Remove(receiver);
        receiver.Sources.Remove(sender);
        if (tree.LinksMap.TryGetValue(sender, out var receivers))
        {
            receivers.Remove(receiver);
            if (receivers.Count == 0)
                tree.LinksMap.Remove(sender);
        }
        tree.Links.Remove(link);
        link.IsVisible = false;
        sender.GetHelper().UpdateState();
        receiver.GetHelper().UpdateState();
    }

    private static void RestoreLink(IWorkflowTreeViewModel tree, IWorkflowLinkViewModel link)
    {
        var sender = link.Sender;
        var receiver = link.Receiver;
        if (sender.Parent?.Parent != tree || receiver.Parent?.Parent != tree)
            return;

        if (!tree.LinksMap.TryGetValue(sender, out var receivers))
        {
            receivers = [];
            tree.LinksMap[sender] = receivers;
        }
        receivers[receiver] = link;
        if (!tree.Links.Contains(link))
            tree.Links.Add(link);
        if (!sender.Targets.Contains(receiver))
            sender.Targets.Add(receiver);
        if (!receiver.Sources.Contains(sender))
            receiver.Sources.Add(sender);
        link.IsVisible = true;
        sender.GetHelper().UpdateState();
        receiver.GetHelper().UpdateState();
    }

    private sealed class SelectorState(
        string typeName,
        Type? type,
        IReadOnlyList<ConditionalSlot<TSlot>> items,
        IReadOnlyList<IWorkflowLinkViewModel> links)
    {
        public string TypeName { get; } = typeName;
        public Type? Type { get; } = type;
        public IReadOnlyList<ConditionalSlot<TSlot>> Items { get; } = items;
        public IReadOnlyList<IWorkflowLinkViewModel> Links { get; } = links;
    }

    public void Install(IWorkflowNodeViewModel parent, string memberName)
    {
        Parent = parent;
        _memberName = memberName;
    }

    public void Uninstall()
    {
        FlushDeferredRemovals();
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
