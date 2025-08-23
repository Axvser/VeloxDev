using Microsoft.UI.Dispatching;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.WinUI.PlatformAdapters
{
    public class TransitionScheduler : TransitionSchedulerCore<
            UIThreadInspector,
            TransitionInterpreter,
            DispatcherQueuePriority>
    {

    }
}
