using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.MAUI.PlatformAdapters.Interpolators
{
    public class ShadowInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            // 创建默认阴影值
            var defaultShadow = new Shadow
            {
                Offset = new Point(0, 0),
                Radius = 0,
                Opacity = 0,
                Brush = new SolidColorBrush(Colors.Transparent)
            };

            var s1 = (Shadow)(start ?? defaultShadow);
            var s2 = (Shadow)(end ?? defaultShadow);

            if (steps <= 0) return [];
            if (steps == 1) return [s2];

            List<object?> result = new(steps);

            // 处理Brush为null的情况
            var brush1 = s1.Brush ?? new SolidColorBrush(Colors.Transparent);
            var brush2 = s2.Brush ?? new SolidColorBrush(Colors.Transparent);

            var deltaOffsetX = s2.Offset.X - s1.Offset.X;
            var deltaOffsetY = s2.Offset.Y - s1.Offset.Y;
            var deltaRadius = s2.Radius - s1.Radius;
            var deltaOpacity = s2.Opacity - s1.Opacity;

            for (int i = 0; i < steps; i++)
            {
                var t = (float)(i + 1) / steps;

                var shadow = new Shadow
                {
                    Offset = new Point(
                        s1.Offset.X + deltaOffsetX * t,
                        s1.Offset.Y + deltaOffsetY * t
                    ),
                    Radius = s1.Radius + deltaRadius * t,
                    Opacity = Math.Max(0, Math.Min(1, s1.Opacity + deltaOpacity * t)),
                    Brush = t >= 0.5 ? brush2 : brush1 // 简单处理Brush过渡
                };

                result.Add(shadow);
            }

            result[0] = start ?? s1;
            result[steps - 1] = end ?? s2;
            return result;
        }
    }
}
