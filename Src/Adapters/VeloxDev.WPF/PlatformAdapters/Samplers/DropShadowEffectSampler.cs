using System.Windows.Media;
using System.Windows.Media.Effects;

namespace VeloxDev.Adapters.NativeSamplers
{
    /// <summary>
    /// Interpolates the whole <see cref="Effect"/> family. The sampler is registered under
    /// <c>typeof(Effect)</c>, so the values it receives may be any effect, not only a
    /// <see cref="DropShadowEffect"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per-field interpolation exists only for a pair of <see cref="DropShadowEffect"/> instances. Any other
    /// pair switches between the two endpoints at <c>t &gt;= 0.5</c>, preferring the end value; it never
    /// fabricates a <see cref="DropShadowEffect"/> to stand in for a different effect.
    /// </para>
    /// </remarks>
    public class DropShadowEffectSampler : ISampler
    {
        /// <inheritdoc />
        public object? NormalizeStart(object? start, object? end, object? options) => start;

        /// <inheritdoc />
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        /// <inheritdoc />
        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {

            if (start is DropShadowEffect e1 && end is DropShadowEffect e2)
            {
                // 每帧零分配：复用同一个 scratch 实例，每帧都从原始的 start/end 重算。
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

            // 两端不是同一类具体效果：没有可插的公共面（BlurEffect 没有 Color/Direction/ShadowDepth，ShaderEffect
            // 更是只有一张自绘面），按阈值切换，并把调用方给的实例原样交出。到达终点优先于停在起点。
            property.SetValue(target, t >= 0.5d ? end : start);
        }

        private static Color InterpolateColor(Color c1, Color c2, double t)
        {
            // R/G/B 共用同一进度，越界时不会偏色；alpha 走自己的范围。
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

        // 饱和处理而非回绕 —— 直接转 byte 会把 300 变成 44。
        private static byte Channel(double value)
        {
            if (value <= 0d) return 0;
            if (value >= 255d) return 255;
            return (byte)value;
        }
    }
}
