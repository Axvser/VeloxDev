using VeloxDev.Core.TransitionSystem;
using VeloxDev.WinForms.Interpolators;

namespace VeloxDev.TransitionSystem
{
    public class Interpolator : InterpolatorCore<InterpolatorOutput>
    {
        static Interpolator()
        {
            RegisterInterpolator(typeof(Padding), new PaddingInterpolator());
        }
    }
}
