using System.Collections.Concurrent;

namespace VeloxDev.Timing;

/// <summary>
/// The registry that hands out time sources and samplers, so a platform can substitute its own and everything else
/// keeps asking for the same contract.
/// </summary>
/// <remarks>
/// Core installs its defaults in the static constructor, so a lookup always resolves whether or not any platform
/// opted in. A platform replaces one by registering under the <em>contract</em> it is replacing — the key is the
/// contract type, not the implementation type:
/// <code>TimerCore.RegisterTimeSource&lt;ITimeSourceControl&gt;(static () => new GameLoopTimeSource());</code>
/// Registering under an implementation type would file it where nothing looks, so it is the contract that has to be
/// named. Installation is last-writer-wins and atomic, the same rule
/// <see cref="TransitionSystem.Abstractions.InterpolatorCore.RegisterInterpolator"/> follows.
/// <para>
/// The substitution that exists is for a <b>host that owns time</b> — a player loop, a media position, an audio
/// callback — which supplies its clock through <see cref="TimeSourceCore"/>'s protected constructor and reports
/// whether it is still feeding through <see cref="TimeSourceCore.SetHostFeeding"/>, rather than reimplementing the
/// timeline behind <see cref="ITimeSourceControl"/>. It is deliberately <em>not</em> for a framework's render loop:
/// a clock that only moves on frames makes the frame rate the timing authority, and the sampling path is built on
/// the opposite — the timeline decides how far an animation has gone, and the wake-up is only a reminder. Holding a
/// loop on a UI thread is a real but different problem, solved by <c>FramePacerCore</c> in the subsystem that owns
/// the loop, not here.
/// </para>
/// <para>
/// Every lookup builds a new instance, and a host with one clock keeps that shape: return a fresh wrapper over the
/// one feed per call rather than a shared singleton, so each consumer keeps its own pause and rate. A singleton
/// registered here would make pausing one channel pause every channel — the opposite of what the per-channel
/// sources in <c>MonoBehaviourManager</c> are for.
/// </para>
/// <para>
/// The keys are private and never handed out: a subclass or a caller replacing the dictionary wholesale would drop
/// the defaults installed here and leave every lookup unresolvable.
/// </para>
/// </remarks>
public static class TimerCore
{
    /// <summary>The fixed step Core's default compensating sampler starts from.</summary>
    public const int DefaultFixedStepMilliseconds = 16;

    private static readonly ConcurrentDictionary<Type, Func<ITimeSourceControl>> SourceFactories = new();
    private static readonly ConcurrentDictionary<Type, Func<ITimeSource, ITimeSampler>> SamplerFactories = new();

    static TimerCore()
    {
        RegisterTimeSource<ITimeSourceControl>(static () => new TimeSourceCore());
        RegisterTimeSampler<IUncompensatedTimeSampler>(static source => new UncompensatedTimeSampler(source));
        RegisterTimeSampler<ICompensatingTimeSampler>(
            static source => new CompensatingTimeSampler(source, TimeSpan.FromMilliseconds(DefaultFixedStepMilliseconds)));
    }

    /// <summary>
    /// Installs <paramref name="factory"/> as the source for <typeparamref name="TContract"/>, replacing whatever
    /// was there.
    /// </summary>
    /// <remarks>
    /// The type argument is the contract being replaced, not the type being installed: register
    /// <c>RegisterTimeSource&lt;ITimeSourceControl&gt;(...)</c>, not the concrete source's own type.
    /// </remarks>
    public static bool RegisterTimeSource<TContract>(Func<TContract> factory)
        where TContract : class, ITimeSourceControl
    {
        if (factory is null) throw new ArgumentNullException(nameof(factory));

        SourceFactories.AddOrUpdate(
            typeof(TContract),
            factory,
            (_, _) => factory);
        return true;
    }

    /// <summary>
    /// Installs <paramref name="factory"/> as the sampler for <typeparamref name="TSampler"/>, replacing whatever
    /// was there. As with <see cref="RegisterTimeSource{TContract}"/>, the type argument is the contract.
    /// </summary>
    public static bool RegisterTimeSampler<TSampler>(Func<ITimeSource, TSampler> factory)
        where TSampler : class, ITimeSampler
    {
        if (factory is null) throw new ArgumentNullException(nameof(factory));

        SamplerFactories.AddOrUpdate(
            typeof(TSampler),
            factory,
            (_, _) => factory);
        return true;
    }

    /// <summary>Removes a source registration, leaving the contract unresolvable again.</summary>
    public static bool UnregisterTimeSource<TContract>() where TContract : class, ITimeSourceControl
        => SourceFactories.TryRemove(typeof(TContract), out _);

    /// <summary>Removes a sampler registration, leaving the contract unresolvable again.</summary>
    public static bool UnregisterTimeSampler<TSampler>() where TSampler : class, ITimeSampler
        => SamplerFactories.TryRemove(typeof(TSampler), out _);

    /// <summary>
    /// Creates a source for <typeparamref name="TContract"/>, which is always the Core default unless a platform
    /// registered over that same contract.
    /// </summary>
    /// <remarks>
    /// Looked up by exact contract, with no fallback to a wider or narrower registration. A fallback would have to
    /// pick between several assignable keys, and dictionary order is not specified — the same lookup would then
    /// return different implementations across runs. Asking for <see cref="ITimeSourceControl"/> is what a consumer
    /// does, since that is the contract Core provides and the one a platform replaces.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Nothing is registered under <typeparamref name="TContract"/>.</exception>
    public static TContract CreateTimeSource<TContract>() where TContract : class, ITimeSourceControl
    {
        var requested = typeof(TContract);

        if (SourceFactories.TryGetValue(requested, out var factory) && factory() is TContract source)
        {
            return source;
        }

        throw new InvalidOperationException(
            $"No time source is registered for {requested.FullName}. Register one under that same contract with " +
            $"{nameof(RegisterTimeSource)}<{requested.Name}>(...); a registration under an implementation type is not found by a contract lookup.");
    }

    /// <summary>Creates a sampler over <paramref name="source"/>, or throws when the contract is unregistered.</summary>
    /// <exception cref="InvalidOperationException">Nothing is registered under <typeparamref name="TSampler"/>.</exception>
    public static TSampler CreateTimeSampler<TSampler>(ITimeSource source) where TSampler : class, ITimeSampler
    {
        if (source is null) throw new ArgumentNullException(nameof(source));

        var requested = typeof(TSampler);

        if (SamplerFactories.TryGetValue(requested, out var factory) && factory(source) is TSampler sampler)
        {
            return sampler;
        }

        throw new InvalidOperationException(
            $"No time sampler is registered for {requested.FullName}. Register one under that same contract with " +
            $"{nameof(RegisterTimeSampler)}<{requested.Name}>(...).");
    }
}
