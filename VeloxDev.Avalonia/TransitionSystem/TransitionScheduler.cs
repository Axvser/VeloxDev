using Avalonia.Threading;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.Avalonia.TransitionSystem
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
