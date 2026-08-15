using System.Collections.ObjectModel;
using System.ComponentModel;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

// ---------------------------------------------------------------------------
// Minimal stubs – enough to exercise SlotEnumerator without a real workflow.
// ---------------------------------------------------------------------------

file sealed class StubCommand : IVeloxCommand
{
    public event EventHandler? CanExecuteChanged;
    public event CommandEventHandler? Created;
    public event CommandEventHandler? Started;
    public event CommandEventHandler? Completed;
    public event CommandEventHandler? Canceled;
    public event CommandEventHandler? Failed;
    public event CommandEventHandler? Exited;
    public event CommandEventHandler? Enqueued;
    public event CommandEventHandler? Dequeued;

    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) { }
    public void Lock() { }
    public void UnLock() { }
    public void Notify() { }
    public void Clear() { }
    public void Interrupt() { }
    public void Continue() { }
    public void ChangeSemaphore(int semaphore) { }
    public Task ExecuteAsync(object? parameter) => Task.CompletedTask;
    public Task LockAsync() => Task.CompletedTask;
    public Task UnLockAsync() => Task.CompletedTask;
    public Task ClearAsync() => Task.CompletedTask;
    public Task InterruptAsync() => Task.CompletedTask;
    public Task ContinueAsync() => Task.CompletedTask;
    public Task ChangeSemaphoreAsync(int semaphore) => Task.CompletedTask;
}

file sealed class StubSlot : IWorkflowSlotViewModel
{
    public ObservableCollection<IWorkflowSlotViewModel> Targets { get; set; } = [];
    public ObservableCollection<IWorkflowSlotViewModel> Sources { get; set; } = [];
    public IWorkflowNodeViewModel? Parent { get; set; }
    public SlotChannel Channel { get; set; }
    public SlotState State { get; set; }
    public Anchor Anchor { get; set; } = new();

    public IVeloxCommand SetChannelCommand { get; } = new StubCommand();
    public IVeloxCommand SendConnectionCommand { get; } = new StubCommand();
    public IVeloxCommand ReceiveConnectionCommand { get; } = new StubCommand();
    public IVeloxCommand DeleteCommand { get; } = new StubCommand();
    public IVeloxCommand CloseCommand { get; } = new StubCommand();
    public event PropertyChangingEventHandler? PropertyChanging;
    public event PropertyChangedEventHandler? PropertyChanged;

    public void InitializeWorkflow() { }
    public void OnPropertyChanging(string propertyName) => PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(propertyName));
    public void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    public IWorkflowSlotViewModelHelper GetHelper() => throw new NotSupportedException();
    public void SetHelper(IWorkflowSlotViewModelHelper helper) { }
}

file sealed class StubNode : IWorkflowNodeViewModel
{
    // Track CreateSlotCommand.Execute calls
    public List<IWorkflowSlotViewModel> CreatedSlots { get; } = [];

    private sealed class TrackingCreateSlotCommand(StubNode owner) : IVeloxCommand
    {
        public event EventHandler? CanExecuteChanged;
        public event CommandEventHandler? Created;
        public event CommandEventHandler? Started;
        public event CommandEventHandler? Completed;
        public event CommandEventHandler? Canceled;
        public event CommandEventHandler? Failed;
        public event CommandEventHandler? Exited;
        public event CommandEventHandler? Enqueued;
        public event CommandEventHandler? Dequeued;

        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter)
        {
            if (parameter is IWorkflowSlotViewModel slot)
                owner.CreatedSlots.Add(slot);
        }
        public void Lock() { }
        public void UnLock() { }
        public void Notify() { }
        public void Clear() { }
        public void Interrupt() { }
        public void Continue() { }
        public void ChangeSemaphore(int semaphore) { }
        public Task ExecuteAsync(object? parameter) => Task.CompletedTask;
        public Task LockAsync() => Task.CompletedTask;
        public Task UnLockAsync() => Task.CompletedTask;
        public Task ClearAsync() => Task.CompletedTask;
        public Task InterruptAsync() => Task.CompletedTask;
        public Task ContinueAsync() => Task.CompletedTask;
        public Task ChangeSemaphoreAsync(int semaphore) => Task.CompletedTask;
    }

    public IWorkflowTreeViewModel? Parent { get; set; }
    public Anchor Anchor { get; set; } = new();
    public Size Size { get; set; } = new();
    public ObservableCollection<IWorkflowSlotViewModel> Slots { get; set; } = [];

    public IVeloxCommand MoveCommand { get; } = new StubCommand();
    public IVeloxCommand SetAnchorCommand { get; } = new StubCommand();
    public IVeloxCommand SetSizeCommand { get; } = new StubCommand();
    public IVeloxCommand CreateSlotCommand { get; }
    public IVeloxCommand DeleteCommand { get; } = new StubCommand();
    public IVeloxCommand ReceiveCommand { get; } = new StubCommand();
    public IVeloxCommand BroadcastCommand { get; } = new StubCommand();
    public IVeloxCommand ReverseBroadcastCommand { get; } = new StubCommand();
    public IVeloxCommand CloseCommand { get; } = new StubCommand();

    public event PropertyChangingEventHandler? PropertyChanging;
    public event PropertyChangedEventHandler? PropertyChanged;

    public StubNode() { CreateSlotCommand = new TrackingCreateSlotCommand(this); }

    public void InitializeWorkflow() { }
    public void OnPropertyChanging(string propertyName) => PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(propertyName));
    public void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    public IWorkflowNodeViewModelHelper GetHelper() => throw new NotSupportedException();
    public void SetHelper(IWorkflowNodeViewModelHelper helper) { }
}

// ---------------------------------------------------------------------------
// The selector type used in tests
// ---------------------------------------------------------------------------

file enum BranchKind { Yes, No }
file enum AlternateBranchKind { First, Second, Third }
file enum KindWithNo { No, Maybe }
file enum ThirdKind { Yes, Maybe }

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

[TestClass]
public class SlotEnumeratorTests
{
    [TestMethod]
    public void WhenSelectorChanges_UndoOnceRestoresSlotsAndConnections()
    {
        var tree = new TreeDefaultViewModel();
        var senderNode = new NodeDefaultViewModel();
        var receiverNode = new NodeDefaultViewModel();
        var receiverSlot = new SlotDefaultViewModel { Channel = SlotChannel.OneSource };
        var enumerator = new SlotEnumerator<SlotDefaultViewModel>();

        tree.GetHelper().CreateNode(senderNode);
        tree.GetHelper().CreateNode(receiverNode);
        receiverNode.GetHelper().CreateSlot(receiverSlot);
        enumerator.Install(senderNode, "OutputSlots");
        enumerator.SetSelector(typeof(BranchKind));

        var originalItems = enumerator.Items.ToArray();
        var originalSender = originalItems[0].Slot;
        originalSender.Channel = SlotChannel.OneTarget;
        tree.GetHelper().SendConnection(originalSender);
        tree.GetHelper().ReceiveConnection(receiverSlot);
        Assert.HasCount(1, tree.Links);

        tree.GetHelper().ClearHistory();
        enumerator.SetSelector(typeof(AlternateBranchKind));

        Assert.HasCount(3, enumerator.Items);
        // The connection is preserved — re-routed onto the new type's first branch by position.
        Assert.HasCount(1, tree.Links, "type switch re-routes the connection onto the new branch");

        tree.GetHelper().Undo();

        CollectionAssert.AreEqual(originalItems, enumerator.Items.ToArray());
        Assert.HasCount(1, tree.Links);
        Assert.AreSame(originalSender, tree.Links[0].Sender);
        Assert.AreSame(receiverSlot, tree.Links[0].Receiver);

        tree.GetHelper().Undo();

        CollectionAssert.AreEqual(originalItems, enumerator.Items.ToArray());
        Assert.HasCount(1, tree.Links);

        tree.GetHelper().Redo();

        Assert.HasCount(3, enumerator.Items);
        Assert.HasCount(1, tree.Links, "redo re-routes the connection onto the new branch again");
    }

    /// <summary>
    /// Simulates the JSON round-trip: after SetSelector creates placeholder slots,
    /// the deserializer appends NEW slot instances (carrying real connection history)
    /// for the same enum values into the existing ObservableCollection.
    /// Stale items may remain in Items (reentrancy guard prevents removal during
    /// CollectionChanged), but conditionMap always points to the latest slot.
    /// </summary>
    [TestMethod]
    public void WhenItemsAppendedExternallyAfterSetSelector_CountPreservesNewEntries()
    {
        var node = new StubNode();
        var enumerator = new SlotEnumerator<StubSlot>();
        enumerator.Install(node, "OutputSlots");
        enumerator.SetSelector(typeof(BranchKind));

        int initialCount = Enum.GetValues(typeof(BranchKind)).Length; // 2
        Assert.HasCount(initialCount, enumerator.Items, "Initial count after SetSelector should match enum values.");

        // Simulate JSON deserialization: new slot instances, same Values.
        var snapshot = enumerator.Items.ToList();
        foreach (var conditional in snapshot)
        {
            enumerator.Items.Add(new ConditionalSlot<StubSlot>
            {
                Name = conditional.Name,
                Value = conditional.Value,
                Slot = new StubSlot() // new instance, as JSON would produce
            });
        }

        // During CollectionChanged reentrancy, stale items cannot be removed.
        // Items grows (4 total) but conditionMap routes correctly.
        Assert.HasCount(initialCount * 2, enumerator.Items,
            "Stale items remain due to reentrancy guard; Items count doubles.");
    }

    /// <summary>
    /// After SetSelector, TrySelect must resolve each enum value to exactly one slot.
    /// </summary>
    [TestMethod]
    public void WhenSetSelectorCalled_TrySelectResolvesEachEnumValue()
    {
        var node = new StubNode();
        var enumerator = new SlotEnumerator<StubSlot>();
        enumerator.Install(node, "OutputSlots");
        enumerator.SetSelector(typeof(BranchKind));

        Assert.IsTrue(enumerator.TrySelect(BranchKind.Yes, out var yes) && yes is not null);
        Assert.IsTrue(enumerator.TrySelect(BranchKind.No, out var no) && no is not null);
        Assert.AreNotSame(yes, no, "Each enum value must map to a distinct slot.");
    }

    /// <summary>
    /// The JSON-deserialized slot (new instance) must replace the stale constructor
    /// placeholder in conditionMap. The slot resolved by TrySelect after re-population
    /// must be the incoming JSON slot, not the discarded constructor one.
    /// </summary>
    [TestMethod]
    public void WhenItemsAppendedExternallyAfterSetSelector_JsonSlotReplacesConstructorSlot()
    {
        var node = new StubNode();
        var enumerator = new SlotEnumerator<StubSlot>();
        enumerator.Install(node, "OutputSlots");
        enumerator.SetSelector(typeof(BranchKind));

        // These are the stale constructor-created placeholders.
        enumerator.TrySelect(BranchKind.Yes, out var constructorYes);
        enumerator.TrySelect(BranchKind.No, out var constructorNo);

        // Simulate JSON: brand-new slots with the same Values.
        var jsonYes = new StubSlot();
        var jsonNo = new StubSlot();

        var snapshot = enumerator.Items.ToList();
        foreach (var conditional in snapshot)
        {
            var jsonSlot = Equals(conditional.Value, BranchKind.Yes) ? jsonYes : jsonNo;
            enumerator.Items.Add(new ConditionalSlot<StubSlot>
            {
                Name = conditional.Name,
                Value = conditional.Value,
                Slot = jsonSlot
            });
        }

        Assert.IsTrue(enumerator.TrySelect(BranchKind.Yes, out var resolvedYes));
        Assert.IsTrue(enumerator.TrySelect(BranchKind.No, out var resolvedNo));

        Assert.AreSame(jsonYes, resolvedYes, "TrySelect(Yes) must return the JSON slot, not the constructor placeholder.");
        Assert.AreSame(jsonNo, resolvedNo, "TrySelect(No) must return the JSON slot, not the constructor placeholder.");
        Assert.AreNotSame(constructorYes, resolvedYes, "Constructor placeholder must have been evicted for Yes.");
        Assert.AreNotSame(constructorNo, resolvedNo, "Constructor placeholder must have been evicted for No.");
    }

    /// <summary>
    /// The stale constructor slot may remain in Items (reentrancy guard prevents
    /// removal during CollectionChanged), but conditionMap always routes to the
    /// JSON slot. At least one entry per value must reference the JSON slot.
    /// </summary>
    [TestMethod]
    public void WhenItemsAppendedExternallyAfterSetSelector_JsonSlotIsRouted()
    {
        var node = new StubNode();
        var enumerator = new SlotEnumerator<StubSlot>();
        enumerator.Install(node, "OutputSlots");
        enumerator.SetSelector(typeof(BranchKind));

        var jsonYes = new StubSlot();
        var jsonNo = new StubSlot();

        var snapshot = enumerator.Items.ToList();
        foreach (var conditional in snapshot)
        {
            var jsonSlot = Equals(conditional.Value, BranchKind.Yes) ? jsonYes : jsonNo;
            enumerator.Items.Add(new ConditionalSlot<StubSlot>
            {
                Name = conditional.Name,
                Value = conditional.Value,
                Slot = jsonSlot
            });
        }

        // conditionMap always routes to the latest slot for each value.
        Assert.IsTrue(enumerator.TrySelect(BranchKind.Yes, out var routedYes));
        Assert.IsTrue(enumerator.TrySelect(BranchKind.No, out var routedNo));
        Assert.AreSame(jsonYes, routedYes, "TrySelect(Yes) must route to the JSON slot.");
        Assert.AreSame(jsonNo, routedNo, "TrySelect(No) must route to the JSON slot.");
    }

    /// <summary>
    /// CreateSlotCommand must be called for the incoming JSON slot so it is properly
    /// registered with the parent node, and must NOT be called again for the evicted
    /// constructor placeholder.
    /// </summary>
    [TestMethod]
    public void WhenItemsAppendedExternallyAfterSetSelector_CreateSlotCommandCalledForJsonSlots()
    {
        var node = new StubNode();
        var enumerator = new SlotEnumerator<StubSlot>();
        enumerator.Install(node, "OutputSlots");
        enumerator.SetSelector(typeof(BranchKind));

        var jsonYes = new StubSlot();
        var jsonNo = new StubSlot();

        var snapshot = enumerator.Items.ToList();
        foreach (var conditional in snapshot)
        {
            var jsonSlot = Equals(conditional.Value, BranchKind.Yes) ? jsonYes : jsonNo;
            enumerator.Items.Add(new ConditionalSlot<StubSlot>
            {
                Name = conditional.Name,
                Value = conditional.Value,
                Slot = jsonSlot
            });
        }

        Assert.Contains(jsonYes, node.CreatedSlots, "CreateSlotCommand must have been called for the JSON Yes slot.");
        Assert.Contains(jsonNo, node.CreatedSlots, "CreateSlotCommand must have been called for the JSON No slot.");
    }

    [TestMethod]
    public void WhenBoolSelectorSet_ItemsContainsTrueAndFalseSlots()
    {
        var node = new StubNode();
        var enumerator = new SlotEnumerator<StubSlot>();
        enumerator.Install(node, "OutputSlots");
        enumerator.SetSelector(typeof(bool));

        Assert.HasCount(2, enumerator.Items);
        Assert.IsTrue(enumerator.TrySelect(false, out _));
        Assert.IsTrue(enumerator.TrySelect(true, out _));
    }

    [TestMethod]
    public void WhenUninstallCalled_ItemsAndConditionMapAreCleared()
    {
        var node = new StubNode();
        var enumerator = new SlotEnumerator<StubSlot>();
        enumerator.Install(node, "OutputSlots");
        enumerator.SetSelector(typeof(BranchKind));
        enumerator.Uninstall();

        Assert.IsEmpty(enumerator.Items);
        Assert.IsFalse(enumerator.TrySelect(BranchKind.Yes, out _));
    }

    // -----------------------------------------------------------------------
    // Newtonsoft.Json enum-as-integer regression tests
    //
    // When TypeNameHandling.Auto is in use, plain JSON numbers (enum underlying
    // values) are deserialized as `long`, not as the enum type.  The fix must
    // normalise these back to the enum type before doing conditionMap lookups.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Simulates Newtonsoft.Json appending items whose Value is a long (the enum's
    /// underlying integer), not the enum type itself.  Count must not double.
    /// </summary>
    [TestMethod]
    public void WhenEnumValueDeserializedAsLong_CountStaleEntriesRemain()
    {
        var node = new StubNode();
        var enumerator = new SlotEnumerator<StubSlot>();
        enumerator.Install(node, "OutputSlots");
        enumerator.SetSelector(typeof(BranchKind));

        int expectedCount = Enum.GetValues(typeof(BranchKind)).Length; // 2
        Assert.HasCount(expectedCount, enumerator.Items);

        // Simulate Newtonsoft: value arrives as long, not BranchKind.
        var snapshot = enumerator.Items.ToList();
        foreach (var conditional in snapshot)
        {
            enumerator.Items.Add(new ConditionalSlot<StubSlot>
            {
                Name = conditional.Name,
                Value = Convert.ToInt64(conditional.Value), // mimics JSON integer
                Slot = new StubSlot()
            });
        }

        // Stale entries remain due to reentrancy guard; conditionMap is correct.
        Assert.HasCount(expectedCount * 2, enumerator.Items,
            "Stale entries remain; Items count doubles when enum values arrive as long from JSON.");
    }

    /// <summary>
    /// The JSON slot (arriving with a long Value) must replace the constructor
    /// placeholder in conditionMap, and TrySelect with the enum key must find it.
    /// </summary>
    [TestMethod]
    public void WhenEnumValueDeserializedAsLong_JsonSlotReplacesConstructorSlot()
    {
        var node = new StubNode();
        var enumerator = new SlotEnumerator<StubSlot>();
        enumerator.Install(node, "OutputSlots");
        enumerator.SetSelector(typeof(BranchKind));

        var jsonYes = new StubSlot();
        var jsonNo = new StubSlot();

        var snapshot = enumerator.Items.ToList();
        foreach (var conditional in snapshot)
        {
            var jsonSlot = Equals(conditional.Value, BranchKind.Yes) ? jsonYes : jsonNo;
            enumerator.Items.Add(new ConditionalSlot<StubSlot>
            {
                Name = conditional.Name,
                Value = Convert.ToInt64(conditional.Value), // mimics JSON integer
                Slot = jsonSlot
            });
        }

        Assert.IsTrue(enumerator.TrySelect(BranchKind.Yes, out var resolvedYes));
        Assert.IsTrue(enumerator.TrySelect(BranchKind.No, out var resolvedNo));
        Assert.AreSame(jsonYes, resolvedYes, "TrySelect(Yes) must return the JSON slot after long-valued deserialization.");
        Assert.AreSame(jsonNo, resolvedNo, "TrySelect(No) must return the JSON slot after long-valued deserialization.");
    }

    /// <summary>
    /// After long-valued deserialization, each Value in Items must have been
    /// normalised back to the enum type so that UI bindings and TrySelect work.
    /// </summary>
    [TestMethod]
    public void WhenEnumValueDeserializedAsLong_ItemValueNormalisedToEnumType()
    {
        var node = new StubNode();
        var enumerator = new SlotEnumerator<StubSlot>();
        enumerator.Install(node, "OutputSlots");
        enumerator.SetSelector(typeof(BranchKind));

        var snapshot = enumerator.Items.ToList();
        foreach (var conditional in snapshot)
        {
            enumerator.Items.Add(new ConditionalSlot<StubSlot>
            {
                Name = conditional.Name,
                Value = Convert.ToInt64(conditional.Value),
                Slot = new StubSlot()
            });
        }

        foreach (var entry in enumerator.Items)
        {
            Assert.IsInstanceOfType<BranchKind>(entry.Value,
                $"Item.Value must be normalised to BranchKind, was {entry.Value?.GetType().Name}.");
        }
    }

    /// <summary>
    /// Regression test for the Demo Enum-selector dropdown not updating in real-time.
    /// Consumers (e.g. EnumType/EnumValues) recompute derived values on the
    /// SelectorTypeName notification, so SelectorType must already hold the NEW type
    /// by the time SelectorTypeName fires — i.e. SelectorType must be raised FIRST.
    /// </summary>
    [TestMethod]
    public void WhenSetSelectorCalled_SelectorTypeNotifiesBeforeSelectorTypeName()
    {
        var node = new StubNode();
        var enumerator = new SlotEnumerator<StubSlot>();
        enumerator.Install(node, "OutputSlots");

        var order = new List<string>();
        enumerator.PropertyChanged += (_, e) => order.Add(e.PropertyName ?? string.Empty);

        enumerator.SetSelector(typeof(BranchKind));

        var typeIdx = order.IndexOf(nameof(SlotEnumerator<StubSlot>.SelectorType));
        var nameIdx = order.IndexOf(nameof(SlotEnumerator<StubSlot>.SelectorTypeName));
        Assert.IsTrue(typeIdx >= 0, "SelectorType should raise PropertyChanged during SetSelector.");
        Assert.IsTrue(nameIdx >= 0, "SelectorTypeName should raise PropertyChanged during SetSelector.");
        Assert.IsTrue(typeIdx < nameIdx,
            "SelectorType must be raised before SelectorTypeName so derived refreshes read the new type.");
    }

    /// <summary>
    /// Switching the selector type keeps the current value in sync with the new type:
    /// a value whose member NAME exists in the new enum is preserved; otherwise the value
    /// defaults to the new type's FIRST member (never remapped by underlying number, which
    /// would collapse unrelated values arbitrarily), so routing always has a valid key.
    /// PropertyChanged(CurrentValue) fires so a bound dropdown refreshes. Undo restores the
    /// exact current value captured for the old type.
    /// </summary>
    [TestMethod]
    public void WhenSetSelectorSwitchesType_CurrentValuePreservedByName_ElseDefaultsToFirst_AndUndoRestores()
    {
        var tree = new TreeDefaultViewModel();
        var node = new NodeDefaultViewModel();
        tree.GetHelper().CreateNode(node);
        var enumerator = new SlotEnumerator<SlotDefaultViewModel>();
        enumerator.Install(node, "OutputSlots");

        enumerator.SetSelector(typeof(BranchKind));   // Yes, No
        enumerator.CurrentValue = BranchKind.Yes;
        Assert.AreEqual("Yes", enumerator.CurrentValue);

        var notified = new List<string>();
        enumerator.PropertyChanged += (_, e) => notified.Add(e.PropertyName ?? string.Empty);

        // "Yes" has no member named "Yes" in AlternateBranchKind → defaults to "First".
        enumerator.SetSelector(typeof(AlternateBranchKind));
        Assert.AreEqual(typeof(AlternateBranchKind), enumerator.SelectorType);
        Assert.AreEqual("First", enumerator.CurrentValue,
            "an old value with no same-named member in the new type defaults to the new type's first member");
        Assert.IsTrue(notified.Contains(nameof(SlotEnumerator<SlotDefaultViewModel>.CurrentValue)),
            "the type switch must notify CurrentValue so a bound dropdown refreshes");

        // Undo restores BranchKind and the current value captured for it.
        tree.GetHelper().Undo();
        Assert.AreEqual(typeof(BranchKind), enumerator.SelectorType);
        Assert.AreEqual("Yes", enumerator.CurrentValue,
            "undo must restore the current value that was captured for the old type");

        // Name-preservation: a member NAME present in both enums survives the switch.
        enumerator.CurrentValue = BranchKind.No;
        enumerator.SetSelector(typeof(KindWithNo));   // shares the "No" member name
        Assert.AreEqual("No", enumerator.CurrentValue,
            "a member name present in both enums must be preserved across the type switch");
    }

    /// <summary>
    /// The undo timeline is one entry per SetSelector, not per value change: a selection inside
    /// a type is live state remembered for that type. Undoing a SetSelector restores the old
    /// type with its remembered value in a single step; switching back to a previously-used
    /// type restores that type's last-selected value directly.
    /// </summary>
    [TestMethod]
    public void WhenTypeSwitchThenSelection_UndoPerSetSelector_RestoresRememberedValue()
    {
        var tree = new TreeDefaultViewModel();
        var node = new NodeDefaultViewModel();
        tree.GetHelper().CreateNode(node);
        var enumerator = new SlotEnumerator<SlotDefaultViewModel>();
        enumerator.Install(node, "OutputSlots");

        enumerator.SetSelector(typeof(BranchKind));          // Yes, No
        enumerator.CurrentValue = BranchKind.No;             // A2 = "No"
        Assert.AreEqual("No", enumerator.CurrentValue);

        tree.GetHelper().ClearHistory();

        // Switch to Alt (one SetSelector = one undo entry). "No" has no same-named member
        // in Alt → defaults to Alt's first member ("First").
        enumerator.SetSelector(typeof(AlternateBranchKind)); // First, Second, Third
        Assert.AreEqual("First", enumerator.CurrentValue);

        // Selecting a value is live state remembered for Alt, NOT a separate undo entry.
        enumerator.CurrentValue = AlternateBranchKind.Third; // B3
        Assert.AreEqual("Third", enumerator.CurrentValue);

        // ONE undo reverts the whole SetSelector: old type + its remembered value "No".
        tree.GetHelper().Undo();
        Assert.AreEqual(typeof(BranchKind), enumerator.SelectorType);
        Assert.AreEqual("No", enumerator.CurrentValue,
            "one undo of the SetSelector restores the old type with its remembered value");

        // Switching back to Alt restores its last-selected value directly — no multi-step undo.
        enumerator.SetSelector(typeof(AlternateBranchKind));
        Assert.AreEqual(typeof(AlternateBranchKind), enumerator.SelectorType);
        Assert.AreEqual("Third", enumerator.CurrentValue,
            "switching back to a selector restores the value last selected on it");
    }

    /// <summary>
    /// Switching between two selector types back and forth must restore EACH type's own
    /// last-selected value, not the other type's — per-type state memory.
    /// </summary>
    [TestMethod]
    public void WhenSwitchingBackAndForth_EachSelectorRestoresItsLastSelectedValue()
    {
        var tree = new TreeDefaultViewModel();
        var node = new NodeDefaultViewModel();
        tree.GetHelper().CreateNode(node);
        var enumerator = new SlotEnumerator<SlotDefaultViewModel>();
        enumerator.Install(node, "OutputSlots");

        enumerator.SetSelector(typeof(BranchKind));   // Yes, No
        enumerator.CurrentValue = BranchKind.No;
        tree.GetHelper().ClearHistory();

        // A → B, pick Third on B.
        enumerator.SetSelector(typeof(AlternateBranchKind));
        enumerator.CurrentValue = AlternateBranchKind.Third;

        // B → A: A restores its remembered "No".
        enumerator.SetSelector(typeof(BranchKind));
        Assert.AreEqual("No", enumerator.CurrentValue,
            "switching back to A restores A's remembered value");

        // A → B: B restores its remembered "Third".
        enumerator.SetSelector(typeof(AlternateBranchKind));
        Assert.AreEqual("Third", enumerator.CurrentValue,
            "switching back to B restores B's remembered value");
    }

    /// <summary>
    /// EVERY credential's last-selected value is remembered independently — even when not
    /// active — so repeated switching and undo/redo never lose a credential's selection to
    /// another's. The private credential→value dictionary is the source of truth.
    /// </summary>
    [TestMethod]
    public void WhenMultipleCredentialsWithUndoRedo_EachRestoresItsLastSelectedValue()
    {
        var tree = new TreeDefaultViewModel();
        var node = new NodeDefaultViewModel();
        tree.GetHelper().CreateNode(node);
        var enumerator = new SlotEnumerator<SlotDefaultViewModel>();
        enumerator.Install(node, "OutputSlots");

        enumerator.SetSelector(typeof(BranchKind));
        enumerator.CurrentValue = BranchKind.Yes;             // A = Yes
        enumerator.SetSelector(typeof(AlternateBranchKind));
        enumerator.CurrentValue = AlternateBranchKind.Third;  // B = Third

        // Switch back and forth — each must keep its own value.
        enumerator.SetSelector(typeof(BranchKind));
        Assert.AreEqual("Yes", enumerator.CurrentValue, "switching back to A restores A's value");
        enumerator.SetSelector(typeof(AlternateBranchKind));
        Assert.AreEqual("Third", enumerator.CurrentValue, "switching back to B restores B's value");

        // Undo/redo and switch again — both credentials stay preserved.
        tree.GetHelper().Undo();
        Assert.AreEqual(typeof(BranchKind), enumerator.SelectorType);
        Assert.AreEqual("Yes", enumerator.CurrentValue, "undo restores A's value");
        tree.GetHelper().Redo();
        Assert.AreEqual("Third", enumerator.CurrentValue, "redo restores B's value");
        enumerator.SetSelector(typeof(BranchKind));
        Assert.AreEqual("Yes", enumerator.CurrentValue, "A's value survives further switching");
        enumerator.SetSelector(typeof(AlternateBranchKind));
        Assert.AreEqual("Third", enumerator.CurrentValue, "B's value survives further switching");
    }

    /// <summary>
    /// Each selector type remembers its full state — including connections — so switching
    /// away destroys nothing permanently: switching back restores that type's branches and
    /// wiring. This is what dynamic branch construction requires.
    /// </summary>
    [TestMethod]
    public void WhenSwitchingBetweenTypes_EachTypesConnectionsAreRestored()
    {
        var tree = new TreeDefaultViewModel();
        var sender = new NodeDefaultViewModel();
        var receiver = new NodeDefaultViewModel();
        tree.GetHelper().CreateNode(sender);
        tree.GetHelper().CreateNode(receiver);
        var enumerator = new SlotEnumerator<SlotDefaultViewModel>();
        enumerator.Install(sender, "OutputSlots");

        enumerator.SetSelector(typeof(BranchKind));   // Yes, No
        var yesSlot = enumerator.TrySelect(BranchKind.Yes, out var s) ? s : null;
        yesSlot!.Channel = SlotChannel.OneTarget;
        var receiverSlot = new SlotDefaultViewModel { Channel = SlotChannel.OneSource };
        receiver.GetHelper().CreateSlot(receiverSlot);
        tree.GetHelper().SendConnection(yesSlot);
        tree.GetHelper().ReceiveConnection(receiverSlot);
        Assert.HasCount(1, tree.Links);

        // Switch to Alt: the connection is re-routed onto the new type's first branch.
        enumerator.SetSelector(typeof(AlternateBranchKind));
        Assert.HasCount(1, tree.Links,
            "type switch re-routes the connection onto the new type's branch by position");

        // Switch back to BranchKind: its remembered connection is restored.
        enumerator.SetSelector(typeof(BranchKind));
        Assert.HasCount(1, tree.Links,
            "switching back to a type restores its remembered connections");
    }

    /// <summary>
    /// Regression: a ComboBox TwoWay binding pushes null into the selected value the moment its
    /// ItemsSource is regenerated (which fires synchronously from the SelectorTypeName notification
    /// during a type switch/undo). That transient null must never become the credential's remembered
    /// value, or undo/redo would restore an empty selection in the dropdown.
    /// </summary>
    [TestMethod]
    public void WhenTransientNullWrittenToCurrentValue_RememberedValueSurvives()
    {
        var tree = new TreeDefaultViewModel();
        var node = new NodeDefaultViewModel();
        tree.GetHelper().CreateNode(node);
        var enumerator = new SlotEnumerator<SlotDefaultViewModel>();
        enumerator.Install(node, "OutputSlots");

        enumerator.SetSelector(typeof(BranchKind));   // Yes, No
        enumerator.CurrentValue = BranchKind.No;
        Assert.AreEqual("No", enumerator.CurrentValue);
        tree.GetHelper().ClearHistory();

        // Simulate the UI null-push that happens when the ComboBox regenerates its items.
        enumerator.CurrentValue = null;
        Assert.IsNull(enumerator.CurrentValue, "the live value may be null temporarily");

        // Switch away and back: the remembered "No" must survive the transient null write.
        enumerator.SetSelector(typeof(AlternateBranchKind));
        enumerator.SetSelector(typeof(BranchKind));
        Assert.AreEqual("No", enumerator.CurrentValue,
            "a transient null write must not poison the credential's remembered value");
    }
}
