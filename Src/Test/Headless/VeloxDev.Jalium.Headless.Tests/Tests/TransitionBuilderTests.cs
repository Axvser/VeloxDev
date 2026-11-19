using VeloxDev.TransitionSystem;
using JaliumBrush = global::Jalium.UI.Media.Brush;
using JaliumColor = global::Jalium.UI.Media.Color;
using JaliumCornerRadius = global::Jalium.UI.CornerRadius;
using JaliumPoint = global::Jalium.UI.Point;
using JaliumRect = global::Jalium.UI.Rect;
using JaliumSize = global::Jalium.UI.Size;
using JaliumThickness = global::Jalium.UI.Thickness;
using JaliumTransform = global::Jalium.UI.Media.Transform;
using JaliumTransform3D = global::Jalium.UI.Media.Media3D.Transform3D;
using JaliumTranslateTransform = global::Jalium.UI.Media.TranslateTransform;

namespace VeloxDev.Jalium.Headless.Tests.Tests;

// Jalium 的 .Property(...) 重载；不执行动画。
[TestClass]
public sealed class TransitionBuilderTests
{
    private sealed class Target
    {
        public JaliumBrush? Brush { get; set; }
        public JaliumTransform? Transform { get; set; }
        public JaliumTransform3D? Transform3D { get; set; }
        public JaliumPoint Point { get; set; }
        public JaliumRect Rect { get; set; }
        public JaliumThickness Thickness { get; set; }
        public JaliumCornerRadius CornerRadius { get; set; }
        public JaliumSize Size { get; set; }
        public JaliumColor Color { get; set; }
        public int Int { get; set; }
        public double Double { get; set; }
        public float Float { get; set; }
    }

    [TestMethod]
    public void EveryPropertyOverload_BuildsATransition()
    {
        var transition = Transition<Target>.Create()
            .Property(x => x.Brush, (JaliumBrush?)null)
            .Property(x => x.Transform, [new JaliumTranslateTransform(1d, 2d)])
            .Property(x => x.Transform3D, (JaliumTransform3D?)null)
            .Property(x => x.Point, new JaliumPoint(1d, 2d))
            .Property(x => x.Rect, new JaliumRect(0d, 0d, 1d, 2d))
            .Property(x => x.Thickness, new JaliumThickness(1d))
            .Property(x => x.CornerRadius, new JaliumCornerRadius(1d))
            .Property(x => x.Size, new JaliumSize(1d, 2d))
            .Property(x => x.Color, default)
            .Property(x => x.Int, 1)
            .Property(x => x.Double, 1d)
            .Property(x => x.Float, 1f);

        Assert.IsNotNull(transition);
    }

    [TestMethod]
    public void EffectOverloads_AcceptPresetAndSetter()
    {
        Assert.IsNotNull(Transition<Target>.Create().Effect(e => e.Duration = TimeSpan.Zero));
        Assert.IsNotNull(Transition<Target>.Create().Effect(new TransitionEffect { Duration = TimeSpan.Zero }));
    }
}
