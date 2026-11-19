using Jalium.UI.Threading;

namespace VeloxDev.TransitionSystem
{
    public class TransitionEffect : TransitionEffectCore<DispatcherPriority>
    {
        /// <inheritdoc />
        public override DispatcherPriority Priority { get; set; } = DispatcherPriority.Render;
    }
}
