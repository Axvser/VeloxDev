using Microsoft.Maui.Controls.Shapes;
using VeloxDev.Core.TransitionSystem;
using VeloxDev.MAUI.PlatformAdapters.Interpolators;

namespace VeloxDev.MAUI.PlatformAdapters
{
    public class Interpolator : InterpolatorCore<InterpolatorOutput>
    {
        static Interpolator()
        {
            RegisterInterpolator(typeof(Brush), new BrushInterpolator());
            RegisterInterpolator(typeof(Thickness), new ThicknessInterpolator());
            RegisterInterpolator(typeof(Point), new PointInterpolator());
            RegisterInterpolator(typeof(PointF), new PointFInterpolator());
            RegisterInterpolator(typeof(CornerRadius), new CornerRadiusInterpolator());
            RegisterInterpolator(typeof(Transform), new TransformInterpolator());
            RegisterInterpolator(typeof(Color), new ColorInterpolator());
            RegisterInterpolator(typeof(Size), new SizeInterpolator());
            RegisterInterpolator(typeof(SizeF), new SizeFInterpolator());
            RegisterInterpolator(typeof(Rect), new RectInterpolator());
            RegisterInterpolator(typeof(RectF), new RectFInterpolator());
            RegisterInterpolator(typeof(Shadow), new ShadowInterpolator());
        }
    }
}
