using System.Collections.ObjectModel;

namespace VeloxDev.WorkflowSystem;

/// <summary>A slot provider whose slots follow one selector, typed by <typeparamref name="TSlot"/>.</summary>
public interface IConditionalSlotProvider<TSlot> : IEnumerable<TSlot>
    where TSlot : IWorkflowSlotViewModel, new()
{
    /// <summary>The node the provider is installed on, or <see langword="null"/> when it is not installed.</summary>
    public IWorkflowNodeViewModel? Parent { get; set; }

    /// <summary>The selector type's name, as stored in the model.</summary>
    public string SelectorTypeName { get; set; }

    /// <summary>The slots this provider holds, one per selector value.</summary>
    public ObservableCollection<ConditionalSlot<TSlot>> Items { get; set; }

    /// <summary>The selector's currently selected value.</summary>
    public object? CurrentValue { get; set; }

    /// <summary>Finds the slot <paramref name="value"/> maps to.</summary>
    /// <param name="value">The selector value.</param>
    /// <param name="slot">The slot, or <see langword="null"/> when the value has none.</param>
    /// <returns><see langword="true"/> when a slot was found.</returns>
    public bool TrySelect(object value, out TSlot? slot);

    /// <summary>Switches the provider to another selector.</summary>
    /// <param name="selector">The selector — a well-known value, or an <c>ISlotProvider</c>.</param>
    public void SetSelector(object? selector);

    /// <summary>Installs the provider on <paramref name="parent"/> for the member <paramref name="memberName"/>.</summary>
    /// <param name="parent">The node the provider is installed on.</param>
    /// <param name="memberName">The member whose value selects the slot.</param>
    public void Install(IWorkflowNodeViewModel parent, string memberName);

    /// <summary>Detaches the provider from its node.</summary>
    public void Uninstall();
}

/// <summary>
/// A conditional slot provider seen without its slot type argument.
/// </summary>
/// <remarks>
/// <para>
/// The generic interface names <c>TSlot</c>, and a caller that only has an object cannot close it. This view
/// carries the same state with the slot type erased, so a consumer can read the selector, enumerate the slots
/// and drive the selection without knowing the type argument — and without reflecting over it.
/// </para>
/// <para>
/// <see cref="SlotEnumerator{TSlot}"/> implements this explicitly; nothing that already uses the generic members
/// changes meaning.
/// </para>
/// </remarks>
public interface IConditionalSlotProvider
{
    /// <summary>The node the enumerator is installed on, or <see langword="null"/> when it is not installed.</summary>
    public IWorkflowNodeViewModel? Parent { get; }

    /// <summary>The selector type's name, as stored in the model.</summary>
    public string SelectorTypeName { get; }

    /// <summary>The selector type, once it has been resolved.</summary>
    public Type? SelectorType { get; }

    /// <summary>The selector's currently selected value.</summary>
    public object? CurrentValue { get; }

    /// <summary>The slots this enumerator currently holds, one per selector value.</summary>
    public IReadOnlyList<IConditionalSlot> Slots { get; }

    /// <summary>
    /// Finds the slot a selector value maps to.
    /// </summary>
    /// <param name="value">The selector value.</param>
    /// <param name="slot">The slot, or <see langword="null"/> when the value has none.</param>
    /// <returns><see langword="true"/> when a slot was found.</returns>
    public bool TrySelect(object value, out IWorkflowSlotViewModel? slot);

    /// <summary>
    /// Switches the enumerator to another selector type.
    /// </summary>
    /// <param name="selector">The selector — a well-known value, or an <c>ISlotProvider</c>.</param>
    public void SetSelector(object? selector);
}
