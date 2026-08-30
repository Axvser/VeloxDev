using System.Numerics;

namespace VeloxDev.TransitionSystem.NativeSamplers
{
#if !NETSTANDARD2_0
    public class Vector3Sampler : ISampler
    {
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var v1 = (Vector3)(start ?? default(Vector3));
            var v2 = (Vector3)(end ?? v1);
            property.SetValue(target, v1 + (v2 - v1) * (float)t);
        }
    }
#endif
}
