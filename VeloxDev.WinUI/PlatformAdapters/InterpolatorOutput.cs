using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.WinUI.PlatformAdapters
{
    public class InterpolatorOutput : InterpolatorOutputCore<DispatcherQueuePriority>
    {
        public override void Update(object target, int frameIndex, bool isUIAccess, DispatcherQueuePriority priority)
        {
            var _ = (target as FrameworkElement)?.DispatcherQueue?.TryEnqueue(priority,
                () => Update(target, frameIndex)
            );
        }
    }
}
