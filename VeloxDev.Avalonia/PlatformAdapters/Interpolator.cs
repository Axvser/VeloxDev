using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using VeloxDev.Avalonia.PlatformAdapters.Interpolators;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.Avalonia.PlatformAdapters
{
    public class Interpolator : InterpolatorCore<InterpolatorOutput, DispatcherPriority>
    {
        static Interpolator()
        {
            RegisterInterpolator(typeof(IBrush), new BrushInterpolator());
            RegisterInterpolator(typeof(ITransform), new TransformInterpolator());
            RegisterInterpolator(typeof(Thickness), new ThicknessInterpolator());
            RegisterInterpolator(typeof(Point), new PointInterpolator());
            RegisterInterpolator(typeof(CornerRadius), new CornerRadiusInterpolator());
            RegisterInterpolator(typeof(Size), new SizeInterpolator());
            RegisterInterpolator(typeof(PixelPoint), new PixelPointInterpolator());
            RegisterInterpolator(typeof(PixelSize), new PixelSizeInterpolator());
            RegisterInterpolator(typeof(PixelRect), new PixelRectInterpolator());
            RegisterInterpolator(typeof(RelativePoint), new RelativePointInterpolator());
            RegisterInterpolator(typeof(RelativeRect), new RelativeRectInterpolator());
            RegisterInterpolator(typeof(Color), new ColorInterpolator());
            RegisterInterpolator(typeof(BoxShadows), new BoxShadowsInterpolator());
            RegisterInterpolator(typeof(GridLength), new GridLengthInterpolator());
        }
    }
}
