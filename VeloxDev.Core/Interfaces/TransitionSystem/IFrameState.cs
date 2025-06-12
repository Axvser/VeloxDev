using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace VeloxDev.Core.Interfaces.TransitionSystem
{
    public interface IFrameState<TTarget, TOutput, TPriority> where TOutput : IFrameSequence<TPriority>
    {
        public ConcurrentDictionary<PropertyInfo, object?> Values { get; }
        public ConcurrentDictionary<PropertyInfo, object> Interpolators { get; }
        public void SetInterpolator(Expression<Func<TTarget>> expression, IFrameInterpolator<TTarget, TOutput, TPriority> interpolator);
        public bool TryGetInterpolator(Expression<Func<TTarget>> expression, out IFrameInterpolator<TTarget, TOutput, TPriority>? interpolator);
        public void SetValue(Expression<Func<TTarget>> expression, object? value);
        public bool TryGetValue(Expression<Func<TTarget>> expression, out object? value);
        public IFrameState<TTarget, TOutput, TPriority> DeepCopy();
    }
}
