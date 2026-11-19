using System;
using System.Diagnostics;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// A pool policy that tunes its batch size from measured throughput, targeting a larger time budget
/// while a large visible set is materializing and a smaller one during ordinary scrolling.
/// </summary>
/// <remarks>
/// <para>
/// Throughput is a smoothed views-per-millisecond estimate. After each batch the policy computes the
/// batch size that would fill the current target budget and updates only when the new value differs
/// enough to avoid oscillation. The target budget is 8 ms for a small pending set, 12 ms above 256
/// and 16 ms above 1024, so bulk visibility gets through the dispatcher faster without making every
/// scroll step wait for a full batch.
/// </para>
/// <para>
/// This is per-process feedback, not a machine fingerprint. It needs no calibration pass and adapts
/// to the current adapter, template complexity and machine automatically. The tunable parameters live
/// in <see cref="ViewPoolOptions"/>, which is serializable and can be stored or
/// edited independently of this policy instance.
/// </para>
/// </remarks>
public sealed class AdaptiveViewPoolPolicy : IViewPoolPolicy, IViewBatchFeedback
{
    private readonly ViewPoolOptions options;
    private double viewsPerMillisecond;
    private int batchSize;
    private double frameBudgetMilliseconds;

    /// <summary>Creates an adaptive policy with conservative initial values.</summary>
    /// <param name="initialBatchSize">The first batch size; clamps to the supported range.</param>
    /// <param name="initialBudgetMilliseconds">The first time budget; clamps to a positive value.</param>
    public AdaptiveViewPoolPolicy(int initialBatchSize = 64, double initialBudgetMilliseconds = 8d)
        : this(new ViewPoolOptions
        {
            InitialBatchSize = initialBatchSize,
            InitialBudgetMilliseconds = initialBudgetMilliseconds,
        })
    {
    }

    /// <summary>Creates an adaptive policy from a serializable profile.</summary>
    /// <param name="options">The profile to copy and use; it is normalized before the policy starts.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public AdaptiveViewPoolPolicy(ViewPoolOptions options)
    {
        this.options = (options ?? throw new ArgumentNullException(nameof(options))).Clone();
        this.options.Normalize();

        batchSize = this.options.InitialBatchSize;
        frameBudgetMilliseconds = this.options.InitialBudgetMilliseconds;
        viewsPerMillisecond = this.options.InitialViewsPerMillisecond;
    }

    /// <summary>Gets a copy of the profile this policy was created from.</summary>
    /// <remarks>
    /// The returned object is detached from the running policy; edit it and construct a new policy to replace the
    /// current tuning. The learned throughput and current batch size are not part of the profile.
    /// </remarks>
    public ViewPoolOptions Options => options.Clone();

    /// <summary>Creates a policy from a serializable profile.</summary>
    /// <param name="options">The profile to copy and use.</param>
    /// <returns>A new adaptive policy.</returns>
    public static AdaptiveViewPoolPolicy FromOptions(ViewPoolOptions options)
        => new(options);

    /// <inheritdoc />
    public int BatchSize => batchSize;

    /// <inheritdoc />
    public double FrameBudgetMilliseconds => frameBudgetMilliseconds;

    /// <inheritdoc />
    public int MaxRecycledViewsPerType => options.MaxRecycledViewsPerType;

    /// <inheritdoc />
    public void OnBatchCompleted(int processed, long elapsedTicks, int pending)
    {
        if (processed <= 0 || elapsedTicks <= 0)
        {
            return;
        }

        var elapsedMilliseconds = elapsedTicks * 1000d / Stopwatch.Frequency;
        var throughput = processed / Math.Max(0.001d, elapsedMilliseconds);
        var previousWeight = 1d - options.MeasurementWeight;
        viewsPerMillisecond = viewsPerMillisecond * previousWeight + throughput * options.MeasurementWeight;

        var targetBudget = pending > options.LargePendingThreshold
            ? options.LargeBudgetMilliseconds
            : pending > options.SmallPendingThreshold
                ? options.MediumBudgetMilliseconds
                : options.SmallBudgetMilliseconds;
        var desired = Clamp(
            (int)Math.Round(viewsPerMillisecond * targetBudget),
            options.MinimumBatchSize,
            options.MaximumBatchSize);

        // The estimate moves every batch; only move the actual batch size when it is meaningfully
        // different, otherwise the policy keeps re-allocating the same tail work.
        if (desired > batchSize * options.GrowThreshold || desired < batchSize * options.ShrinkThreshold)
        {
            batchSize = desired;
        }

        frameBudgetMilliseconds = targetBudget;
    }

    private static int Clamp(int value, int minimum, int maximum)
        => Math.Min(maximum, Math.Max(minimum, value));
}
