using System.Windows.Media;
using System.Windows.Media.Effects;

namespace VeloxDev.Adapters.NativeSamplers
{
    /// <summary>
    /// 服务整个 <see cref="Effect"/> 家族：注册键是 <c>typeof(Effect)</c>（<c>PlatformAdapters/Interpolator.cs</c>），
    /// 所以交到手上的可能是任何一种效果，不只是 <see cref="DropShadowEffect"/>。
    /// </summary>
    /// <remarks>
    /// 插值只对成对的 <see cref="DropShadowEffect"/> 存在；其余（异型，或别的子类）在 <c>t &gt;= 0.5</c> 于两端之间切换 ——
    /// 没有可插的公共面时，选「到达终点」那一支而不是「停在起点」。**任何情况都不再凭空造一个 DropShadowEffect
    /// 去顶替别的效果**：旧兜底分支把非 DropShadowEffect 静默画成阴影，那是注册成 <c>Effect</c> 之后最不能留的一手。
    /// 端点不在 t==0/1 短路（与 <c>TransformSampler</c> 不同）：插值分支只在两端同型时才走，scratch 的运行期类型必与端点
    /// 一致，端点值也由同一套算式给出。
    /// </remarks>
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

            // 两端不是同一类具体效果：没有可插的公共面（BlurEffect 没有 Color/Direction/ShadowDepth，ShaderEffect
            // 更是只有一张自绘面），按阈值切换，并把调用方给的实例原样交出。到达终点优先于停在起点。
            property.SetValue(target, t >= 0.5d ? end : start);
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
