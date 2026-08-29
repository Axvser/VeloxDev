namespace VeloxDev.Adapters.NativeSamplers
{
    public class PaddingSampler : ISampleable, ISampler
    {
        public ISampler Normalize(object? start, object? end, object? options) => this;

        public void Update(object target, ITransitionProperty property, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var padding1 = (Padding)(start ?? new Padding(0));
            var padding2 = (Padding)(end ?? padding1);
            property.SetValue(target, new Padding(
                padding1.Left + (int)(t * (padding2.Left - padding1.Left)),
                padding1.Top + (int)(t * (padding2.Top - padding1.Top)),
                padding1.Right + (int)(t * (padding2.Right - padding1.Right)),
                padding1.Bottom + (int)(t * (padding2.Bottom - padding1.Bottom))));
        }
    }
}
