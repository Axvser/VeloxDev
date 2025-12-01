using System.Collections.Concurrent;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem
{
    public abstract class InterpolatorCore<
        TOutputCore,
        TPriorityCore> : InterpolatorCore, IFrameInterpolator<TPriorityCore>
        where TOutputCore : IFrameSequence<TPriorityCore>, new()
    {
        public override IFrameSequenceCore Interpolate(
            object target,
            IFrameState state,
            ITransitionEffectCore effect,
            bool isUIAccess,
            IUIThreadInspectorCore inspector)
        {
            if (effect is not ITransitionEffect<TPriorityCore> cvt_effect) throw new InvalidOperationException("Failed to Convert from IUIThreadInspectorCore to ITransitionEffect<TPriorityCore> !");
            if (inspector is not IUIThreadInspector<TPriorityCore> cvt_inspector) throw new InvalidOperationException("Failed to Convert from IUIThreadInspectorCore to IUIThreadInspector<TPriorityCore> !");
            
            return Interpolate(target, state, cvt_effect, isUIAccess, cvt_inspector);
        }

        public virtual IFrameSequence<TPriorityCore> Interpolate(
            object target,
            IFrameState state,
            ITransitionEffect<TPriorityCore> effect,
            bool isUIAccess,
            IUIThreadInspector<TPriorityCore> inspector)
        {
            var output = new TOutputCore();
            var count = (int)(effect.Duration.TotalMilliseconds / (1000.0 / effect.FPS));
            count = count > 0 ? count : 1;
            output.SetCount(count);
            foreach (var kvp in state.Values)
            {
                var currentValue = inspector.ProtectedGetValue(isUIAccess, target, kvp.Key);
                var newValue = kvp.Value;
                if (TryGetInterpolator(kvp.Key.PropertyType, out var interpolator))
                {
                    if (state.TryGetInterpolator(kvp.Key, out var item))
                    {
                        if (item != null)
                        {
                            var frames = inspector.ProtectedInterpolate(isUIAccess, () => item.Interpolate(currentValue, newValue, count));
                            output.AddPropertyInterpolations(kvp.Key, frames);
                        }
                    }
                    else
                    {
                        if (interpolator != null)
                        {
                            var frames = inspector.ProtectedInterpolate(isUIAccess, () => interpolator.Interpolate(currentValue, newValue, count));
                            output.AddPropertyInterpolations(kvp.Key, frames);
                        }
                    }
                }
                else
                {
                    if (currentValue is IInterpolable v1)
                    {
                        var frames = inspector.ProtectedInterpolate(isUIAccess, () => v1.Interpolate(currentValue, newValue, count));
                        output.AddPropertyInterpolations(kvp.Key, frames);
                    }
                    else if (newValue is IInterpolable v2)
                    {
                        var frames = inspector.ProtectedInterpolate(isUIAccess, () => v2.Interpolate(currentValue, newValue, count));
                        output.AddPropertyInterpolations(kvp.Key, frames);
                    }
                }
            }
            return output;
        }
    }

    public abstract class InterpolatorCore<TOutputCore> : InterpolatorCore, IFrameInterpolator
        where TOutputCore : IFrameSequence, new()
    {
        public override IFrameSequenceCore Interpolate(
            object target,
            IFrameState state,
            ITransitionEffectCore effect,
            bool isUIAccess,
            IUIThreadInspectorCore inspector)
        {
            if (inspector is not IUIThreadInspector cvt_inspector) throw new InvalidOperationException("Failed to Convert from IUIThreadInspectorCore to IUIThreadInspector !");
            
            return Interpolate(target, state, effect, isUIAccess, cvt_inspector);
        }

        public virtual IFrameSequence Interpolate(
            object target,
            IFrameState state,
            ITransitionEffectCore effect,
            bool isUIAccess,
            IUIThreadInspector inspector)
        {
            var output = new TOutputCore();
            var count = (int)(effect.Duration.TotalMilliseconds / (1000.0 / effect.FPS));
            count = count > 0 ? count : 1;
            output.SetCount(count);
            foreach (var kvp in state.Values)
            {
                var currentValue = isUIAccess ? kvp.Key.GetValue(target) : inspector.ProtectedGetValue(isUIAccess, target, kvp.Key);
                var newValue = kvp.Value;
                if (TryGetInterpolator(kvp.Key.PropertyType, out var interpolator))
                {
                    if (state.TryGetInterpolator(kvp.Key, out var item))
                    {
                        if (item != null)
                        {
                            var frames = inspector.ProtectedInterpolate(isUIAccess, () => item.Interpolate(currentValue, newValue, count));
                            output.AddPropertyInterpolations(kvp.Key, frames);
                        }
                    }
                    else
                    {
                        if (interpolator != null)
                        {
                            var frames = inspector.ProtectedInterpolate(isUIAccess, () => interpolator.Interpolate(currentValue, newValue, count));
                            output.AddPropertyInterpolations(kvp.Key, frames);
                        }
                    }
                }
                else
                {
                    if (currentValue is IInterpolable v1)
                    {
                        var frames = inspector.ProtectedInterpolate(isUIAccess, () => v1.Interpolate(currentValue, newValue, count));
                        output.AddPropertyInterpolations(kvp.Key, frames);
                    }
                    else if (newValue is IInterpolable v2)
                    {
                        var frames = inspector.ProtectedInterpolate(isUIAccess, () => v2.Interpolate(currentValue, newValue, count));
                        output.AddPropertyInterpolations(kvp.Key, frames);
                    }
                }
            }
            return output;
        }
    }

    public abstract class InterpolatorCore : IFrameInterpolatorCore
    {
        public static ConcurrentDictionary<Type, IValueInterpolator> NativeInterpolators { get; protected set; } = [];
        public static bool TryGetInterpolator(Type type, out IValueInterpolator? interpolator)
        {
            if (NativeInterpolators.TryGetValue(type, out interpolator))
            {
                return true;
            }
            interpolator = null;
            return false;
        }
        public static bool RegisterInterpolator(Type type, IValueInterpolator interpolator)
        {
            if (NativeInterpolators.TryGetValue(type, out var oldValue))
            {
                return NativeInterpolators.TryUpdate(type, interpolator, oldValue);
            }
            else
            {
                return NativeInterpolators.TryAdd(type, interpolator);
            }
        }
        public static bool UnregisterInterpolator(Type type, out IValueInterpolator? interpolator)
        {
            return NativeInterpolators.TryRemove(type, out interpolator);
        }
        public bool TryGetValue(Type type, out IValueInterpolator? interpolator)
        {
            if (NativeInterpolators.TryGetValue(type, out interpolator))
            {
                return true;
            }
            interpolator = null;
            return false;
        }
        public bool Register(Type type, IValueInterpolator interpolator)
        {
            if (NativeInterpolators.TryGetValue(type, out var oldValue))
            {
                return NativeInterpolators.TryUpdate(type, interpolator, oldValue);
            }
            else
            {
                return NativeInterpolators.TryAdd(type, interpolator);
            }
        }
        public bool Unregister(Type type, out IValueInterpolator? interpolator)
        {
            return NativeInterpolators.TryRemove(type, out interpolator);
        }

        public abstract IFrameSequenceCore Interpolate(object target, IFrameState state, ITransitionEffectCore effect, bool isUIAccess, IUIThreadInspectorCore inspector);
    }
}
