using Avalonia.Threading;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.WinUI.TransitionSystem
{
    public class TransitionEffect : TransitionEffectCore<DispatcherPriority>
    {
        public override DispatcherPriority Priority { get; set; } = DispatcherPriority.Render;
    }
}
