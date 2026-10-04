using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// One entry of a conditional slot provider, seen without its slot type argument.
/// </summary>
/// <remarks>
/// The generic <see cref="ConditionalSlot{TSlot}"/> names its slot type, so a caller holding only an object
/// cannot read <see cref="Slot"/> out of it; this view erases that argument.
/// </remarks>
public interface IConditionalSlot
{
    /// <summary>The entry's label — the selector value it stands for, or a name the provider gave it.</summary>
    public string Name { get; }

    /// <summary>The selector value this entry answers to.</summary>
    public object? Value { get; }

    /// <summary>The slot created for this entry.</summary>
    public IWorkflowSlotViewModel Slot { get; }
}

/// <summary>One entry of a conditional slot provider: the selector value it answers to and the slot built for it.</summary>
public partial class ConditionalSlot<TSlot> : IConditionalSlot
    where TSlot : IWorkflowSlotViewModel, new()
{
    [VeloxProperty] private string _name = string.Empty;
    [VeloxProperty] private object? _value;
    [VeloxProperty] private TSlot _slot = new();

    // 提升出来的 Slot 是 TSlot，接口要的是 IWorkflowSlotViewModel —— 属性类型不协变，只能显式实现。
    IWorkflowSlotViewModel IConditionalSlot.Slot => _slot;
}
