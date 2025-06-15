using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem
{
    /// <summary>
    /// <para>---</para>
    /// ✨ ⌈ 核心 ⌋ 过渡调度器
    /// <para>解释 : </para>
    /// <para>1. 在不同平台实现过渡系统时，您仅需一个此核心的具体实现就能用于调度过渡的启动和终止</para>
    /// <para>2. Execute 和 Exit 方法可以重写，通常您不需要这么做，内部已有完善的实现</para>
    /// </summary>
    /// <typeparam name="TTarget">保留此抽象，不要指定具体内容</typeparam>
    /// <typeparam name="TOutputCore">帧计算完成后，需要一个统一的结构用于存储结果并按索引更新帧</typeparam>
    /// <typeparam name="TPriorityCore">在不同框架中，使用不同的结构来表示UI更新操作的优先级</typeparam>
    /// <typeparam name="TUIThreadInspectorCore">在不同框架中，使用不同的方式来检查是否位于UI线程</typeparam>
    /// <typeparam name="TStateCore">您在具体框架对StateCore的实现类</typeparam>
    /// <typeparam name="TTransitionEffectCore">您在具体框架对TransitionEffect的实现类</typeparam>
    /// <typeparam name="TTransitionInterpreterCore">解释器负责动画帧的实际控制</typeparam>
    public abstract class TransitionSchedulerCore<
        TTarget,
        TOutputCore,
        TPriorityCore,
        TUIThreadInspectorCore,
        TStateCore,
        TTransitionEffectCore,
        TTransitionInterpreterCore> : TransitionSchedulerCore, ITransitionScheduler<
            TTarget, 
            TUIThreadInspectorCore, 
            TOutputCore, 
            TStateCore, 
            TTransitionEffectCore, 
            TPriorityCore>
        where TStateCore : IFrameState<TStateCore>
        where TTransitionEffectCore : ITransitionEffect<TTransitionEffectCore>
        where TTarget : class
        where TOutputCore : IFrameSequence<TPriorityCore>
        where TUIThreadInspectorCore : IUIThreadInspector, new()
        where TTransitionInterpreterCore : ITransitionInterpreter<TTransitionEffectCore, TPriorityCore>, new()
    {
        protected WeakReference<TTarget>? targetref = null;
        protected CancellationTokenSource? cts = null;
        protected ITransitionInterpreter<TTransitionEffectCore, TPriorityCore>? interpreter = null;
        protected TUIThreadInspectorCore uIThreadInspector = new();
        protected TTransitionInterpreterCore transitionInterpreter = new();

        public virtual async void Execute(IFrameInterpolator<TTransitionEffectCore, TStateCore, TOutputCore, TPriorityCore> interpolator, IFrameState<TStateCore> state, ITransitionEffect<TTransitionEffectCore, TPriorityCore> effect)
        {
            Exit();
            if (targetref is null || !targetref.TryGetTarget(out var target))
            {
                targetref = null;
                return;
            }
            var newCts = new CancellationTokenSource();
            TTransitionInterpreterCore newInterpreter = new();
            cts = newCts;
            interpreter = newInterpreter;
            effect.InvokeAwake(target, newInterpreter.Args);
            var frames = interpolator.Interpolate(target, state, effect);
            await newInterpreter.Execute(target, frames, effect, uIThreadInspector.IsUIThread(), newCts);
        }

        public override void Exit()
        {
            Interlocked.Exchange(ref interpreter, null);
            var oldCts = Interlocked.Exchange(ref cts, null);
            oldCts?.Cancel();
        }
    }

    /// <summary>
    /// <para>---</para>
    /// ✨ ⌈ 核心 ⌋ 过渡调度器
    /// <para>解释 : </para>
    /// <para>1. 在不同平台实现过渡系统时，您仅需一个此核心的具体实现就能用于调度过渡的启动和终止</para>
    /// <para>2. Execute 和 Exit 方法可以重写，通常您不需要这么做，内部已有完善的实现</para>
    /// </summary>
    /// <typeparam name="TTarget">保留此抽象，不要指定具体内容</typeparam>
    /// <typeparam name="TOutputCore">帧计算完成后，需要一个统一的结构用于存储结果并按索引更新帧</typeparam>
    /// <typeparam name="TUIThreadInspectorCore">在不同框架中，使用不同的方式来检查是否位于UI线程</typeparam>
    /// <typeparam name="TStateCore">您在具体框架对StateCore的实现类</typeparam>
    /// <typeparam name="TTransitionEffectCore">您在具体框架对TransitionEffect的实现类</typeparam>
    /// <typeparam name="TTransitionInterpreterCore">解释器负责动画帧的实际控制</typeparam>
    public abstract class TransitionSchedulerCore<
        TTarget,
        TOutputCore,
        TUIThreadInspectorCore,
        TStateCore,
        TTransitionEffectCore,
        TTransitionInterpreterCore> : TransitionSchedulerCore, ITransitionScheduler<
            TTarget, 
            TUIThreadInspectorCore, 
            TOutputCore, 
            TStateCore, 
            TTransitionEffectCore>
        where TStateCore : IFrameState<TStateCore>
        where TTransitionEffectCore : ITransitionEffect<TTransitionEffectCore>
        where TTarget : class
        where TOutputCore : IFrameSequence
        where TUIThreadInspectorCore : IUIThreadInspector, new()
        where TTransitionInterpreterCore : ITransitionInterpreter<TTransitionEffectCore>, new()
    {
        protected WeakReference<TTarget>? targetref = null;
        protected CancellationTokenSource? cts = null;
        protected ITransitionInterpreter<TTransitionEffectCore>? interpreter = null;
        protected TUIThreadInspectorCore uIThreadInspector = new();
        protected TTransitionInterpreterCore transitionInterpreter = new();

        public virtual async void Execute(IFrameInterpolator<TTransitionEffectCore, TStateCore, TOutputCore> interpolator, IFrameState<TStateCore> state, ITransitionEffect<TTransitionEffectCore> effect)
        {
            Exit();
            if (targetref is null || !targetref.TryGetTarget(out var target))
            {
                targetref = null;
                return;
            }
            var newCts = new CancellationTokenSource();
            TTransitionInterpreterCore newInterpreter = new();
            cts = newCts;
            interpreter = newInterpreter;
            effect.InvokeAwake(target, newInterpreter.Args);
            var frames = interpolator.Interpolate(target, state, effect);
            await newInterpreter.Execute(target, frames, effect, uIThreadInspector.IsUIThread(), newCts);
        }

        public override void Exit()
        {
            Interlocked.Exchange(ref interpreter, null);
            var oldCts = Interlocked.Exchange(ref cts, null);
            oldCts?.Cancel();
        }
    }

    /// <summary>
    /// <para>---</para>
    /// ✨ ⌈ 核心 ⌋ 过渡调度器
    /// <para>解释 : </para>
    /// <para>1. 在不同平台实现过渡系统时，您仅需一个此核心的具体实现就能用于调度过渡的启动和终止</para>
    /// <para>2. 请使用带有泛型的Core子类以构建调度器</para>
    /// </summary>
    public abstract class TransitionSchedulerCore : ITransitionSchedulerCore
    {
        public abstract void Exit();
    }
}
