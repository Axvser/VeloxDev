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
    /// <typeparam name="TOutput">帧计算完成后，需要一个统一的结构用于存储结果并按索引更新帧</typeparam>
    /// <typeparam name="TPriority">在不同框架中，使用不同的结构来表示UI更新操作的优先级</typeparam>
    public class TransitionInterpreterCore<
        TOutput,
        TPriority> : ITransitionInterpreter<TPriority>
        where TOutput : IFrameSequence<TPriority>
    {
        public virtual FrameEventArgs Args { get; set; } = new();

        public virtual async Task Execute(
            object target,
            IFrameSequence<TPriority> frameSequence,
            ITransitionEffect<TPriority> effect,
            bool isUIAccess,
            CancellationTokenSource cts)
        {
            var spans = GetAccDeltaTimes(effect, frameSequence.Count);
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
                        effect.InvokeUpdate(target, Args);
                        frameSequence.Update(
                            target,
                            index,
                            isUIAccess,
                            effect.Priority);
                        effect.InvokeLateUpdate(target, Args);
                        await Task.Delay(spans[index], cts.Token);
                    }

                    if (effect.IsAutoReverse)
                    {
                        for (int index = frameSequence.Count - 1; index >= 0; index--)
                        {
                            effect.InvokeUpdate(target, Args);
                            frameSequence.Update(
                                target,
                                index,
                                isUIAccess,
                                effect.Priority);
                            effect.InvokeLateUpdate(target, Args);
                            await Task.Delay(spans[index], cts.Token);
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

        public virtual void Exit()
        {
            Dispose();
        }

        public virtual void Dispose()
        {
            GC.SuppressFinalize(this);
        }

        private static List<int> GetAccDeltaTimes(
            ITransitionEffect<TPriority> effect,
            int steps)
        {
            List<int> result = [];
            var standardDeltaTime = 1000d / effect.FPS;
            double acc;
            if (effect.Acceleration > 1) acc = 1;
            else if (effect.Acceleration < -1) acc = -1;
            else acc = effect.Acceleration;
            var start = standardDeltaTime * (1 + acc);
            var end = standardDeltaTime * (1 - acc);
            var delta = end - start;
            for (int i = 0; i < steps; i++)
            {
                var t = (double)(i + 1) / steps;
                result.Add((int)(start + t * delta));
            }
            return result;
        }
    }
}
