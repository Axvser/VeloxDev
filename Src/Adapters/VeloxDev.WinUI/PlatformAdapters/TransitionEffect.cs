using Microsoft.UI.Dispatching;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.WinUI.PlatformAdapters
{
    public class TransitionEffect : TransitionEffectCore<DispatcherQueuePriority>
    {
        public override DispatcherQueuePriority Priority { get; set; } = DispatcherQueuePriority.Normal;
    }
}
