using Microsoft.UI.Xaml;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class CornerRadiusSampler : ISampler
    {
        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        // CornerRadius 只收非负分量，连构造函数都会 Validate —— 越界是抛异常，不是截断，所以必须钳。
        private static double ClampAtZero(double value) => value <= 0d ? 0d : value;

        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {

            var c1 = start is CornerRadius s ? s : new(0);
            var c2 = end is CornerRadius e ? e : c1;

            // 四个角各自钳制，不共用进度：圆角不是颜色那种"一个通道越界就整组失真"的量，四个分量本就互不相干，
            // 谁先缩到 0 与另外三个无关，逼它们同进同退只会让还能缩的角无谓地停住。
            property.SetValue(target, new CornerRadius(
                ClampAtZero(Lerp(c1.TopLeft, c2.TopLeft, t)),
                ClampAtZero(Lerp(c1.TopRight, c2.TopRight, t)),
                ClampAtZero(Lerp(c1.BottomRight, c2.BottomRight, t)),
                ClampAtZero(Lerp(c1.BottomLeft, c2.BottomLeft, t))));
        }
    }
}
