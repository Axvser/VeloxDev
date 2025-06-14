using Avalonia;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Avalonia.TransitionSystem
{
    public static class Transition
    {
        public static StateSnapshot<T> Create<T>()
            where T : class
        {
            var value = new StateSnapshot<T>();
            value.root = value;
            return value;
        }
        public static StateSnapshot<T> Create<T>(T target)
            where T : class
        {
            var value = new StateSnapshot<T>()
            {
                targetref = new(target)
            };
            value.root = value;
            return value;
        }

        public class StateSnapshot<T>
            where T : class
        {
            internal WeakReference<T>? targetref = null;
            internal State state = new();
            internal StateSnapshot<T>? root;
            internal StateSnapshot<T>? next = null;
            internal TransitionEffect effect = TransitionEffects.Empty;
            internal TimeSpan delay = TimeSpan.Zero;
            internal Interpolator interpolator = new();
            internal CancellationTokenSource? cts = null;

            public StateSnapshot<T> Property(Expression<Func<T, double>> propertyLambda, double newValue)
            {
                state.SetValue<T, double>(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot<T> Property(Expression<Func<T, IBrush?>> propertyLambda, IBrush newValue)
            {
                state.SetValue<T, IBrush?>(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot<T> Property(Expression<Func<T, ITransform?>> propertyLambda, ICollection<Transform> newValue)
            {
                var transformGroup = new TransformGroup()
                {
                    Children = [.. newValue]
                };
                state.SetValue<T, ITransform?>(propertyLambda, transformGroup);
                return this;
            }
            public StateSnapshot<T> Property(Expression<Func<T, Point>> propertyLambda, Point newValue)
            {
                state.SetValue<T, Point>(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot<T> Property(Expression<Func<T, CornerRadius>> propertyLambda, CornerRadius newValue)
            {
                state.SetValue<T, CornerRadius>(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot<T> Property(Expression<Func<T, Thickness>> propertyLambda, Thickness newValue)
            {
                state.SetValue<T, Thickness>(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot<T> Property<TInterpolable>(Expression<Func<T, TInterpolable>> propertyLambda, TInterpolable newValue)
                where TInterpolable : IInterpolable
            {
                state.SetValue<T, TInterpolable>(propertyLambda, newValue);
                return this;
            }

            public StateSnapshot<T> Effect(TransitionEffect effect)
            {
                this.effect = effect;
                return this;
            }
            public StateSnapshot<T> Effect(Action<TransitionEffect> effectSetter)
            {
                var newEffect = new TransitionEffect();
                effectSetter.Invoke(newEffect);
                effect = newEffect;
                return this;
            }

            public StateSnapshot<T> Await(TimeSpan timeSpan)
            {
                delay += timeSpan;
                return this;
            }

            public StateSnapshot<T> Then()
            {
                var newNode = new StateSnapshot<T>
                {
                    root = this.root,
                    targetref = this.targetref
                };
                next = newNode;
                return newNode;
            }
            public StateSnapshot<T> AwaitThen(TimeSpan timeSpan)
            {
                var newNode = new StateSnapshot<T>
                {
                    root = this.root,
                    targetref = this.targetref,
                    delay = timeSpan
                };
                next = newNode;
                return newNode;
            }
            public StateSnapshot<T> Then(StateSnapshot<T> next)
            {
                next.root = root;
                next.targetref = targetref;
                this.next = next;
                return next;
            }
            public StateSnapshot<T> AwaitThen(TimeSpan timeSpan, StateSnapshot<T> next)
            {
                next.root = root;
                next.targetref = targetref;
                next.delay = timeSpan;
                this.next = next;
                return next;
            }

            public async void Start()
            {
                if (root is not null) await root.StartAsync();
            }
            public async void Start(T target)
            {
                if (root is not null) await root.StartAsync(target);
            }
            internal async Task StartAsync()
            {
                if (targetref?.TryGetTarget(out var target) ?? false)
                {
                    var cts = RefreshCts();
                    var scheduler = TransitionScheduler.FindOrCreate<T>(target);
                    scheduler.Exit();
                    await Task.Delay(delay, cts.Token);
                    var copyEffect = effect.DeepCopy();
                    copyEffect.Completed += (s, e) =>
                    {
                        next?.StartAsync();
                    };
                    scheduler.Execute(interpolator, state, copyEffect);
                }
            }
            internal async Task StartAsync(T target)
            {
                var cts = RefreshCts();
                var scheduler = TransitionScheduler.FindOrCreate<T>(target);
                scheduler.Exit();
                var copyEffect = effect.DeepCopy();
                copyEffect.Completed += (s, e) =>
                {
                    next?.StartAsync(target);
                };
                await Task.Delay(delay, cts.Token);
                scheduler.Execute(interpolator, state, copyEffect);
            }
            internal CancellationTokenSource RefreshCts()
            {
                var newCts = new CancellationTokenSource();
                if (root is not null)
                {
                    var oldCts = Interlocked.Exchange(ref root.cts, newCts);
                    if (oldCts is not null && !oldCts.IsCancellationRequested) oldCts.Cancel();
                }
                return newCts;
            }
        }
    }
}
