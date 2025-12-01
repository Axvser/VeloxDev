using Avalonia.Threading;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.Avalonia.PlatformAdapters
{
    public class InterpolatorOutput : InterpolatorOutput<DispatcherPriority>
    {
        public override void Update(object target, int frameIndex, bool isUIAccess, DispatcherPriority priority)
        {
            if (isUIAccess)
            {
                SetValues(target, frameIndex);
            }
            else
            {
                Dispatcher.UIThread?.InvokeAsync(() => SetValues(target, frameIndex), priority);
            }
        }
    }
}
