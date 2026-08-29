using System.Windows.Media;
using System.Windows.Media.Effects;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class DropShadowEffectSampler : ISampleable, ISampler
    {
        public ISampler Normalize(object? start, object? end, object? options) => this;

        public void Update(object target, ITransitionProperty property, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            if (start is DropShadowEffect effect1 && end is DropShadowEffect effect2 && !effect1.IsFrozen)
            {
                // 原地修改现有实例，不 new
                effect1.Color = InterpolateColor(effect1.Color, effect2.Color, t);
                effect1.Direction = effect1.Direction + t * (effect2.Direction - effect1.Direction);
                effect1.ShadowDepth = effect1.ShadowDepth + t * (effect2.ShadowDepth - effect1.ShadowDepth);
                effect1.Opacity = effect1.Opacity + t * (effect2.Opacity - effect1.Opacity);
                effect1.BlurRadius = effect1.BlurRadius + t * (effect2.BlurRadius - effect1.BlurRadius);
                return;
            }

            // 冻结/空 → 计算路径（分配）
            var eff1 = start as DropShadowEffect ?? new DropShadowEffect();
            var eff2 = end as DropShadowEffect ?? new DropShadowEffect();
            property.SetValue(target, new DropShadowEffect
            {
                Color = InterpolateColor(eff1.Color, eff2.Color, t),
                Direction = eff1.Direction + t * (eff2.Direction - eff1.Direction),
                ShadowDepth = eff1.ShadowDepth + t * (eff2.ShadowDepth - eff1.ShadowDepth),
                Opacity = eff1.Opacity + t * (eff2.Opacity - eff1.Opacity),
                BlurRadius = eff1.BlurRadius + t * (eff2.BlurRadius - eff1.BlurRadius)
            });
        }

        private static Color InterpolateColor(Color c1, Color c2, double t) => Color.FromArgb(
            (byte)(c1.A + (c2.A - c1.A) * t),
            (byte)(c1.R + (c2.R - c1.R) * t),
            (byte)(c1.G + (c2.G - c1.G) * t),
            (byte)(c1.B + (c2.B - c1.B) * t));
    }
}
