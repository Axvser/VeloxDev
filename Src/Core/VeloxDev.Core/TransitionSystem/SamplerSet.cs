using System.Threading;
using VeloxDev.Threading;
using VeloxDev.Timing;

namespace VeloxDev.TransitionSystem.Abstractions;

/// <summary>
/// Per-property sampling container produced by normalization: each entry holds (sampler, start, end, options).
/// <see cref="Apply"/> marshals to the UI thread and calls <see cref="ISampler.InsertFrame"/> per property, skipping
/// when the animation is cancelled (stale-frame guard).
/// </summary>
/// <typeparam name="TPriorityCore">
/// The host's dispatcher priority type, or <see cref="NonPriority"/> for a host that has none. Carrying it as a type
/// parameter lets <see cref="Apply"/> hand the priority to the host unboxed: the previous <c>object?</c> parameter
/// boxed a <c>DispatcherPriority</c> on every frame of every animation.
/// </typeparam>
public sealed class SamplerSet<TPriorityCore>
{
    private readonly ITransitionHost<TPriorityCore> _host;
    private readonly List<Entry> _entries = [];
    private volatile CancellationTokenSource? _cts;
    private TransitionRun? _run;
    private TransitionDiagnostics? _diagnostics;

    // Reusable UI-thread apply delegate: one closure per target (fixed per animation), with the time passed via a
    // field instead of a capture — avoids a closure allocation per sample.
    private Action? _cachedApply;
    private object? _cachedTarget;
    private long _cachedTimeBits;

    private sealed class Entry
    {
        public Entry(ITransitionProperty property, ISampler sampler, object? start, object? end, object? options)
        {
            Property = property;
            Sampler = sampler;
            Start = start;
            End = end;
            Options = options;
        }

        public ITransitionProperty Property { get; }
        public ISampler Sampler { get; }
        public object? Start { get; }
        public object? End { get; }
        public object? Options { get; }

        // Per-animation reusable scratch, lazily created by the sampler on the first middle-frame call.
        public object? Working;
    }

    public SamplerSet(ITransitionHost<TPriorityCore> host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    internal void Add(ITransitionProperty property, ISampler sampler, object? start, object? end, object? options)
    {
        _entries.Add(new Entry(property, sampler, start, end, options));
    }

    /// <summary>
    /// Lets the sampler set carry the animation's cancellation token: after the animation is cancelled (e.g.
    /// <c>Transition.Exit</c> on reset), stale updates already queued to the UI thread are skipped in
    /// <see cref="Apply"/>, preventing them from overwriting the reset result.
    /// </summary>
    internal void SetCancellation(CancellationTokenSource cts)
    {
        _cts = cts;
    }

    /// <summary>
    /// The animation being sampled, carried the same way the token source is: it holds the timeline the frames are
    /// positioned against and the pass they belong to. The scheduler installs it before the interpreter starts; a
    /// set built outside the scheduler — a test driving the interpreter directly — keeps a private run on its own
    /// timeline, which nothing controls, so the sampling loop never has to handle a null one.
    /// </summary>
    internal TransitionRun Run => _run ??= new TransitionRun(TimerCore.CreateTimeSource<ITimeSourceControl>());

    internal void SetRun(TransitionRun run) => _run = run;

    internal void SetDiagnostics(TransitionDiagnostics diagnostics) => _diagnostics = diagnostics;

    /// <summary>
    /// The host every write goes through, exposed so the interpreter can derive its frame pacer from the same answer
    /// instead of re-deriving the thread from the platform and risking a pacer on a different one.
    /// </summary>
    internal ITransitionHost<TPriorityCore> Host => _host;

    public bool CanSetValue() => _host.IsAlive;

    /// <summary>
    /// Marshals the per-property updates to the UI thread. Returns immediately when the animation is cancelled or
    /// the app is no longer alive, so stale queued frames never overwrite a reset result.
    /// </summary>
    /// <param name="priority">
    /// Passed straight to the host, unboxed. Omitting it passes <c>default(TPriorityCore)</c> — the zero value
    /// of the host's priority, which for <see cref="NonPriority"/> is the whole story, since it carries nothing.
    /// </param>
    public void Apply(object target, double t, TPriorityCore priority = default!)
    {
        if (_cts?.IsCancellationRequested == true) return;
        if (!CanSetValue()) return;
        if (!ReferenceEquals(_cachedTarget, target))
        {
            _cachedTarget = target;
            _cachedApply = () => ApplyCore(
                _cachedTarget!,
                BitConverter.Int64BitsToDouble(Interlocked.Read(ref _cachedTimeBits)));
        }
        Interlocked.Exchange(ref _cachedTimeBits, BitConverter.DoubleToInt64Bits(t));

        // 这一趟的线程由启动它的调度器钉在 run 上；没有 run 的调用方——测试直接驱动解释器——才就近问宿主。
        var thread = _run is { } bound && !bound.Thread.IsNone ? bound.Thread : _host.ThreadFor(target);
        if (!_host.Post(target, thread, _cachedApply!, priority))
        {
            _diagnostics?.Warn("Dropped", "the host refused a frame; the animation carries on without it.");
        }
    }

    private void ApplyCore(object target, double t)
    {
        // Re-checked here, not only in Apply: the write happens on the UI thread when the queued message is pumped,
        // and the animation can be cancelled in between. Checking only before queueing lets a frame that was already
        // stale when it landed run anyway and silently overwrite a reset.
        if (_cts?.IsCancellationRequested == true) return;

        foreach (var entry in _entries)
        {
            if (!CanSetValue()) return;

            try
            {
                entry.Sampler.InsertFrame(target, entry.Property, ref entry.Working, entry.Start, entry.End, entry.Options, t);
            }
            catch (Exception exception)
            {
                // 一个绘制不动的动画不该继续以帧率抛异常：报一次，结束这一趟。
                _diagnostics?.Error("Sampling", exception);
                CancelQuietly();
                return;
            }
        }
    }

    private void CancelQuietly()
    {
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 动画或适配器已经释放了令牌源，没有可取消的东西了。
        }
    }
}
