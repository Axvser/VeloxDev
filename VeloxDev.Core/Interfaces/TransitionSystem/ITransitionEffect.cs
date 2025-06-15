namespace VeloxDev.Core.Interfaces.TransitionSystem
{
    public class FrameEventArgs : EventArgs
    {
        public bool Handled { get; set; } = false;
    }

    public interface ITransitionEffect<TPriorityCore> : ITransitionEffectCore
    {
        public TPriorityCore Priority { get; set; }

        public new ITransitionEffect<TPriorityCore> Clone();
    }

    public interface ITransitionEffectCore
    {
        public int FPS { get; set; }
        public TimeSpan Duration { get; set; }
        public bool IsAutoReverse { get; set; }
        public int LoopTime { get; set; }
        public IEaseCalculator EaseCalculator { get; set; }

        public event EventHandler<FrameEventArgs> Awaked;
        public event EventHandler<FrameEventArgs> Start;
        public event EventHandler<FrameEventArgs> Update;
        public event EventHandler<FrameEventArgs> LateUpdate;
        public event EventHandler<FrameEventArgs> Canceled;
        public event EventHandler<FrameEventArgs> Completed;
        public event EventHandler<FrameEventArgs> Finally;

        public void InvokeAwake(object sender, FrameEventArgs e);
        public void InvokeStart(object sender, FrameEventArgs e);
        public void InvokeUpdate(object sender, FrameEventArgs e);
        public void InvokeLateUpdate(object sender, FrameEventArgs e);
        public void InvokeCompleted(object sender, FrameEventArgs e);
        public void InvokeCancled(object sender, FrameEventArgs e);
        public void InvokeFinally(object sender, FrameEventArgs e);

        public ITransitionEffectCore Clone();
    }
}
