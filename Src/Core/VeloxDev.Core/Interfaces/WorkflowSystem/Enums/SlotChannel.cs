namespace VeloxDev.WorkflowSystem;

/// <summary>The connection capacity of a slot in each direction (a flags enum).</summary>
[Flags]
public enum SlotChannel : int
{
    None = 0,

    OneTarget = 1,

    OneSource = 2,

    OneBoth = OneTarget | OneSource,

    MultipleTargets = 4,

    MultipleSources = 8,

    MultipleBoth = MultipleTargets | MultipleSources
}

/// <summary>The runtime state of a slot (a flags enum, so states can combine).</summary>
[Flags]
public enum SlotState : int
{
    StandBy = 1,

    PreviewSender = 2,

    PreviewReceiver = 4,

    Sender = 8,

    Receiver = 16
}