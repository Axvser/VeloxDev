using System.Data.SqlTypes;
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
        protected TStateCore state = new();
        protected StateSnapshotCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore>? root;
        protected StateSnapshotCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore>? next = null;
        protected TEffectCore effect = new();
        protected TInterpolatorCore interpolator = new();

        internal override void AsRoot()
        {
            root = this;
        }
        internal override void SetTarget(T target)
        {
            root ??= this;
            root.targetref = new WeakReference<T>(target);
        }

        internal override IFrameState RecordState()
        {
            return state.Clone();
        }
        public override IFrameState Merge(StateSnapshotCore<T> snapshot)
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

        public StateSnapshotCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore> Property<TInterpolable>(Expression<Func<T, TInterpolable?>> propertyLambda, TInterpolable newValue)
            where TInterpolable : IInterpolable
        {
            state.SetValue<T, TInterpolable?>(propertyLambda, newValue);
            return this;
        }

        public StateSnapshotCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore> Effect(TEffectCore effect)
        {
            this.effect = effect;
            return this;
        }
        public StateSnapshotCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore> Effect(Action<TEffectCore> effectSetter)
        {
            var newEffect = new TEffectCore();
            effectSetter.Invoke(newEffect);
            effect = newEffect;
            return this;
        }

        public StateSnapshotCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore> Await(TimeSpan timeSpan)
        {
            delay += timeSpan;
            return this;
        }

        public StateSnapshotCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore> Then()
        {
            var newNode = new StateSnapshotCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore>
            {
                root = this.root,
                targetref = this.targetref
            };
            next = newNode;
            return newNode;
        }
        public StateSnapshotCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore> AwaitThen(TimeSpan timeSpan)
        {
            var newNode = new StateSnapshotCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore>
            {
                root = this.root,
                targetref = this.targetref,
                delay = timeSpan
            };
            next = newNode;
            return newNode;
        }

        public override async void Excute(bool CanSTAThread = true)
        {
            root ??= this;
            await root.StartAsync(CanSTAThread);
        }
        public override async void Excute(T target, bool CanSTAThread = true)
        {
            root ??= this;
            await root.StartAsync(target, CanSTAThread);
        }
        internal async Task StartAsync(bool CanSTAThread = true)
        {
            if (targetref?.TryGetTarget(out var target) ?? false)
            {
                var cts = RefreshCts();
                await Task.Delay(delay, cts.Token);
                if (!cts.IsCancellationRequested)
                {
                    var scheduler = TransitionSchedulerCore<TUIThreadInspectorCore, TTransitionInterpreterCore>.FindOrCreate(target, CanSTAThread);
                    scheduler.Exit();
                    var copyEffect = effect.Clone();
                    copyEffect.Completed += (s, e) =>
                    {
                        next?.StartAsync(CanSTAThread);
                    };
                    if (!CanSTAThread)
                    {
                        TransitionCore.AddNoSTA(target, [scheduler]);
                        copyEffect.Finally += (s, e) =>
                        {
                            TransitionCore.RemoveNoSTA(target, [scheduler]);
                        };
                    }
                    scheduler.Execute(interpolator, state, copyEffect);
                }
            }
        }
        internal async Task StartAsync(T target, bool CanSTAThread = true)
        {
            var cts = RefreshCts();
            await Task.Delay(delay, cts.Token);
            if (!cts.IsCancellationRequested)
            {
                var scheduler = TransitionSchedulerCore<TUIThreadInspectorCore, TTransitionInterpreterCore>.FindOrCreate(target, CanSTAThread);
                scheduler.Exit();
                var copyEffect = effect.Clone();
                copyEffect.Completed += (s, e) =>
                {
                    next?.StartAsync(target, CanSTAThread);
                };
                if (!CanSTAThread)
                {
                    TransitionCore.AddNoSTA(target, [scheduler]);
                    copyEffect.Finally += (s, e) =>
                    {
                        TransitionCore.RemoveNoSTA(target, [scheduler]);
                    };
                }
                scheduler.Execute(interpolator, state, copyEffect);
            }
        }
        internal CancellationTokenSource RefreshCts(bool CanSTAThread = true)
        {
            var newCts = new CancellationTokenSource();
            var oldCts = Interlocked.Exchange(ref cts, newCts);
            if (oldCts is not null && CanSTAThread && !oldCts.IsCancellationRequested) oldCts.Cancel();
            return newCts;
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
        where TUIThreadInspectorCore : IUIThreadInspector, new()
        where TTransitionInterpreterCore : class, ITransitionInterpreter<TPriorityCore>, new()
    {
        protected TStateCore state = new();
        protected StateSnapshotCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore, TPriorityCore>? root;
        protected StateSnapshotCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore, TPriorityCore>? next = null;
        protected TEffectCore effect = new();
        protected TInterpolatorCore interpolator = new();

        internal override void AsRoot()
        {
            root = this;
        }
        internal override void SetTarget(T target)
        {
            root ??= this;
            root.targetref = new WeakReference<T>(target);
        }

        internal override IFrameState RecordState()
        {
            return state.Clone();
        }
        public override IFrameState Merge(StateSnapshotCore<T> snapshot)
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

        public StateSnapshotCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore, TPriorityCore> Property<TInterpolable>(Expression<Func<T, TInterpolable?>> propertyLambda, TInterpolable newValue)
            where TInterpolable : IInterpolable
        {
            state.SetValue<T, TInterpolable?>(propertyLambda, newValue);
            return this;
        }

        public StateSnapshotCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore, TPriorityCore> Effect(TEffectCore effect)
        {
            this.effect = effect;
            return this;
        }
        public StateSnapshotCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore, TPriorityCore> Effect(Action<TEffectCore> effectSetter)
        {
            var newEffect = new TEffectCore();
            effectSetter.Invoke(newEffect);
            effect = newEffect;
            return this;
        }

        public StateSnapshotCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore, TPriorityCore> Await(TimeSpan timeSpan)
        {
            delay += timeSpan;
            return this;
        }

        public StateSnapshotCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore, TPriorityCore> Then()
        {
            var newNode = new StateSnapshotCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore, TPriorityCore>
            {
                root = this.root,
                targetref = this.targetref
            };
            next = newNode;
            return newNode;
        }
        public StateSnapshotCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore, TPriorityCore> AwaitThen(TimeSpan timeSpan)
        {
            var newNode = new StateSnapshotCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore, TPriorityCore>
            {
                root = this.root,
                targetref = this.targetref,
                delay = timeSpan
            };
            next = newNode;
            return newNode;
        }

        public override async void Excute(bool CanSTAThread = true)
        {
            root ??= this;
            await root.StartAsync(CanSTAThread);
        }
        public override async void Excute(T target, bool CanSTAThread = true)
        {
            root ??= this;
            await root.StartAsync(target, CanSTAThread);
        }
        internal async Task StartAsync(bool CanSTAThread = true)
        {
            if (targetref?.TryGetTarget(out var target) ?? false)
            {
                var cts = RefreshCts();
                await Task.Delay(delay, cts.Token);
                if (!cts.IsCancellationRequested)
                {
                    var scheduler = TransitionSchedulerCore<TUIThreadInspectorCore, TTransitionInterpreterCore, TPriorityCore>.FindOrCreate(target, CanSTAThread);
                    scheduler.Exit();
                    var copyEffect = effect.Clone();
                    copyEffect.Completed += (s, e) =>
                    {
                        next?.StartAsync(CanSTAThread);
                    };
                    if (!CanSTAThread)
                    {
                        TransitionCore.AddNoSTA(target, [scheduler]);
                        copyEffect.Finally += (s, e) =>
                        {
                            TransitionCore.RemoveNoSTA(target, [scheduler]);
                        };
                    }
                    scheduler.Execute(interpolator, state, copyEffect);
                }
            }
        }
        internal async Task StartAsync(T target, bool CanSTAThread = true)
        {
            var cts = RefreshCts();
            await Task.Delay(delay, cts.Token);
            if (!cts.IsCancellationRequested)
            {
                var scheduler = TransitionSchedulerCore<TUIThreadInspectorCore, TTransitionInterpreterCore, TPriorityCore>.FindOrCreate(target, CanSTAThread);
                scheduler.Exit();
                var copyEffect = effect.Clone();
                copyEffect.Completed += (s, e) =>
                {
                    next?.StartAsync(target, CanSTAThread);
                };
                if (!CanSTAThread)
                {
                    TransitionCore.AddNoSTA(target, [scheduler]);
                    copyEffect.Finally += (s, e) =>
                    {
                        TransitionCore.RemoveNoSTA(target, [scheduler]);
                    };
                }
                scheduler.Execute(interpolator, state, copyEffect);
            }
        }
        internal CancellationTokenSource RefreshCts(bool CanSTAThread = true)
        {
            var newCts = new CancellationTokenSource();
            var oldCts = Interlocked.Exchange(ref cts, newCts);
            if (oldCts is not null && CanSTAThread && !oldCts.IsCancellationRequested) oldCts.Cancel();
            return newCts;
        }
    }

    public abstract class StateSnapshotCore<T>
        where T : class
    {
        internal WeakReference<T>? targetref = null;
        protected TimeSpan delay = TimeSpan.Zero;
        protected CancellationTokenSource? cts = null;
        internal abstract IFrameState RecordState();
        public abstract IFrameState Merge(StateSnapshotCore<T> snapshot);
        internal virtual void AsRoot()
        {

        }
        internal virtual void SetTarget(T target)
        {
            targetref = new WeakReference<T>(target);
        }
        public abstract void Excute(bool CanSTAThread = true);
        public abstract void Excute(T target, bool CanSTAThread = true);
    }
}
