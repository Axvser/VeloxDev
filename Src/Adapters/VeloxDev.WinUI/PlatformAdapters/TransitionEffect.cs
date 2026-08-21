using Microsoft.UI.Dispatching;

namespace VeloxDev.TransitionSystem
{
    public class TransitionEffect : TransitionEffectCore<DispatcherQueuePriority>
    {
        // Queue animation-frame writes at High priority so they are processed before rendering, reducing stutter.
        public override DispatcherQueuePriority Priority { get; set; } = DispatcherQueuePriority.High;
    }
}
