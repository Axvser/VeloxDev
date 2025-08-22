using System.Windows.Threading;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.WPF.PlatformAdapters
{
    public class TransitionScheduler : TransitionSchedulerCore<
            UIThreadInspector,
            TransitionInterpreter,
            DispatcherPriority>
    {

    }
}
