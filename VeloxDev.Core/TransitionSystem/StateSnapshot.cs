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
            if (target is not T)
                throw new InvalidDataException($"The target is not a {typeof(T).Name} !");

            root ??= this;

            var scheduler = TransitionSchedulerCore<TUIThreadInspectorCore, TTransitionInterpreterCore>.FindOrCreate(target, CanMutualTask);
            if (CanMutualTask) scheduler.Exit();

            CancellationTokenSource cts = new();
            Queue<IFrameInterpolatorCore> interpolators = [];
            Queue<TimeSpan> spans = [];
            Queue<ITransitionEffectCore> effects = [];
            Queue<IFrameState> states = [];
            int Count = 0;

            StateSnapshotCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore>? currentNode = root;
            do
            {
                interpolators.Enqueue(currentNode.interpolator);
                spans.Enqueue(currentNode.delay);
                effects.Enqueue(currentNode.effect.Clone());
                states.Enqueue(currentNode.state);
                Count++;
                currentNode = currentNode.next;
            }
            while (currentNode is not null);

            await slim.WaitAsync().ConfigureAwait(false);

            while (!cts.IsCancellationRequested && Count > 0)
            {
                try
                {
                    await Task.Delay(spans.Dequeue(), cts.Token);
                }
                catch { }
                await scheduler.Execute(interpolators.Dequeue(), states.Dequeue(), effects.Dequeue(), cts);
                Count--;
            }

            slim.Release();
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
            if (target is not T)
                throw new InvalidDataException($"The target is not a {typeof(T).Name} !");

            root ??= this;

            var scheduler = TransitionSchedulerCore<TUIThreadInspectorCore, TTransitionInterpreterCore, TPriorityCore>.FindOrCreate(target, CanMutualTask);
            if (CanMutualTask) scheduler.Exit();

            CancellationTokenSource cts = new();
            Queue<IFrameInterpolatorCore> interpolators = [];
            Queue<TimeSpan> spans = [];
            Queue<ITransitionEffectCore> effects = [];
            Queue<IFrameState> states = [];
            int Count = 0;

            StateSnapshotCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore, TPriorityCore>? currentNode = root;
            do
            {
                interpolators.Enqueue(currentNode.interpolator);
                spans.Enqueue(currentNode.delay);
                effects.Enqueue(currentNode.effect.Clone());
                states.Enqueue(currentNode.state);
                Count++;
                currentNode = currentNode.next;
            }
            while (currentNode is not null);

            await slim.WaitAsync().ConfigureAwait(false);

            while (!cts.IsCancellationRequested && Count > 0)
            {
                try
                {
                    await Task.Delay(spans.Dequeue(), cts.Token);
                }
                catch { }
                await scheduler.Execute(interpolators.Dequeue(), states.Dequeue(), effects.Dequeue(), cts);
                Count--;
            }

            slim.Release();
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
        internal SemaphoreSlim slim = new(1, 1);

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
        internal abstract void CoreExecute(object target, bool CanMutualTask = true);
    }
}
