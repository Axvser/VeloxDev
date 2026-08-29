using Jalium.UI;
using Jalium.UI.Media;
using Jalium.UI.Threading;
using VeloxDev.Adapters.NativeSamplers;

namespace VeloxDev.TransitionSystem
{
    public class Interpolator : InterpolatorCore
    {
        static Interpolator()
        {
            // Exact-type lookup: register BOTH Brush and SolidColorBrush so brush properties
            // declared as either type animate (anchor/port/link fills).
            RegisterInterpolator(typeof(Point), new PointSampler());
            RegisterInterpolator(typeof(Rect), new RectSampler());
            RegisterInterpolator(typeof(Thickness), new ThicknessSampler());
            RegisterInterpolator(typeof(CornerRadius), new CornerRadiusSampler());
            RegisterInterpolator(typeof(Size), new SizeSampler());
            RegisterInterpolator(typeof(Color), new ColorSampler());
            RegisterInterpolator(typeof(Brush), new BrushSampler());
            RegisterInterpolator(typeof(SolidColorBrush), new BrushSampler());
            RegisterInterpolator(typeof(Transform), new TransformSampler());
            RegisterInterpolator(typeof(Jalium.UI.Media.Media3D.Transform3D), new Transform3DSampler());
        }
    }
}
