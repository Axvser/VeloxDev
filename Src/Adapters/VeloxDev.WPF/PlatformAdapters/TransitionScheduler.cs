using System.Windows.Threading;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.TransitionSystem
{
    public class TransitionScheduler : TransitionSchedulerCore<
            UIThreadInspector,
            TransitionInterpreter,
            DispatcherPriority>
    {

    }
}
