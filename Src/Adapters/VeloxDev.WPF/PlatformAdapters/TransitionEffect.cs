using System.Windows.Threading;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.TransitionSystem
{
    public class TransitionEffect : TransitionEffectCore<DispatcherPriority>
    {
        public override DispatcherPriority Priority { get; set; } = DispatcherPriority.Render;
    }
}
