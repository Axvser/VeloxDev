using System.Windows.Threading;

namespace VeloxDev.TransitionSystem
{
    public class TransitionScheduler : TransitionSchedulerCore<
            UIThreadInspector,
            TransitionInterpreter,
            DispatcherPriority>
    {

    }
}
