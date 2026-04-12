using Microsoft.UI.Dispatching;

namespace VeloxDev.TransitionSystem
{
    public class TransitionEffect : TransitionEffectCore<DispatcherQueuePriority>
    {
        public override DispatcherQueuePriority Priority { get; set; } = DispatcherQueuePriority.Normal;
    }
}
