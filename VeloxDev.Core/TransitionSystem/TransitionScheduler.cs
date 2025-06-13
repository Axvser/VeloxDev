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
    /// <typeparam name="TOutput">帧计算完成后，需要一个统一的结构用于存储结果并按索引更新帧</typeparam>
    /// <typeparam name="TPriority">在不同框架中，使用不同的结构来表示UI更新操作的优先级</typeparam>
    /// <typeparam name="TUIThreadInspector">在不同框架中，使用不同的方式来检查是否位于UI线程</typeparam>
    /// <typeparam name="TTransitionInterpreter">解释器负责动画帧的实际控制</typeparam>
    public class TransitionSchedulerCore<
        TTarget,
        TOutput,
        TPriority,
        TUIThreadInspector,
        TTransitionInterpreter> : ITransitionScheduler<TTarget, TUIThreadInspector, TOutput, TPriority>
        where TTarget : class
        where TOutput : IFrameSequence<TPriority>
        where TUIThreadInspector : IUIThreadInspector, new()
        where TTransitionInterpreter : ITransitionInterpreter<TPriority>, new()
    {
        protected WeakReference<TTarget>? targetref = null;
        protected CancellationTokenSource? cts = null;
        protected ITransitionInterpreter? interpreter = null;
        protected TUIThreadInspector uIThreadInspector = new();
        protected TTransitionInterpreter transitionInterpreter = new();

        public virtual async void Execute(IFrameInterpolator<TOutput, TPriority> interpolator, IFrameState<TOutput, TPriority> state, ITransitionEffect<TPriority> effect)
        {
            Exit();
            if (targetref is null || !targetref.TryGetTarget(out var target))
            {
                targetref = null;
                return;
            }
            var newCts = new CancellationTokenSource();
            TTransitionInterpreter newInterpreter = new();
            cts = newCts;
            interpreter = newInterpreter;
            effect.InvokeAwake(target, newInterpreter.Args);
            var frames = interpolator.Interpolate(target, state, effect);
            await newInterpreter.Execute(target, frames, effect, uIThreadInspector.IsUIThread(), newCts);
        }

        public virtual void Exit()
        {
            Interlocked.Exchange(ref interpreter, null);
            var oldCts = Interlocked.Exchange(ref cts, null);
            oldCts?.Cancel();
        }
    }
}
