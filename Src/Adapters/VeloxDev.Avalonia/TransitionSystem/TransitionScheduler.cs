using Avalonia.Threading;

namespace VeloxDev.TransitionSystem
{
    public class TransitionScheduler<TTarget> : TransitionSchedulerCore<
            UIThreadInspector,
            TransitionInterpreter,
            DispatcherPriority>
    {

    }
}
