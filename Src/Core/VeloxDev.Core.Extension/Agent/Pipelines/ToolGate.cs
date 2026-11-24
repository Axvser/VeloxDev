using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VeloxDev.AI.Pipelines;

/// <summary>How one tool call takes the tree's gate.</summary>
public enum ToolAccess
{
    /// <summary>The call only reads the graph; it may run alongside other readers.</summary>
    Shared,

    /// <summary>The call changes the graph; it runs alone.</summary>
    Exclusive,
}

/// <summary>
/// An asynchronous readers-writer gate for tool calls: shared calls overlap, exclusive calls run alone.
/// </summary>
/// <remarks>
/// <para>
/// Callers are admitted in arrival order. A reader that arrives while nobody waits enters immediately; a caller
/// that arrives while anybody waits queues behind them — so a steady stream of reads cannot starve a writer.
/// </para>
/// <para>
/// Every wait is asynchronous; nothing here blocks a thread. A canceled waiter leaves the queue without consuming
/// a slot, and a lease releases exactly once.
/// </para>
/// <para>
/// One instance serves one tree. The surface's scopes (parent and sub-agents) resolve the same instance, which is
/// what makes "no read overlaps a write, and no two writes overlap" a property of the tree rather than of a scope.
/// </para>
/// </remarks>
public sealed class ToolGate : IDisposable
{
    private readonly object sync = new();
    private readonly Queue<Waiter> waiters = new();
    private int readers;
    private bool writer;
    private bool disposed;

    /// <summary>Enters the gate, waiting asynchronously for a free slot.</summary>
    /// <param name="access">Shared for reads, exclusive for edits and executions.</param>
    /// <param name="cancellationToken">Abandons the wait; the gate is left as it was.</param>
    /// <returns>A lease that releases the slot on <see cref="IDisposable.Dispose"/>.</returns>
    /// <exception cref="ObjectDisposedException">The gate has been disposed.</exception>
    public ValueTask<IDisposable> EnterAsync(ToolAccess access, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new ValueTask<IDisposable>(Task.FromCanceled<IDisposable>(cancellationToken));
        }

        lock (sync)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(ToolGate));
            }

            if (!writer && waiters.Count == 0)
            {
                if (access == ToolAccess.Shared)
                {
                    readers++;
                    return new ValueTask<IDisposable>(new Lease(this, shared: true));
                }

                writer = true;
                return new ValueTask<IDisposable>(new Lease(this, shared: false));
            }

            var waiter = new Waiter(this, access, cancellationToken);
            waiters.Enqueue(waiter);

            // Register can run the callback inline when the token is already canceled; settle the queue then.
            if (waiter.Canceled)
            {
                Pump();
            }

            // 取消回调在别的线程上取同一把锁；注销放到 await 这边做，避免持锁等回调造成互等。
            return new ValueTask<IDisposable>(AwaitAndReleaseRegistrationAsync(waiter));
        }
    }

    /// <summary>Fails every waiter still queued; leases already granted keep releasing normally.</summary>
    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            while (waiters.Count > 0)
            {
                var waiter = waiters.Dequeue();
                waiter.Completion.TrySetException(new ObjectDisposedException(nameof(ToolGate)));
            }
        }
    }

    private void Release(bool shared)
    {
        lock (sync)
        {
            if (shared)
            {
                readers--;
            }
            else
            {
                writer = false;
            }

            Pump();
        }
    }

    // 只在持锁时调用。按到达顺序放行：队头的写者要等读者清空；连续的读者一起放行；
    // 读者排在等着的写者后面，写者因此不会被源源不断的读饿死。
    private void Pump()
    {
        if (writer || disposed)
        {
            return;
        }

        while (waiters.Count > 0)
        {
            var head = waiters.Peek();
            if (head.Canceled)
            {
                waiters.Dequeue();
                continue;
            }

            if (head.Access == ToolAccess.Exclusive)
            {
                if (readers > 0)
                {
                    return;
                }

                waiters.Dequeue();
                writer = true;
                head.Grant(new Lease(this, shared: false));
                return;
            }

            waiters.Dequeue();
            readers++;
            head.Grant(new Lease(this, shared: true));
        }
    }

    private void OnCanceled(Waiter waiter)
    {
        lock (sync)
        {
            waiter.Canceled = true;
            waiter.Completion.TrySetCanceled(waiter.Token);
            Pump();
        }
    }

    private static async Task<IDisposable> AwaitAndReleaseRegistrationAsync(Waiter waiter)
    {
        try
        {
            return await waiter.Completion.Task.ConfigureAwait(false);
        }
        finally
        {
            waiter.DisposeRegistration();
        }
    }

    private sealed class Lease(ToolGate owner, bool shared) : IDisposable
    {
        private ToolGate? owner = owner;

        public void Dispose()
        {
            // 同一张租约被释放两次时只退一次，计数不会被拖成负数。
            var gate = Interlocked.Exchange(ref owner, null);
            gate?.Release(shared);
        }
    }

    private sealed class Waiter
    {
        private readonly ToolGate owner;
        private CancellationTokenRegistration registration;
        private bool registrationCaptured;

        public Waiter(ToolGate owner, ToolAccess access, CancellationToken token)
        {
            this.owner = owner;
            Access = access;
            Token = token;
            Completion = new TaskCompletionSource<IDisposable>(TaskCreationOptions.RunContinuationsAsynchronously);

            if (token.CanBeCanceled)
            {
                registration = token.Register(static state => ((Waiter)state!).Cancel(), this);
                registrationCaptured = true;
            }
        }

        public ToolAccess Access { get; }

        public CancellationToken Token { get; }

        public TaskCompletionSource<IDisposable> Completion { get; }

        public bool Canceled { get; set; }

        public void Cancel() => owner.OnCanceled(this);

        public void Grant(IDisposable lease) => Completion.TrySetResult(lease);

        public void DisposeRegistration()
        {
            if (!registrationCaptured)
            {
                return;
            }

            registrationCaptured = false;
            registration.Dispose();
        }
    }
}
