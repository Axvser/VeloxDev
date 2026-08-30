using System.Windows.Media.Media3D;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class Vector3DSampler : ISampler
    {
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var vector1 = (Vector3D)(start ?? new Vector3D(0, 0, 0));
            var vector2 = (Vector3D)(end ?? vector1);
            property.SetValue(target, new Vector3D(
                vector1.X + t * (vector2.X - vector1.X),
                vector1.Y + t * (vector2.Y - vector1.Y),
                vector1.Z + t * (vector2.Z - vector1.Z)));
        }
    }
}
