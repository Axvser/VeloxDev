using System.Diagnostics;
using System.Text.Json;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

[TestClass]
public sealed class AdaptiveViewPoolPolicyTests
{
    [TestMethod]
    public void DefaultsMatchThePreviousBehavior()
    {
        var policy = new AdaptiveViewPoolPolicy();

        Assert.AreEqual(64, policy.BatchSize);
        Assert.AreEqual(8d, policy.FrameBudgetMilliseconds, 0.0001d);
        Assert.AreEqual(0, policy.MaxRecycledViewsPerType);
    }

    [TestMethod]
    public void OptionsRoundTripThroughJsonAndDriveThePolicy()
    {
        var options = new ViewPoolOptions
        {
            InitialBatchSize = 32,
            InitialBudgetMilliseconds = 6d,
            MinimumBatchSize = 8,
            MaximumBatchSize = 512,
            InitialViewsPerMillisecond = 1.5d,
            SmallPendingThreshold = 100,
            LargePendingThreshold = 500,
            SmallBudgetMilliseconds = 4d,
            MediumBudgetMilliseconds = 6d,
            LargeBudgetMilliseconds = 10d,
            MeasurementWeight = 0.4d,
            GrowThreshold = 1.5d,
            ShrinkThreshold = 0.5d,
            MaxRecycledViewsPerType = 32,
        };

        var json = JsonSerializer.Serialize(options);
        var restored = JsonSerializer.Deserialize<ViewPoolOptions>(json);
        Assert.IsNotNull(restored);

        var policy = AdaptiveViewPoolPolicy.FromOptions(restored);
        Assert.AreEqual(32, policy.BatchSize);
        Assert.AreEqual(6d, policy.FrameBudgetMilliseconds, 0.0001d);
        Assert.AreEqual(32, policy.MaxRecycledViewsPerType);
        Assert.AreEqual(1.5d, policy.Options.InitialViewsPerMillisecond, 0.0001d);
    }

    [TestMethod]
    public void InvalidOptionsAreNormalizedBeforeUse()
    {
        var options = new ViewPoolOptions
        {
            InitialBatchSize = 0,
            InitialBudgetMilliseconds = -1d,
            MinimumBatchSize = 0,
            MaximumBatchSize = -4,
            InitialViewsPerMillisecond = -1d,
            SmallPendingThreshold = -1,
            LargePendingThreshold = -1,
            SmallBudgetMilliseconds = 0d,
            MediumBudgetMilliseconds = -1d,
            LargeBudgetMilliseconds = -1d,
            MeasurementWeight = 2d,
            GrowThreshold = 1d,
            ShrinkThreshold = 2d,
            MaxRecycledViewsPerType = -5,
        };

        options.Normalize();

        Assert.IsTrue(options.MinimumBatchSize >= 1);
        Assert.IsTrue(options.MaximumBatchSize >= options.MinimumBatchSize);
        Assert.IsTrue(options.InitialBatchSize >= options.MinimumBatchSize);
        Assert.IsTrue(options.InitialBatchSize <= options.MaximumBatchSize);
        Assert.IsTrue(options.InitialBudgetMilliseconds > 0d);
        Assert.IsTrue(options.InitialViewsPerMillisecond > 0d);
        Assert.IsTrue(options.SmallPendingThreshold >= 0);
        Assert.IsTrue(options.LargePendingThreshold >= options.SmallPendingThreshold);
        Assert.IsTrue(options.SmallBudgetMilliseconds > 0d);
        Assert.IsTrue(options.MediumBudgetMilliseconds > 0d);
        Assert.IsTrue(options.LargeBudgetMilliseconds > 0d);
        Assert.IsTrue(options.MeasurementWeight is >= 0d and <= 1d);
        Assert.IsTrue(options.GrowThreshold > 1d);
        Assert.IsTrue(options.ShrinkThreshold is > 0d and < 1d);
        Assert.IsTrue(options.MaxRecycledViewsPerType >= 0);
    }

    [TestMethod]
    public void FeedbackGrowsTheBatchTowardThePendingBudget()
    {
        var policy = new AdaptiveViewPoolPolicy(new ViewPoolOptions
        {
            InitialBatchSize = 16,
            MinimumBatchSize = 16,
            MaximumBatchSize = 4096,
            InitialViewsPerMillisecond = 0.25d,
            MeasurementWeight = 0.3d,
            SmallPendingThreshold = 10,
            LargePendingThreshold = 20,
            SmallBudgetMilliseconds = 8d,
            MediumBudgetMilliseconds = 12d,
            LargeBudgetMilliseconds = 16d,
        });

        // One millisecond for 100 views -> the estimate jumps; a large pending set targets 16 ms.
        policy.OnBatchCompleted(100, Stopwatch.Frequency / 1000, pending: 1000);

        Assert.AreEqual(16d, policy.FrameBudgetMilliseconds, 0.0001d);
        Assert.IsTrue(policy.BatchSize > 16, $"Expected growth, got {policy.BatchSize}.");
        Assert.IsTrue(policy.BatchSize <= 4096);

        policy.OnBatchCompleted(10, Stopwatch.Frequency / 1000, pending: 0);
        Assert.AreEqual(8d, policy.FrameBudgetMilliseconds, 0.0001d);
    }

    [TestMethod]
    public void HysteresisKeepsTheBatchWhenTheEstimateIsClose()
    {
        var policy = new AdaptiveViewPoolPolicy(new ViewPoolOptions
        {
            InitialBatchSize = 100,
            MinimumBatchSize = 1,
            MaximumBatchSize = 1000,
            InitialViewsPerMillisecond = 12.5d,
            MeasurementWeight = 0d,
            SmallBudgetMilliseconds = 8d,
            MediumBudgetMilliseconds = 12d,
            LargeBudgetMilliseconds = 16d,
        });

        // 12.5 views/ms * 8 ms = 100, exactly the current batch; the estimate is frozen by weight 0.
        policy.OnBatchCompleted(100, Stopwatch.Frequency / 1000, pending: 0);

        Assert.AreEqual(100, policy.BatchSize);
        Assert.AreEqual(8d, policy.FrameBudgetMilliseconds, 0.0001d);
    }

    [TestMethod]
    public void OptionsPropertyIsDetachedFromTheRunningPolicy()
    {
        var policy = new AdaptiveViewPoolPolicy(new ViewPoolOptions
        {
            InitialBatchSize = 32,
        });

        var snapshot = policy.Options;
        snapshot.InitialBatchSize = 999;

        Assert.AreEqual(32, policy.Options.InitialBatchSize);
    }
}
