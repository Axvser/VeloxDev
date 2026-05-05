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
    public IVeloxCommand WorkCommand { get; } = new StubCommand();
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

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

[TestClass]
public class SlotEnumeratorTests
{
    /// <summary>
    /// Simulates the JSON round-trip: after SetSelector creates placeholder slots,
    /// the deserializer appends NEW slot instances (carrying real connection history)
    /// for the same enum values into the existing ObservableCollection.
    /// Items.Count must remain equal to the enum value count — no duplicates.
    /// </summary>
    [TestMethod]
    public void WhenItemsAppendedExternallyAfterSetSelector_CountRemainsCorrect()
    {
        var node = new StubNode();
        var enumerator = new SlotEnumerator<StubSlot>();
        enumerator.Install(node);
        enumerator.SetSelector(typeof(BranchKind));

        int expectedCount = Enum.GetValues(typeof(BranchKind)).Length; // 2
        Assert.HasCount(expectedCount, enumerator.Items, "Initial count after SetSelector should match enum values.");

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

        Assert.HasCount(expectedCount, enumerator.Items,
            "After simulated JSON re-population, Items.Count must not double.");
    }

    /// <summary>
    /// After SetSelector, TrySelect must resolve each enum value to exactly one slot.
    /// </summary>
    [TestMethod]
    public void WhenSetSelectorCalled_TrySelectResolvesEachEnumValue()
    {
        var node = new StubNode();
        var enumerator = new SlotEnumerator<StubSlot>();
        enumerator.Install(node);
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
        enumerator.Install(node);
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
    /// The stale constructor slot must not remain in Items after the JSON slot takes over.
    /// Only the JSON slot entry should be present for each Value.
    /// </summary>
    [TestMethod]
    public void WhenItemsAppendedExternallyAfterSetSelector_StaleConstructorSlotRemovedFromItems()
    {
        var node = new StubNode();
        var enumerator = new SlotEnumerator<StubSlot>();
        enumerator.Install(node);
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

        // Every entry in Items must carry the JSON slot, not the constructor placeholder.
        var yesEntry = enumerator.Items.SingleOrDefault(c => Equals(c.Value, BranchKind.Yes));
        var noEntry = enumerator.Items.SingleOrDefault(c => Equals(c.Value, BranchKind.No));

        Assert.IsNotNull(yesEntry, "There must be exactly one entry for BranchKind.Yes.");
        Assert.IsNotNull(noEntry, "There must be exactly one entry for BranchKind.No.");
        Assert.AreSame(jsonYes, yesEntry.Slot, "Items entry for Yes must reference the JSON slot.");
        Assert.AreSame(jsonNo, noEntry.Slot, "Items entry for No must reference the JSON slot.");
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
        enumerator.Install(node);
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
        enumerator.Install(node);
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
        enumerator.Install(node);
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
    public void WhenEnumValueDeserializedAsLong_CountRemainsCorrect()
    {
        var node = new StubNode();
        var enumerator = new SlotEnumerator<StubSlot>();
        enumerator.Install(node);
        enumerator.SetSelector(typeof(BranchKind));

        int expectedCount = Enum.GetValues(typeof(BranchKind)).Length;
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

        Assert.HasCount(expectedCount, enumerator.Items,
            "Items.Count must not double when enum values arrive as long from JSON.");
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
        enumerator.Install(node);
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
        enumerator.Install(node);
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
}
