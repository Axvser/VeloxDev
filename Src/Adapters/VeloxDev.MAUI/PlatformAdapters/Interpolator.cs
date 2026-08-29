using Microsoft.Maui.Controls.Shapes;
using VeloxDev.Adapters.NativeSamplers;

namespace VeloxDev.TransitionSystem
{
    public class Interpolator : InterpolatorCore
    {
        static Interpolator()
        {
            RegisterInterpolator(typeof(Brush), new BrushSampler());
            RegisterInterpolator(typeof(Thickness), new ThicknessSampler());
            RegisterInterpolator(typeof(Point), new PointSampler());
            RegisterInterpolator(typeof(PointF), new PointFSampler());
            RegisterInterpolator(typeof(CornerRadius), new CornerRadiusSampler());
            RegisterInterpolator(typeof(Transform), new TransformSampler());
            RegisterInterpolator(typeof(Color), new ColorSampler());
            RegisterInterpolator(typeof(Size), new SizeSampler());
            RegisterInterpolator(typeof(SizeF), new SizeFSampler());
            RegisterInterpolator(typeof(Rect), new RectSampler());
            RegisterInterpolator(typeof(RectF), new RectFSampler());
            RegisterInterpolator(typeof(Shadow), new ShadowSampler());
        }
    }
}
