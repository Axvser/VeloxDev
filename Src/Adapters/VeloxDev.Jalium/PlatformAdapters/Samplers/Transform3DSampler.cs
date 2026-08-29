using Jalium.UI.Media.Media3D;

namespace VeloxDev.Adapters.NativeSamplers
{
    /// <summary>Interpolates <see cref="Transform3D"/>: RotateTransform3D lerps the AxisAngleRotation3D
    /// angle (same axis), otherwise it falls back to Matrix3D component lerp.</summary>
    public class Transform3DSampler : ISampleable, ISampler
    {
        public ISampler Normalize(object? start, object? end, object? options) => this;

        public void Update(object target, ITransitionProperty property, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var startT = (Transform3D?)start;
            var endT = (Transform3D?)end;
            property.SetValue(target, InterpolateSingle(startT, endT, t));
        }

        private static Transform3D InterpolateSingle(Transform3D? start, Transform3D? end, double t)
        {
            if (start is RotateTransform3D rs && end is RotateTransform3D re
                && rs.Rotation is AxisAngleRotation3D as1 && re.Rotation is AxisAngleRotation3D as2)
            {
                return new RotateTransform3D(
                    new AxisAngleRotation3D(as2.Axis, Lerp(as1.Angle, as2.Angle, t)),
                    Lerp(rs.CenterX, re.CenterX, t),
                    Lerp(rs.CenterY, re.CenterY, t),
                    Lerp(rs.CenterZ, re.CenterZ, t));
            }

            var m1 = start?.Value ?? Matrix3D.Identity;
            var m2 = end?.Value ?? Matrix3D.Identity;
            return new MatrixTransform3D(LerpMatrix3D(m1, m2, t));
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
