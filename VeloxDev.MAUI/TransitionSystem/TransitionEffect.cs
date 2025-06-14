using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.MAUI.TransitionSystem
{
    public enum DispatcherPriority : int
    {
        Inactive = 0,
        SystemIdle,
        ApplicationIdle,
        ContextIdle,
        Background,
        Input,
        Loaded,
        Render,
        DataBind,
        Normal,
        Send
    }

    public class TransitionEffect : TransitionEffectCore<DispatcherPriority>
    {
        public override DispatcherPriority Priority { get; set; } = DispatcherPriority.Render;
    }
}
