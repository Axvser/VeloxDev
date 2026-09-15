using System.Diagnostics;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.Core.Test.TransitionSystem;

/// <summary>
/// <see cref="ReusableTimerWait"/> 的契约。这里的每条断言都对应「采样循环停下来」的一种方式——一个不再唤醒的
/// 等待对象不会报错，它只会让动画永远停在那里，所以这些必须是被测过的行为而不是注释。
/// </summary>
/// <remarks>
/// 串行：分配那条断言量的是进程级计数（<c>GC.GetTotalAllocatedBytes</c>），方法级并行时别的方法的分配会落进
/// 它的测量窗口，best-of-2 只是缓解。
/// </remarks>
[TestClass]
[DoNotParallelize]
public class ReusableTimerWaitTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(30);

    [TestMethod]
    public async Task WakesAfterTheInterval()
    {
        using var wait = new ReusableTimerWait();
        var watch = Stopwatch.StartNew();
        var fired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _ = Task.Run(async () =>
        {
            await wait.Await(Interval, CancellationToken.None);
            fired.TrySetResult(true);
        });

        await fired.Task.WaitAsync(TimeSpan.FromSeconds(5));
        watch.Stop();

        // 早于间隔醒来意味着速率上限没兜住；晚太多也不必断言（时间轴才是权威），所以只查下界。
        Assert.IsTrue(watch.Elapsed >= Interval - TimeSpan.FromMilliseconds(8), $"woke after {watch.ElapsedMilliseconds}ms");
    }

    [TestMethod]
    public void DoesNotBlockTheCallingThread()
    {
        using var wait = new ReusableTimerWait();
        var fired = false;
        var watch = Stopwatch.StartNew();

        wait.Schedule(() => fired = true, TimeSpan.FromSeconds(30), CancellationToken.None);

        watch.Stop();
        Assert.IsTrue(watch.ElapsedMilliseconds < 50, $"Schedule blocked for {watch.ElapsedMilliseconds}ms");
        Assert.IsFalse(fired, "the continuation must not run on the scheduling thread");
    }

    [TestMethod]
    public async Task WakesExactlyOnce()
    {
        using var wait = new ReusableTimerWait();
        var count = 0;
        var fired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _ = Task.Run(async () =>
        {
            await wait.Await(TimeSpan.FromMilliseconds(10), CancellationToken.None);
            Interlocked.Increment(ref count);
            fired.TrySetResult(true);
        });

        await fired.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(TimeSpan.FromMilliseconds(120)); // 三个间隔，足够让多出来的唤醒冒头

        Assert.AreEqual(1, count);
    }

    [TestMethod]
    public async Task WakesWhenCancelledBeforeTheInterval()
    {
        using var wait = new ReusableTimerWait();
        using var cts = new CancellationTokenSource();
        var watch = Stopwatch.StartNew();

        var waiting = Task.Run(async () =>
        {
            try { await wait.Await(TimeSpan.FromSeconds(30), cts.Token); }
            catch (OperationCanceledException) { }
        });

        await Task.Delay(TimeSpan.FromMilliseconds(20));
        cts.Cancel();
        await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        watch.Stop();

        // 不唤醒的话循环会一直挂到 30 秒；唤醒后由等待侧抛出，所以这里应当很快返回。
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(3), $"took {watch.ElapsedMilliseconds}ms to observe cancellation");
    }

    [TestMethod]
    public async Task AwaitThrowsOnlyForCancellation()
    {
        // 一个实例只绑一次令牌，绑上就粘住——所以两半各用一个实例。共用一个的话，第一次绑的是 None，
        // 第二次的令牌根本不会登记，那个 await 只能等它自己的 30 秒超时，这条断言就白等了。
        using var normal = new ReusableTimerWait();
        await normal.Await(TimeSpan.FromMilliseconds(10), CancellationToken.None); // 正常唤醒：不抛

        using var wait = new ReusableTimerWait();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(10));
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await wait.Await(TimeSpan.FromSeconds(30), cts.Token));
    }

    [TestMethod]
    public async Task AnAlreadyCancelledTokenWakesImmediately()
    {
        using var wait = new ReusableTimerWait();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var watch = Stopwatch.StartNew();
        var fired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _ = Task.Run(async () =>
        {
            try { await wait.Await(TimeSpan.FromSeconds(30), cts.Token); }
            catch (OperationCanceledException) { }

            fired.TrySetResult(true);
        });

        await fired.Task.WaitAsync(TimeSpan.FromSeconds(5));
        watch.Stop();

        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(3), $"took {watch.ElapsedMilliseconds}ms");
    }

    [TestMethod]
    public async Task ReArmsForEveryWait()
    {
        // 采样循环的形状：反复等待，每次都在续体里安排下一次。等待对象每次都必须重新装上。
        using var wait = new ReusableTimerWait();
        var watch = Stopwatch.StartNew();

        for (var frame = 0; frame < 8; frame++)
        {
            await wait.Await(TimeSpan.FromMilliseconds(5), CancellationToken.None);
        }

        watch.Stop();
        Assert.IsTrue(watch.Elapsed >= TimeSpan.FromMilliseconds(30), $"eight 5ms waits took only {watch.ElapsedMilliseconds}ms");
    }

    [TestMethod]
    public async Task DisposeDoesNotStrandAParkedWait()
    {
        var wait = new ReusableTimerWait();
        var resumed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _ = Task.Run(async () =>
        {
            try { await wait.Await(TimeSpan.FromSeconds(30), CancellationToken.None); }
            catch (ObjectDisposedException) { }

            resumed.TrySetResult(true);
        });

        await Task.Delay(TimeSpan.FromMilliseconds(20));
        wait.Dispose();

        // 被释放时也要唤醒：等待侧自己决定怎么收尾，否则一个已经取消的动画会把它挂到 30 秒后。
        await resumed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task CostsLessToWaitThanTaskDelay()
    {
        // 这个类存在的理由。测量方式有两个坑，两个都踩过：
        //
        // 1. 必须用进程级计数。等待的续体会换线程——没有 SynchronizationContext 时，计时器回调把续体丢到
        //    线程池上跑——所以 GetAllocatedBytesForCurrentThread 会给出负值或假零。它曾经给出过 0，
        //    看起来像完美结果，其实只是下一个 await 换到了别的线程。
        // 2. 必须取多轮最小值。JIT、TimerQueue 这些一次性开销会落在第一轮上，不取最小值就被它主导。
        const int Iterations = 100;
        var interval = TimeSpan.FromMilliseconds(1);

        using var wait = new ReusableTimerWait();

        // 带上令牌：采样循环每帧都传 cts.Token，而 Task.Delay 每帧都要为它登记一次回调，
        // 不带令牌量出来的差距会偏小。
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        long reusableBest = long.MaxValue;
        long delayBest = long.MaxValue;

        for (var round = 0; round < 2; round++)
        {
            reusableBest = Math.Min(reusableBest, await MeasureAsync(wait, interval, Iterations, reusable: true, token));
            delayBest = Math.Min(delayBest, await MeasureAsync(wait, interval, Iterations, reusable: false, token));
        }

        Console.WriteLine(
            $"{Iterations} waits: reusable={reusableBest}B ({reusableBest / (double)Iterations:F1}B/wait), " +
            $"Task.Delay={delayBest}B ({delayBest / (double)Iterations:F1}B/wait)");

        Assert.IsTrue(
            reusableBest < delayBest,
            $"{Iterations} waits: reusable={reusableBest}B, Task.Delay={delayBest}B");
    }

    private static async Task<long> MeasureAsync(ReusableTimerWait wait, TimeSpan interval, int iterations, bool reusable, CancellationToken token)
    {
        for (var warmup = 0; warmup < 5; warmup++)
        {
            if (reusable) await wait.Await(interval, token);
            else await Task.Delay(interval, token);
        }

        var before = GC.GetTotalAllocatedBytes(precise: true);
        for (var index = 0; index < iterations; index++)
        {
            if (reusable) await wait.Await(interval, token);
            else await Task.Delay(interval, token);
        }

        return GC.GetTotalAllocatedBytes(precise: true) - before;
    }
}
