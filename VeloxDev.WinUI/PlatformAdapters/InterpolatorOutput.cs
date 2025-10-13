#nullable enable

using Microsoft.UI.Dispatching;
using System;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.WinUI.PlatformAdapters
{
    public class InterpolatorOutput : InterpolatorOutputCore<DispatcherQueuePriority>
    {
        private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

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
                _dispatcher.TryEnqueue(priority, () => { Update(target, frameIndex); });
            }
            catch
            {

            }
        }
    }
}
