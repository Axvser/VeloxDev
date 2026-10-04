using System.Numerics;

namespace VeloxDev.TransitionSystem.NativeSamplers
{
#if !NETSTANDARD2_0
    /// <summary>Samples a <see cref="Quaternion"/>.</summary>
    public class QuaternionSampler : ISampler
    {
        /// <inheritdoc />
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        /// <inheritdoc />
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        /// <inheritdoc />
        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            // 端点要精确，不能用区间。流水线每一趟的最后一帧正好传 1（反向趟传 0），而调用方自己的
            // 实例必须活到最后：像 ((TranslateTransform)x.RenderTransform).X 这种嵌套路径依赖声明时的
            // 运行时类型，插值的临时对象会把它换掉。超过端点的超调落到下面的通用分支。
            if (t == 0d) { property.SetValue(target, start); return; }
            if (t == 1d) { property.SetValue(target, end); return; }

            var q1 = (Quaternion)(start ?? Quaternion.Identity);
            var q2 = (Quaternion)(end ?? q1);
            var direction = options is RotationDirection d ? d : RotationDirection.Auto;
            property.SetValue(target, SlerpDirectional(q1, q2, (float)t, direction));
        }

        private static Quaternion SlerpDirectional(Quaternion q1, Quaternion q2, float t, RotationDirection direction)
        {
            if (direction == RotationDirection.Auto)
                return Quaternion.Slerp(q1, q2, t);

            var dot = q1.X * q2.X + q1.Y * q2.Y + q1.Z * q2.Z + q1.W * q2.W;

            // ClockWise => dot 应为正（最短的正向旋转），必要时强制；
            // CounterClockWise => dot 应为负（取反 q2 走长路）。
            bool wantNegate = direction.HasFlag(RotationDirection.CounterClockWise) && dot > 0f
                           || direction.HasFlag(RotationDirection.ClockWise) && dot < 0f;

            if (wantNegate)
                q2 = new Quaternion(-q2.X, -q2.Y, -q2.Z, -q2.W);

            return Quaternion.Slerp(q1, q2, t);
        }
    }
#endif
}
