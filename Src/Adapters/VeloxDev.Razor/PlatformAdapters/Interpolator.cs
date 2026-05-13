namespace VeloxDev.TransitionSystem
{
    public class Interpolator : InterpolatorCore<InterpolatorOutput>
    {
        static Interpolator()
        {
            RegisterInterpolator(typeof(string), new VeloxDev.Adapters.NativeInterpolators.StringInterpolator());
        }
    }
}
