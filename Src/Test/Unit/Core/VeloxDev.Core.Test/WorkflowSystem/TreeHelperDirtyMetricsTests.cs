using System.Diagnostics;
using System.Reflection;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

/// <summary>
/// The helper's virtualization counters answer one question: how much work does a burst of <see cref="TreeHelper{T}.MarkDirty"/>
/// calls actually cause? The raw requests and the passes that drain them are counted separately, so a caller that
/// marks on every read can be measured against the tick's coalescing.
/// </summary>
[TestClass]
public class TreeHelperDirtyMetricsTests
{
    /// <summary>Calls the private pass entry point, so the measurement does not depend on the 10fps tick.</summary>
    private static readonly MethodInfo RunPass = typeof(TreeHelper<TreeDefaultViewModel>)
        .GetMethod("RunVirtualizationPass", BindingFlags.NonPublic | BindingFlags.Instance)!;

    public TestContext TestContext { get; set; } = null!;

    /// <summary>A context that accepts posts and never runs them — the UI thread blocked for the whole test.</summary>
    private sealed class StalledContext : SynchronizationContext
    {
        private int _posts;

        public int Posts => Volatile.Read(ref _posts);

        public override void Post(SendOrPostCallback d, object? state) => Interlocked.Increment(ref _posts);
    }

    /// <summary>A context that keeps posted callbacks until the test runs them.</summary>
    private sealed class DeferredContext : SynchronizationContext
    {
        private readonly List<(SendOrPostCallback Callback, object? State)> _pending = [];

        public int PendingCount
        {
            get { lock (_pending) return _pending.Count; }
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            lock (_pending) _pending.Add((d, state));
        }

        public void RunPending()
        {
            (SendOrPostCallback Callback, object? State)[] pending;
            lock (_pending)
            {
                pending = [.. _pending];
                _pending.Clear();
            }

            foreach (var (callback, state) in pending)
            {
                callback(state);
            }
        }
    }

    private static TreeHelper<TreeDefaultViewModel> Install(TreeDefaultViewModel tree)
    {
        var previous = SynchronizationContext.Current;
        try
        {
            // 让 tick 只投递不执行：计数与手动 pass 才可确定性观测。
            SynchronizationContext.SetSynchronizationContext(new StalledContext());
            var helper = new TreeHelper<TreeDefaultViewModel>(cellSize: 100);
            tree.SetHelper(helper);
            helper.ResetVirtualizationMetrics();
            return helper;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private static NodeDefaultViewModel NewNode(TreeDefaultViewModel tree, double left, double top)
        => new()
        {
            Parent = tree,
            Anchor = new Anchor(left, top, 0),
            Size = new Size { Width = 40, Height = 40 },
        };

    [TestMethod]
    public void DirtyRequestsCollapseIntoOnePass()
    {
        var tree = new TreeDefaultViewModel();
        var helper = Install(tree);

        tree.Nodes.Add(NewNode(tree, 10, 10));
        helper.Viewport = new Viewport(0, 0, 400, 400);

        for (var i = 0; i < 10; i++)
        {
            helper.MarkDirty();
        }

        RunPass.Invoke(helper, null);

        Assert.AreEqual(10, helper.DirtyRequestCount, "every MarkDirty call is counted, coalesced or not");
        Assert.AreEqual(1, helper.PassRunCount, "ten requests drain in one pass");
        Assert.AreEqual(0, helper.PassEmptyCount);
        Assert.IsTrue(helper.LastVirtualizationPassDuration > TimeSpan.Zero, "a pass that ran has a measured duration");

        RunPass.Invoke(helper, null);

        Assert.AreEqual(1, helper.PassRunCount, "a second dispatch with nothing dirty is not a run");
        Assert.AreEqual(1, helper.PassEmptyCount, "and it is counted as an empty dispatch");

        helper.Uninstall(tree);
    }

    [TestMethod]
    public void PostedPassIsCountedBeforeTheHostRunsIt()
    {
        var tree = new TreeDefaultViewModel();
        var deferred = new DeferredContext();
        var previous = SynchronizationContext.Current;
        TreeHelper<TreeDefaultViewModel> helper;

        try
        {
            SynchronizationContext.SetSynchronizationContext(deferred);
            helper = new TreeHelper<TreeDefaultViewModel>(cellSize: 100);
            tree.SetHelper(helper);
            helper.Viewport = new Viewport(0, 0, 400, 400);
            helper.ResetVirtualizationMetrics();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        helper.MarkDirty();

        var timeout = Stopwatch.StartNew();
        while (deferred.PendingCount == 0 && timeout.Elapsed < TimeSpan.FromSeconds(3))
        {
            Thread.Sleep(10);
        }

        Assert.IsTrue(deferred.PendingCount > 0, "precondition: the tick posted the pass");
        Assert.AreEqual(1, helper.PassPostCount, "one post per tick while the host has not run it");
        Assert.AreEqual(0, helper.PassRunCount, "a posted pass has not run yet");

        deferred.RunPending();

        Assert.AreEqual(1, helper.PassRunCount, "running the posted callback drains the dirty flag");
        helper.Uninstall(tree);
    }

    [TestMethod]
    public void TwoThousandNodePass_RunsAndReportsItsDuration()
    {
        var tree = new TreeDefaultViewModel();
        var helper = Install(tree);

        // 视口还是空的：插入节点时 Virtualize 直接返回，2000 次插入不会退化成 2000 趟全量物化。
        for (var index = 0; index < 2000; index++)
        {
            tree.Nodes.Add(NewNode(tree, (index % 50) * 80d, (index / 50) * 80d));
        }

        helper.Viewport = new Viewport(0, 0, 4000, 4000);

        helper.ResetVirtualizationMetrics();

        // 五趟取最小/中位：第一趟含 JIT 与首次遍历，单次数字不够稳定。
        var allVisible = new List<double>(5);
        for (var index = 0; index < 5; index++)
        {
            helper.MarkDirty();
            RunPass.Invoke(helper, null);
            allVisible.Add(helper.LastVirtualizationPassDuration.TotalMilliseconds);
        }

        Assert.AreEqual(5, helper.PassRunCount);
        Assert.AreEqual(
            tree.Nodes.Count + 1,
            helper.VisibleItems.Count,
            "the pass covers every node inside the viewport, plus the tree's virtual link");
        Assert.IsTrue(helper.LastVirtualizationPassDuration > TimeSpan.Zero);

        allVisible.Sort();
        TestContext.WriteLine(
            $"2000-node pass (all visible): min {allVisible[0]:F3} ms, median {allVisible[2]:F3} ms, " +
            $"max {allVisible[^1]:F3} ms, visible {helper.VisibleItems.Count}");

        // 第二种更接近实际：2000 节点的树，视口只覆盖几十个。设置视口本身会同步虚拟化一次，
        // 之后重置计数再测「维持一个小可见集」的一趟。
        helper.Viewport = new Viewport(0, 0, 400, 300);
        helper.ResetVirtualizationMetrics();

        var smallViewport = new List<double>(5);
        for (var index = 0; index < 5; index++)
        {
            helper.MarkDirty();
            RunPass.Invoke(helper, null);
            smallViewport.Add(helper.LastVirtualizationPassDuration.TotalMilliseconds);
        }

        Assert.AreEqual(5, helper.PassRunCount);
        Assert.IsTrue(helper.VisibleItems.Count < 2000, "the smaller viewport keeps only a window of the tree visible");

        smallViewport.Sort();
        TestContext.WriteLine(
            $"2000-node pass (small viewport): min {smallViewport[0]:F3} ms, median {smallViewport[2]:F3} ms, " +
            $"max {smallViewport[^1]:F3} ms, visible {helper.VisibleItems.Count}");

        helper.Uninstall(tree);
    }
}
