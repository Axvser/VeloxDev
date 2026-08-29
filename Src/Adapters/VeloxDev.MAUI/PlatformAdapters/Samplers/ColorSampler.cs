namespace VeloxDev.Adapters.NativeSamplers
{
    public class ColorSampler : ISampleable, ISampler
    {
        public ISampler Normalize(object? start, object? end, object? options) => this;

        public void Update(object target, ITransitionProperty property, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            // Handle null values by providing defaults.
            var c1 = (Color)(start ?? Colors.Transparent);
            var c2 = (Color)(end ?? Colors.Transparent);

            var deltaA = c2.Alpha - c1.Alpha;
            var deltaR = c2.Red - c1.Red;
            var deltaG = c2.Green - c1.Green;
            var deltaB = c2.Blue - c1.Blue;

            property.SetValue(target, new Color(
                c1.Red + deltaR * (float)t,
                c1.Green + deltaG * (float)t,
                c1.Blue + deltaB * (float)t,
                c1.Alpha + deltaA * (float)t
            ));
        }
    }
}
