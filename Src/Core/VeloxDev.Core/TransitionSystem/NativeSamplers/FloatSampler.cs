namespace VeloxDev.TransitionSystem.NativeSamplers
{
    public class FloatSampler : ISampler
    {
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var f1 = (float)(start ?? 0f);
            var f2 = (float)(end ?? f1);
            property.SetValue(target, f1 + (f2 - f1) * (float)t);
        }
    }
}
