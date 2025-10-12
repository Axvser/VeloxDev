using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using VeloxDev.Core.TransitionSystem;
using Windows.Foundation;

namespace VeloxDev.WinUI.PlatformAdapters
{
    public class Interpolator : InterpolatorCore<InterpolatorOutput, DispatcherQueuePriority>
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
