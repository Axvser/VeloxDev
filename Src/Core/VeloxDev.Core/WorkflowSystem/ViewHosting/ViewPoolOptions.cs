using System;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// The serializable configuration of <see cref="AdaptiveViewPoolPolicy"/>.
/// </summary>
/// <remarks>
/// <para>
/// This type is a description, not a running policy. Serialize it to store a tuning profile, edit it to create a
/// variant, and pass it to <see cref="AdaptiveViewPoolPolicy(ViewPoolOptions)"/> or
/// <see cref="AdaptiveViewPoolPolicy.FromOptions"/> to replace the default policy. The learned throughput
/// and the current batch size are runtime state, not part of this description.
/// </para>
/// <para>
/// <see cref="Normalize"/> clamps every value into the supported range; the policy calls it in its constructor, so a
/// deserialized profile with missing or invalid fields is safe to use.
/// </para>
/// </remarks>
[Serializable]
public sealed class ViewPoolOptions
{
    /// <summary>Gets or sets the first batch size before any feedback has been observed.</summary>
    public int InitialBatchSize { get; set; } = 64;

    /// <summary>Gets or sets the first wall-clock budget, in milliseconds, before any feedback has been observed.</summary>
    public double InitialBudgetMilliseconds { get; set; } = 8d;

    /// <summary>Gets or sets the smallest batch size the policy may select.</summary>
    public int MinimumBatchSize { get; set; } = 16;

    /// <summary>Gets or sets the largest batch size the policy may select.</summary>
    public int MaximumBatchSize { get; set; } = 4096;

    /// <summary>Gets or sets the initial smoothed throughput, in views per millisecond.</summary>
    public double InitialViewsPerMillisecond { get; set; } = 0.25d;

    /// <summary>Gets or sets the pending count above which the medium budget applies.</summary>
    public int SmallPendingThreshold { get; set; } = 256;

    /// <summary>Gets or sets the pending count above which the large budget applies.</summary>
    public int LargePendingThreshold { get; set; } = 1024;

    /// <summary>Gets or sets the target budget, in milliseconds, for a small pending set.</summary>
    public double SmallBudgetMilliseconds { get; set; } = 8d;

    /// <summary>Gets or sets the target budget, in milliseconds, for a medium pending set.</summary>
    public double MediumBudgetMilliseconds { get; set; } = 12d;

    /// <summary>Gets or sets the target budget, in milliseconds, for a large pending set.</summary>
    public double LargeBudgetMilliseconds { get; set; } = 16d;

    /// <summary>
    /// Gets or sets the weight of the newest throughput sample in the smoothed estimate, from 0 through 1.
    /// Zero freezes the estimate; one uses only the newest batch.
    /// </summary>
    public double MeasurementWeight { get; set; } = 0.3d;

    /// <summary>Gets or sets the factor above the current batch size that triggers growth; must be greater than 1.</summary>
    public double GrowThreshold { get; set; } = 1.25d;

    /// <summary>Gets or sets the factor below the current batch size that triggers shrinkage; must be between 0 and 1.</summary>
    public double ShrinkThreshold { get; set; } = 0.8d;

    /// <summary>
    /// Gets or sets the maximum recycled views kept per runtime type; zero or negative means unlimited.
    /// </summary>
    public int MaxRecycledViewsPerType { get; set; }

    /// <summary>Creates an independent copy of this profile.</summary>
    /// <returns>A new profile with the same values.</returns>
    public ViewPoolOptions Clone()
        => new()
        {
            InitialBatchSize = InitialBatchSize,
            InitialBudgetMilliseconds = InitialBudgetMilliseconds,
            MinimumBatchSize = MinimumBatchSize,
            MaximumBatchSize = MaximumBatchSize,
            InitialViewsPerMillisecond = InitialViewsPerMillisecond,
            SmallPendingThreshold = SmallPendingThreshold,
            LargePendingThreshold = LargePendingThreshold,
            SmallBudgetMilliseconds = SmallBudgetMilliseconds,
            MediumBudgetMilliseconds = MediumBudgetMilliseconds,
            LargeBudgetMilliseconds = LargeBudgetMilliseconds,
            MeasurementWeight = MeasurementWeight,
            GrowThreshold = GrowThreshold,
            ShrinkThreshold = ShrinkThreshold,
            MaxRecycledViewsPerType = MaxRecycledViewsPerType,
        };

    /// <summary>Clamps every field into the range the policy can execute.</summary>
    /// <remarks>
    /// Missing JSON fields deserialize to their property defaults, and those defaults are already valid. This method
    /// exists for profiles that were edited by hand or produced by an older schema.
    /// </remarks>
    public void Normalize()
    {
        MinimumBatchSize = Math.Max(1, MinimumBatchSize);
        MaximumBatchSize = Math.Max(MinimumBatchSize, MaximumBatchSize);
        InitialBatchSize = Clamp(InitialBatchSize, MinimumBatchSize, MaximumBatchSize);
        InitialBudgetMilliseconds = Math.Max(0.1d, InitialBudgetMilliseconds);
        InitialViewsPerMillisecond = Math.Max(0.001d, InitialViewsPerMillisecond);
        SmallPendingThreshold = Math.Max(0, SmallPendingThreshold);
        LargePendingThreshold = Math.Max(SmallPendingThreshold, LargePendingThreshold);
        SmallBudgetMilliseconds = Math.Max(0.1d, SmallBudgetMilliseconds);
        MediumBudgetMilliseconds = Math.Max(0.1d, MediumBudgetMilliseconds);
        LargeBudgetMilliseconds = Math.Max(0.1d, LargeBudgetMilliseconds);
        MeasurementWeight = Clamp(MeasurementWeight, 0d, 1d);
        GrowThreshold = GrowThreshold > 1d ? GrowThreshold : 1.25d;
        ShrinkThreshold = ShrinkThreshold > 0d && ShrinkThreshold < 1d ? ShrinkThreshold : 0.8d;
        MaxRecycledViewsPerType = Math.Max(0, MaxRecycledViewsPerType);
    }

    private static int Clamp(int value, int minimum, int maximum)
        => Math.Min(maximum, Math.Max(minimum, value));

    private static double Clamp(double value, double minimum, double maximum)
        => Math.Min(maximum, Math.Max(minimum, value));
}
