using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.MAUI.TransitionSystem
{
    public class InterpolatorOutput : InterpolatorOutputCore
    {
        private readonly IDispatcher? dispatcher = Dispatcher.GetForCurrentThread();

        public override void Update(object target, int frameIndex, bool isUIAccess)
        {
            if (isUIAccess)
            {
                Update(target, frameIndex);
            }
            else
            {
                dispatcher?.DispatchAsync(() => Update(target, frameIndex));
            }
        }
    }
}
