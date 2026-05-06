using VeloxDev.Adapters.NativeInterpolators;

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
