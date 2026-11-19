using Microsoft.UI.Dispatching;

namespace VeloxDev.TransitionSystem
{
    public class TransitionScheduler<TTarget> : TransitionSchedulerCore<
            UIThreadInspector,
            TransitionInterpreter,
            DispatcherQueuePriority>
    {

    }
}
