using Microsoft.UI.Dispatching;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.TransitionSystem
{
    public class TransitionScheduler<TTarget> : TransitionSchedulerCore<
            UIThreadInspector,
            TransitionInterpreter,
            DispatcherQueuePriority>
    {

    }
}
