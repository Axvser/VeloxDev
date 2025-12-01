using System.Linq.Expressions;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem
{
    public class StateSnapshotCore<
        T,
        TStateCore,
        TEffectCore,
        TInterpolatorCore,
        TUIThreadInspectorCore,
        TTransitionInterpreterCore> : StateSnapshotCore<T>
        where T : class
        where TStateCore : IFrameState, new()
        where TEffectCore : ITransitionEffectCore, new()
        where TInterpolatorCore : IFrameInterpolator, new()
        where TUIThreadInspectorCore : IUIThreadInspector, new()
        where TTransitionInterpreterCore : class, ITransitionInterpreter, new()
    {
        public TStateCore state = new();
        public StateSnapshotCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore>? root;
        public StateSnapshotCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore>? next = null;
        public TEffectCore effect = new();
        public TInterpolatorCore interpolator = new();

        internal override void AsRoot()
        {
            root = this;
        }
        internal override void SetTarget(T target)
        {
            root ??= this;
            root.targetref = new WeakReference<T>(target);
        }

        internal override IFrameState CoreRecordState()
        {
            return state.Clone();
        }
        internal override IFrameState Merge(StateSnapshotCore<T> snapshot)
        {
            var result = state.Clone();

            if (snapshot is StateSnapshotCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore> coreSnapshot)
            {
                foreach (var value in coreSnapshot.state.Values)
                {
                    result.SetValue(value.Key, value.Value);
                }
                foreach (var interpolator in coreSnapshot.state.Interpolators)
                {
                    result.SetInterpolator(interpolator.Key, interpolator.Value);
                }
            }

            return result;
        }

        internal override async void CoreExecute(bool CanMutualTask = true)
        {
            root ??= this;

            if (!(targetref?.TryGetTarget(out var target) ?? false)) return;
            var scheduler = TransitionSchedulerCore<TUIThreadInspectorCore, TTransitionInterpreterCore>.FindOrCreate(target, CanMutualTask);
            if (CanMutualTask) scheduler.Exit();

            await root.StartAsync(target, scheduler, CanMutualTask);
        }
        internal override async void CoreExecute(object target, bool CanMutualTask = true)
        {
            root ??= this;

            if (target is not T cvt_target) throw new InvalidDataException($"The target is not a {typeof(T).Name}");
            var scheduler = TransitionSchedulerCore<TUIThreadInspectorCore, TTransitionInterpreterCore>.FindOrCreate(target, CanMutualTask);
            if (CanMutualTask) scheduler.Exit();

            await root.StartAsync(cvt_target, scheduler, CanMutualTask);
        }
        internal async Task StartAsync(T target, ITransitionScheduler scheduler, bool CanMutualTask = true)
        {
            await Task.Delay(delay);
            var copyEffect = effect.Clone();
            if (!CanMutualTask)
            {
                TransitionCore.AddNoMutual(target, [scheduler]);
                copyEffect.Finally += (s, e) =>
                {
                    TransitionCore.RemoveNoMutual(target, [scheduler]);
                };
            }
            await scheduler.Execute(interpolator, state, copyEffect);
            if (next is null) return;
            next.CoreExecute(target, CanMutualTask);
        }

        internal override T1 CoreThen<T1>()
        {
            var newNode = new T1();
            if (newNode is not StateSnapshotCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore> converted)
            {
                throw new InvalidOperationException($"The current StateSnapshotCore is not of type {typeof(T1).Name}.");
            }
            converted.root = root;
            converted.targetref = targetref;
            next = converted;
            return newNode;
        }
        internal override T1 CoreAwaitThen<T1>(TimeSpan timeSpan)
        {
            var newNode = new T1();
            if (newNode is not StateSnapshotCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore> converted)
            {
                throw new InvalidOperationException($"The current StateSnapshotCore is not of type {typeof(T1).Name}.");
            }
            converted.root = root;
            converted.delay = timeSpan;
            converted.targetref = targetref;
            next = converted;
            return newNode;
        }
        internal override T1 CoreProperty<T1, TInterpolable>(Expression<Func<T1, TInterpolable>> propertyLambda, TInterpolable newValue)
        {
            if (this is not T1 result)
            {
                throw new InvalidOperationException($"The current StateSnapshotCore is not of type {typeof(T1).Name}.");
            }
            state.SetValue<T1, TInterpolable>(propertyLambda, newValue);
            return result;
        }
        internal override T1 CoreEffect<T1, T2>(T2 effect)
        {
            if (this is not T1 result)
            {
                throw new InvalidOperationException($"The current StateSnapshotCore is not of type {typeof(T1).Name}.");
            }
            if (effect is not TEffectCore convertedEffect)
            {
                throw new InvalidOperationException($"The effect setter did not return an effect of type {typeof(TEffectCore).Name}.");
            }
            this.effect = convertedEffect;
            return result;
        }
        internal override T1 CoreEffect<T1, T2>(Action<T2> effectSetter)
        {
            if (this is not T1 result)
            {
                throw new InvalidOperationException($"The current StateSnapshotCore is not of type {typeof(T1).Name}.");
            }
            var newEffect = new T2();
            effectSetter.Invoke(newEffect);
            if (newEffect is not TEffectCore convertedEffect)
            {
                throw new InvalidOperationException($"The effect setter did not return an effect of type {typeof(TEffectCore).Name}.");
            }
            effect = convertedEffect;
            return result;
        }
    }

    public class StateSnapshotCore<
        T,
        TStateCore,
        TEffectCore,
        TInterpolatorCore,
        TUIThreadInspectorCore,
        TTransitionInterpreterCore,
        TPriorityCore> : StateSnapshotCore<T>
        where T : class
        where TStateCore : IFrameState, new()
        where TEffectCore : ITransitionEffect<TPriorityCore>, new()
        where TInterpolatorCore : IFrameInterpolator<TPriorityCore>, new()
        where TUIThreadInspectorCore : IUIThreadInspector<TPriorityCore>, new()
        where TTransitionInterpreterCore : class, ITransitionInterpreter<TPriorityCore>, new()
    {
        public TStateCore state = new();
        public StateSnapshotCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore, TPriorityCore>? root;
        public StateSnapshotCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore, TPriorityCore>? next = null;
        public TEffectCore effect = new();
        public TInterpolatorCore interpolator = new();

        internal override void AsRoot()
        {
            root = this;
        }
        internal override void SetTarget(T target)
        {
            root ??= this;
            root.targetref = new WeakReference<T>(target);
        }

        internal override IFrameState CoreRecordState()
        {
            return state.Clone();
        }
        internal override IFrameState Merge(StateSnapshotCore<T> snapshot)
        {
            var result = state.Clone();

            if (snapshot is StateSnapshotCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore, TPriorityCore> coreSnapshot)
            {
                foreach (var value in coreSnapshot.state.Values)
                {
                    result.SetValue(value.Key, value.Value);
                }
                foreach (var interpolator in coreSnapshot.state.Interpolators)
                {
                    result.SetInterpolator(interpolator.Key, interpolator.Value);
                }
            }

            return result;
        }

        internal override async void CoreExecute(bool CanMutualTask = true)
        {
            root ??= this;

            if (!(targetref?.TryGetTarget(out var target) ?? false)) return;
            var scheduler = TransitionSchedulerCore<TUIThreadInspectorCore, TTransitionInterpreterCore, TPriorityCore>.FindOrCreate(target, CanMutualTask);
            if (CanMutualTask) scheduler.Exit();

            await root.StartAsync(target, scheduler, CanMutualTask);
        }
        internal override async void CoreExecute(object target, bool CanMutualTask = true)
        {
            root ??= this;

            if (target is not T cvt_target) throw new InvalidDataException($"The target is not a {typeof(T).Name}");
            var scheduler = TransitionSchedulerCore<TUIThreadInspectorCore, TTransitionInterpreterCore, TPriorityCore>.FindOrCreate(target, CanMutualTask);
            if (CanMutualTask) scheduler.Exit();

            await root.StartAsync(cvt_target, scheduler, CanMutualTask);
        }
        internal async Task StartAsync(T target, ITransitionScheduler<TPriorityCore> scheduler, bool CanMutualTask = true)
        {
            await Task.Delay(delay);
            var copyEffect = effect.Clone();
            if (!CanMutualTask)
            {
                TransitionCore.AddNoMutual(target, [scheduler]);
                copyEffect.Finally += (s, e) =>
                {
                    TransitionCore.RemoveNoMutual(target, [scheduler]);
                };
            }
            await scheduler.Execute(interpolator, state, copyEffect);
            if (next is null) return;
            next.CoreExecute(target,CanMutualTask);
        }
        internal override T1 CoreThen<T1>()
        {
            var newNode = new T1();
            if (newNode is not StateSnapshotCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore, TPriorityCore> converted)
            {
                throw new InvalidOperationException($"The current StateSnapshotCore is not of type {typeof(T1).Name}.");
            }
            converted.root = root;
            converted.targetref = root?.targetref;
            next = converted;
            return newNode;
        }
        internal override T1 CoreAwaitThen<T1>(TimeSpan timeSpan)
        {
            var newNode = new T1();
            if (newNode is not StateSnapshotCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore, TPriorityCore> converted)
            {
                throw new InvalidOperationException($"The current StateSnapshotCore is not of type {typeof(T1).Name}.");
            }
            converted.root = root;
            converted.delay = timeSpan;
            converted.targetref = root?.targetref;
            next = converted;
            return newNode;
        }
        internal override T1 CoreProperty<T1, TInterpolable>(Expression<Func<T1, TInterpolable>> propertyLambda, TInterpolable newValue)
        {
            if (this is not T1 result)
            {
                throw new InvalidOperationException($"The current StateSnapshotCore is not of type {typeof(T1).Name}.");
            }
            state.SetValue<T1, TInterpolable>(propertyLambda, newValue);
            return result;
        }
        internal override T1 CoreEffect<T1, T2>(T2 effect)
        {
            if (this is not T1 result)
            {
                throw new InvalidOperationException($"The current StateSnapshotCore is not of type {typeof(T1).Name}.");
            }
            if (effect is not TEffectCore convertedEffect)
            {
                throw new InvalidOperationException($"The effect setter did not return an effect of type {typeof(TEffectCore).Name}.");
            }
            this.effect = convertedEffect;
            return result;
        }
        internal override T1 CoreEffect<T1, T2>(Action<T2> effectSetter)
        {
            if (this is not T1 result)
            {
                throw new InvalidOperationException($"The current StateSnapshotCore is not of type {typeof(T1).Name}.");
            }
            var newEffect = new T2();
            effectSetter.Invoke(newEffect);
            if (newEffect is not TEffectCore convertedEffect)
            {
                throw new InvalidOperationException($"The effect setter did not return an effect of type {typeof(TEffectCore).Name}.");
            }
            effect = convertedEffect;
            return result;
        }
    }

    public abstract class StateSnapshotCore<T> : StateSnapshotCore
        where T : class
    {
        internal WeakReference<T>? targetref = null;
        internal TimeSpan delay = TimeSpan.Zero;
        internal abstract IFrameState Merge(StateSnapshotCore<T> snapshot);
        internal virtual void SetTarget(T target)
        {
            targetref = new WeakReference<T>(target);
        }
        internal override T1 CoreAwait<T1>(TimeSpan timeSpan)
        {
            if (this is not T1 snapshot)
            {
                throw new InvalidOperationException($"The current StateSnapshotCore is not of type {typeof(T1).Name}.");
            }
            delay = timeSpan;
            return snapshot;
        }
    }

    public abstract class StateSnapshotCore
    {
        internal abstract void AsRoot();
        internal abstract T CoreProperty<T, TInterpolable>(Expression<Func<T, TInterpolable>> propertyLambda, TInterpolable newValue)
            where T : class
            where TInterpolable : IInterpolable;
        internal abstract T1 CoreEffect<T1, T2>(T2 effect) where T2 : ITransitionEffectCore;
        internal abstract T1 CoreEffect<T1, T2>(Action<T2> effectSetter) where T2 : ITransitionEffectCore, new();
        internal abstract IFrameState CoreRecordState();
        internal abstract T CoreAwait<T>(TimeSpan timeSpan)
            where T : StateSnapshotCore, new();
        internal abstract T CoreThen<T>()
            where T : StateSnapshotCore, new();
        internal abstract T CoreAwaitThen<T>(TimeSpan timeSpan)
            where T : StateSnapshotCore, new();
        internal abstract void CoreExecute(bool CanMutualTask = true);
        internal abstract void CoreExecute(object target, bool CanMutualTask = true);
    }
}
