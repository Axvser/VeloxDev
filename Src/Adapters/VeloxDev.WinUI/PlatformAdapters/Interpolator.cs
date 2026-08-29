using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using VeloxDev.Adapters.NativeSamplers;
using Windows.Foundation;
using Windows.UI;

namespace VeloxDev.TransitionSystem
{
    public class Interpolator : InterpolatorCore
    {
        static Interpolator()
        {
            RegisterInterpolator(typeof(Brush), new BrushSampler());
            RegisterInterpolator(typeof(Thickness), new ThicknessSampler());
            RegisterInterpolator(typeof(Point), new PointSampler());
            RegisterInterpolator(typeof(CornerRadius), new CornerRadiusSampler());
            RegisterInterpolator(typeof(Transform), new TransformSampler());
            RegisterInterpolator(typeof(Projection), new ProjectionSampler());
            RegisterInterpolator(typeof(Size), new SizeSampler());
            RegisterInterpolator(typeof(Rect), new RectSampler());
            RegisterInterpolator(typeof(GridLength), new GridLengthSampler());
            RegisterInterpolator(typeof(Color), new ColorSampler());
        }
    }
}
