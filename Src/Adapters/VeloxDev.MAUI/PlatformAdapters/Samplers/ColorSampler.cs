namespace VeloxDev.Adapters.NativeSamplers
{
    public class ColorSampler : ISampler
    {
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            // Handle null values by providing defaults.
            var c1 = (Color)(start ?? Colors.Transparent);
            var c2 = (Color)(end ?? Colors.Transparent);

            // RGB share one progress so an overshoot cannot shift the hue; alpha is its own range. MAUI's channels
            // are floats, so the range is [0,1] rather than [0,255].
            var rgb = new BoundedProgress(t, 0f, 1f);
            rgb.Add(c1.Red, c2.Red);
            rgb.Add(c1.Green, c2.Green);
            rgb.Add(c1.Blue, c2.Blue);

            property.SetValue(target, new Color(
                (float)Channel(rgb.At(c1.Red, c2.Red)),
                (float)Channel(rgb.At(c1.Green, c2.Green)),
                (float)Channel(rgb.At(c1.Blue, c2.Blue)),
                (float)Channel(c1.Alpha + (c2.Alpha - c1.Alpha) * t)
            ));
        }

        /// <summary>Saturates instead of letting an out-of-gamut channel through — MAUI's colour is float-based.</summary>
        private static double Channel(double value)
        {
            if (value <= 0d) return 0d;
            if (value >= 1d) return 1d;
            return value;
        }
    }
}
