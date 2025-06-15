using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.MAUI.TransitionSystem
{
    public class TransitionScheduler<TTarget> : TransitionSchedulerCore<TTarget, UIThreadInspector, TransitionInterpreter>
        where TTarget : class
    {

    }
}
