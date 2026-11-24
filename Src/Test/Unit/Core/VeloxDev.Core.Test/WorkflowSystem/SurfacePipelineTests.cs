using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

/// <summary>
/// Contracts of the Core surface pipeline: the wheel step and the single-axis rule, canvas growth on
/// overscroll, centering, the one-shot viewport restore, and the metrics/placement push.
/// </summary>
[TestClass]
public class SurfacePipelineTests
{
    // ── Fake hosts ──────────────────────────────────────────────────────────

    private sealed class FakeScrollHost : ISurfaceScrollHost
    {
        public CanvasLayout? Layout { get; set; } = new CanvasLayout();

        public double ViewportWidth { get; set; } = 800d;

        public double ViewportHeight { get; set; } = 600d;

        public double HorizontalOffset { get; private set; }

        public double VerticalOffset { get; private set; }

        public double HorizontalMaximum { get; set; } = 1000d;

        public double VerticalMaximum { get; set; } = 1000d;

        public SurfaceWheelProfile WheelProfile { get; set; } = SurfaceWheelProfile.FromLines(120d, 3d, 16d);

        public int ExpandCalls { get; private set; }

        public Action? OnExpand { get; set; }

        public List<(double? X, double? Y)> Applied { get; } = [];

        public void SetOffsets(double horizontal, double vertical)
        {
            HorizontalOffset = horizontal;
            VerticalOffset = vertical;
        }

        public void ApplyOffset(double? horizontalOffset, double? verticalOffset)
        {
            Applied.Add((horizontalOffset, verticalOffset));
            if (horizontalOffset is { } x) HorizontalOffset = x;
            if (verticalOffset is { } y) VerticalOffset = y;
        }

        public void ExpandCanvas()
        {
            ExpandCalls++;
            OnExpand?.Invoke();
        }
    }

    private sealed class FakeRestoreHost : ISurfaceViewportRestoreHost
    {
        public bool IsRestoreEnabled { get; set; } = true;

        public double HorizontalMaximum { get; set; } = 1000d;

        public double VerticalMaximum { get; set; } = 1000d;

        public int DeferCalls { get; private set; }

        public Action? Deferred { get; private set; }

        public (double Horizontal, double Vertical)? Applied { get; private set; }

        public void ApplyOffset(double horizontalOffset, double verticalOffset)
            => Applied = (horizontalOffset, verticalOffset);

        public void DeferRestore(Action restore)
        {
            DeferCalls++;
            Deferred = restore;
        }

        public void Run() => Deferred?.Invoke();
    }

    private sealed class FakeGridDecorator : IWorkflowGridDecorator
    {
        public double ScrollOffsetX { get; set; }
        public double ScrollOffsetY { get; set; }
        public double ContentOffsetX { get; set; }
        public double ContentOffsetY { get; set; }
        public double RulerBand { get; set; }
    }

    private sealed class FakeMinimap : IWorkflowMinimapOverlay
    {
        public double ScrollOffsetX { get; set; }
        public double ScrollOffsetY { get; set; }
        public double ContentOffsetX { get; set; }
        public double ContentOffsetY { get; set; }
        public double RulerBand { get; set; }
        public double ViewportWidth { get; set; }
        public double ViewportHeight { get; set; }
        public ITreeComponent? WorkflowTree { get; set; }
        public bool IsMinimapVisible { get; set; }
        public double AvailableRight { get; set; }
        public double AvailableTop { get; set; }
        public double AvailableBottom { get; set; }
        public double MinimapMargin { get; set; }
    }

    // ── Wheel steps and single-axis writes ─────────────────────────────────

    [TestMethod]
    public void ScrollBy_VerticalNotch_UsesLinesTimesLineHeight()
    {
        var host = new FakeScrollHost();
        host.SetOffsets(0d, 100d);
        var scroller = new SurfaceScrollerCore(host);

        // 3 lines × 16 = 48; a positive wheel delta moves towards the content above, i.e. the offset decreases.
        scroller.ScrollBy(0d, 120d);

        Assert.AreEqual(52d, host.VerticalOffset, 1e-9);
        Assert.AreEqual(1, host.Applied.Count);
        Assert.IsNull(host.Applied[0].X, "a zero horizontal delta must not write the horizontal axis");
        Assert.AreEqual(52d, host.Applied[0].Y);
    }

    [TestMethod]
    public void ScrollBy_HorizontalNotch_MovesOnlyTheHorizontalAxis()
    {
        var host = new FakeScrollHost();
        host.SetOffsets(250d, 100d);
        var scroller = new SurfaceScrollerCore(host);

        scroller.ScrollBy(120d, 0d);

        Assert.AreEqual(298d, host.HorizontalOffset, 1e-9);
        Assert.AreEqual(100d, host.VerticalOffset, 1e-9);
        Assert.AreEqual(298d, host.Applied[0].X);
        Assert.IsNull(host.Applied[0].Y);
    }

    [TestMethod]
    public void ScrollBy_BothAxes_WritesBothInOneCall()
    {
        var host = new FakeScrollHost();
        host.SetOffsets(0d, 100d);
        var scroller = new SurfaceScrollerCore(host);

        scroller.ScrollBy(120d, 120d);

        Assert.AreEqual(48d, host.HorizontalOffset, 1e-9);
        Assert.AreEqual(52d, host.VerticalOffset, 1e-9);
        Assert.IsNotNull(host.Applied[0].X);
        Assert.IsNotNull(host.Applied[0].Y);
    }

    [TestMethod]
    public void ScrollBy_PixelProfile_UsesPixelsPerNotch()
    {
        var host = new FakeScrollHost
        {
            // Avalonia / Jalium report one notch as a delta of 1 and scroll 50 / 48 pixels.
            WheelProfile = SurfaceWheelProfile.FromPixels(1d, 50d),
        };
        var scroller = new SurfaceScrollerCore(host);

        scroller.ScrollBy(1d, 0d);

        Assert.AreEqual(50d, host.HorizontalOffset, 1e-9);
    }

    [TestMethod]
    public void ScrollBy_WithoutLayout_ClampsAtTheNativeEdge()
    {
        var host = new FakeScrollHost { Layout = null };
        host.SetOffsets(0d, 990d);
        var scroller = new SurfaceScrollerCore(host);

        scroller.ScrollBy(0d, 120d);

        Assert.AreEqual(942d, host.VerticalOffset, 1e-9);
        Assert.AreEqual(0, host.ExpandCalls, "no layout means no canvas to grow");
    }

    // ── Overscroll growth ──────────────────────────────────────────────────

    [TestMethod]
    public void ScrollBy_PastFarEdge_GrowsTheCanvasAndReReadsTheLimit()
    {
        var host = new FakeScrollHost();
        host.SetOffsets(990d, 0d);
        host.OnExpand = () => host.HorizontalMaximum = 1200d;
        var scroller = new SurfaceScrollerCore(host);

        scroller.ScrollBy(120d, 0d);

        Assert.AreEqual(1, host.ExpandCalls);
        Assert.IsTrue(host.Layout!.PositiveOffset.Horizontal > 0d, "the far edge grew the canvas");
        // The positive-edge growth lengthens the extent; the current write stays at the old limit and the
        // new headroom is glided through by later wheel turns.
        Assert.AreEqual(1000d, host.HorizontalOffset, 1e-9);
    }

    [TestMethod]
    public void ScrollBy_BeforeOrigin_GrowsNegativeOffsetAndReturnsTheCompensation()
    {
        var host = new FakeScrollHost();
        host.SetOffsets(10d, 0d);
        var scroller = new SurfaceScrollerCore(host);

        scroller.ScrollBy(-120d, 0d);

        // base extent 1920 → quantum 288; the grown amount is returned so translate(+growth) and
        // scroll(+growth) cancel under the cursor.
        Assert.AreEqual(288d, host.Layout!.NegativeOffset.Horizontal, 1e-9);
        Assert.AreEqual(1, host.ExpandCalls);
        Assert.AreEqual(288d, host.HorizontalOffset, 1e-9);
    }

    // ── Centering / navigation ─────────────────────────────────────────────

    [TestMethod]
    public void CenterOn_PlacesTheLocalPointAtTheViewportCenter()
    {
        var host = new FakeScrollHost { HorizontalMaximum = 2000d, VerticalMaximum = 2000d };
        var scroller = new SurfaceScrollerCore(host);

        scroller.CenterOn(1000d, 900d);

        Assert.AreEqual(600d, host.HorizontalOffset, 1e-9); // 1000 − 800/2
        Assert.AreEqual(600d, host.VerticalOffset, 1e-9);   // 900 − 600/2
    }

    [TestMethod]
    public void CenterOn_WhenTheCanvasIsTooSmall_GrowsItThenRecenters()
    {
        var host = new FakeScrollHost
        {
            HorizontalMaximum = 2000d,
            VerticalMaximum = 2000d,
        };
        host.OnExpand = () =>
        {
            host.HorizontalMaximum = 2000d;
            host.VerticalMaximum = 2000d;
        };
        var scroller = new SurfaceScrollerCore(host);

        scroller.CenterOn(0d, 0d);

        Assert.AreEqual(400d, host.Layout!.NegativeOffset.Horizontal, 1e-9);
        Assert.AreEqual(300d, host.Layout.NegativeOffset.Vertical, 1e-9);
        Assert.AreEqual(1, host.ExpandCalls);
        // After the negative cover grew, ActualOffset cancels the centering target.
        Assert.AreEqual(0d, host.HorizontalOffset, 1e-9);
        Assert.AreEqual(0d, host.VerticalOffset, 1e-9);
    }

    // ── Viewport restore ───────────────────────────────────────────────────

    private static TreeDefaultViewModel RestorableTree(double localX, double localY, double negativeX = 0d, double negativeY = 0d)
    {
        var tree = new TreeDefaultViewModel();
        tree.Layout.NegativeOffset = new Offset(negativeX, negativeY);
        tree.Layout.ViewportOffset = new Offset(localX, localY);
        return tree;
    }

    [TestMethod]
    public void Restore_CapturesSavedViewportAndAppliesItOnce()
    {
        var tree = RestorableTree(200d, 120d, 50d, 30d);
        var restore = new SurfaceViewportRestoreCore();
        var host = new FakeRestoreHost();

        restore.Capture(tree);
        Assert.IsTrue(restore.HasPendingRestore);

        restore.Schedule(host);
        Assert.AreEqual(1, host.DeferCalls);

        host.Run();
        Assert.AreEqual<(double Horizontal, double Vertical)?>((250d, 150d), host.Applied); // local + ActualOffset
        Assert.IsFalse(restore.HasPendingRestore);

        restore.Schedule(host);
        Assert.AreEqual(1, host.DeferCalls, "a restored tree is never restored again");
    }

    [TestMethod]
    public void Restore_SameTreeRefresh_DoesNotRecapture()
    {
        var tree = RestorableTree(200d, 120d);
        var restore = new SurfaceViewportRestoreCore();
        var host = new FakeRestoreHost();

        restore.Capture(tree);
        host.Run(); // 未排过，什么也不做
        restore.Schedule(host);
        host.Run();

        tree.Layout.ViewportOffset = new Offset(700d, 500d);
        restore.Capture(tree);
        restore.Schedule(host);
        host.Run();

        Assert.AreEqual<(double Horizontal, double Vertical)?>((200d, 120d), host.Applied, "only the first attach restores");
    }

    [TestMethod]
    public void Restore_NewTreeWithoutSavedViewport_ClearsThePendingPosition()
    {
        var tree = RestorableTree(200d, 120d);
        var restore = new SurfaceViewportRestoreCore();

        restore.Capture(tree);
        restore.Capture(new TreeDefaultViewModel());

        Assert.IsFalse(restore.HasPendingRestore);
    }

    [TestMethod]
    public void Restore_NewTreeWithSavedViewport_OverwritesThePendingPosition()
    {
        var first = RestorableTree(200d, 120d);
        var second = RestorableTree(400d, 300d);
        var restore = new SurfaceViewportRestoreCore();
        var host = new FakeRestoreHost();

        restore.Capture(first);
        restore.Capture(second);
        restore.Schedule(host);
        host.Run();

        Assert.AreEqual<(double Horizontal, double Vertical)?>((400d, 300d), host.Applied);
    }

    [TestMethod]
    public void Restore_WhenTheAdapterIsNotReady_KeepsThePositionForALaterPass()
    {
        var tree = RestorableTree(200d, 120d);
        var restore = new SurfaceViewportRestoreCore();
        var host = new FakeRestoreHost { IsRestoreEnabled = false };

        restore.Capture(tree);
        restore.Schedule(host);
        host.Run();

        Assert.IsNull(host.Applied);
        Assert.IsTrue(restore.HasPendingRestore, "the pending position survives an unready adapter");

        host.IsRestoreEnabled = true;
        restore.Schedule(host);
        host.Run();

        Assert.AreEqual<(double Horizontal, double Vertical)?>((200d, 120d), host.Applied);
    }

    [TestMethod]
    public void Restore_ClampsToTheCurrentLimits()
    {
        var tree = RestorableTree(200d, 120d);
        var restore = new SurfaceViewportRestoreCore();
        var host = new FakeRestoreHost { HorizontalMaximum = 150d, VerticalMaximum = 100d };

        restore.Capture(tree);
        restore.Schedule(host);
        host.Run();

        Assert.AreEqual<(double Horizontal, double Vertical)?>((150d, 100d), host.Applied);
    }

    // ── Metrics / placement push ───────────────────────────────────────────

    [TestMethod]
    public void SurfaceMetrics_Create_DerivesAvailableEdges()
    {
        var metrics = SurfaceMetrics.Create(
            scrollOffsetX: 120d,
            scrollOffsetY: 80d,
            contentOffsetX: 40d,
            contentOffsetY: 30d,
            viewportWidth: 800d,
            viewportHeight: 600d,
            surfaceWidth: 1000d,
            surfaceHeight: 700d,
            rulerBand: 24d,
            verticalScrollBarThickness: 17d,
            horizontalScrollBarThickness: 17d,
            margin: 12d);

        Assert.AreEqual(983d, metrics.Available.Right, 1e-9);
        Assert.AreEqual(24d, metrics.Available.Top, 1e-9);
        Assert.AreEqual(683d, metrics.Available.Bottom, 1e-9);
        Assert.AreEqual(12d, metrics.Available.Margin, 1e-9);
        Assert.AreEqual(120d, metrics.ScrollOffsetX, 1e-9);
        Assert.AreEqual(40d, metrics.ContentOffsetX, 1e-9);
        Assert.AreEqual(800d, metrics.ViewportWidth, 1e-9);
    }

    [TestMethod]
    public void SurfaceMetrics_Create_ClampsTheEdgesAtZero()
    {
        var metrics = SurfaceMetrics.Create(
            0d, 0d, 0d, 0d, 800d, 600d, 10d, 10d, -5d, 20d, 20d, 0d);

        Assert.AreEqual(0d, metrics.Available.Right, 1e-9);
        Assert.AreEqual(0d, metrics.Available.Top, 1e-9);
        Assert.AreEqual(0d, metrics.Available.Bottom, 1e-9);
    }

    [TestMethod]
    public void SurfaceMetricsPush_PushGrid_WritesOffsets()
    {
        var metrics = SurfaceMetrics.Create(
            120d, 80d, 40d, 30d, 800d, 600d, 1000d, 700d, 24d, 17d, 17d, 12d);
        var grid = new FakeGridDecorator { RulerBand = 24d };
        var tree = new TreeDefaultViewModel();

        SurfaceMetricsPush.PushGrid(metrics, grid, tree);

        Assert.AreEqual(120d, grid.ScrollOffsetX, 1e-9);
        Assert.AreEqual(80d, grid.ScrollOffsetY, 1e-9);
        Assert.AreEqual(40d, grid.ContentOffsetX, 1e-9);
        Assert.AreEqual(30d, grid.ContentOffsetY, 1e-9);
    }

    [TestMethod]
    public void SurfaceMetricsPush_PushMinimap_WritesPlacementAndViewport()
    {
        var tree = new TreeDefaultViewModel();
        var metrics = SurfaceMetrics.Create(
            120d, 80d, 40d, 30d, 800d, 600d, 1000d, 700d, 24d, 17d, 17d, 12d);
        var minimap = new FakeMinimap { MinimapMargin = 12d };

        SurfaceMetricsPush.PushMinimap(metrics, minimap, tree);

        Assert.AreEqual(120d, minimap.ScrollOffsetX, 1e-9);
        Assert.AreEqual(80d, minimap.ScrollOffsetY, 1e-9);
        Assert.AreEqual(40d, minimap.ContentOffsetX, 1e-9);
        Assert.AreEqual(30d, minimap.ContentOffsetY, 1e-9);
        Assert.AreEqual(800d, minimap.ViewportWidth, 1e-9);
        Assert.AreEqual(600d, minimap.ViewportHeight, 1e-9);
        Assert.AreEqual(983d, minimap.AvailableRight, 1e-9);
        Assert.AreEqual(24d, minimap.AvailableTop, 1e-9);
        Assert.AreEqual(683d, minimap.AvailableBottom, 1e-9);
        Assert.AreEqual(12d, minimap.MinimapMargin, 1e-9);
        Assert.AreSame(tree, minimap.WorkflowTree);
    }

    [TestMethod]
    public void SurfaceMetricsPush_PushViewport_WritesTheCanvasLocalViewportAndPersistsIt()
    {
        var tree = new TreeDefaultViewModel();
        tree.SetHelper(new TreeHelper(200d));
        var metrics = SurfaceMetrics.Create(
            120d, 80d, 40d, 30d, 800d, 600d, 1000d, 700d, 0d, 0d, 0d, 0d);

        SurfaceMetricsPush.PushViewport(metrics, tree);

        Assert.AreEqual(80d, tree.GetHelper().Viewport.Horizontal, 1e-9);  // 120 − 40
        Assert.AreEqual(50d, tree.GetHelper().Viewport.Vertical, 1e-9);    // 80 − 30
        Assert.AreEqual(800d, tree.GetHelper().Viewport.Width, 1e-9);
        Assert.AreEqual(600d, tree.GetHelper().Viewport.Height, 1e-9);
        Assert.AreEqual(80d, tree.Layout.ViewportOffset.Horizontal, 1e-9);
        Assert.AreEqual(50d, tree.Layout.ViewportOffset.Vertical, 1e-9);
    }
}
