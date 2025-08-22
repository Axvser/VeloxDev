using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.WPF.PlatformAdapters
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
            RegisterInterpolator(typeof(Transform), new NativeInterpolators.TransformInterpolator());
        }
    }
}
