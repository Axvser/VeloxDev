using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;
using VeloxDev.Threading;

namespace VeloxDev.Core.Test.TransitionSystem;

/// <summary>
/// A chain's loops: <c>Repeat(n)</c> on a segment repeats the chain from its first segment through that one,
/// n further times, and loops nest by where they end.
/// </summary>
/// <remarks>
/// Deterministic by construction. Every segment in the counting tests has a zero duration, which makes a pass
/// write exactly one frame (its endpoint) and lets the whole chain run without waiting on a clock; the one test
/// that needs a loop to still be running reads a short real duration instead, because a zero-duration pass
/// completes without yielding and a forever loop built on those would spin rather than loop.
/// </remarks>
[TestClass]
public class ChainRepeatTests
{
    private sealed class Target
    {
        public double First { get; set; }
        public double Second { get; set; }
    }

    private sealed class Stats
    {
        public int Updates { get; set; }
        public int Completed { get; set; }
    }

    /// <summary>Records every frame a path is asked to write, with the endpoints it was handed.</summary>
    private sealed class RecordingSampler : ISampler
    {
        public List<(double Start, double End, double T)> Frames { get; } = [];

        public object? NormalizeStart(object? start, object? end, object? options) => start;

        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working,
            object? start, object? end, object? options, double t)
        {
            var from = (double)start!;
            var to = (double)end!;
            Frames.Add((from, to, t));

            // Writes the interpolated value, as a real sampler does — the target has to end up where the
            // animation says it does, or an iteration that re-read the target would look like a replay.
            property.SetValue(target, from + ((to - from) * t));
        }
    }

    private sealed class TestInterpolator : InterpolatorCore
    {
    }

    private sealed class TestInterpreter : TransitionInterpreterCore<TransitionEffectCore>
    {
    }

    private sealed class ImmediateInspector : ImmediateHost
    {
    }

    /// <summary>One segment of a test chain, with what it animates and reports reachable from here.</summary>
    private sealed class ChainNode : TransitionCore<
        Target, StateCore, TransitionEffectCore, TestInterpolator, ImmediateInspector, TestInterpreter, NonPriority>
    {
        public ChainNode OnlyFirst(double end)
        {
            state.SetValue<Target, double>(t => t.First, end);
            return this;
        }

        public ChainNode OnlySecond(double end)
        {
            state.SetValue<Target, double>(t => t.Second, end);
            return this;
        }

        public ChainNode RecordsFirst(ISampler sampler)
        {
            state.SetInterpolator<Target, double>(t => t.First, sampler);
            return this;
        }

        public ChainNode RecordsSecond(ISampler sampler)
        {
            state.SetInterpolator<Target, double>(t => t.Second, sampler);
            return this;
        }

        /// <summary>
        /// Attaches an effect and counts what it reports, optionally appending <paramref name="index"/> to
        /// <paramref name="order"/> as it completes — which is how a test reads the order the segments ran in.
        /// Each segment gets its own effect instance: the events live on the effect, so one shared by two segments
        /// would count both segments' frames in one.
        /// </summary>
        public ChainNode WithEffect(TransitionEffectCore effect, Stats stats, List<int>? order = null, int index = 0)
        {
            var wired = CoreEffect<ChainNode, TransitionEffectCore>(effect);
            effect.Update += (_, _) => stats.Updates++;
            effect.Completed += (_, _) =>
            {
                stats.Completed++;
                order?.Add(index);
            };
            return wired;
        }
    }

    /// <summary>One pass, one frame, no waiting.</summary>
    private static TransitionEffectCore Tiny(int loopTime = 0) => new()
    {
        Duration = TimeSpan.Zero,
        LoopTime = loopTime,
        Ease = Eases.Default,
    };

    private static async Task WaitFor(Func<bool> condition, string what)
    {
        for (var i = 0; i < 300; i++)
        {
            if (condition()) return;
            await Task.Delay(10);
        }

        Assert.Fail($"timed out waiting for {what}");
    }

    /// <summary>
    /// A chain of three segments 1, 2, 3, each asking for one further iteration of the loop it closes.
    /// </summary>
    private static ChainNode ThreeSegments(
        List<int> order, Stats[] stats, out ChainNode second, out ChainNode third)
    {
        var chain = TransitionCore.Create<ChainNode>();
        chain.OnlyFirst(100d).WithEffect(Tiny(), stats[0], order, 1);
        second = chain.Then();
        second.OnlySecond(100d).WithEffect(Tiny(), stats[1], order, 2);
        third = second.Then();
        third.OnlyFirst(50d).WithEffect(Tiny(), stats[2], order, 3);
        return chain;
    }

    /// <summary>
    /// A chain of three segments animates all three. This is the case that used to fail silently: a segment whose
    /// turn came second broke out of its own sampling loop before its first pass, because the loop guard read a
    /// pass counter the whole run shares — it reported `Start` and `Completed` and wrote no frame at all.
    /// </summary>
    [TestMethod]
    public async Task Chain_RunsEverySegment()
    {
        var target = new Target();
        var firstFrames = new RecordingSampler();
        var secondFrames = new RecordingSampler();
        var first = new Stats();
        var second = new Stats();

        var chain = TransitionCore.Create<ChainNode>();
        chain.OnlyFirst(100d).RecordsFirst(firstFrames).WithEffect(Tiny(), first);
        var tail = chain.Then();
        tail.OnlySecond(100d).RecordsSecond(secondFrames).WithEffect(Tiny(), second);

        chain.Execute(target);
        await WaitFor(() => second.Completed == 1, "the second segment to complete");

        Assert.AreEqual(1, first.Completed, "the first segment completed once");
        Assert.AreEqual(1, second.Completed, "the second segment completed once");
        Assert.AreEqual(1, firstFrames.Frames.Count, "the first segment wrote its frame");
        Assert.AreEqual(1, secondFrames.Frames.Count, "the second segment wrote its frame");
    }

    /// <summary>
    /// The worked example: three segments each carrying <c>Repeat(1)</c> run
    /// <c>1, 1, 2, 1, 1, 2, 3, 1, 1, 2, 1, 1, 2, 3</c>.
    /// </summary>
    /// <remarks>
    /// The middle segment's loop closes around the first, and the last segment's closes around both, so the
    /// innermost repetition is the earliest segment's. Reading it out in order is the only way to see whether the
    /// nesting came out right: the counts alone are the same for any arrangement.
    /// </remarks>
    [TestMethod]
    public async Task Repeat_LoopsNestByWhereTheyEnd()
    {
        var target = new Target();
        var order = new List<int>();
        var stats = new[] { new Stats(), new Stats(), new Stats() };

        var chain = ThreeSegments(order, stats, out var second, out var third);
        chain.Repeat(1);
        second.Repeat(1);
        third.Repeat(1);

        chain.Execute(target);
        await WaitFor(() => stats[2].Completed == 2, "the outer loop's two passes");
        await Task.Delay(80);

        CollectionAssert.AreEqual(
            new[] { 1, 1, 2, 1, 1, 2, 3, 1, 1, 2, 1, 1, 2, 3 },
            order,
            "the order the segments ran in");
    }

    /// <summary>A count on the first segment's loop repeats only what that segment does.</summary>
    [TestMethod]
    public async Task Repeat_OnTheFirstSegment_LoopsOnlyIt()
    {
        var target = new Target();
        var order = new List<int>();
        var stats = new[] { new Stats(), new Stats(), new Stats() };

        var chain = ThreeSegments(order, stats, out _, out _);
        chain.Repeat(2);

        chain.Execute(target);
        await WaitFor(() => stats[2].Completed == 1, "the chain to finish");
        await Task.Delay(80);

        CollectionAssert.AreEqual(new[] { 1, 1, 1, 2, 3 }, order, "the order the segments ran in");
    }

    /// <summary>A count on the last segment's loop repeats the whole chain.</summary>
    [TestMethod]
    public async Task Repeat_OnTheLastSegment_LoopsTheWholeChain()
    {
        var target = new Target();
        var order = new List<int>();
        var stats = new[] { new Stats(), new Stats(), new Stats() };

        var chain = ThreeSegments(order, stats, out _, out var third);
        third.Repeat(2);

        chain.Execute(target);
        await WaitFor(() => stats[2].Completed == 3, "three passes of the chain");
        await Task.Delay(80);

        CollectionAssert.AreEqual(new[] { 1, 2, 3, 1, 2, 3, 1, 2, 3 }, order, "the order the segments ran in");
    }

    [TestMethod]
    public async Task Repeat_Zero_RunsTheChainOnce()
    {
        var target = new Target();
        var order = new List<int>();
        var stats = new[] { new Stats(), new Stats(), new Stats() };

        var chain = ThreeSegments(order, stats, out _, out _);
        chain.Execute(target);
        await WaitFor(() => stats[2].Completed == 1, "the chain to finish");
        await Task.Delay(80);

        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, order, "the order the segments ran in");
    }

    /// <summary>
    /// An iteration after a segment's first replays the endpoints that first iteration captured rather than
    /// re-reading the target — which is what keeps a repeated segment from walking backwards, or drifting a
    /// little further every pass. The first iteration moves the target, so the two readings are distinguishable.
    /// </summary>
    [TestMethod]
    public async Task Repeat_ReplaysTheEndpointsTheFirstIterationCaptured()
    {
        var target = new Target();
        var frames = new RecordingSampler();
        var stats = new Stats();

        var chain = TransitionCore.Create<ChainNode>();
        chain.OnlyFirst(100d).RecordsFirst(frames).WithEffect(Tiny(), stats);
        chain.Repeat(1);

        chain.Execute(target);
        await WaitFor(() => stats.Completed == 2, "two iterations");
        await Task.Delay(80);

        Assert.AreEqual(2, frames.Frames.Count, "one frame per iteration");
        Assert.AreEqual(0d, frames.Frames[0].Start, "the first iteration started from the target's own value");
        Assert.AreEqual(0d, frames.Frames[1].Start, "the second started from that same captured value");
        Assert.AreEqual(100d, target.First, "and the target is left at the captured end");
    }

    /// <summary>
    /// A one-segment chain looping through <c>Repeat</c> is the same animation as that segment looping through
    /// its own <c>LoopTime</c>: a chain's loop generalizes a segment's rather than competing with it.
    /// </summary>
    [TestMethod]
    public async Task Repeat_OnASingleSegment_MatchesTheSegmentsOwnLoop()
    {
        var chainTarget = new Target();
        var chainFrames = new RecordingSampler();
        var chain = new Stats();
        var chained = TransitionCore.Create<ChainNode>();
        chained.OnlyFirst(100d).RecordsFirst(chainFrames).WithEffect(Tiny(), chain);
        chained.Repeat(2);
        chained.Execute(chainTarget);
        await WaitFor(() => chain.Completed == 3, "the chained loop");

        var loopTarget = new Target();
        var loopFrames = new RecordingSampler();
        var loop = new Stats();
        var looping = TransitionCore.Create<ChainNode>();
        looping.OnlyFirst(100d).RecordsFirst(loopFrames).WithEffect(Tiny(loopTime: 2), loop);
        looping.Execute(loopTarget);
        await WaitFor(() => loop.Completed == 1, "the segment loop");

        await Task.Delay(80);
        Assert.AreEqual(3, chainFrames.Frames.Count, "three frames from the chained loop");
        Assert.AreEqual(loopFrames.Frames.Count, chainFrames.Frames.Count, "the same animation either way");
        Assert.AreEqual(3, chain.Updates, "three passes either way");
        Assert.AreEqual(3, loop.Updates, "the segment's own loop also runs three passes");
        Assert.AreEqual(3, chain.Completed, "the chain completes once per iteration");
        Assert.AreEqual(1, loop.Completed, "the segment completes once for its whole loop");
    }

    /// <summary>A forever loop keeps running until it is stopped, and stops when it is.</summary>
    [TestMethod]
    public async Task Repeat_Forever_RunsUntilExit()
    {
        var target = new Target();
        var order = new List<int>();
        var stats = new[] { new Stats(), new Stats() };

        var chain = TransitionCore.Create<ChainNode>();
        chain.OnlyFirst(100d).WithEffect(
            new TransitionEffectCore { Duration = TimeSpan.FromMilliseconds(5), Ease = Eases.Default }, stats[0], order, 1);
        var second = chain.Then();
        second.OnlySecond(100d).WithEffect(
            new TransitionEffectCore { Duration = TimeSpan.FromMilliseconds(5), Ease = Eases.Default }, stats[1], order, 2);

        second.Repeat(int.MaxValue);
        chain.Execute(target);

        await WaitFor(() => stats[1].Completed >= 3, "the chain to loop");

        TransitionCore.Exit(target);
        await Task.Delay(40);
        var settled = order.Count;
        await Task.Delay(150);

        Assert.IsTrue(settled >= 6, "the loop ran more than once before it was stopped");
        Assert.AreEqual(settled, order.Count, "no segment after Exit");
        CollectionAssert.AreEqual(new[] { 1, 2, 1, 2, 1, 2 }, order.GetRange(0, 6), "the chain loops as a unit");
    }
}
