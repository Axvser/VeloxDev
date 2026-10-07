using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using VeloxDev.AI;

namespace VeloxDev.Core.Test.AI;

/// <summary>
/// The registry must never hold its own lock while it reads a fragment's members.
/// </summary>
/// <remarks>
/// <para>
/// A fragment is a static type in <i>another</i> assembly, so reading its <c>TypeNames</c> for the first time runs
/// that assembly's module initializer — and the generated initializer registers itself, which needs the very lock
/// the reader would be holding. Two threads then wait on each other: one for the lock, one for the module
/// initializer. A single thread never sees it, because <see cref="Monitor"/> is reentrant.
/// </para>
/// <para>
/// This test states that cycle directly rather than racing two ordinary lookups: the window only exists until the
/// index is built and every fragment has registered, which in a suite that has already run any other test is
/// already closed. A test that hoped to hit it would pass whether or not the lock was held, which is worse than
/// no test. Here a probe fragment holds the reading thread while a second thread performs a lookup, and reports
/// whether that lookup finished before the read returned.
/// </para>
/// <para>
/// It leaves nothing behind: the probe is registered to force the index to be rebuilt, and the registry is handed
/// back exactly as it was found — same fragments, same order — before the test ends.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]   // 探针要独占这一次索引重建：别的方例同时查找会抢先消费掉它
public class AIContextTreeLockingTests
{
    /// <summary>The fragment that reports when its members are read.</summary>
    private const string ProbeAssembly = "VeloxDev.Core.Test.LockingProbe";

    /// <summary>A lookup for a type no fragment carries — the name does not matter, only that it rebuilds.</summary>
    private const string AbsentType = "VeloxDev.Core.Test.NoSuchType";

    /// <summary>A fragment that contributes nothing — it exists only to be read.</summary>
    private sealed class InertFragment(string assemblyName, Func<string[]> readTypeNames) : AIContextFragment
    {
        /// <inheritdoc/>
        public override string AssemblyName => assemblyName;

        /// <inheritdoc/>
        public override IReadOnlyList<string> DirectoryPaths => [];

        /// <inheritdoc/>
        public override IReadOnlyList<AIContextNode> ChildrenOf(string directoryPath) => [];

        // 空表：要的是「被读到」，不是往索引里加东西。
        /// <inheritdoc/>
        public override IReadOnlyList<string> TypeNames => readTypeNames();
    }

    /// <summary>Holds the reading thread while another thread looks a type up, and records the outcome.</summary>
    private sealed class LookupRace
    {
        private readonly int _readingThread = Environment.CurrentManagedThreadId;
        private readonly ManualResetEventSlim _readStarted = new(false);
        private readonly ManualResetEventSlim _lookupDone = new(false);
        private int _read;
        private int _gated;

        /// <summary>Whether the other thread's lookup returned before the read it was racing did.</summary>
        public bool LookupFinishedDuringRead { get; private set; }

        /// <summary>Whether the reading thread got as far as this fragment.</summary>
        public bool WasRead => Volatile.Read(ref _read) == 1;

        /// <summary>Called from the fragment's <c>TypeNames</c>, on whichever thread is doing the lookup.</summary>
        public string[] ReadTypeNames()
        {
            // 只有发起用例的那个线程才拦：另一线程的重建若也被拦，它就等一个只有它能置位的信号，自锁。
            if (Environment.CurrentManagedThreadId != _readingThread) return [];

            Volatile.Write(ref _read, 1);

            // 只拦第一次：不持锁的写法在索引被作废后会重建一趟，那一趟不该再等。
            if (Interlocked.Exchange(ref _gated, 1) == 1) return [];

            _readStarted.Set();
            LookupFinishedDuringRead = _lookupDone.Wait(TimeSpan.FromSeconds(5));
            return [];
        }

        // 另一个线程做的是一次普通查找 —— 它要的正是注册表那把锁。
        public void LookupOnAnotherThread()
        {
            if (!_readStarted.Wait(TimeSpan.FromSeconds(5))) return;

            AIContextTreeRegistry.PathFor(AbsentType);
            _lookupDone.Set();
        }
    }

    [TestMethod]
    public void PathFor_DoesNotHoldTheRegistryLockAcrossAFragmentRead()
    {
        var before = AIContextTreeRegistry.Fragments.Select(static f => f.AssemblyName).ToArray();

        var race = new LookupRace();

        // 注册探针顺带把类型索引作废 —— PathFor 只在索引为空时才重建，不作废就观察不到这一趟。
        AIContextTreeRegistry.RegisterFragment(new InertFragment(ProbeAssembly, race.ReadTypeNames));

        try
        {
            var worker = new Thread(race.LookupOnAnotherThread) { IsBackground = true };
            worker.Start();

            AIContextTreeRegistry.PathFor(AbsentType);

            // 自证守卫：这次查找必须真的重建了索引并读到探针，否则下面那条只是在说「什么都没发生」。
            Assert.IsTrue(race.WasRead,
                "the lookup must have rebuilt the index and read the probe fragment — otherwise nothing was exercised");

            Assert.IsTrue(worker.Join(TimeSpan.FromSeconds(5)),
                "the other thread's lookup must finish: nothing may hold the lock it is waiting behind");

            Assert.IsTrue(race.LookupFinishedDuringRead,
                "a lookup must be able to complete while PathFor is reading a fragment. Holding the registry lock "
                + "across that read is what deadlocks: one thread waits for the lock while the other waits for "
                + "another assembly's module initializer (see memory/modules/AI/architecture.md §七·十二)");
        }
        finally
        {
            AIContextTreeRegistry.UnregisterFragment(ProbeAssembly);
        }

        CollectionAssert.AreEqual(before, AIContextTreeRegistry.Fragments.Select(static f => f.AssemblyName).ToArray(),
            "this test must hand the registry back exactly as it found it — same fragments, same order");
    }
}
