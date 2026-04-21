using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using VeloxDev.WinUI.NativeInterpolators;
using Windows.Foundation;
using Windows.UI;

namespace VeloxDev.TransitionSystem
{
    public class Interpolator : InterpolatorCore<InterpolatorOutput, DispatcherQueuePriority>
    {
        static Interpolator()
        {
            RegisterInterpolator(typeof(Brush), new BrushInterpolator());
            RegisterInterpolator(typeof(Thickness), new ThicknessInterpolator());
            RegisterInterpolator(typeof(Point), new PointInterpolator());
            RegisterInterpolator(typeof(CornerRadius), new CornerRadiusInterpolator());
            RegisterInterpolator(typeof(Transform), new TransformInterpolator());
            RegisterInterpolator(typeof(Projection), new ProjectionInterpolator());
            RegisterInterpolator(typeof(Size), new SizeInterpolator());
            RegisterInterpolator(typeof(Rect), new RectInterpolator());
            RegisterInterpolator(typeof(GridLength), new GridLengthInterpolator());
            RegisterInterpolator(typeof(Color), new ColorInterpolator());
        }
    }
}
