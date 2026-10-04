using Jalium.UI.Media.Media3D;

namespace VeloxDev.Adapters.NativeSamplers
{
    /// <summary>Interpolates <see cref="Transform3D"/>: RotateTransform3D lerps the AxisAngleRotation3D
    /// angle (same axis), otherwise it falls back to Matrix3D component lerp.</summary>
    public class Transform3DSampler : ISampler
    {
        /// <inheritdoc />
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        /// <inheritdoc />
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        /// <inheritdoc />
        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {

            var startT = (Transform3D?)start;
            var endT = (Transform3D?)end;

            if (startT is RotateTransform3D rs && endT is RotateTransform3D re
                && rs.Rotation is AxisAngleRotation3D as1 && re.Rotation is AxisAngleRotation3D as2
                && as1.Axis == as2.Axis)
            {
                // 每帧零分配：复用 scratch RotateTransform3D，从原始 start/end 重算角度。轴也要纳入门禁：
                // scratch 整段动画都保留起始轴，两轴不同会落到「起始轴 + 终点角度」这种两边都不是的姿态；轴不同走矩阵兜底。
                if (working is not RotateTransform3D wt || wt.Rotation is not AxisAngleRotation3D)
                {
                    wt = new RotateTransform3D(new AxisAngleRotation3D(as1.Axis, as1.Angle), rs.CenterX, rs.CenterY, rs.CenterZ);
                    working = wt;
                }
                if (working is RotateTransform3D wt2 && wt2.Rotation is AxisAngleRotation3D wa)
                {
                    wa.Angle = Lerp(as1.Angle, as2.Angle, t);
                    wt2.CenterX = Lerp(rs.CenterX, re.CenterX, t);
                    wt2.CenterY = Lerp(rs.CenterY, re.CenterY, t);
                    wt2.CenterZ = Lerp(rs.CenterZ, re.CenterZ, t);
                    property.SetValue(target, wt2);
                    return;
                }
            }

            // 矩阵兜底：新建 MatrixTransform3D（Value 只读）。
            var m1 = startT?.Value ?? Matrix3D.Identity;
            var m2 = endT?.Value ?? Matrix3D.Identity;
            property.SetValue(target, new MatrixTransform3D(LerpMatrix3D(m1, m2, t)));
        }

        private static double Lerp(double a, double b, double t) => a + t * (b - a);

        private static Matrix3D LerpMatrix3D(Matrix3D m1, Matrix3D m2, double t) => new(
            Lerp(m1.M11, m2.M11, t),
            Lerp(m1.M12, m2.M12, t),
            Lerp(m1.M13, m2.M13, t),
            Lerp(m1.M14, m2.M14, t),
            Lerp(m1.M21, m2.M21, t),
            Lerp(m1.M22, m2.M22, t),
            Lerp(m1.M23, m2.M23, t),
            Lerp(m1.M24, m2.M24, t),
            Lerp(m1.M31, m2.M31, t),
            Lerp(m1.M32, m2.M32, t),
            Lerp(m1.M33, m2.M33, t),
            Lerp(m1.M34, m2.M34, t),
            Lerp(m1.OffsetX, m2.OffsetX, t),
            Lerp(m1.OffsetY, m2.OffsetY, t),
            Lerp(m1.OffsetZ, m2.OffsetZ, t),
            Lerp(m1.M44, m2.M44, t));
    }
}
