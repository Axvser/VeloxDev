using VeloxDev.TransitionSystem;

namespace VeloxDev.Razor.Headless.Tests.Tests;

// Razor 的 .Property(...) 重载；不执行动画。
[TestClass]
public sealed class TransitionBuilderTests
{
    private sealed class Target
    {
        public int Int { get; set; }
        public string? Text { get; set; }
        public double Double { get; set; }
        public float Float { get; set; }
        public decimal Decimal { get; set; }
        public System.Drawing.Point Point { get; set; }
        public System.Drawing.PointF PointF { get; set; }
        public System.Drawing.Size Size { get; set; }
        public System.Drawing.SizeF SizeF { get; set; }
        public System.Drawing.Color Color { get; set; }
        public System.Drawing.Rectangle Rectangle { get; set; }
        public System.Drawing.RectangleF RectangleF { get; set; }
        public System.Numerics.Vector2 Vector2 { get; set; }
        public System.Numerics.Vector3 Vector3 { get; set; }
        public System.Numerics.Vector4 Vector4 { get; set; }
        public System.Numerics.Quaternion Quaternion { get; set; }
    }

    [TestMethod]
    public void EveryPropertyOverload_BuildsATransition()
    {
        var transition = Transition<Target>.Create()
            .Property(x => x.Int, 1)
            .Property(x => x.Text, "x")
            .Property(x => x.Double, 1d)
            .Property(x => x.Float, 1f)
            .Property(x => x.Decimal, 1m)
            .Property(x => x.Point, new System.Drawing.Point(1, 2))
            .Property(x => x.PointF, new System.Drawing.PointF(1f, 2f))
            .Property(x => x.Size, new System.Drawing.Size(1, 2))
            .Property(x => x.SizeF, new System.Drawing.SizeF(1f, 2f))
            .Property(x => x.Color, System.Drawing.Color.Red)
            .Property(x => x.Rectangle, new System.Drawing.Rectangle(1, 2, 3, 4))
            .Property(x => x.RectangleF, new System.Drawing.RectangleF(1f, 2f, 3f, 4f))
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
