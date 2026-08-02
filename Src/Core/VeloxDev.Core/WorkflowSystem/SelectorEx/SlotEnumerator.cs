using System.Collections;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Threading;
using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

public partial class SlotEnumerator<TSlot> : IConditionalSlotProvider<TSlot>, System.ComponentModel.INotifyPropertyChanged
    where TSlot : IWorkflowSlotViewModel, new()
{
    public SlotEnumerator()
    {
        Items = [];
    }

    [VeloxProperty] private IWorkflowNodeViewModel? _parent;
    [VeloxProperty] private string selectorTypeName = string.Empty;
    [VeloxProperty] private Dictionary<object, TSlot> conditionMap = [];

    private object? _currentValue;

    // Remembers each selector type's full state (slots, connections) so switching back to a
    // previously-used type restores its wiring — the undo timeline is one entry per SetSelector.
    private readonly Dictionary<string, SelectorState> _typeStates = [];

    // The single source of truth for each credential's last-selected value (the credential is
    // the selector type). Every credential is remembered independently — even when not active —
    // and the first time a credential is used its value defaults to the selector's first member.
    private readonly Dictionary<string, object?> _currentValuesByCredential = [];

    private bool _isDeduplicating = false;
    private bool _isApplyingState = false;
    private bool _isDeserializing = false;
    private string _memberName = string.Empty;
    private readonly List<ConditionalSlot<TSlot>> _deferredRemovals = [];

    [VeloxProperty] public partial Type? SelectorType { get; protected set; }
    [VeloxProperty] public partial ObservableCollection<ConditionalSlot<TSlot>> Items { get; set; }
    public int Count { get { FlushDeferredRemovals(); return Items.Count; } }
    public TSlot this[int index] { get { FlushDeferredRemovals(); return Items[index].Slot; } }

    public object? CurrentValue
    {
        get => _currentValue?.ToString();
        set
        {
            // During deserialization the raw stored value is written first and re-validated
            // in OnDeserialized (the selector type may not be resolved yet).
            if (_isDeserializing)
            {
                _currentValue = value;
                return;
            }

            // Re-entrancy guard: a ComboBox TwoWay binding pushes null into the selected value
            // the moment its ItemsSource is regenerated — which fires synchronously from the
            // SelectorTypeName notification inside ApplyAttachedState while _isApplyingState is
            // true. That write is UI bookkeeping, not a real selection; ignore it, the state
            // application restores the remembered value itself.
            if (_isApplyingState)
                return;

            var newValue = NormalizeSelectorValue(value);
            if (Equals(_currentValue, newValue)) return;

            _currentValue = newValue;
            // Record the selection for this credential so switching back restores it.
            // Never record null: a transient null (e.g. the ComboBox regenerating its items)
            // must not become the remembered value, or undo/redo would restore an empty selection.
            if (_currentValue is not null)
                _currentValuesByCredential[SelectorTypeName] = _currentValue;
            OnPropertyChanged(nameof(CurrentValue));
        }
    }

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

            if (!_isDeserializing)
                Parent?.CreateSlotCommand.Execute(item.Slot);
        }
    }

    public object? NormalizeSelectorValue(object? value) => NormalizeValue(value);

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
                return value is string s ? Enum.Parse(targetType, s, true) : Enum.ToObject(targetType, value);

            return Convert.ChangeType(value, targetType);
        }
        catch
        {
            return value;
        }
    }

    private object? RestoreCredentialCurrentValue(SelectorState state)
    {
        // The current value for the applied credential comes from the private dictionary (the
        // source of truth), never from the state snapshot — so undo/redo consistently restores
        // each credential's last-selected value instead of a stale switch-time snapshot.
        // A null remembered value is treated as "not remembered" and falls back to the type's
        // first member — defensive: even if a transient null ever reaches the dictionary, a
        // restored selector always holds a valid selection (and routing keeps a valid key).
        var value = _currentValuesByCredential.TryGetValue(state.TypeName, out var v) && v is not null
            ? v
            : FirstMemberOf(state.Type);
        return ValidateCurrentValue(value);
    }

    private static object? FirstMemberOf(Type? type)
    {
        if (type is null) return null;
        if (type == typeof(bool)) return false;
        if (!type.IsEnum) return null;
        return Enum.GetValues(type).Cast<object>().FirstOrDefault();
    }

    private object? ValidateCurrentValue(object? value)
    {
        if (value is null) return null;

        Type? targetType = null;
        foreach (var key in ConditionMap.Keys)
        {
            targetType = key.GetType();
            break;
        }
        targetType ??= SelectorType;
        if (targetType is null) return null;

        // Already a member of the current selector type — keep it.
        if (value.GetType() == targetType)
            return ConditionMap.ContainsKey(value) ? value : null;

        // Enum selector: preserve by member NAME when the name exists in the new type; never
        // remap by underlying number. When no same-named member exists, fall back to the new
        // type's FIRST member so the selector always holds a valid selection and routing keeps
        // waking up a downstream branch (undo/redo still restore the exact remembered value).
        if (targetType.IsEnum)
        {
            try
            {
                var parsed = Enum.Parse(targetType, value.ToString()!, ignoreCase: true);
                if (ConditionMap.ContainsKey(parsed)) return parsed;
            }
            catch
            {
                // name does not exist — fall through to the first-member default
            }

            var first = Enum.GetValues(targetType).Cast<object>().FirstOrDefault();
            return first is not null && ConditionMap.ContainsKey(first) ? first : null;
        }

        // Non-enum selectors (bool / ISlotProvider): normalize then check membership.
        var normalized = NormalizeValue(value);
        return normalized is not null && ConditionMap.ContainsKey(normalized) ? normalized : null;
    }

    partial void OnItemRemovedFromItems(IEnumerable<ConditionalSlot<TSlot>> items)
    {
        if (_isApplyingState || _isDeserializing)
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

        // Capture each current branch's downstream targets, so switching to a FRESH type can
        // re-wire the new branches onto the same downstream nodes (preserves routing topology
        // instead of leaving the new branches disconnected).
        var targetsByIndex = Items.Select(item => item.Slot.Targets.ToArray()).ToList();

        // Remember the current selector's full state so switching back restores it.
        // The undo timeline is one entry per SetSelector: a value change inside a type is
        // live state, not a separate timeline point.
        _typeStates[SelectorTypeName] = CaptureState();
        var oldState = _typeStates[SelectorTypeName];

        // Restore this credential's last-selected value, or (first time on this credential)
        // default to its first member and record the pair — so EVERY credential is remembered,
        // not just the currently-active one.
        if (!_currentValuesByCredential.TryGetValue(newTypeName, out _))
        {
            _currentValuesByCredential[newTypeName] = FirstMemberOf(newType);
        }

        // Restore the target type's remembered state, or build it fresh on first use.
        bool isFresh;
        SelectorState newState;
        if (_typeStates.TryGetValue(newTypeName, out var remembered))
        {
            newState = remembered;
            isFresh = false;
        }
        else
        {
            newState = new SelectorState(newTypeName, newType, newItems, []);
            _typeStates[newTypeName] = newState;
            isFresh = true;
        }

        var tree = Parent.Parent;
        if (tree is null)
        {
            ApplyDetachedState(newState);
            return;
        }

        tree.GetHelper().Submit(new WorkflowActionPair(
            () => ApplyNewState(tree, newState, isFresh ? targetsByIndex : null),
            () => ApplyAttachedState(tree, oldState)));
    }

    private void ApplyNewState(IWorkflowTreeViewModel tree, SelectorState state, List<IWorkflowSlotViewModel[]>? rewireTargets)
    {
        ApplyAttachedState(tree, state);
        if (rewireTargets is null) return;

        // Reconnect each new branch onto the downstream nodes the previous type's branch at
        // the same position was connected to — a type switch preserves the wiring topology.
        for (int i = 0; i < state.Items.Count && i < rewireTargets.Count; i++)
        {
            var sender = state.Items[i].Slot;
            foreach (var receiver in rewireTargets[i])
            {
                if (receiver.Parent?.Parent != tree) continue;
                ConnectSlots(tree, sender, receiver);
            }
        }
    }

    private static void ConnectSlots(IWorkflowTreeViewModel tree, IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
    {
        if (tree.LinksMap.TryGetValue(sender, out var existing) && existing.ContainsKey(receiver)) return;

        var link = tree.GetHelper().CreateLink(sender, receiver);
        if (!tree.LinksMap.TryGetValue(sender, out var receivers))
        {
            receivers = [];
            tree.LinksMap[sender] = receivers;
        }
        receivers[receiver] = link;
        if (!tree.Links.Contains(link)) tree.Links.Add(link);
        if (!sender.Targets.Contains(receiver)) sender.Targets.Add(receiver);
        if (!receiver.Sources.Contains(sender)) receiver.Sources.Add(sender);
        link.IsVisible = true;
        sender.GetHelper().UpdateState();
        receiver.GetHelper().UpdateState();
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
        // Set SelectorType BEFORE SelectorTypeName: consumers that refresh derived values
        // (e.g. EnumType/EnumValues) listen to SelectorTypeName, so the type must already be
        // the new value by the time that notification fires, or they read a stale type.
        SelectorType = state.Type;
        SelectorTypeName = state.TypeName;
        ConditionMap.Clear();
        Items.Clear();
        foreach (var item in state.Items)
            Items.Add(item);

        _currentValue = RestoreCredentialCurrentValue(state);
        OnPropertyChanged(nameof(CurrentValue));
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
            // SelectorType first, then SelectorTypeName — consumers listening to SelectorTypeName
            // (e.g. EnumType/EnumValues) must observe the NEW type when it fires.
            SelectorType = state.Type;
            SelectorTypeName = state.TypeName;
            ConditionMap.Clear();

            Items.Clear();
            foreach (var item in state.Items)
            {
                if (item.Value is not null)
                    ConditionMap[item.Value] = item.Slot;
                Items.Add(item);
            }

            _currentValue = RestoreCredentialCurrentValue(state);
        }
        finally
        {
            _isApplyingState = false;
        }

        OnPropertyChanged(nameof(CurrentValue));

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

    [OnDeserializing]
    private void OnDeserializing(StreamingContext context)
    {
        _isDeserializing = true;

        // The constructor may have called SetSelector (e.g. via an owning
        // ViewModel's constructor), pre-populating Items with the default
        // selector's slots.  JSON.NET appends deserialized items to the
        // *existing* collection rather than replacing it, so we must clear
        // both Items and ConditionMap before the serializer populates them.
        ConditionMap.Clear();
        Items.Clear();
    }

    [OnDeserialized]
    private void OnDeserialized(StreamingContext context)
    {
        _isDeserializing = false;

        // SelectorType (a Type with a protected setter) is not emitted by the writable-only
        // contract resolver, so it is NOT restored from JSON. SelectorTypeName (a string) IS
        // preserved. The constructor may have left SelectorType at the default selector (e.g.
        // NetworkRequestMethod) — which is NOT null — so we must always re-resolve from the
        // serialized name (and correct any mismatch), not only when SelectorType happens to be null.
        // Otherwise EnumType/EnumValues read the stale default type and the dropdown reverts.
        if (!string.IsNullOrEmpty(SelectorTypeName))
        {
            var resolved = ResolveTypeByName(SelectorTypeName);
            if (resolved is not null)
            {
                SelectorType = resolved;
                // Consumers (e.g. the demo node) refresh EnumValues on SelectorTypeName, so
                // re-raise it now that SelectorType holds the resolved type — the serializer's
                // earlier write of the name still saw the constructor's default type.
                OnPropertyChanged(nameof(SelectorTypeName));
            }
        }

        // During deserialization, OnItemAddedToItems normalized each item.Value against the
        // constructor's default selector type (the serialized type is only resolved above), so
        // values from a different enum got remapped onto the default type. Re-normalize every
        // item against the resolved type and rebuild conditionMap — otherwise TrySelect, the
        // route table and CurrentValue validation all see keys of the wrong enum type.
        ConditionMap.Clear();
        foreach (var item in Items)
        {
            var normalized = NormalizeValue(item.Value);
            if (normalized is not null)
            {
                item.Value = normalized;
                ConditionMap[normalized] = item.Slot;
            }
        }

        // CurrentValue was deserialized through its normalizing setter while SelectorType may not
        // have been resolved yet. Re-normalize it now that the type is known (e.g. a JSON string
        // member name or an Int64 becomes the actual enum member); drop it if it does not match
        // the restored selector, and notify so a bound dropdown refreshes its selection.
        _currentValue = ValidateCurrentValue(_currentValue);
        OnPropertyChanged(nameof(CurrentValue));

        // Seed the per-type cache with the restored selector's state and record the credential's
        // current value, so post-load switching still restores each type's wiring/value. Both are
        // runtime-only (not serialized), so they must be re-established here.
        if (!string.IsNullOrEmpty(SelectorTypeName))
        {
            _typeStates[SelectorTypeName] = CaptureState();
            _currentValuesByCredential[SelectorTypeName] = _currentValue;
        }

        // During deserialization, OnItemAddedToItems skips CreateSlotCommand
        // (_isDeserializing was true), so the deserialized slots have not been
        // registered with the parent node.  Without this step the slots exist in
        // Items but their Parent reference and Slots-collection membership are
        // missing, breaking the object-reference-level identity that the tree's
        // Links depend on.
        //
        // JSON.NET is configured with PreserveReferencesHandling.Objects, so the
        // TSlot instances created here are the same instances that the Links'
        // Sender/Receiver properties point to — we simply need to wire them into
        // the parent node's hierarchy.
        if (Parent is not null)
        {
            foreach (var item in Items)
            {
                var slot = item.Slot;
                if (slot.Parent is null)
                    slot.Parent = Parent;
                if (!Parent.Slots.Any(s => ReferenceEquals(s, slot)))
                    Parent.Slots.Add(slot);
            }
        }
    }

    public IEnumerator<TSlot> GetEnumerator()
    {
        foreach (var item in Items)
            yield return item.Slot;
    }

    private static Type? ResolveTypeByName(string fullName)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType(fullName, throwOnError: false, ignoreCase: false);
            if (t is not null) return t;
        }
        return null;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
