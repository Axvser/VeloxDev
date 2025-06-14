using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.MAUI.TransitionSystem
{
    public class Interpolator : InterpolatorCore<InterpolatorOutput, DispatcherPriority>
    {
        static Interpolator()
        {
            RegisterInterpolator(typeof(double), new NativeInterpolators.DoubleInterpolator());
            RegisterInterpolator(typeof(Brush), new NativeInterpolators.BrushInterpolator());
            RegisterInterpolator(typeof(Thickness), new NativeInterpolators.ThicknessInterpolator());
            RegisterInterpolator(typeof(Point), new NativeInterpolators.PointInterpolator());
            RegisterInterpolator(typeof(CornerRadius), new NativeInterpolators.CornerRadiusInterpolator());
        }
    }
}
