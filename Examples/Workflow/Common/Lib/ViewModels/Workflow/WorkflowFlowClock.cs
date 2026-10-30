using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using VeloxDev.TransitionSystem;
using VeloxDev.WorkflowSystem;

namespace Demo.Workflow;

/// <summary>
/// The one flow clock a workflow tree shares: every link on that tree samples the same
/// <see cref="BandHead"/> / <see cref="BandIntensity"/> pair instead of running a timer of its own.
/// </summary>
/// <remarks>
/// <para>
/// A tree with many links used to pay one <c>Transition</c> (one frame pacer, one phase machine) per link.
/// The comet's motion does not depend on the link at all — every link travels the same three phases — so the
/// motion is owned here, once per tree, and the views only copy the two scalars and repaint.
/// </para>
/// <para>
/// The instance is created on first use through a <see cref="ConditionalWeakTable{TKey,TValue}"/> keyed by the
/// tree, so it lives exactly as long as the tree does. Callers get it from <see cref="For"/> (a tree) or
/// <see cref="ForLink"/> (a link, which resolves its tree through the slot and node parents); a link that is
/// detached from any tree answers <see langword="null"/>.
/// </para>
/// <para>
/// <c>Subscribe</c> is reference counted: the transition starts when the first subscriber arrives and stops when
/// the last one leaves, so many links on one tree still run exactly one timer. A view released by the pool must
/// unsubscribe before it is rebound; a view rebound to another tree unsubscribes from the old clock and subscribes
/// to the new one.
/// </para>
/// <para>
/// The transition itself is built by <see cref="RegisterDriver"/> rather than here. <c>Transition&lt;T&gt;</c>
/// and <c>TransitionEffect</c> live in the per-platform adapter assemblies, while this file is compiled
/// into the shared netstandard library that only references Core; the platform's link view installs a factory
/// once, and it is invoked at most once per tree, when the clock first starts.
/// </para>
/// </remarks>
public sealed class WorkflowFlowClock
{
    // 平台驱动工厂。写成私有字段 + 公开方法而不是公开静态属性：AIContextTree 生成器会把公开成员编进
    // Agent 目录，而它对静态属性生成的是 `to.Member = ...` 这样的实例访问，编译期就是 CS0176。
    // 静态方法不在它的目录里，正好避开。
    private static Func<WorkflowFlowClock, IWorkflowFlowDriver>? _driverFactory;

    private static readonly ConditionalWeakTable<IWorkflowTreeViewModel, WorkflowFlowClock> Clocks = new();

    // 一次周期里每帧到达的时刻：两个标量都已写入之后才通知订户，所以订户只重画一次。
    private readonly EventHandler<TransitionEventArgs> _lateUpdate;

    // 订户列表按到达顺序保存；每帧遍历的是它，不建快照、不分配。
    private readonly List<Action> _subscribers = [];

    private IWorkflowFlowDriver? _driver;

    private WorkflowFlowClock()
    {
        // 缓存成字段：把它交给 TransitionEffect 时是同一个委托，不为每棵树或每帧新建。
        _lateUpdate = OnLateUpdate;
    }

    /// <summary>How far along its link the comet's head has travelled, as a fraction of its length.</summary>
    /// <remarks>Written by the transition, read by every subscriber on each <see cref="LateUpdateHandler"/> call.</remarks>
    public double BandHead { get; set; }

    /// <summary>How lit the comet is: 0 while it is absent, 1 while it travels.</summary>
    /// <remarks>Written by the transition, read by every subscriber on each <see cref="LateUpdateHandler"/> call.</remarks>
    public double BandIntensity { get; set; }

    /// <summary>
    /// The cached <c>LateUpdate</c> handler that fans one frame out to every subscriber.
    /// </summary>
    /// <remarks>
    /// Hand this same delegate to each phase's effect; the transition's interpreter runs <c>Update</c> callbacks,
    /// writes both scalars, then invokes <c>LateUpdate</c>, so a subscriber never sees half a frame.
    /// </remarks>
    public EventHandler<TransitionEventArgs> LateUpdateHandler => _lateUpdate;

    /// <summary>How many views currently sample this clock.</summary>
    public int SubscriberCount => _subscribers.Count;

    /// <summary>Whether the shared transition is currently running.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Installs the platform transition builder. Called once by each platform's link view, before any subscription.
    /// </summary>
    /// <param name="factory">
    /// Builds the transition that drives <paramref name="clock"/> and returns it as a start/stop driver. It is
    /// called at most once per clock, and the returned driver is reused across stop/start cycles, so it must
    /// accept <c>Execute</c> after a previous <c>Exit</c>.
    /// </param>
    public static void RegisterDriver(Func<WorkflowFlowClock, IWorkflowFlowDriver> factory)
    {
        _driverFactory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>Gets the clock shared by <paramref name="tree"/>.</summary>
    public static WorkflowFlowClock For(IWorkflowTreeViewModel tree)
    {
        if (tree is null) throw new ArgumentNullException(nameof(tree));

        return Clocks.GetValue(tree, static _ => new WorkflowFlowClock());
    }

    /// <summary>
    /// Gets the clock of <paramref name="link"/>'s tree, or <see langword="null"/> when the link is detached
    /// from a tree.
    /// </summary>
    public static WorkflowFlowClock? ForLink(IWorkflowLinkViewModel? link)
    {
        var tree = link?.Sender?.Parent?.Parent as IWorkflowTreeViewModel
            ?? link?.Receiver?.Parent?.Parent as IWorkflowTreeViewModel;

        return tree is null ? null : For(tree);
    }

    /// <summary>
    /// Adds <paramref name="tick"/> to the fan-out, starting the shared transition if it was the first subscriber.
    /// </summary>
    /// <remarks>
    /// Adding the same delegate twice is a no-op. Each tick runs on the UI thread the transition was started on.
    /// </remarks>
    public void Subscribe(Action tick)
    {
        if (tick is null) throw new ArgumentNullException(nameof(tick));

        if (_subscribers.Contains(tick)) return;

        _subscribers.Add(tick);
        if (_subscribers.Count == 1)
        {
            StartFlow();
        }
    }

    /// <summary>
    /// Removes <paramref name="tick"/> from the fan-out, stopping the shared transition when the last subscriber
    /// leaves.
    /// </summary>
    /// <returns><see langword="true"/> when the delegate was subscribed.</returns>
    public bool Unsubscribe(Action tick)
    {
        if (tick is null) return false;
        if (!_subscribers.Remove(tick)) return false;

        if (_subscribers.Count == 0)
        {
            StopFlow();
        }

        return true;
    }

    private void StartFlow()
    {
        if (_driver is null)
        {
            var factory = _driverFactory;
            if (factory is null) return;

            _driver = factory(this);
        }

        // 周期从目标读起值，所以执行前两个标量都回到起点；链在每个接缝重放这份起值。
        BandHead = 0d;
        BandIntensity = 0d;

        _driver.Start();
        IsRunning = true;
    }

    private void StopFlow()
    {
        if (!IsRunning) return;

        // 停表是由平台驱动的：它会 Exit 掉并释放帧调度器，而驱动本身留着，下一次首订直接重跑。
        _driver?.Stop();
        IsRunning = false;
        BandHead = 0d;
        BandIntensity = 0d;
    }

    private void OnLateUpdate(object? sender, TransitionEventArgs e)
    {
        // 倒序遍历：订户在 tick 里退订（视图卸载）时，后面的下标仍然有效。
        for (var i = _subscribers.Count - 1; i >= 0; i--)
        {
            _subscribers[i]();
        }
    }
}

/// <summary>The start/stop pair a platform builds for one <see cref="WorkflowFlowClock"/>.</summary>
public interface IWorkflowFlowDriver
{
    /// <summary>Starts (or restarts) the shared transition at the beginning of its cycle.</summary>
    void Start();

    /// <summary>Stops the shared transition and releases its timer.</summary>
    void Stop();
}
