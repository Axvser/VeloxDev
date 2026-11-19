using System.Drawing;
using System.Windows.Forms;
using VeloxDev.TransitionSystem;

namespace VeloxDev.WinForms.Headless.Tests.Tests;

// WinForms 的 .Property(...) 重载；不执行动画。
[TestClass]
public sealed class TransitionBuilderTests
{
    private sealed class Target
    {
        public Padding Padding { get; set; }
        public int Int { get; set; }
        public double Double { get; set; }
        public float Float { get; set; }
        public decimal Decimal { get; set; }
        public Point Point { get; set; }
        public PointF PointF { get; set; }
        public Size Size { get; set; }
        public SizeF SizeF { get; set; }
        public Color Color { get; set; }
        public Rectangle Rectangle { get; set; }
        public RectangleF RectangleF { get; set; }
        public System.Numerics.Vector2 Vector2 { get; set; }
        public System.Numerics.Vector3 Vector3 { get; set; }
        public System.Numerics.Vector4 Vector4 { get; set; }
        public System.Numerics.Quaternion Quaternion { get; set; }
    }

    [TestMethod]
    public void EveryPropertyOverload_BuildsATransition()
    {
        var transition = Transition<Target>.Create()
            .Property(x => x.Padding, new Padding(1))
            .Property(x => x.Int, 1)
            .Property(x => x.Double, 1d)
            .Property(x => x.Float, 1f)
            .Property(x => x.Decimal, 1m)
            .Property(x => x.Point, new Point(1, 2))
            .Property(x => x.PointF, new PointF(1f, 2f))
            .Property(x => x.Size, new Size(1, 2))
            .Property(x => x.SizeF, new SizeF(1f, 2f))
            .Property(x => x.Color, Color.Red)
            .Property(x => x.Rectangle, new Rectangle(1, 2, 3, 4))
            .Property(x => x.RectangleF, new RectangleF(1f, 2f, 3f, 4f))
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
