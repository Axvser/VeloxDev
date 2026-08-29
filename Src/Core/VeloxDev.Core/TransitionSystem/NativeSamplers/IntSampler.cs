namespace VeloxDev.TransitionSystem.NativeSamplers
{
    public class IntSampler : ISampleable, ISampler
    {
        public ISampler Normalize(object? start, object? end, object? options) => this;

        public void Update(object target, ITransitionProperty property, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var i1 = (int)(start ?? 0);
            var i2 = (int)(end ?? i1);
            if (i1 == i2) { property.SetValue(target, i1); return; }

            var delta = (double)i2 - i1;
            property.SetValue(target, (int)Math.Round(i1 + t * delta));
        }
    }
}
