using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.MAUI.PlatformAdapters
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
                Application.Current?.Dispatcher?.DispatchAsync(() => Update(target, frameIndex));
            }
        }
    }
}
