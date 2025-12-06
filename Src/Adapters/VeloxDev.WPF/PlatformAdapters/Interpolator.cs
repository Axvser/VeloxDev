using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using VeloxDev.Core.TransitionSystem;
using VeloxDev.WPF.PlatformAdapters.Interpolators;

namespace VeloxDev.WPF.PlatformAdapters
{
    public class Interpolator : InterpolatorCore<InterpolatorOutput, DispatcherPriority>
    {
        static Interpolator()
        {
            RegisterInterpolator(typeof(Brush), new BrushInterpolator());
            RegisterInterpolator(typeof(Thickness), new ThicknessInterpolator());
            RegisterInterpolator(typeof(Point), new PointInterpolator());
            RegisterInterpolator(typeof(CornerRadius), new CornerRadiusInterpolator());
            RegisterInterpolator(typeof(Transform), new TransformInterpolator());
            RegisterInterpolator(typeof(Size), new SizeInterpolator());
            RegisterInterpolator(typeof(Rect), new RectInterpolator());
            RegisterInterpolator(typeof(Vector), new VectorInterpolator());
            RegisterInterpolator(typeof(Color), new ColorInterpolator());
            RegisterInterpolator(typeof(DropShadowEffect), new DropShadowEffectInterpolator());
            RegisterInterpolator(typeof(Point3D), new Point3DInterpolator());
            RegisterInterpolator(typeof(Vector3D), new Vector3DInterpolator());
        }
    }
}
