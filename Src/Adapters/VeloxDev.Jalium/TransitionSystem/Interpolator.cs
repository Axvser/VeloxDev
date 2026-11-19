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
            // 按精确类型查找：Brush 与 SolidColorBrush 都要注册，声明成任一种的画刷属性才能动画（锚点/端口/连线填充）。
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

        /// <inheritdoc />
        public override TransitionSchedulerCore? CreateScheduler(object target, ITransitionEffectCore effect)
            => effect is ITransitionEffect<DispatcherPriority>
                ? (TransitionSchedulerCore)TransitionSchedulerCore<UIThreadInspector, TransitionInterpreter, DispatcherPriority>.FindOrCreate(target)
                : null;
    }
}
