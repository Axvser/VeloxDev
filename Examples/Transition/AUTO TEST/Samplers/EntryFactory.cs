using System.Linq.Expressions;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.SamplerTest;

/// <summary>
/// Builds a registry entry from a sampler and the closed form it must satisfy.
/// </summary>
internal static class EntryFactory
{
    /// <summary>
    /// One entry: a fresh <typeparamref name="TTarget"/> is written at <paramref name="start"/>, one frame is run at
    /// t, and the value that landed is read back through the same property path a transition would use.
    /// </summary>
    /// <remarks>
    /// <paramref name="expected"/> takes only t, because the endpoints are the ones passed here — so the test's
    /// comparison is against a statement written from the rule rather than against the sampler's own output.
    /// </remarks>
    internal static SamplerEntry Create<TSampler, TTarget, TValue>(
        TSampler sampler,
        string adapter,
        SamplerRule rule,
        object start,
        object end,
        Expression<Func<TTarget, TValue>> selector,
        Func<double, TValue> expected)
        where TSampler : ISampler
        where TTarget : new()
        where TValue : notnull
    {
        if (!TransitionProperty.TryCreate(selector, out var property) || property is null)
        {
            throw new InvalidOperationException($"'{selector}' 不能描述一条可动画的属性路径。");
        }

        return new SamplerEntry
        {
            SamplerType = typeof(TSampler),
            Adapter = adapter,
            Rule = rule,
            Write = (_, t) =>
            {
                var target = new TTarget();
                object? working = null;
                sampler.InsertFrame(target, property, ref working, start, end, null, t);
                return property.GetValue(target);
            },
            Expected = t => expected(t),
        };
    }
}
