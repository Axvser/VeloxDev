namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Represents a single slot entry produced by a custom <see cref="ISlotProvider"/>.
/// </summary>
public sealed class SlotDefinition(object value, string label = "")
{
    /// <summary>The routing key used to look up this slot via <c>TrySelect</c>.</summary>
    public object Value { get; } = value;

    /// <summary>Human-readable label shown on the slot. Falls back to <see cref="Value"/> when empty.</summary>
    public string Label { get; } = label;
}
