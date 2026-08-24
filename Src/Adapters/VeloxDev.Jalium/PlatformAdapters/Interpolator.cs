using Jalium.UI;
using Jalium.UI.Media;
using Jalium.UI.Threading;
using VeloxDev.Adapters.NativeInterpolators;

namespace VeloxDev.TransitionSystem
{
    public class Interpolator : InterpolatorCore<InterpolatorOutput, DispatcherPriority>
    {
        static Interpolator()
        {
            // Exact-type lookup: register BOTH Brush and SolidColorBrush so brush properties
            // declared as either type animate (anchor/port/link fills).
            RegisterInterpolator(typeof(Point), new PointInterpolator());
            RegisterInterpolator(typeof(Rect), new RectInterpolator());
            RegisterInterpolator(typeof(Thickness), new ThicknessInterpolator());
            RegisterInterpolator(typeof(CornerRadius), new CornerRadiusInterpolator());
            RegisterInterpolator(typeof(Size), new SizeInterpolator());
            RegisterInterpolator(typeof(Color), new ColorInterpolator());
            RegisterInterpolator(typeof(Brush), new BrushInterpolator());
            RegisterInterpolator(typeof(SolidColorBrush), new BrushInterpolator());
            RegisterInterpolator(typeof(Transform), new TransformInterpolator());
            RegisterInterpolator(typeof(Jalium.UI.Media.Media3D.Transform3D), new Transform3DInterpolator());
        }
    }
}
