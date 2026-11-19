using Microsoft.UI.Dispatching;

namespace VeloxDev.TransitionSystem
{
    public class TransitionEffect : TransitionEffectCore<DispatcherQueuePriority>
    {
        // 以 High 优先级排队动画帧写入，让它们在渲染前处理，减少卡顿。
        /// <inheritdoc />
        public override DispatcherQueuePriority Priority { get; set; } = DispatcherQueuePriority.High;
    }
}
