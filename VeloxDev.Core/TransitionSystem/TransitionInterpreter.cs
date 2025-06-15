using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem
{
    /// <summary>
    /// <para>---</para>
    /// ✨ ⌈ 核心 ⌋ 过渡解释器
    /// <para>解释 : </para>
    /// <para>1. 在不同平台实现过渡系统时，您仅需一个此核心的具体实现就能用于控制动画帧的执行细节</para>
    /// <para>2. Execute 和 Exit 方法可以重写，通常您不需要这么做，内部已有完善的实现</para>
    /// </summary>
    /// <typeparam name="TTransitionEffectCore">您在具体框架对ITransitionEffect的实现类</typeparam>
    /// <typeparam name="TOutputCore">帧计算完成后，需要一个统一的结构用于存储结果并按索引更新帧</typeparam>
    /// <typeparam name="TPriorityCore">在不同框架中，使用不同的结构来表示UI更新操作的优先级</typeparam>
    public abstract class TransitionInterpreterCore<
        TOutputCore,
        TTransitionEffectCore,
        TPriorityCore> : TransitionInterpreterCore, ITransitionInterpreter<
            TTransitionEffectCore, 
            TPriorityCore>
        where TTransitionEffectCore : ITransitionEffect<TTransitionEffectCore>
        where TOutputCore : IFrameSequence<TPriorityCore>
    {
        public virtual async Task Execute(
            object target,
            IFrameSequence<TPriorityCore> frameSequence,
            ITransitionEffect<TTransitionEffectCore, TPriorityCore> effect,
            bool isUIAccess,
            CancellationTokenSource cts)
        {
            this.cts = cts;
            var indexs = GetEaseIndex(effect, frameSequence.Count);
            var span = GetDeltaTime(effect);
            var foreverloop = effect.LoopTime == int.MaxValue;
            try
            {
                effect.InvokeStart(target, Args);
                for (int loop = 0;
                    loop <= effect.LoopTime || foreverloop;
                    loop += foreverloop ? 0 : 1)
                {
                    for (int index = 0; index < frameSequence.Count; index++)
                    {
                        if (cts.IsCancellationRequested || Args.Handled) throw new OperationCanceledException();
                        effect.InvokeUpdate(target, Args);
                        frameSequence.Update(
                            target,
                            indexs[index],
                            isUIAccess,
                            effect.Priority);
                        effect.InvokeLateUpdate(target, Args);
                        await Task.Delay(span, cts.Token);
                    }

                    if (effect.IsAutoReverse)
                    {
                        for (int index = frameSequence.Count - 1; index >= 0; index--)
                        {
                            if (cts.IsCancellationRequested || Args.Handled) throw new OperationCanceledException();
                            effect.InvokeUpdate(target, Args);
                            frameSequence.Update(
                                target,
                                indexs[index],
                                isUIAccess,
                                effect.Priority);
                            effect.InvokeLateUpdate(target, Args);
                            await Task.Delay(span, cts.Token);
                        }
                    }
                }
                effect.InvokeCompleted(target, Args);
            }
            catch
            {
                effect.InvokeCancled(target, Args);
            }
            finally
            {
                effect.InvokeFinally(target, Args);
                Exit();
            }
        }

        private static int GetDeltaTime(ITransitionEffect<TTransitionEffectCore, TPriorityCore> effect)
            => (int)(1000d / effect.FPS);

        private static List<int> GetEaseIndex(
            ITransitionEffect<TTransitionEffectCore, TPriorityCore> effect,
            int steps)
        {
            List<int> result = [];
            var endIndex = steps - 1d;
            for (int i = 0; i < steps; i++)
            {
                var ease = effect.EaseCalculator.Ease(i / endIndex);
                var index = (int)(steps * ease);
                if (index < 0) result.Add(0);
                else if (index >= steps) result.Add(steps - 1);
                else result.Add(index);
            }
            return result;
        }
    }

    /// <summary>
    /// <para>---</para>
    /// ✨ ⌈ 核心 ⌋ 过渡解释器
    /// <para>解释 : </para>
    /// <para>1. 在不同平台实现过渡系统时，您仅需一个此核心的具体实现就能用于控制动画帧的执行细节</para>
    /// <para>2. Execute 和 Exit 方法可以重写，通常您不需要这么做，内部已有完善的实现</para>
    /// </summary>
    /// <typeparam name="TTransitionEffectCore">您在具体框架对ITransitionEffect的实现类</typeparam>
    /// <typeparam name="TOutputCore">帧计算完成后，需要一个统一的结构用于存储结果并按索引更新帧</typeparam>
    public abstract class TransitionInterpreterCore<
        TOutputCore,
        TTransitionEffectCore> : TransitionInterpreterCore, ITransitionInterpreter<TTransitionEffectCore>
        where TTransitionEffectCore : ITransitionEffect<TTransitionEffectCore>
        where TOutputCore : IFrameSequence
    {
        public virtual async Task Execute(
            object target,
            IFrameSequence frameSequence,
            ITransitionEffect<TTransitionEffectCore> effect,
            bool isUIAccess,
            CancellationTokenSource cts)
        {
            this.cts = cts;
            var indexs = GetEaseIndex(effect, frameSequence.Count);
            var span = GetDeltaTime(effect);
            var foreverloop = effect.LoopTime == int.MaxValue;
            try
            {
                effect.InvokeStart(target, Args);
                for (int loop = 0;
                    loop <= effect.LoopTime || foreverloop;
                    loop += foreverloop ? 0 : 1)
                {
                    for (int index = 0; index < frameSequence.Count; index++)
                    {
                        if (cts.IsCancellationRequested || Args.Handled) throw new OperationCanceledException();
                        effect.InvokeUpdate(target, Args);
                        frameSequence.Update(
                            target,
                            indexs[index],
                            isUIAccess);
                        effect.InvokeLateUpdate(target, Args);
                        await Task.Delay(span, cts.Token);
                    }

                    if (effect.IsAutoReverse)
                    {
                        for (int index = frameSequence.Count - 1; index >= 0; index--)
                        {
                            if (cts.IsCancellationRequested || Args.Handled) throw new OperationCanceledException();
                            effect.InvokeUpdate(target, Args);
                            frameSequence.Update(
                                target,
                                indexs[index],
                                isUIAccess);
                            effect.InvokeLateUpdate(target, Args);
                            await Task.Delay(span, cts.Token);
                        }
                    }
                }
                effect.InvokeCompleted(target, Args);
            }
            catch
            {
                effect.InvokeCancled(target, Args);
            }
            finally
            {
                effect.InvokeFinally(target, Args);
                Exit();
            }
        }

        private static int GetDeltaTime(ITransitionEffect<TTransitionEffectCore> effect)
            => (int)(1000d / effect.FPS);

        private static List<int> GetEaseIndex(
            ITransitionEffect<TTransitionEffectCore> effect,
            int steps)
        {
            List<int> result = [];
            var endIndex = steps - 1d;
            for (int i = 0; i < steps; i++)
            {
                var ease = effect.EaseCalculator.Ease(i / endIndex);
                var index = (int)(steps * ease);
                if (index < 0) result.Add(0);
                else if (index >= steps) result.Add(steps - 1);
                else result.Add(index);
            }
            return result;
        }
    }

    public abstract class TransitionInterpreterCore : ITransitionInterpreterCore, IDisposable
    {
        protected CancellationTokenSource? cts = null;
        public virtual FrameEventArgs Args { get; set; } = new();

        public virtual void Exit()
        {
            var oldCts = Interlocked.Exchange(ref cts, null);
            if (oldCts != null && !oldCts.IsCancellationRequested)
            {
                oldCts.Cancel();
            }
            Dispose();
        }

        public virtual void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}
