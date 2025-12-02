#nullable enable

using Microsoft.UI.Dispatching;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.WinUI.PlatformAdapters
{
    public class InterpolatorOutput : InterpolatorOutputCore<UIThreadInspector, DispatcherQueuePriority>
    {
        public override void Update(object target, int frameIndex, bool isUIAccess, DispatcherQueuePriority priority)
        {
            if (isUIAccess)
            {
                SetValues(target, frameIndex);
                return;
            }
            UIThreadInspector.DispatcherQueue?.TryEnqueue(priority, () => { SetValues(target, frameIndex); });
        }
    }
}
