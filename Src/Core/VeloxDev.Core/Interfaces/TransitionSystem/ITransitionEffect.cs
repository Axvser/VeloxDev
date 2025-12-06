using VeloxDev.Core.TimeLine;

namespace VeloxDev.Core.Interfaces.TransitionSystem
{
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
        public IEaseCalculator Ease { get; set; }

        public event EventHandler<TransitionEventArgs> Awaked;
        public event EventHandler<TransitionEventArgs> Start;
        public event EventHandler<TransitionEventArgs> Update;
        public event EventHandler<TransitionEventArgs> LateUpdate;
        public event EventHandler<TransitionEventArgs> Canceled;
        public event EventHandler<TransitionEventArgs> Completed;
        public event EventHandler<TransitionEventArgs> Finally;

        public void InvokeAwake(object sender, TransitionEventArgs e);
        public void InvokeStart(object sender, TransitionEventArgs e);
        public void InvokeUpdate(object sender, TransitionEventArgs e);
        public void InvokeLateUpdate(object sender, TransitionEventArgs e);
        public void InvokeCompleted(object sender, TransitionEventArgs e);
        public void InvokeCancled(object sender, TransitionEventArgs e);
        public void InvokeFinally(object sender, TransitionEventArgs e);

        public ITransitionEffectCore Clone();
    }
}
