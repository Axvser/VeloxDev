using System.Windows.Threading;
using VeloxDev.Core.Interfaces.TransitionSystem;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.WPF.TransitionSystem
{
    public class FrameInterpolator : IFrameInterpolator<InterpolatorOutputBase<DispatcherPriority>, DispatcherPriority>
    {
        public InterpolatorOutputBase<DispatcherPriority> Interpolate(object? start, object? end, ITransitionEffect<DispatcherPriority> effect)
        {
            var result = new InterpolatorOutput();

            var frameCount = (int)(effect.FPS / 1000d * effect.Duration.TotalMilliseconds);
            frameCount = frameCount > 0 ? frameCount : 1;
            result.SetCount(frameCount > 0 ? frameCount : 1);

            return result;
        }
    }
}
