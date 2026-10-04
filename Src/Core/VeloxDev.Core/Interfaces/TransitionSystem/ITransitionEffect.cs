namespace VeloxDev.TransitionSystem
{
    /// <summary>A transition effect whose priority type is <typeparamref name="TPriorityCore"/>.</summary>
    public interface ITransitionEffect<TPriorityCore> : ITransitionEffectCore
    {
        /// <summary>The effect's scheduling priority.</summary>
        public TPriorityCore Priority { get; set; }

        /// <summary>Returns a copy of this effect.</summary>
        public new ITransitionEffect<TPriorityCore> Clone();
    }

    /// <summary>The timing, easing and lifecycle events of one transition effect.</summary>
    public interface ITransitionEffectCore
    {
        /// <summary>The target frame rate, in frames per second.</summary>
        public int FPS { get; set; }

        /// <summary>How long one pass of the animation lasts.</summary>
        public TimeSpan Duration { get; set; }

        /// <summary>Whether the animation plays back in reverse after reaching the end.</summary>
        public bool IsAutoReverse { get; set; }

        /// <summary>How many times the animation repeats.</summary>
        public int LoopTime { get; set; }

        /// <summary>The easing curve applied to normalized time.</summary>
        public IEaseCalculator Ease { get; set; }

        /// <summary>Raised when the animation wakes.</summary>
        public event EventHandler<TransitionEventArgs> Awaked;

        /// <summary>Raised when the animation starts.</summary>
        public event EventHandler<TransitionEventArgs> Start;

        /// <summary>Raised on each update frame.</summary>
        public event EventHandler<TransitionEventArgs> Update;

        /// <summary>Raised after all update frames of a pass.</summary>
        public event EventHandler<TransitionEventArgs> LateUpdate;

        /// <summary>Raised when the animation is cancelled.</summary>
        public event EventHandler<TransitionEventArgs> Canceled;

        /// <summary>Raised when the animation completes.</summary>
        public event EventHandler<TransitionEventArgs> Completed;

        /// <summary>Raised once the animation has finished, however it ended.</summary>
        public event EventHandler<TransitionEventArgs> Finally;

        /// <summary>Raised when the run degrades through a recoverable stage, carrying which stage and its message.</summary>
        public event EventHandler<TransitionEventArgs<WarnStage, string>> Warn;

        /// <summary>Raised when the run fails, carrying which stage and the escaped exception.</summary>
        public event EventHandler<TransitionEventArgs<ErrorStage, Exception>> Error;

        /// <summary>Raises <see cref="Awaked"/>.</summary>
        public void InvokeAwake(object sender, TransitionEventArgs e);

        /// <summary>Raises <see cref="Start"/>.</summary>
        public void InvokeStart(object sender, TransitionEventArgs e);

        /// <summary>Raises <see cref="Update"/>.</summary>
        public void InvokeUpdate(object sender, TransitionEventArgs e);

        /// <summary>Raises <see cref="LateUpdate"/>.</summary>
        public void InvokeLateUpdate(object sender, TransitionEventArgs e);

        /// <summary>Raises <see cref="Completed"/>.</summary>
        public void InvokeCompleted(object sender, TransitionEventArgs e);

        /// <summary>Raises <see cref="Canceled"/>.</summary>
        public void InvokeCancled(object sender, TransitionEventArgs e);

        /// <summary>Raises <see cref="Finally"/>.</summary>
        public void InvokeFinally(object sender, TransitionEventArgs e);

        /// <summary>Raises <see cref="Warn"/>.</summary>
        public void InvokeWarn(object sender, TransitionEventArgs<WarnStage, string> e);

        /// <summary>Raises <see cref="Error"/>.</summary>
        public void InvokeError(object sender, TransitionEventArgs<ErrorStage, Exception> e);

        /// <summary>Returns a copy of this effect.</summary>
        public ITransitionEffectCore Clone();
    }
}
