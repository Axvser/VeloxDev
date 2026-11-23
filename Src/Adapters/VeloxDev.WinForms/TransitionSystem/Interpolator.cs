using System.Drawing;
using System.Drawing.Drawing2D;
using VeloxDev.Adapters.NativeSamplers;
using Easel.Controls;

namespace VeloxDev.TransitionSystem
{
    public class Interpolator : InterpolatorCore
    {
        static Interpolator()
        {
            RegisterInterpolator(typeof(Padding), new PaddingSampler());
            RegisterInterpolator(typeof(Thickness), new ThicknessSampler());
            RegisterInterpolator(typeof(Easel.Brush), new BrushSampler());

            // 渲染 DP：Matrix 变换与可空裁剪矩形有采样器；bool 保持不可动画，由 VisualTransition 显式拒绝。
            RegisterInterpolator(typeof(Matrix), new MatrixSampler());
            RegisterInterpolator(typeof(RectangleF?), new NullableRectangleFSampler());
        }

        /// <inheritdoc />
        public override TransitionSchedulerCore? CreateScheduler(object target, ITransitionEffectCore effect)
            => effect is ITransitionEffect<NonPriority>
                ? (TransitionSchedulerCore)TransitionSchedulerCore<UIThreadInspector, TransitionInterpreter, NonPriority>.FindOrCreate(target)
                : null;
    }
}
