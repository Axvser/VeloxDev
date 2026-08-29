using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using VeloxDev.Adapters.NativeSamplers;

namespace VeloxDev.TransitionSystem
{
    public class Interpolator : InterpolatorCore
    {
        static Interpolator()
        {
            RegisterInterpolator(typeof(IBrush), new BrushSampler());
            RegisterInterpolator(typeof(ITransform), new TransformSampler());
            RegisterInterpolator(typeof(Thickness), new ThicknessSampler());
            RegisterInterpolator(typeof(Point), new PointSampler());
            RegisterInterpolator(typeof(CornerRadius), new CornerRadiusSampler());
            RegisterInterpolator(typeof(Size), new SizeSampler());
            RegisterInterpolator(typeof(PixelPoint), new PixelPointSampler());
            RegisterInterpolator(typeof(PixelSize), new PixelSizeSampler());
            RegisterInterpolator(typeof(PixelRect), new PixelRectSampler());
            RegisterInterpolator(typeof(RelativePoint), new RelativePointSampler());
            RegisterInterpolator(typeof(RelativeRect), new RelativeRectSampler());
            RegisterInterpolator(typeof(Color), new ColorSampler());
            RegisterInterpolator(typeof(BoxShadows), new BoxShadowsSampler());
            RegisterInterpolator(typeof(GridLength), new GridLengthSampler());
        }
    }
}
