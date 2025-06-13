using System.Reflection;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem
{
    /// <summary>
    /// <para>---</para>
    /// ✨ ⌈ 核心 ⌋ 插值输出
    /// <para>解释 : </para>
    /// <para>在不同平台实现过渡系统时，您需要具体地实现此核心以用于定义动画帧的数据结构以及动画帧的按索引更新操作</para>
    /// </summary>
    /// <typeparam name="TPriority">在不同框架中，使用不同的结构来表示UI更新操作的优先级</typeparam>
    public abstract class InterpolatorOutputCore<TPriority> : IFrameSequence<TPriority>
    {
        public virtual Dictionary<PropertyInfo, List<object?>> Frames { get; protected set; } = [];
        public virtual int Count { get; protected set; } = 0;
        public abstract void Update(object target, int frameIndex, bool isUIAccess, TPriority priority);
    }
}
