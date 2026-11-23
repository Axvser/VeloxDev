using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Visuals;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// Render-level optimizations of the WinForms backend: damage-aware child culling and the lower-resolution motion
/// frames painted while a pointer gesture is active.
/// </summary>
[STATestClass]
public sealed class VisualMotionDamageTests
{
    [TestMethod]
    public void PartialDamage_SkipsTheDistantChild()
    {
        using var surface = new VisualHost { Size = new Size(200, 100), DpiScaleOverride = 1f };
        var (near, far) = CreateTwoChildren(surface);

        RenderPartial(surface, new Rectangle(0, 0, 60, 60));

        Assert.AreEqual(1, near.RenderCount, "the child inside the damage is painted");
        Assert.AreEqual(0, far.RenderCount, "the child wholly outside the damage is culled");
    }

    [TestMethod]
    public void FullDamage_PaintsEveryChild()
    {
        using var surface = new VisualHost { Size = new Size(200, 100), DpiScaleOverride = 1f };
        var (near, far) = CreateTwoChildren(surface);

        RenderPartial(surface, new Rectangle(0, 0, 60, 60));
        near.RenderCount = 0;
        far.RenderCount = 0;

        using var bitmap = new Bitmap(200, 100);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            surface.RenderTo(graphics, new Size(200, 100));
        }

        Assert.AreEqual(1, near.RenderCount, "the headless path always paints the full tree");
        Assert.AreEqual(1, far.RenderCount);
    }

    [TestMethod]
    public void PartialDamage_KeepsBitmapCachesAndEffects()
    {
        using var surface = new VisualHost { Size = new Size(200, 100), DpiScaleOverride = 1f };
        var root = new ContainerVisual();
        var cached = new CountingVisual
        {
            Bounds = new RectangleF(150f, 4f, 40f, 40f),
            CacheAsBitmap = true,
        };
        var effected = new CountingVisual
        {
            Bounds = new RectangleF(150f, 80f, 40f, 20f),
            Effect = new DropShadowEffect(),
        };
        root.Add(cached);
        root.Add(effected);
        surface.Root = root;

        RenderPartial(surface, new Rectangle(0, 0, 60, 40));

        Assert.AreEqual(1, cached.RenderCount, "a bitmap cache may paint outside its bounds, so it is not culled");
        Assert.AreEqual(1, effected.RenderCount, "an effect adds padding, so the visual is not culled");
    }

    [TestMethod]
    public void GestureMotionFrame_RendersIntoADownscaledBitmap()
    {
        using var surface = new VisualHost { Size = new Size(200, 100), DpiScaleOverride = 1f };
        var root = new ContainerVisual();
        root.Add(new CountingVisual { Bounds = new RectangleF(0f, 0f, 200f, 100f) });
        surface.Root = root;

        Assert.AreEqual(1f, surface.MotionScale, 0.0001f, "motion rendering is off by default");
        surface.MotionScale = 0.65f;
        Assert.IsFalse(surface.IsPointerGestureActive);

        surface.ProcessPointerDown(new PointF(10f, 10f), MouseButtons.Left);
        Assert.IsTrue(surface.IsPointerGestureActive, "a synthesized press starts a gesture");

        var before = surface.LastPaintTimestamp;
        RenderPartial(surface, new Rectangle(0, 0, 200, 100));

        var motionBitmap = GetMotionBitmap(surface);
        Assert.IsNotNull(motionBitmap, "the gesture frame is drawn through the offscreen bitmap");
        Assert.AreEqual(130, motionBitmap!.Width, "200 logical units at 0.65 are 130 pixels");
        Assert.AreEqual(65, motionBitmap.Height);
        Assert.IsGreaterThan(before, surface.LastPaintTimestamp, "the motion frame still advances the paint clock");

        surface.ProcessPointerUp(new PointF(10f, 10f), MouseButtons.Left);
        Assert.IsFalse(surface.IsPointerGestureActive, "the release ends the gesture");
    }

    [TestMethod]
    public void RenderTo_StaysFullResolutionDuringAGesture()
    {
        using var surface = new VisualHost { Size = new Size(200, 100), DpiScaleOverride = 1f };
        var root = new ContainerVisual();
        root.Add(new CountingVisual { Bounds = new RectangleF(0f, 0f, 200f, 100f) });
        surface.Root = root;

        surface.ProcessPointerDown(new PointF(10f, 10f), MouseButtons.Left);
        Assert.IsTrue(surface.IsPointerGestureActive);

        using var bitmap = new Bitmap(200, 100);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            surface.RenderTo(graphics, new Size(200, 100));
        }

        Assert.IsNull(GetMotionBitmap(surface), "the headless path never allocates the motion bitmap");
    }

    private static (CountingVisual Near, CountingVisual Far) CreateTwoChildren(VisualHost surface)
    {
        var root = new ContainerVisual();
        var near = new CountingVisual { Bounds = new RectangleF(4f, 4f, 40f, 40f) };
        var far = new CountingVisual { Bounds = new RectangleF(150f, 4f, 40f, 40f) };
        root.Add(near);
        root.Add(far);
        surface.Root = root;
        return (near, far);
    }

    // RenderPhysical 是 internal 的真实窗口绘制入口；测试用反射按给定物理脏区驱动它。
    private static void RenderPartial(VisualHost surface, Rectangle clip)
    {
        var method = typeof(VisualHost).GetMethod(
            "RenderPhysical",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method, "VisualHost.RenderPhysical exists");

        using var bitmap = new Bitmap(Math.Max(1, clip.Right), Math.Max(1, clip.Bottom));
        using var graphics = Graphics.FromImage(bitmap);
        method!.Invoke(surface, new object[] { graphics, clip });
    }

    private static Bitmap? GetMotionBitmap(VisualHost surface)
        => (Bitmap?)typeof(VisualHost)
            .GetField("_motionBitmap", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(surface);

    private sealed class CountingVisual : Visual
    {
        public int RenderCount { get; set; }

        protected override void RenderContent(Graphics graphics) => RenderCount++;
    }
}
