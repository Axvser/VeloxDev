using System.Threading;

namespace VeloxDev.TransitionSystem.Abstractions;

/// <summary>
/// Per-property sampling container produced by normalization: each entry holds (sampler, start, end, options).
/// <see cref="Apply"/> marshals to the UI thread and calls <see cref="ISampler.InsertFrame"/> per property, skipping
/// when the animation is cancelled (stale-frame guard).
/// </summary>
public sealed class SamplerSet
{
    private readonly IUIThreadInspectorCore _inspector;
    private readonly List<Entry> _entries = [];
    private volatile CancellationTokenSource? _cts;

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

    public SamplerSet(IUIThreadInspectorCore inspector)
    {
        _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
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

    public bool CanSetValue() => _inspector.IsAppAlive();

    /// <summary>
    /// Marshals the per-property updates to the UI thread. Returns immediately when the animation is cancelled or
    /// the app is no longer alive, so stale queued frames never overwrite a reset result.
    /// </summary>
    public void Apply(object target, double t, object? priority = default)
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
        _inspector.ProtectedInvoke(target, _cachedApply!, priority);
    }

    private void ApplyCore(object target, double t)
    {
        foreach (var entry in _entries)
        {
            if (!CanSetValue()) return;
            entry.Sampler.InsertFrame(target, entry.Property, ref entry.Working, entry.Start, entry.End, entry.Options, t);
        }
    }
}
