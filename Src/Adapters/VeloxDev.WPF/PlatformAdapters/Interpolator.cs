using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;
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
            RegisterInterpolator(typeof(CornerRadius), new CornerRadiusSampler());
            RegisterInterpolator(typeof(Transform), new TransformSampler());
            RegisterInterpolator(typeof(Size), new SizeSampler());
            RegisterInterpolator(typeof(Rect), new RectSampler());
            RegisterInterpolator(typeof(Vector), new VectorSampler());
            RegisterInterpolator(typeof(Color), new ColorSampler());
            RegisterInterpolator(typeof(DropShadowEffect), new DropShadowEffectSampler());
            RegisterInterpolator(typeof(Point3D), new Point3DSampler());
            RegisterInterpolator(typeof(Vector3D), new Vector3DSampler());
        }
    }
}
