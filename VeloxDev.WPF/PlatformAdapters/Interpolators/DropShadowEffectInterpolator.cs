using System.Windows.Media;
using System.Windows.Media.Effects;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.WPF.PlatformAdapters.Interpolators
{
    public class DropShadowEffectInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            var effect1 = start as DropShadowEffect ?? new DropShadowEffect();
            var effect2 = end as DropShadowEffect ?? new DropShadowEffect();

            if (steps <= 0) return [];
            if (steps == 1) return [effect2];

            List<object?> result = new(steps);
            for (int i = 0; i < steps; i++)
            {
                double t = (double)i / (steps - 1);
                result.Add(new DropShadowEffect
                {
                    Color = InterpolateColor(effect1.Color, effect2.Color, t),
                    Direction = effect1.Direction + t * (effect2.Direction - effect1.Direction),
                    ShadowDepth = effect1.ShadowDepth + t * (effect2.ShadowDepth - effect1.ShadowDepth),
                    Opacity = effect1.Opacity + t * (effect2.Opacity - effect1.Opacity),
                    BlurRadius = effect1.BlurRadius + t * (effect2.BlurRadius - effect1.BlurRadius)
                });
            }

            result[0] = start;
            result[steps - 1] = end;
            return result;
        }

        private static Color InterpolateColor(Color color1, Color color2, double t)
        {
            return Color.FromArgb(
                (byte)(color1.A + (color2.A - color1.A) * t),
                (byte)(color1.R + (color2.R - color1.R) * t),
                (byte)(color1.G + (color2.G - color1.G) * t),
                (byte)(color1.B + (color2.B - color1.B) * t));
        }
    }
}
