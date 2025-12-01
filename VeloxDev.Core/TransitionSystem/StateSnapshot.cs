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

        internal override IFrameState CoreRecordState()
        {
            return state.Clone();
        }

        internal override async void CoreExecute(object target, bool CanMutualTask = true)
        {
            await slim.WaitAsync().ConfigureAwait(false);

            root ??= this;
            if (root.spreador is not null) await root.spreador.CloseAsync();

            if (target is not T cvt_target) throw new InvalidDataException($"The target is not a {typeof(T).Name}");
            var scheduler = TransitionSchedulerCore<TUIThreadInspectorCore, TTransitionInterpreterCore>.FindOrCreate(target, CanMutualTask);
            if (CanMutualTask) scheduler.Exit();

            var spreador = new StateSnapshotChainSpreador<T>(root, cvt_target, scheduler, CanMutualTask);
            root.spreador = spreador;

            await root.StartAsync(spreador);

            slim.Release();
        }
        internal async Task StartAsync(StateSnapshotChainSpreador<T> spreador)
        {
            var nowCts = spreador.cts;
            if (nowCts is null) return;
            try
            {
                await Task.Delay(delay, nowCts.Token);
            }
            catch { }
            var copyEffect = effect.Clone();
            if (!spreador.CanMutualTask)
            {
                TransitionCore.AddNoMutual(spreador.Target, [spreador.Scheduler]);
                copyEffect.Finally += (s, e) =>
                {
                    TransitionCore.RemoveNoMutual(spreador.Target, [spreador.Scheduler]);
                };
            }
            await spreador.Scheduler.Execute(interpolator, state, copyEffect, nowCts);
            if (next is null || nowCts.IsCancellationRequested) return;
            await next.StartAsync(spreador);
        }

        internal override T1 CoreThen<T1>()
        {
            var newNode = new T1();
            if (newNode is not StateSnapshotCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore> converted)
            {
                throw new InvalidOperationException($"The current StateSnapshotCore is not of type {typeof(T1).Name}.");
            }
            converted.root = root;
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

        internal override IFrameState CoreRecordState()
        {
            return state.Clone();
        }

        internal override async void CoreExecute(object target, bool CanMutualTask = true)
        {
            await slim.WaitAsync().ConfigureAwait(false);

            root ??= this;
            if(root.spreador is not null) await root.spreador.CloseAsync();

            if (target is not T cvt_target) throw new InvalidDataException($"The target is not a {typeof(T).Name}");
            var scheduler = TransitionSchedulerCore<TUIThreadInspectorCore, TTransitionInterpreterCore, TPriorityCore>.FindOrCreate(target, CanMutualTask);
            if (CanMutualTask) scheduler.Exit();

            var spreador = new StateSnapshotChainSpreador<T, TPriorityCore>(root, cvt_target, scheduler, CanMutualTask);
            root.spreador = spreador;

            await root.StartAsync(spreador);

            slim.Release();
        }
        internal async Task StartAsync(StateSnapshotChainSpreador<T, TPriorityCore> spreador)
        {
            var nowCts = spreador.cts;
            if (nowCts is null || nowCts.IsCancellationRequested) return;  // 提前检查取消

            try
            {
                await Task.Delay(delay, nowCts.Token);  // 支持取消的Delay
            }
            catch (OperationCanceledException)
            {
                return;  // 被取消时直接返回
            }

            var copyEffect = effect.Clone();
            if (!spreador.CanMutualTask)
            {
                TransitionCore.AddNoMutual(spreador.Target, [spreador.Scheduler]);
                copyEffect.Finally += (s, e) =>
                {
                    TransitionCore.RemoveNoMutual(spreador.Target, [spreador.Scheduler]);
                };
            }

            await spreador.Scheduler.Execute(interpolator, state, copyEffect, nowCts);

            // 关键修复：检查取消状态后再决定是否执行下一个
            if (next is null || nowCts.IsCancellationRequested) return;
            await next.StartAsync(spreador);
        }
        internal override T1 CoreThen<T1>()
        {
            var newNode = new T1();
            if (newNode is not StateSnapshotCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore, TPriorityCore> converted)
            {
                throw new InvalidOperationException($"The current StateSnapshotCore is not of type {typeof(T1).Name}.");
            }
            converted.root = root;
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
        internal TimeSpan delay = TimeSpan.Zero;

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
        internal SemaphoreSlim slim = new(1, 1);
        internal StateSnapshotChainSpreadorCore? spreador = null;

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
        internal abstract void CoreExecute(object target, bool CanMutualTask = true);
    }
}
