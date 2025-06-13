using VeloxDev.Core.Interfaces.TransitionSystem;
using VeloxDev.Core.WeakTypes;

namespace VeloxDev.Core.TransitionSystem
{
    public class TransitionEffectBase<TPriority>() : ITransitionEffect<TPriority>
        where TPriority : Enum
    {
        private WeakDelegate<EventHandler<FrameEventArgs>> _awaked = new();
        private WeakDelegate<EventHandler<FrameEventArgs>> _start = new();
        private WeakDelegate<EventHandler<FrameEventArgs>> _update = new();
        private WeakDelegate<EventHandler<FrameEventArgs>> _lateupdate = new();
        private WeakDelegate<EventHandler<FrameEventArgs>> _cancled = new();
        private WeakDelegate<EventHandler<FrameEventArgs>> _completed = new();
        private WeakDelegate<EventHandler<FrameEventArgs>> _finally = new();

#pragma warning disable CS8618
        public virtual TPriority Priority { get; set; }
#pragma warning restore CS8618
        public virtual int FPS { get; set; } = 60;
        public virtual TimeSpan Duration { get; set; } = TimeSpan.FromMilliseconds(0);
        public virtual bool IsAutoReverse { get; set; } = false;
        public virtual int LoopTime { get; set; } = 0;
        public virtual double Acceleration { get; set; } = 0;

        public virtual event EventHandler<FrameEventArgs> Awaked
        {
            add => _awaked.AddHandler(value);
            remove => _awaked.RemoveHandler(value);
        }
        public virtual event EventHandler<FrameEventArgs> Start
        {
            add => _start.AddHandler(value);
            remove => _start.RemoveHandler(value);
        }
        public virtual event EventHandler<FrameEventArgs> Update
        {
            add => _update.AddHandler(value);
            remove => _update.RemoveHandler(value);
        }
        public virtual event EventHandler<FrameEventArgs> LateUpdate
        {
            add => _lateupdate.AddHandler(value);
            remove => _lateupdate.RemoveHandler(value);
        }
        public virtual event EventHandler<FrameEventArgs> Canceled
        {
            add => _cancled.AddHandler(value);
            remove => _cancled.RemoveHandler(value);
        }
        public virtual event EventHandler<FrameEventArgs> Completed
        {
            add => _completed.AddHandler(value);
            remove => _completed.RemoveHandler(value);
        }
        public virtual event EventHandler<FrameEventArgs> Finally
        {
            add => _finally.AddHandler(value);
            remove => _finally.RemoveHandler(value);
        }

        public virtual void InvokeAwake(object sender, FrameEventArgs e)
        {
            _awaked.Invoke([sender, e]);
        }
        public virtual void InvokeStart(object sender, FrameEventArgs e)
        {
            _start.Invoke([sender, e]);
        }
        public virtual void InvokeUpdate(object sender, FrameEventArgs e)
        {
            _update.Invoke([sender, e]);
        }
        public virtual void InvokeLateUpdate(object sender, FrameEventArgs e)
        {
            _lateupdate.Invoke([sender, e]);
        }
        public virtual void InvokeCompleted(object sender, FrameEventArgs e)
        {
            _completed.Invoke([sender, e]);
        }
        public virtual void InvokeCancled(object sender, FrameEventArgs e)
        {
            _cancled.Invoke([sender, e]);
        }
        public virtual void InvokeFinally(object sender, FrameEventArgs e)
        {
            _finally.Invoke([sender, e]);
        }

        public virtual object Clone()
        {
            return DeepCopy();
        }
        public virtual ITransitionEffect<TPriority> DeepCopy()
        {
            var copy = new TransitionEffectBase<TPriority>
            {
                _awaked = _awaked.DeepCopy(),
                _start = _start.DeepCopy(),
                _update = _update.DeepCopy(),
                _lateupdate = _lateupdate.DeepCopy(),
                _cancled = _cancled.DeepCopy(),
                _completed = _completed.DeepCopy(),
                _finally = _finally.DeepCopy(),
                IsAutoReverse = IsAutoReverse,
                LoopTime = LoopTime,
                Duration = Duration,
                FPS = FPS,
                Acceleration = Acceleration,
                Priority = Priority,
            };
            return copy;
        }
    }
}
