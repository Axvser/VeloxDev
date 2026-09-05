using System.Collections.ObjectModel;
using System.ComponentModel;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

// ── Stubs (mirrors EnsureNegativeCoverTests; file-scoped so each test file owns its own) ──

file sealed class StubCommand : IVeloxCommand
{
    public event EventHandler? CanExecuteChanged;
    public event CommandEventHandler? Created;
    public event CommandEventHandler? Started;
    public event CommandEventHandler? Completed;
    public event CommandEventHandler? Canceled;
    public event CommandEventHandler? Failed;
    public event CommandEventHandler? Exited;
    public event CommandEventHandler? Enqueued;
    public event CommandEventHandler? Dequeued;

    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) { }
    public void Lock() { }
    public void UnLock() { }
    public void Notify() { }
    public void Clear() { }
    public void Interrupt() { }
    public void Continue() { }
    public void ChangeSemaphore(int semaphore) { }
    public Task ExecuteAsync(object? parameter) => Task.CompletedTask;
    public Task LockAsync() => Task.CompletedTask;
    public Task UnLockAsync() => Task.CompletedTask;
    public Task ClearAsync() => Task.CompletedTask;
    public Task InterruptAsync() => Task.CompletedTask;
    public Task ContinueAsync() => Task.CompletedTask;
    public Task ChangeSemaphoreAsync(int semaphore) => Task.CompletedTask;
}

file sealed class StubNode : IWorkflowNodeViewModel
{
    public IWorkflowTreeViewModel? Parent { get; set; }
    public Anchor Anchor { get; set; } = new();
    public Size Size { get; set; } = new();
    public ObservableCollection<IWorkflowSlotViewModel> Slots { get; set; } = [];

    public IVeloxCommand MoveCommand { get; } = new StubCommand();
    public IVeloxCommand SetAnchorCommand { get; } = new StubCommand();
    public IVeloxCommand SetSizeCommand { get; } = new StubCommand();
    public IVeloxCommand CreateSlotCommand { get; } = new StubCommand();
    public IVeloxCommand DeleteCommand { get; } = new StubCommand();
    public IVeloxCommand ReceiveCommand { get; } = new StubCommand();
    public IVeloxCommand BroadcastCommand { get; } = new StubCommand();
    public IVeloxCommand ReverseBroadcastCommand { get; } = new StubCommand();
    public IVeloxCommand CloseCommand { get; } = new StubCommand();

    public event PropertyChangingEventHandler? PropertyChanging;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void InitializeWorkflow() { }
    public void OnPropertyChanging(string p) => PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(p));
    public void OnPropertyChanged(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    public IWorkflowNodeViewModelHelper GetHelper() => throw new NotSupportedException();
    public void SetHelper(IWorkflowNodeViewModelHelper helper) { }
}

file sealed class StubTree : IWorkflowTreeViewModel
{
    public CanvasLayout Layout { get; set; } = new();
    public IWorkflowLinkViewModel VirtualLink { get; set; } = null!;
    public ObservableCollection<IWorkflowNodeViewModel> Nodes { get; set; } = [];
    public ObservableCollection<IWorkflowLinkViewModel> Links { get; set; } = [];
    public Dictionary<IWorkflowSlotViewModel, Dictionary<IWorkflowSlotViewModel, IWorkflowLinkViewModel>> LinksMap { get; set; } = [];
    public IVeloxCommand CreateNodeCommand { get; } = new StubCommand();
    public IVeloxCommand SetPointerCommand { get; } = new StubCommand();
    public IVeloxCommand ResetVirtualLinkCommand { get; } = new StubCommand();
    public IVeloxCommand SendConnectionCommand { get; } = new StubCommand();
    public IVeloxCommand ReceiveConnectionCommand { get; } = new StubCommand();
    public IVeloxCommand SubmitCommand { get; } = new StubCommand();
    public IVeloxCommand RedoCommand { get; } = new StubCommand();
    public IVeloxCommand UndoCommand { get; } = new StubCommand();
    public IVeloxCommand CloseCommand { get; } = new StubCommand();
    public event PropertyChangingEventHandler? PropertyChanging;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void InitializeWorkflow() { }
    public void OnPropertyChanging(string p) => PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(p));
    public void OnPropertyChanged(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    public IWorkflowTreeViewModelHelper GetHelper() => throw new NotSupportedException();
    public void SetHelper(IWorkflowTreeViewModelHelper helper) { }
}

/// <summary>
/// Pure-data zoom simulation: no GUI, no renderer. Reproduces the demo's four-quadrant graph at
/// world anchors ±140 / size 260×180 and sweeps the collapse scale from 1 down to 0.1 (the wheel's
/// ×1/1.1 notches, clamped at 0.1), mirroring the adapter order — write Scale, let Anchor/Size
/// collapse (here: compute world/Scale exactly as the VM getters do), then EnsureNegativeCover —
/// and asserts at EVERY scale that every node card is fully inside the reachable canvas region
/// [−ActualOffset, ActualSize − ActualOffset]. A link polyline lies inside the union of its two
/// endpoint node cards, so node-card reachability ⟹ every link is reachable too (nothing for the
/// ScrollViewer/translate to cut off-screen).
///
/// Box-containment corollary (the WinUI offset-frame fix's linchpin): a link endpoint sits on a node
/// card edge, so raw canvas-local x obeys −negX ≤ raw ≤ reachX = ActualSize.Width − negX; baking
/// +ActualOffset yields local geometry x ∈ [0, ActualSize.Width]. The mL ≥ 0 / mR ≥ 0 asserts below
/// ARE that containment — an offset-frame LinkView (element placed at −ActualOffset, geometry baked
/// +ActualOffset) can therefore never push its polyline out of its own [0, ActualSize] element box,
/// no matter how deep the zoom.
///
/// This answers "is the canvas size right while zooming?" in the model: if content ever escapes the
/// reachable canvas here, that scale is where a GUI would truncate regardless of renderer.
/// </summary>
[TestClass]
public class DeepZoomReachabilitySimTests
{
    private const double TOL = 1e-6;

    private static readonly Anchor[] WorldAnchors =
    [
        new(140, 140, 0),
        new(-140, 140, 0),
        new(-140, -140, 0),
        new(140, -140, 0),
    ];
    private static readonly Size WorldSize = new(260, 180);

    private static List<double> ZoomDownSequence()
    {
        var seq = new List<double>();
        double s = 1.0;
        while (true)
        {
            seq.Add(s);
            double next = Math.Max(0.1, s / 1.1);
            if (next >= s - 1e-12)
            {
                break;
            }
            s = next;
        }
        return seq;
    }

    private static List<IWorkflowNodeViewModel> BuildQuadrantNodes(double scale)
    {
        var nodes = new List<IWorkflowNodeViewModel>();
        var scaleValue = new Scale(scale, scale);
        foreach (var world in WorldAnchors)
        {
            nodes.Add(new StubNode
            {
                Anchor = world.Collapse(scaleValue),
                Size = WorldSize.Collapse(scaleValue),
            });
        }
        return nodes;
    }

    [TestMethod]
    public void QuadrantContent_StaysReachable_FromScale1ToDeepZoom()
    {
        var tree = new StubTree();
        // Demo baseline: a fixed negative cover that the winui trimmed MainWindow seeds before nodes.
        tree.Layout.NegativeOffset = new Offset(320, 260);

        double worstLeft = double.PositiveInfinity;   // margin of the tightest-fit content vs each bound
        double worstRight = double.PositiveInfinity;
        double worstTop = double.PositiveInfinity;
        double worstBottom = double.PositiveInfinity;
        var scaleLog = new List<string>();

        var sequence = ZoomDownSequence();
        Assert.IsTrue(sequence[^1] <= 0.1 + 1e-9, "sequence must reach deep zoom");
        Assert.IsTrue(sequence.Count > 15, "wheel-notch sequence must be fine-grained");

        double lastNegX = -1, lastNegY = -1;
        foreach (var scale in sequence)
        {
            // Mirror the adapter zoom order: Scale first (collapsed getters now yield world/Scale),
            // THEN EnsureNegativeCover must see those collapsed values and grow the cover.
            tree.Layout.Scale = new Scale(scale, scale);
            foreach (var node in BuildQuadrantNodes(scale))
            {
                tree.Nodes.Add(node);
            }

            bool grew = WorkflowSurfaceMath.EnsureNegativeCover(tree);
            Assert.IsTrue(grew || tree.Layout.NegativeOffset.Horizontal >= lastNegX - TOL
                              && tree.Layout.NegativeOffset.Vertical >= lastNegY - TOL,
                $"cover must be monotonic at scale {scale}");
            lastNegX = tree.Layout.NegativeOffset.Horizontal;
            lastNegY = tree.Layout.NegativeOffset.Vertical;

            var layout = tree.Layout;
            Assert.AreEqual(layout.NegativeOffset.Horizontal, layout.ActualOffset.Horizontal, TOL);
            Assert.AreEqual(layout.NegativeOffset.Vertical, layout.ActualOffset.Vertical, TOL);
            Assert.IsFalse(double.IsNaN(layout.ActualSize.Width) || double.IsNaN(layout.ActualSize.Height));
            Assert.IsFalse(double.IsNaN(layout.ActualOffset.Horizontal) || double.IsNaN(layout.ActualOffset.Vertical));

            // Reachable content region in canvas-local coordinates:
            //   rendered = p + ActualOffset must lie in [0, ActualSize] to be scrollable into view.
            double negX = layout.ActualOffset.Horizontal;
            double negY = layout.ActualOffset.Vertical;
            double reachX = layout.ActualSize.Width - negX;   // exclusive right bound
            double reachY = layout.ActualSize.Height - negY;

            string? firstViolation = null;
            foreach (var node in tree.Nodes)
            {
                double left = node.Anchor.Horizontal;
                double top = node.Anchor.Vertical;
                double right = left + node.Size.Width;
                double bottom = top + node.Size.Height;

                double mL = left + negX;            // rendered left edge ≥ 0 ?
                double mR = reachX - right;         // rendered right edge ≤ ActualSize ?
                double mT = top + negY;
                double mB = reachY - bottom;

                worstLeft = Math.Min(worstLeft, mL);
                worstRight = Math.Min(worstRight, mR);
                worstTop = Math.Min(worstTop, mT);
                worstBottom = Math.Min(worstBottom, mB);

                if (mL < -TOL || mR < -TOL || mT < -TOL || mB < -TOL)
                {
                    firstViolation ??=
                        $"scale={scale:F4} node@({node.Anchor.Horizontal:F1},{node.Anchor.Vertical:F1}) " +
                        $"card=[{left:F1},{top:F1}]-[{right:F1},{bottom:F1}] " +
                        $"mL={mL:F1} mR={mR:F1} mT={mT:F1} mB={mB:F1} " +
                        $"neg=({negX:F1},{negY:F1}) actualSize=({layout.ActualSize.Width:F1},{layout.ActualSize.Height:F1})";
                }
            }

            scaleLog.Add(
                $"s={scale:F4} neg=({negX:F1},{negY:F1}) asize=({layout.ActualSize.Width:F1},{layout.ActualSize.Height:F1}) " +
                $"reachX={reachX:F1} reachY={reachY:F1}");

            Assert.IsNull(firstViolation, "content escaped the reachable canvas:\n" + firstViolation);
            tree.Nodes.Clear(); // rebuild nodes per scale like the getters recompute per scale
        }

        // The whole point of the sweep: report HOW tight the fit is at the deep end, so a GUI-side
        // rounding/ruler margin is visible as near-zero slack rather than silent. Rendered left/top sit
        // exactly at scroll-0 once the cover catches up (flush is reachable — scroll 0 shows it); only
        // truly-negative slack is a defect.
        Assert.IsTrue(worstLeft >= -TOL, $"left content escaped reachable region (tightest {worstLeft:F3})");
        Assert.IsTrue(worstTop >= -TOL, $"top content escaped reachable region (tightest {worstTop:F3})");
        Assert.IsTrue(worstRight >= -TOL, $"right content exceeded canvas width (tightest {worstRight:F3})");
        Assert.IsTrue(worstBottom >= -TOL, $"bottom content exceeded canvas height (tightest {worstBottom:F3})");

        // Keep a readable trace for a failing deep scale (surface the full scale log).
        Assert.IsTrue(scaleLog.Count > 0, "expected at least one scale step");
    }
}
