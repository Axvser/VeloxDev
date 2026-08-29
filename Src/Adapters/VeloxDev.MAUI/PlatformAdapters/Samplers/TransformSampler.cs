using Microsoft.Maui.Controls.Shapes;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class TransformSampler : ISampleable, ISampler
    {
        public ISampler Normalize(object? start, object? end, object? options) => this;

        public void Update(object target, ITransitionProperty property, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var m1 = start as Transform ?? new Transform() { Value = Matrix.Identity };
            var m2 = end as Transform ?? m1;

            var matrix1 = m1.Value;
            var matrix2 = m2.Value;

            property.SetValue(target, new Transform
            {
                Value = new Matrix(
                    matrix1.M11 + t * (matrix2.M11 - matrix1.M11),
                    matrix1.M12 + t * (matrix2.M12 - matrix1.M12),
                    matrix1.M21 + t * (matrix2.M21 - matrix1.M21),
                    matrix1.M22 + t * (matrix2.M22 - matrix1.M22),
                    matrix1.OffsetX + t * (matrix2.OffsetX - matrix1.OffsetX),
                    matrix1.OffsetY + t * (matrix2.OffsetY - matrix1.OffsetY)
                )
            });
        }
    }
}
