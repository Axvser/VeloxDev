using System.Windows.Media;
using System.Windows.Media.Effects;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class DropShadowEffectSampler : ISampler
    {
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {

            if (start is DropShadowEffect e1 && end is DropShadowEffect e2)
            {
                // Zero per-frame allocation: reuse a scratch effect, recomputing from the pristine start/end each frame.
                if (working is not DropShadowEffect we)
                {
                    we = new DropShadowEffect();
                    working = we;
                }
                we.Color = InterpolateColor(e1.Color, e2.Color, t);
                we.Direction = e1.Direction + t * (e2.Direction - e1.Direction);
                we.ShadowDepth = e1.ShadowDepth + t * (e2.ShadowDepth - e1.ShadowDepth);
                we.Opacity = Math.Max(0, Math.Min(1, e1.Opacity + t * (e2.Opacity - e1.Opacity)));
                we.BlurRadius = e1.BlurRadius + t * (e2.BlurRadius - e1.BlurRadius);
                property.SetValue(target, we);
                return;
            }

            // Fallback: allocate a fresh effect (start/end are never mutated).
            var eff1 = start as DropShadowEffect ?? new DropShadowEffect();
            var eff2 = end as DropShadowEffect ?? new DropShadowEffect();
            property.SetValue(target, new DropShadowEffect
            {
                Color = InterpolateColor(eff1.Color, eff2.Color, t),
                Direction = eff1.Direction + t * (eff2.Direction - eff1.Direction),
                ShadowDepth = eff1.ShadowDepth + t * (eff2.ShadowDepth - eff1.ShadowDepth),
                Opacity = Math.Max(0, Math.Min(1, eff1.Opacity + t * (eff2.Opacity - eff1.Opacity))),
                BlurRadius = eff1.BlurRadius + t * (eff2.BlurRadius - eff1.BlurRadius)
            });
        }

        private static Color InterpolateColor(Color c1, Color c2, double t)
        {
            // R/G/B share one progress so an overshoot cannot shift the hue; alpha is its own range.
            var rgb = new BoundedProgress(t, 0d, 255d);
            rgb.Add(c1.R, c2.R);
            rgb.Add(c1.G, c2.G);
            rgb.Add(c1.B, c2.B);

            return Color.FromArgb(
                Channel(c1.A + (c2.A - c1.A) * t),
                Channel(rgb.At(c1.R, c2.R)),
                Channel(rgb.At(c1.G, c2.G)),
                Channel(rgb.At(c1.B, c2.B)));
        }

        /// <summary>Saturates instead of wrapping — a bare byte cast turns 300 into 44.</summary>
        private static byte Channel(double value)
        {
            if (value <= 0d) return 0;
            if (value >= 255d) return 255;
            return (byte)value;
        }
    }
}
