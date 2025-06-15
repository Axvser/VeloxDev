using System.Runtime.CompilerServices;
using System.Windows.Threading;
using VeloxDev.Core.Interfaces.TransitionSystem;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.WPF.TransitionSystem
{
    public class TransitionScheduler<TTarget> : TransitionSchedulerCore<
            TTarget,
            UIThreadInspector,
            TransitionInterpreter,
            DispatcherPriority>
        where TTarget : class
    {

    }
}
