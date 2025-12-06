using VeloxDev.Core.TransitionSystem;
using VeloxDev.WinForms.PlatformAdapters.Interpolators;

namespace VeloxDev.WinForms.PlatformAdapters
{
    public class Interpolator : InterpolatorCore<InterpolatorOutput>
    {
        static Interpolator()
        {
            RegisterInterpolator(typeof(Padding), new PaddingInterpolator());
        }
    }
}
