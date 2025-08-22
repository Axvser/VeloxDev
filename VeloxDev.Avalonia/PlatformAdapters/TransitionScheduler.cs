using Avalonia.Threading;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.Avalonia.PlatformAdapters
{
    public class TransitionScheduler<TTarget> : TransitionSchedulerCore<
            UIThreadInspector,
            TransitionInterpreter,
            DispatcherPriority>
    {

    }
}
