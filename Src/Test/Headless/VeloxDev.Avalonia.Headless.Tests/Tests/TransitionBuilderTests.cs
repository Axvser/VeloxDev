using Avalonia;
using Avalonia.Media;
using VeloxDev.TransitionSystem;

namespace VeloxDev.Avalonia.Headless.Tests.Tests;

// 适配器的 .Property(...) 重载逐个走一遍；不执行动画。
[TestClass]
public sealed class TransitionBuilderTests
{
    private sealed class Target
    {
        public IBrush? Brush { get; set; }
        public ITransform? Transform { get; set; }
        public Point Point { get; set; }
        public CornerRadius CornerRadius { get; set; }
        public Thickness Thickness { get; set; }
        public Size Size { get; set; }
        public PixelPoint PixelPoint { get; set; }
        public PixelSize PixelSize { get; set; }
        public PixelRect PixelRect { get; set; }
        public RelativePoint RelativePoint { get; set; }
        public RelativeRect RelativeRect { get; set; }
        public Color Color { get; set; }
        public BoxShadows BoxShadows { get; set; }
        public int Int { get; set; }
        public double Double { get; set; }
        public float Float { get; set; }
        public decimal Decimal { get; set; }
        public System.Drawing.Point DrawingPoint { get; set; }
        public System.Drawing.PointF DrawingPointF { get; set; }
        public System.Drawing.Size DrawingSize { get; set; }
        public System.Drawing.SizeF DrawingSizeF { get; set; }
        public System.Drawing.Color DrawingColor { get; set; }
        public System.Drawing.Rectangle DrawingRectangle { get; set; }
        public System.Drawing.RectangleF DrawingRectangleF { get; set; }
        public System.Numerics.Vector2 Vector2 { get; set; }
        public System.Numerics.Vector3 Vector3 { get; set; }
        public System.Numerics.Vector4 Vector4 { get; set; }
        public System.Numerics.Quaternion Quaternion { get; set; }
    }

    [TestMethod]
    public void EveryPropertyOverload_BuildsATransition()
    {
        var transition = Transition<Target>.Create()
            .Property(x => x.Brush, Brushes.Red)
            .Property(x => x.Transform, [new TranslateTransform(1d, 2d)])
            .Property(x => x.Transform, new TranslateTransform(1d, 2d))
            .Property(x => x.Point, new Point(1d, 2d))
            .Property(x => x.CornerRadius, new CornerRadius(1d))
            .Property(x => x.Thickness, new Thickness(1d))
            .Property(x => x.Size, new Size(1d, 2d))
            .Property(x => x.PixelPoint, new PixelPoint(1, 2))
            .Property(x => x.PixelSize, new PixelSize(1, 2))
            .Property(x => x.PixelRect, new PixelRect(0, 0, 1, 2))
            .Property(x => x.RelativePoint, new RelativePoint(1d, 2d, RelativeUnit.Absolute))
            .Property(x => x.RelativeRect, new RelativeRect(0d, 0d, 1d, 2d, RelativeUnit.Absolute))
            .Property(x => x.Color, Colors.Red)
            .Property(x => x.BoxShadows, default(BoxShadows))
            .Property(x => x.Int, 1)
            .Property(x => x.Double, 1d)
            .Property(x => x.Float, 1f)
            .Property(x => x.Decimal, 1m)
            .Property(x => x.DrawingPoint, new System.Drawing.Point(1, 2))
            .Property(x => x.DrawingPointF, new System.Drawing.PointF(1f, 2f))
            .Property(x => x.DrawingSize, new System.Drawing.Size(1, 2))
            .Property(x => x.DrawingSizeF, new System.Drawing.SizeF(1f, 2f))
            .Property(x => x.DrawingColor, System.Drawing.Color.Red)
            .Property(x => x.DrawingRectangle, new System.Drawing.Rectangle(1, 2, 3, 4))
            .Property(x => x.DrawingRectangleF, new System.Drawing.RectangleF(1f, 2f, 3f, 4f))
            .Property(x => x.Vector2, new System.Numerics.Vector2(1f, 2f))
            .Property(x => x.Vector3, new System.Numerics.Vector3(1f, 2f, 3f))
            .Property(x => x.Vector4, new System.Numerics.Vector4(1f, 2f, 3f, 4f))
            .Property(x => x.Quaternion, System.Numerics.Quaternion.Identity);

        Assert.IsNotNull(transition);
    }

    [TestMethod]
    public void EffectOverloads_AcceptPresetAndSetter()
    {
        Assert.IsNotNull(Transition<Target>.Create().Effect(e => e.Duration = TimeSpan.Zero));
        Assert.IsNotNull(Transition<Target>.Create().Effect(new TransitionEffect { Duration = TimeSpan.Zero }));
    }
}
