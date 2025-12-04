using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.WinForms.PlatformAdapters
{
    public class Interpolator : InterpolatorCore<InterpolatorOutput>
    {
        static Interpolator()
        {
            // 数值类型
            RegisterInterpolator(typeof(int), new NativeInterpolators.IntInterpolator());
            RegisterInterpolator(typeof(float), new NativeInterpolators.FloatInterpolator());
            RegisterInterpolator(typeof(double), new NativeInterpolators.DoubleInterpolator());

            // 颜色
            RegisterInterpolator(typeof(Color), new NativeInterpolators.ColorInterpolator());

            // 几何类型
            RegisterInterpolator(typeof(Point), new NativeInterpolators.PointInterpolator());
            RegisterInterpolator(typeof(PointF), new NativeInterpolators.PointFInterpolator());
            RegisterInterpolator(typeof(Size), new NativeInterpolators.SizeInterpolator());
            RegisterInterpolator(typeof(SizeF), new NativeInterpolators.SizeFInterpolator());
            RegisterInterpolator(typeof(Rectangle), new NativeInterpolators.RectangleInterpolator());
            RegisterInterpolator(typeof(RectangleF), new NativeInterpolators.RectangleFInterpolator());

            // 布局
            RegisterInterpolator(typeof(Padding), new NativeInterpolators.PaddingInterpolator());
            RegisterInterpolator(typeof(Font), new NativeInterpolators.FontInterpolator());
        }
    }
}
