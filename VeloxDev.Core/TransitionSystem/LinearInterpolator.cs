using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem
{
    /// <summary>
    /// <para>---</para>
    /// ✨ ⌈ 核心 ⌋ 插值器
    /// <para>解释 : </para>
    /// <para>在不同平台实现过渡系统时，您需要具体地实现此核心以构建用于计算过渡过程的插值器</para>
    /// </summary>
    /// <typeparam name="TOutput">帧计算完成后，需要一个统一的结构用于存储结果并按索引更新帧</typeparam>
    /// <typeparam name="TPriority">在不同框架中，使用不同的结构来表示UI更新操作的优先级</typeparam>
    public abstract class InterpolatorCore<
        TOutput, 
        TPriority> : IFrameInterpolator<TOutput, TPriority>
        where TOutput : IFrameSequence<TPriority>
    {
        public abstract TOutput Interpolate(object target, IFrameState<TOutput, TPriority> state, ITransitionEffect<TPriority> effect);
    }
}
