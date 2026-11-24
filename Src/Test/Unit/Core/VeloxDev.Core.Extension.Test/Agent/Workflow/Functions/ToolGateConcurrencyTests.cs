using Microsoft.Extensions.AI;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.AI;
using VeloxDev.AI.Pipelines;
using VeloxDev.AI.Safety;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// Wall-clock proof for the tree's tool gate: reads may overlap, writes may not overlap anything.
/// <para>
/// The legacy <see cref="ToolPipeline.MutationGate"/> path stays measurable as a compatibility baseline:
/// a host that only supplies the old semaphore keeps full mutual exclusion.
/// </para>
/// </summary>
[TestClass]
public class ToolGateConcurrencyTests
{
    private const int Readers = 4;
    private static readonly TimeSpan BodyDuration = TimeSpan.FromMilliseconds(20);

    private static async Task<(long ElapsedMs, int MaxConcurrent)> RunReadsAsync(ToolPipeline tools)
    {
        var current = 0;
        var max = 0;

        var probe = AIFunctionFactory.Create(async (CancellationToken ct) =>
        {
            var now = Interlocked.Increment(ref current);
            InterlockedMax(ref max, now);

            try
            {
                await Task.Delay(BodyDuration, ct);
                return "ok";
            }
            finally
            {
                Interlocked.Decrement(ref current);
            }
        }, "SlowRead");

        var function = new TrackedAIFunction(probe, tools, category: AgentActionCategory.Read);
        var stopwatch = Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, Readers).Select(_ => Task.Run(() => InvokeAsync(function))));
        return (stopwatch.ElapsedMilliseconds, max);
    }

    private static object? InvokeAsync(AIFunction function)
        => function.InvokeAsync(new AIFunctionArguments(), CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();

    private static void InterlockedMax(ref int target, int value)
    {
        while (true)
        {
            var current = Volatile.Read(ref target);
            if (value <= current || Interlocked.CompareExchange(ref target, value, current) == current)
            {
                return;
            }
        }
    }

    [TestMethod]
    public async Task LegacyMutationGate_SerializesReads()
    {
        using var gate = new SemaphoreSlim(1, 1);
        var tools = new ToolPipeline { MutationGate = () => gate };

        var (elapsed, maxConcurrent) = await RunReadsAsync(tools);

        Console.WriteLine($"legacy gate: {Readers} reads x {BodyDuration.TotalMilliseconds} ms -> {elapsed} ms, max concurrency {maxConcurrent}");
        Assert.AreEqual(1, maxConcurrent, "the legacy semaphore path keeps full mutual exclusion");
        Assert.IsGreaterThanOrEqualTo(
            (long)(Readers * BodyDuration.TotalMilliseconds * 0.8),
            elapsed,
            "serialized reads cost about one body per call");
    }

    [TestMethod]
    public async Task SharedGate_OverlapsReads()
    {
        using var gate = new ToolGate();
        var tools = new ToolPipeline { ToolGate = () => gate };

        // 确定的并发证明：每个读者的工体都停在 release 上，所以 K 个都进入才算并行；
        // 串行实现会卡在第二个读者。墙钟只打印，不作断言（报告里的 135→52 ms 是同口径实测）。
        using var entered = new SemaphoreSlim(0, Readers);
        using var release = new SemaphoreSlim(0, Readers);
        var probe = AIFunctionFactory.Create(async (CancellationToken ct) =>
        {
            entered.Release();
            await release.WaitAsync(ct);
            await Task.Delay(BodyDuration, ct);
            return "ok";
        }, "RendezvousRead");

        var function = new TrackedAIFunction(probe, tools, category: AgentActionCategory.Read);
        var calls = Enumerable.Range(0, Readers)
            .Select(_ => Task.Run(() => InvokeAsync(function)))
            .ToArray();

        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < Readers; i++)
        {
            Assert.IsTrue(
                entered.Wait(TimeSpan.FromSeconds(15)),
                "every reader must be inside the gate at the same time");
        }

        stopwatch.Stop();
        Console.WriteLine($"shared gate: all {Readers} readers inside after {stopwatch.ElapsedMilliseconds} ms");

        // 全部进入之后一起放行、一起跑满一个工体时长：串行实现这里会是 Readers 份时长，共享模式只有一份。
        var drain = Stopwatch.StartNew();
        release.Release(Readers);
        await Task.WhenAll(calls).WaitAsync(TimeSpan.FromSeconds(15));
        drain.Stop();
        Console.WriteLine($"shared gate: {Readers} readers drained {BodyDuration.TotalMilliseconds} ms bodies in {drain.ElapsedMilliseconds} ms");
    }

    [TestMethod]
    public async Task Reader_WaitsForAHeldWrite()
    {
        using var gate = new ToolGate();
        using var release = new SemaphoreSlim(0, 1);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writeOrder = new List<string>();

        var write = Task.Run(async () =>
        {
            using var lease = await gate.EnterAsync(ToolAccess.Exclusive, CancellationToken.None);
            entered.SetResult();
            await release.WaitAsync();
            writeOrder.Add("write-end");
        });

        await entered.Task;
        var read = Task.Run(async () =>
        {
            using var lease = await gate.EnterAsync(ToolAccess.Shared, CancellationToken.None);
            writeOrder.Add("read-end");
        });

        await Task.Delay(100);
        Assert.IsFalse(read.IsCompleted, "a read must not enter while a write holds the gate");
        release.Release();
        await Task.WhenAll(write, read).WaitAsync(TimeSpan.FromSeconds(15));
        CollectionAssert.AreEqual(new[] { "write-end", "read-end" }, writeOrder);
    }

    [TestMethod]
    public async Task Writes_RunAlone()
    {
        using var gate = new ToolGate();
        var current = 0;
        var max = 0;

        async Task OneWrite()
        {
            using var lease = await gate.EnterAsync(ToolAccess.Exclusive, CancellationToken.None);
            var now = Interlocked.Increment(ref current);
            InterlockedMax(ref max, now);
            try
            {
                await Task.Delay(BodyDuration);
            }
            finally
            {
                Interlocked.Decrement(ref current);
            }
        }

        await Task.WhenAll(Enumerable.Range(0, Readers).Select(_ => Task.Run(OneWrite)));
        Assert.AreEqual(1, max, "two writes must never hold the gate at the same time");
    }

    [TestMethod]
    public async Task ContinuousReaders_DoNotStarveAWriter()
    {
        using var gate = new ToolGate();

        var firstBatch = Enumerable.Range(0, Readers)
            .Select(_ => gate.EnterAsync(ToolAccess.Shared, CancellationToken.None).AsTask())
            .ToArray();
        var held = await Task.WhenAll(firstBatch);

        var writer = gate.EnterAsync(ToolAccess.Exclusive, CancellationToken.None).AsTask();
        var lateReaders = Enumerable.Range(0, Readers)
            .Select(_ => gate.EnterAsync(ToolAccess.Shared, CancellationToken.None).AsTask())
            .ToArray();

        // 前一批读者退出后，写者必须排在后续读者之前拿到门。
        foreach (var lease in held)
        {
            lease.Dispose();
        }

        var granted = await writer.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.IsFalse(lateReaders.Any(reader => reader.IsCompleted), "readers behind a waiting writer must not barge");
        granted.Dispose();

        foreach (var reader in lateReaders)
        {
            (await reader.WaitAsync(TimeSpan.FromSeconds(15))).Dispose();
        }
    }

    [TestMethod]
    public async Task CanceledWaiter_LeavesNoSlotBehind()
    {
        using var gate = new ToolGate();
        var held = await gate.EnterAsync(ToolAccess.Exclusive, CancellationToken.None);

        using var cts = new CancellationTokenSource();
        var canceled = gate.EnterAsync(ToolAccess.Shared, cts.Token).AsTask();
        var next = gate.EnterAsync(ToolAccess.Shared, CancellationToken.None).AsTask();

        cts.Cancel();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => await canceled);

        held.Dispose();
        var lease = await next.WaitAsync(TimeSpan.FromSeconds(15));
        lease.Dispose();
    }
}
