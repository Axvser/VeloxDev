using Avalonia.Threading;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.Avalonia.TransitionSystem
{
    public class InterpolatorOutput : InterpolatorOutputCore<DispatcherPriority>
    {
        public override void Update(object target, int frameIndex, bool isUIAccess, DispatcherPriority priority)
        {
            if (isUIAccess)
            {
                Update(target, frameIndex);
            }
            else
            {
                Dispatcher.UIThread.InvokeAsync(() => Update(target, frameIndex), priority);
            }
        }
    }
}
