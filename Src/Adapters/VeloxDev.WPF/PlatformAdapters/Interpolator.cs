using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
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
            // 注册抽象基类 Effect，不是具体类型 DropShadowEffect：WPF 自己的 UIElement.Effect DP 就是按 Effect 声明的，
            // 而查找只向上走，注册具体类型会让所有声明成 Effect 的属性一条键都查不到（静默 Unsampled）。
            RegisterInterpolator(typeof(Effect), new DropShadowEffectSampler());
            RegisterInterpolator(typeof(Point3D), new Point3DSampler());
            RegisterInterpolator(typeof(Vector3D), new Vector3DSampler());
        }

        public override TransitionSchedulerCore? CreateScheduler(object target, ITransitionEffectCore effect)
            => effect is ITransitionEffect<DispatcherPriority>
                ? (TransitionSchedulerCore)TransitionSchedulerCore<UIThreadInspector, TransitionInterpreter, DispatcherPriority>.FindOrCreate(target)
                : null;
    }
}
