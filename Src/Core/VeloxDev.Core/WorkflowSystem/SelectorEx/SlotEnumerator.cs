using System.Collections;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Threading;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem.StandardEx;

namespace VeloxDev.WorkflowSystem;

/// <summary>A slot collection driven by one selector: each value of the selector type maps to one slot, and switching the selector swaps the whole set.</summary>
public partial class SlotEnumerator<TSlot> : IConditionalSlotProvider<TSlot>, IConditionalSlotProvider, System.ComponentModel.INotifyPropertyChanged
    where TSlot : IWorkflowSlotViewModel, new()
{
    /// <summary>Creates an empty enumerator.</summary>
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
    /// <summary>The number of items in the collection.</summary>
    public int Count { get { FlushDeferredRemovals(); return Items.Count; } }
    /// <summary>Returns the slot at <paramref name="index"/>.</summary>
    public TSlot this[int index] { get { FlushDeferredRemovals(); return Items[index].Slot; } }

    /// <inheritdoc />
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

            if (!_isDeserializing && Parent is { } parent)
            {
                // Register the slot with the parent while the node is still detached so a later
                // deferred CreateSlotCommand dispatch cannot execute AFTER the node is mounted —
                // there StandardCreateSlot's attached branch would Submit a phantom undo entry
                // for a slot construction already created. StandardCreateSlot is a plain
                // no-Submit collection add here; its idempotency guard then makes the command's
                // re-dispatch a no-op. The command is still dispatched to honor the standard
                // slot-creation flow (a node's CreateSlotCommand is the registration contract).
                if (parent.Parent is null)
                    parent.StandardCreateSlot(item.Slot);
                parent.CreateSlotCommand.Execute(item.Slot);   // attached → one undoable step per slot
            }
        }
    }

    /// <summary>Normalizes <paramref name="value"/> onto the current selector type.</summary>
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
        return EnumMembersOf(type).FirstOrDefault();
    }

    // 成员顺序与 Enum.GetValues(type) 一致，但不用它：那条带 RequiresDynamicCode，AOT 下抛
    // NotSupportedException（IL3050）。Enum.GetValues<T>() 是 .NET 5 起、GetValuesAsUnderlyingType
    // 是 .NET 8 起，而本程序集的目标档含 netstandard2.0 / net461 —— 所以走 GetNames + Parse，
    // 这两个全档可用且都无标注。GetNames 与 GetValues 同样按常量的二进制值排序，逐位对应。
    private static object[] EnumMembersOf(Type type)
    {
        var names = Enum.GetNames(type);
        var values = new object[names.Length];

        for (var i = 0; i < names.Length; i++)
        {
            values[i] = Enum.Parse(type, names[i]);
        }

        return values;
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

            var first = EnumMembersOf(targetType).FirstOrDefault();
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

    /// <inheritdoc />
    public bool TrySelect(object value, out TSlot? slot)
    {
        return conditionMap.TryGetValue(value, out slot);
    }

    /// <inheritdoc />
    public void SetSelector(object? selector)
    {
        FlushDeferredRemovals();

        if (Parent is null)
        {
            WorkflowGuard.Fail("The selector is not installed on a node; SetSelector cannot apply the selection.");
            return;
        }

        List<ConditionalSlot<TSlot>> newItems = [];
        string newTypeName;
        Type? newType;
        bool isProviderSelector = selector is ISlotProvider;

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
                : EnumMembersOf(selectorType);

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

        // Capture each current branch's wiring on **both** sides before the old slots are retired, so a FRESH
        // selector can re-wire the new branches onto the same neighbours (preserves routing topology instead of
        // leaving the new branches disconnected).
        //
        // 两边的连线都要记：分支发往的（Targets）与喂给它的（Sources）。只记 Targets 是给**输出**枚举写的，
        // 而**输入**枚举的分支没有 Targets —— 它上面挂的是 Sources。先前只有 Targets，于是重建一个输入端口集
        // 会把喂给它的连线连同旧槽一起静默丢掉（调用照常报成功），只有比对连线数才发现。
        var previousSelectorTypeName = SelectorTypeName;
        var previousWiring = Items
            .Select(item => (
                Name: item.Name,
                Targets: item.Slot.Targets.ToArray(),
                Sources: item.Slot.Sources.ToArray()))
            .ToList();

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
        //
        // 缓存的单位是**选择器类型名**，那是为枚举写的：枚举的槽位来自类型本身，切回来时要连布线一起复原。
        // 一个 ISlotProvider 不是类型而是**值** —— 端口表在它身上，同类型的不同实例给出的端口可以完全不同。
        // 所以对它查这张表只会取回上一次的快照、把刚建好的槽位丢掉，而调用方看到的是「设置成功」。
        // Agent 的 SetEnumSlotCollection 正是这条路径，症状是同一个节点第二次改端口不起作用。
        bool isFresh;
        SelectorState newState;
        if (!isProviderSelector && _typeStates.TryGetValue(newTypeName, out var remembered))
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
            () => ApplyNewState(tree, newState, isFresh ? previousSelectorTypeName : null, isFresh ? previousWiring : null),
            () => ApplyAttachedState(tree, oldState)));
    }

    private void ApplyNewState(
        IWorkflowTreeViewModel tree,
        SelectorState state,
        string? previousSelectorTypeName,
        List<(string Name, IWorkflowSlotViewModel[] Targets, IWorkflowSlotViewModel[] Sources)>? previousWiring)
    {
        ApplyAttachedState(tree, state);
        if (previousWiring is null) return;

        // Reconnect each new branch to the neighbours the old branch it stands for was connected to, in **both**
        // directions — an input set's branches carry their links in Sources and an output set's in Targets, and
        // restoring only Targets lost every link feeding a rebuilt input set.
        var byName = string.Equals(previousSelectorTypeName, state.TypeName, StringComparison.Ordinal);

        for (int i = 0; i < state.Items.Count; i++)
        {
            var branch = state.Items[i].Slot;
            var previous = MatchBranch(previousWiring, state.Items[i].Name, i, byName);
            if (previous is null) continue;

            foreach (var receiver in previous.Value.Targets)
            {
                if (receiver.Parent?.Parent != tree) continue;
                ConnectSlots(tree, branch, receiver);
            }

            foreach (var sender in previous.Value.Sources)
            {
                if (sender.Parent?.Parent != tree) continue;
                ConnectSlots(tree, sender, branch);
            }
        }
    }

    /// <summary>The old branch a new one stands for, or <see langword="null"/> when it stands for none.</summary>
    /// <remarks>
    /// <para>
    /// Which branch a new one "stands for" depends on what kind of change this is, and the selector type name is
    /// what tells them apart.
    /// </para>
    /// <para>
    /// <b>A different type</b> — an enum swapped for another enum, where the members of one have nothing to do
    /// with the members of the other. Identity is then the <i>position</i>, which is the rule this has always
    /// followed and the one the type-switch tests pin.
    /// </para>
    /// <para>
    /// <b>The same type</b> — a provider whose port list was edited, which is the only way to reach here with the
    /// name unchanged. Identity is then the <i>name</i>: it is what the caller reordering or inserting into a port
    /// list is thinking in, and pairing by position would silently re-route a connection onto a different port.
    /// A branch whose name is not in the old set is genuinely new and gets no wiring, which is the honest answer.
    /// </para>
    /// </remarks>
    private static (string Name, IWorkflowSlotViewModel[] Targets, IWorkflowSlotViewModel[] Sources)? MatchBranch(
        List<(string Name, IWorkflowSlotViewModel[] Targets, IWorkflowSlotViewModel[] Sources)> previous,
        string name,
        int index,
        bool byName)
    {
        if (byName)
        {
            foreach (var candidate in previous)
                if (string.Equals(candidate.Name, name, StringComparison.Ordinal)) return candidate;

            return null;
        }

        return index < previous.Count ? previous[index] : null;
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

    /// <inheritdoc />
    public void Install(IWorkflowNodeViewModel parent, string memberName)
    {
        Parent = parent;
        _memberName = memberName;

        // 把本枚举器交给持有节点的调用方 —— 条目的名字在我身上，而我的属性名只有节点知道。
        if (parent?.GetHelper() is IConditionalSlotProviders registry
            && !registry.Providers.Contains(this))
        {
            registry.Providers.Add(this);
        }
    }

    /// <inheritdoc />
    public void Uninstall()
    {
        if (Parent?.GetHelper() is IConditionalSlotProviders registry)
        {
            registry.Providers.Remove(this);
        }

        FlushDeferredRemovals();
        Parent = null;
        conditionMap.Clear();
        for (int i = Items.Count - 1; i >= 0; i--)
            Items.RemoveAt(i);
    }

    // 非泛型视图：TSlot 擦掉之后给拿不到类型实参的调用方用（Agent 工具面）。
    // Slots 与 TrySelect 显式实现 —— 属性类型不协变，且 out 参数的类型不参与重载解析。
    IReadOnlyList<IConditionalSlot> IConditionalSlotProvider.Slots => Items;

    bool IConditionalSlotProvider.TrySelect(object value, out IWorkflowSlotViewModel? slot)
    {
        var found = TrySelect(value, out TSlot? typed);
        slot = typed;
        return found;
    }

    /// <summary>
    /// Clears the collections the constructor may have pre-populated, before the serializer fills them.
    /// </summary>
    /// <remarks>
    /// Public, unlike the hooks on types this assembly serializes itself: a consuming assembly serializes its own
    /// closed <see cref="SlotEnumerator{TSlot}"/>, and the generated code that calls this is emitted there — where
    /// an <c>internal</c> member would not be visible at all.
    /// </remarks>
    /// <param name="context">The deserialization context; this format has none to offer and passes <c>default</c>.</param>
    [OnDeserializing]
    public void OnDeserializing(StreamingContext context)
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

    /// <summary>
    /// Settles the instance once every member has been read: re-resolves the selector type from its stored name
    /// and re-normalizes the items that were mapped against the constructor's default type.
    /// </summary>
    /// <remarks>
    /// Public for the same reason as <see cref="OnDeserializing(StreamingContext)"/>: the caller is generated into
    /// the consuming assembly.
    /// </remarks>
    /// <param name="context">The deserialization context; this format has none to offer and passes <c>default</c>.</param>
    [OnDeserialized]
    public void OnDeserialized(StreamingContext context)
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
            var resolved = VeloxDev.AI.AgentTypeResolver.ResolveType(SelectorTypeName);
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

    // 按名字还原选择器类型——走编译期目录，不扫程序集。存档里只留 FullName，目录按类型全名索引，
    // 所以查不到就说明那个类型没进目录，还原不出来。
    private static Type? ResolveTypeByName(string fullName)
        => VeloxDev.AI.AgentTypeResolver.ResolveType(fullName);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
