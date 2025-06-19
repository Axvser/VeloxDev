using System.Runtime.CompilerServices;
using System.Windows.Threading;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.WPF.TransitionSystem
{
    public class TransitionScheduler : TransitionSchedulerCore<
            UIThreadInspector,
            TransitionInterpreter,
            DispatcherPriority>
    {

    }
}
