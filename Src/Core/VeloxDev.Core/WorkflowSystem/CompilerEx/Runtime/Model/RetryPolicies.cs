using System;
using System.Threading;
using System.Threading.Tasks;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// Retries a failed node a fixed number of times, doubling the wait each round. The default a host gets when it
/// asks for retries without wanting to write a policy.
/// </summary>
/// <remarks>
/// Waits are pure delays: the engine hands the policy a node failure and it answers with a <see cref="TimeSpan"/>,
/// so there is nothing here that inspects the exception. A host that wants "retry a timeout but not a validation
/// error" writes its own — the decision is one method.
/// </remarks>
public sealed class ExponentialBackoffRetry : INodeRetryPolicy
{
    private readonly int _maxAttempts;
    private readonly double _baseDelayMs;
    private readonly double _factor;
    private readonly double _maxDelayMs;

    /// <summary>Creates the policy.</summary>
    /// <param name="maxAttempts">
    /// How many attempts a node gets in total, the first one included — <c>3</c> means "try, retry, retry".
    /// </param>
    /// <param name="baseDelayMs">The wait before the first retry, in milliseconds.</param>
    /// <param name="factor">What each successive wait is multiplied by.</param>
    /// <param name="maxDelayMs">A ceiling on a single wait, so a long chain of retries stays bounded.</param>
    public ExponentialBackoffRetry(
        int maxAttempts = 3, double baseDelayMs = 200, double factor = 2.0, double maxDelayMs = 5000)
    {
        _maxAttempts = Math.Max(1, maxAttempts);
        _baseDelayMs = Math.Max(0, baseDelayMs);
        _factor = Math.Max(1, factor);
        _maxDelayMs = Math.Max(_baseDelayMs, maxDelayMs);
    }

    /// <summary>The number of attempts a node gets in total, the first one included.</summary>
    public int MaxAttempts => _maxAttempts;

    /// <inheritdoc />
    public Task<TimeSpan?> NextRetryAsync(NodeFailure failure, CancellationToken cancellationToken)
    {
        // RetryNumber 从 1 起，所以刚失败的那次就是第 RetryNumber 次；它等于 maxAttempts 时已无机会可给。
        // 首次尝试本身由 maxAttempts == 1 覆盖（那时 RetryNumber 也是 1，直接停）。
        if (failure.RetryNumber >= _maxAttempts) return Task.FromResult<TimeSpan?>((TimeSpan?)null);

        var delay = Math.Min(_baseDelayMs * Math.Pow(_factor, failure.RetryNumber - 1), _maxDelayMs);
        return Task.FromResult<TimeSpan?>(TimeSpan.FromMilliseconds(delay));
    }
}
