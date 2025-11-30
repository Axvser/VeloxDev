#nullable enable

using Microsoft.UI.Dispatching;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.WinUI.PlatformAdapters
{
    public class InterpolatorOutput : InterpolatorOutputCore<DispatcherQueuePriority>
    {
        public override void Update(object target, int frameIndex, bool isUIAccess, DispatcherQueuePriority priority)
        {
            try
            {
                if (target is null) return;
                if (isUIAccess)
                {
                    Update(target, frameIndex);
                    return;
                }
                UIThreadInspector.DispatcherQueue?.TryEnqueue(priority, () => { Update(target, frameIndex); });
            }
            catch
            {

            }
        }
    }
}
