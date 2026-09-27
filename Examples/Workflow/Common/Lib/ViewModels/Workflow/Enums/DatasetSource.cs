using VeloxDev.AI;

namespace Demo.ViewModels;

/// <summary>
/// Where the demo's dataset comes from. The demo's source selector is compiled in <c>Static</c> mode, so this enum
/// doubles as the compile-time pruning showcase: the option that is not selected is cut from the compiled graph and
/// its downstream nodes stop at <c>Order = -1</c>.
/// </summary>
[AgentContext(AgentLanguages.English, "Dataset source: Synthetic generates samples in-process, SampleFile reads a recorded set.")]
internal enum DatasetSource
{
    /// <summary>Generate the samples with a script.</summary>
    Synthetic = 0,

    /// <summary>Read a recorded sample set instead (pruned in the shipped demo — the selector is locked to Synthetic).</summary>
    SampleFile = 1,
}
