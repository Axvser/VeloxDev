using Microsoft.UI.Dispatching;

namespace VeloxDev.TransitionSystem
{
    public class TransitionEffect : TransitionEffectCore<DispatcherQueuePriority>
    {
        // 动画帧写入用 High 优先级入队，确保在渲染前处理，减少卡顿。
        public override DispatcherQueuePriority Priority { get; set; } = DispatcherQueuePriority.High;
    }
}
