using Avalonia.Threading;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.TransitionSystem
{
    public class TransitionScheduler<TTarget> : TransitionSchedulerCore<
            UIThreadInspector,
            TransitionInterpreter,
            DispatcherPriority>
    {

    }
}
