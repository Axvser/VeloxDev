#nullable enable

using Microsoft.UI.Dispatching;
using System;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.WinUI.PlatformAdapters
{
    public class InterpolatorOutput : InterpolatorOutputCore<DispatcherQueuePriority>
    {
        private DispatcherQueue? _dispatcher;

        private DispatcherQueue Dispatcher
        {
            get
            {
                if (_dispatcher == null)
                    _dispatcher = DispatcherQueue.GetForCurrentThread();

                return _dispatcher
                    ?? throw new InvalidOperationException("Cannot find a valid DispatcherQueue for UI thread.");
            }
        }

        public override void Update(object target, int frameIndex, bool isUIAccess, DispatcherQueuePriority priority)
        {
            ArgumentNullException.ThrowIfNull(target);

            if (isUIAccess)
            {
                Update(target, frameIndex);
                return;
            }

            if (!Dispatcher.TryEnqueue(priority, () => { Update(target, frameIndex); }))
                throw new InvalidOperationException("Failed to enqueue interpolation update to UI thread.");
        }
    }
}
