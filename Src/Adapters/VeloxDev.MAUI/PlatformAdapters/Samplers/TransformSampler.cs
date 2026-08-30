using Microsoft.Maui.Controls.Shapes;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class TransformSampler : ISampler
    {
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var m1 = start as Transform;
            var m2 = end as Transform;
            var matrix1 = m1?.Value ?? Matrix.Identity;
            var matrix2 = m2?.Value ?? matrix1;

            // Zero per-frame allocation: reuse a scratch Transform, recomputing its Value (a struct) from the pristine start/end.
            if (working is not Transform wt)
            {
                wt = new Transform();
                working = wt;
            }
            wt.Value = new Matrix(
                matrix1.M11 + t * (matrix2.M11 - matrix1.M11),
                matrix1.M12 + t * (matrix2.M12 - matrix1.M12),
                matrix1.M21 + t * (matrix2.M21 - matrix1.M21),
                matrix1.M22 + t * (matrix2.M22 - matrix1.M22),
                matrix1.OffsetX + t * (matrix2.OffsetX - matrix1.OffsetX),
                matrix1.OffsetY + t * (matrix2.OffsetY - matrix1.OffsetY)
            );
            property.SetValue(target, wt);
        }
    }
}
