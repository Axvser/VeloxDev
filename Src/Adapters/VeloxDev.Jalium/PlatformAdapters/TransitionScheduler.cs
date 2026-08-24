using Jalium.UI.Threading;

namespace VeloxDev.TransitionSystem
{
    public class TransitionScheduler : TransitionSchedulerCore<
            UIThreadInspector,
            TransitionInterpreter,
            DispatcherPriority>
    {
    }
}
