using Microsoft.Maui.Controls.Shapes;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.MAUI.TransitionSystem
{
    public class Interpolator : InterpolatorCore<InterpolatorOutput>
    {
        static Interpolator()
        {
            RegisterInterpolator(typeof(double), new NativeInterpolators.DoubleInterpolator());
            RegisterInterpolator(typeof(Brush), new NativeInterpolators.BrushInterpolator());
            RegisterInterpolator(typeof(Thickness), new NativeInterpolators.ThicknessInterpolator());
            RegisterInterpolator(typeof(Point), new NativeInterpolators.PointInterpolator());
            RegisterInterpolator(typeof(CornerRadius), new NativeInterpolators.CornerRadiusInterpolator());
            RegisterInterpolator(typeof(Transform), new NativeInterpolators.TransformInterpolator());
        }
    }
}
