using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;
using VeloxDev.TransitionSystem;

namespace VeloxDev.WPF.Headless.Tests.Tests;

// 适配器的 .Property(...) 重载是逐个平台类型的薄层；把它们全部走一遍，
// 不需要真的执行动画（执行与计时由 Core 的测试覆盖）。
[TestClass]
public sealed class TransitionBuilderTests
{
    private sealed class Target
    {
        public Brush? Brush { get; set; }
        public Transform? Transform { get; set; }
        public Point Point { get; set; }
        public CornerRadius CornerRadius { get; set; }
        public Thickness Thickness { get; set; }
        public System.Windows.Size Size { get; set; }
        public Rect Rect { get; set; }
        public Vector Vector { get; set; }
        public Color Color { get; set; }
        public DropShadowEffect? Shadow { get; set; }
        public Point3D Point3D { get; set; }
        public Vector3D Vector3D { get; set; }
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
            .Property(x => x.Point, new Point(1d, 2d))
            .Property(x => x.CornerRadius, new CornerRadius(1d))
            .Property(x => x.Thickness, new Thickness(1d))
            .Property(x => x.Size, new System.Windows.Size(1d, 2d))
            .Property(x => x.Rect, new Rect(1d, 2d, 3d, 4d))
            .Property(x => x.Vector, new Vector(1d, 2d))
            .Property(x => x.Color, Colors.Red)
            .Property(x => x.Shadow, new DropShadowEffect())
            .Property(x => x.Point3D, new Point3D(1d, 2d, 3d))
            .Property(x => x.Vector3D, new Vector3D(1d, 2d, 3d))
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
        var bySetter = Transition<Target>.Create().Effect(e => e.Duration = TimeSpan.Zero);
        var byPreset = Transition<Target>.Create().Effect(new TransitionEffect { Duration = TimeSpan.Zero });

        Assert.IsNotNull(bySetter);
        Assert.IsNotNull(byPreset);
    }
}
