using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

/// <summary>
/// A virtualizing helper's tick runs on its own timer thread, but everything it produces — the visible set and the
/// node layout notifications — is bound to the host's UI. Running the pass where the tick happens is therefore a
/// cross-thread write into a bound collection: a view pool hides the items it sees removed, while the model still
/// has them, and a binding engine may throw at any moment. The pass has to run on the context the helper was
/// installed on.
/// </summary>
[TestClass]
public class TreeHelperTickTests
{
    /// <summary>A thread with a <see cref="SynchronizationContext"/> and a message loop, so work posted to it
    /// actually runs — what a UI thread is, reduced to the part under test.</summary>
    private sealed class PumpContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
        private readonly Thread _thread;

        public PumpContext()
        {
            _thread = new Thread(Run) { Name = "test-ui", IsBackground = true };
            _thread.Start();
        }

        /// <summary>The thread that owns this context — what every notification has to arrive on.</summary>
        public int ThreadId => _thread.ManagedThreadId;

        public override void Post(SendOrPostCallback d, object? state)
        {
            // 关停后 tick 仍可能再 post 一次；丢掉即可，测试已经结束。
            try { _queue.Add((d, state)); } catch (InvalidOperationException) { }
        }

        /// <summary>Runs <paramref name="action"/> on the pump thread and propagates its exceptions.</summary>
        public void Invoke(Action action)
        {
            if (Thread.CurrentThread.ManagedThreadId == ThreadId)
            {
                action();
                return;
            }

            using var done = new ManualResetEventSlim();
            Exception? failure = null;
            Post(_ =>
            {
                try { action(); } catch (Exception ex) { failure = ex; } finally { done.Set(); }
            }, null);
            done.Wait();
            if (failure is not null) throw new AggregateException(failure);
        }

        private void Run()
        {
            SetSynchronizationContext(this);
            foreach (var (callback, state) in _queue.GetConsumingEnumerable())
            {
                callback(state);
            }
        }

        public void Dispose()
        {
            _queue.CompleteAdding();
            _thread.Join();
            _queue.Dispose();
        }
    }

    /// <summary>A context that never runs what is posted to it, and counts the posts — a UI thread blocked for the
    /// whole test.</summary>
    private sealed class CountingContext : SynchronizationContext
    {
        private int _posts;

        public int Posts => Volatile.Read(ref _posts);

        public override void Post(SendOrPostCallback d, object? state) => Interlocked.Increment(ref _posts);
    }

    [TestMethod]
    public void WhileTheHostContextIsBusyOnlyOnePassIsQueued()
    {
        var previous = SynchronizationContext.Current;
        var counting = new CountingContext();
        TreeDefaultViewModel tree = null!;
        TreeHelper<TreeDefaultViewModel> helper = null!;

        try
        {
            SynchronizationContext.SetSynchronizationContext(counting);
            tree = new TreeDefaultViewModel();
            helper = new TreeHelper<TreeDefaultViewModel>(cellSize: 100);
            tree.SetHelper(helper);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        helper.MarkDirty();

        // 等到第一趟被排上，再多等两三个 tick：UI 一直不消费，后面的 tick 不该再排。
        var timeout = Stopwatch.StartNew();
        while (counting.Posts == 0 && timeout.Elapsed < TimeSpan.FromSeconds(3))
        {
            Thread.Sleep(20);
        }
        Thread.Sleep(250);

        helper.Uninstall(tree);

        Assert.AreEqual(1, counting.Posts,
            "a busy UI thread must collapse the pending pass into one posted callback, not one per tick");
    }

    /// <summary>A context that keeps what is posted until the test runs it — an old UI thread that has not got to
    /// the callback yet when the helper is moved to another tree.</summary>
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

    [TestMethod]
    public void APassQueuedForAnEarlierInstallationIsDroppedAfterReinstall()
    {
        var previous = SynchronizationContext.Current;
        var old = new DeferredContext();
        var rebound = new DeferredContext();
        TreeDefaultViewModel tree = null!;
        TreeHelper<TreeDefaultViewModel> helper = null!;
        NodeDefaultViewModel node = null!;

        try
        {
            SynchronizationContext.SetSynchronizationContext(old);
            tree = new TreeDefaultViewModel();
            helper = new TreeHelper<TreeDefaultViewModel>(cellSize: 100);
            tree.SetHelper(helper);
            node = new NodeDefaultViewModel
            {
                Parent = tree,
                Anchor = new Anchor(10, 10, 0),
                Size = new Size { Width = 40, Height = 40 },
            };
            tree.Nodes.Add(node);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        // 旧上下文里留一趟还没跑的回投。
        var timeout = Stopwatch.StartNew();
        while (old.PendingCount == 0 && timeout.Elapsed < TimeSpan.FromSeconds(3))
        {
            Thread.Sleep(20);
        }

        Assert.IsTrue(old.PendingCount > 0, "precondition: the first installation left a callback queued on its own context");

        helper.Uninstall(tree);

        var notifications = new ConcurrentBag<int>();
        try
        {
            SynchronizationContext.SetSynchronizationContext(rebound);
            helper.Install(tree);
            helper.Viewport = new Viewport(0, 0, 400, 400);
            node.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(NodeDefaultViewModel.Anchor) or nameof(NodeDefaultViewModel.Size))
                {
                    notifications.Add(Environment.CurrentManagedThreadId);
                }
            };
            helper.VisibleItems.CollectionChanged += (_, _) =>
                notifications.Add(Environment.CurrentManagedThreadId);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        helper.MarkDirty();

        var staleThread = Environment.CurrentManagedThreadId;
        old.RunPending();   // 旧 UI 线程终于跑到那次回投

        // 先取快照：下面的 Uninstall 会在测试线程上清 VisibleItems，那一条与回投无关。
        var threads = notifications.ToArray();

        helper.Uninstall(tree);

        Assert.IsFalse(threads.Any(id => id == staleThread),
            "a callback posted for an installation that has since been replaced must be dropped, not run against the new tree on the old thread");
    }

    [TestMethod]
    public void ReinstallingOnAnotherThreadRebindsThePassToThatThread()
    {
        var previous = SynchronizationContext.Current;
        var stalled = new CountingContext();
        TreeDefaultViewModel tree = null!;
        TreeHelper<TreeDefaultViewModel> helper = null!;
        NodeDefaultViewModel node = null!;

        try
        {
            SynchronizationContext.SetSynchronizationContext(stalled);
            tree = new TreeDefaultViewModel();
            helper = new TreeHelper<TreeDefaultViewModel>(cellSize: 100);
            tree.SetHelper(helper);
            node = new NodeDefaultViewModel
            {
                Parent = tree,
                Anchor = new Anchor(10, 10, 0),
                Size = new Size { Width = 40, Height = 40 },
            };
            tree.Nodes.Add(node);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        // 第一段安装真的投递过一趟，但那个上下文永远不消费它 —— 排队旗标会一直立着。
        var timeout = Stopwatch.StartNew();
        while (stalled.Posts == 0 && timeout.Elapsed < TimeSpan.FromSeconds(3))
        {
            Thread.Sleep(20);
        }

        var framesBeforeUninstall = TickManager.TotalFrames(nameof(TreeHelper));
        helper.Uninstall(tree);

        // 等两帧，让卸载的注销真的被 tick 消费掉 —— 否则同一帧内的「加回」会被随后的「移除」抹掉。
        timeout.Restart();
        while (TickManager.TotalFrames(nameof(TreeHelper)) < framesBeforeUninstall + 2 &&
               timeout.Elapsed < TimeSpan.FromSeconds(3))
        {
            Thread.Sleep(10);
        }

        using var ui = new PumpContext();
        var layoutNotifications = new ConcurrentBag<int>();

        ui.Invoke(() =>
        {
            helper.Install(tree);
            helper.Viewport = new Viewport(0, 0, 400, 400);
            node.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(NodeDefaultViewModel.Anchor) or nameof(NodeDefaultViewModel.Size))
                {
                    layoutNotifications.Add(Environment.CurrentManagedThreadId);
                }
            };
        });

        helper.MarkDirty();

        timeout.Restart();
        while (layoutNotifications.IsEmpty && timeout.Elapsed < TimeSpan.FromSeconds(3))
        {
            Thread.Sleep(20);
        }

        var threads = layoutNotifications.ToArray();
        ui.Invoke(() => helper.Uninstall(tree));

        Assert.IsNotEmpty(threads,
            "a helper installed on a new thread has to be rebound to that thread, and the pass left queued by the previous installation must not block it");
        foreach (var threadId in threads)
        {
            Assert.AreEqual(ui.ThreadId, threadId,
                "the second installation's context is the pump thread, not the one the helper was first installed on");
        }
    }

    [TestMethod]
    public void TheDirtyPassTouchesTheVisibleSetOnTheThreadThatInstalledTheHelper()
    {
        using var ui = new PumpContext();

        TreeDefaultViewModel tree = null!;
        TreeHelper<TreeDefaultViewModel> helper = null!;
        bool bothVisibleBeforeDirty = false;
        var collectionChanges = new ConcurrentBag<int>();
        var layoutNotifications = new ConcurrentBag<int>();

        ui.Invoke(() =>
        {
            tree = new TreeDefaultViewModel();
            helper = new TreeHelper<TreeDefaultViewModel>(cellSize: 100);
            tree.SetHelper(helper);
            helper.Viewport = new Viewport(0, 0, 400, 400);

            var moving = NewNode(tree, 10, 10);
            var staying = NewNode(tree, 150, 150);
            tree.Nodes.Add(moving);
            tree.Nodes.Add(staying);
            bothVisibleBeforeDirty =
                helper.VisibleItems.Contains(moving) && helper.VisibleItems.Contains(staying);

            // 直接写 Anchor 不动脏标记：只有 tick 那一趟会把它移出可见集，这正是要观测的 Remove。
            moving.Anchor = new Anchor(-5000, -5000, 0);

            helper.VisibleItems.CollectionChanged += (_, _) =>
                collectionChanges.Add(Environment.CurrentManagedThreadId);

            staying.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(NodeDefaultViewModel.Anchor) or nameof(NodeDefaultViewModel.Size))
                {
                    layoutNotifications.Add(Environment.CurrentManagedThreadId);
                }
            };

            helper.MarkDirty();
        });

        // 通道是 10fps：给 tick 至少跑一次，并留出 post 回 UI 线程的余量。
        var timeout = Stopwatch.StartNew();
        while ((collectionChanges.IsEmpty || layoutNotifications.IsEmpty) &&
               timeout.Elapsed < TimeSpan.FromSeconds(3))
        {
            Thread.Sleep(20);
        }

        var collectionThreads = collectionChanges.ToArray();
        var layoutThreads = layoutNotifications.ToArray();

        ui.Invoke(() => helper.Uninstall(tree));

        Assert.IsTrue(bothVisibleBeforeDirty, "precondition: both nodes are in the visible set before the dirty pass");
        Assert.IsNotEmpty(collectionThreads, "the dirty pass has to remove the node that left the viewport");
        Assert.IsNotEmpty(layoutThreads, "the dirty pass has to broadcast node layout");

        foreach (var threadId in collectionThreads)
        {
            Assert.AreEqual(ui.ThreadId, threadId,
                "the visible set is bound to UI; the tick thread must post the pass to the installing context instead of editing the collection itself");
        }

        foreach (var threadId in layoutThreads)
        {
            Assert.AreEqual(ui.ThreadId, threadId,
                "the layout notifications drive bound link endpoints; they must be raised on the installing context too");
        }
    }

    private static NodeDefaultViewModel NewNode(TreeDefaultViewModel tree, double left, double top)
        => new()
        {
            Parent = tree,
            Anchor = new Anchor(left, top, 0),
            Size = new Size { Width = 40, Height = 40 },
        };
}
