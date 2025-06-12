namespace VeloxDev.Core.Interfaces.TransitionSystem
{
    public class FrameEventArgs : EventArgs
    {
        public bool Handled { get; set; } = false;
    }

    public interface ITransitionEffect<TPriority> : ICloneable
    {
        public TPriority Priority { get; set; }
        public int FPS { get; set; }
        public TimeSpan Duration { get; set; }
        public bool IsAutoReverse { get; set; }
        public int LoopTime { get; set; }
        public double Acceleration { get; set; }

        public event EventHandler<FrameEventArgs> Awaked;
        public event EventHandler<FrameEventArgs> Start;
        public event EventHandler<FrameEventArgs> Update;
        public event EventHandler<FrameEventArgs> LateUpdate;
        public event EventHandler<FrameEventArgs> Canceled;
        public event EventHandler<FrameEventArgs> Completed;
        public event EventHandler<FrameEventArgs> Finally;

        public void AwakeInvoke(object sender, FrameEventArgs e);
        public void StartInvoke(object sender, FrameEventArgs e);
        public void UpdateInvoke(object sender, FrameEventArgs e);
        public void LateUpdateInvoke(object sender, FrameEventArgs e);
        public void CompletedInvoke(object sender, FrameEventArgs e);
        public void CancledInvoke(object sender, FrameEventArgs e);
        public void FinallyInvoke(object sender, FrameEventArgs e);

        public ITransitionEffect<TPriority> DeepCopy();
    }
}
