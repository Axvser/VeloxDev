using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.MAUI.TransitionSystem
{
    public class InterpolatorOutput : InterpolatorOutputCore
    {
        public override void Update(object target, int frameIndex, bool isUIAccess)
        {
            if (isUIAccess)
            {
                Update(target, frameIndex);
            }
            else
            {
                Dispatcher.GetForCurrentThread()?.DispatchAsync(() => Update(target, frameIndex));
            }
        }
    }
}
