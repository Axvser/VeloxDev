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
        public TPriority Priority { get; set; }
#pragma warning restore CS8618
        public int FPS { get; set; }
        public TimeSpan Duration { get; set; }
        public bool IsAutoReverse { get; set; }
        public int LoopTime { get; set; }
        public double Acceleration { get; set; }

        public event EventHandler<FrameEventArgs> Awaked
        {
            add => _awaked.AddHandler(value);
            remove => _awaked.RemoveHandler(value);
        }
        public event EventHandler<FrameEventArgs> Start
        {
            add => _start.AddHandler(value);
            remove => _start.RemoveHandler(value);
        }
        public event EventHandler<FrameEventArgs> Update
        {
            add => _update.AddHandler(value);
            remove => _update.RemoveHandler(value);
        }
        public event EventHandler<FrameEventArgs> LateUpdate
        {
            add => _lateupdate.AddHandler(value);
            remove => _lateupdate.RemoveHandler(value);
        }
        public event EventHandler<FrameEventArgs> Canceled
        {
            add => _cancled.AddHandler(value);
            remove => _cancled.RemoveHandler(value);
        }
        public event EventHandler<FrameEventArgs> Completed
        {
            add => _completed.AddHandler(value);
            remove => _completed.RemoveHandler(value);
        }
        public event EventHandler<FrameEventArgs> Finally
        {
            add => _finally.AddHandler(value);
            remove => _finally.RemoveHandler(value);
        }

        public void AwakeInvoke(object sender, FrameEventArgs e)
        {
            var handlers = _awaked.GetInvocationList();
            handlers?.Invoke(sender, e);
        }
        public void StartInvoke(object sender, FrameEventArgs e)
        {
            var handlers = _start.GetInvocationList();
            handlers?.Invoke(sender, e);
        }
        public void UpdateInvoke(object sender, FrameEventArgs e)
        {
            var handlers = _update.GetInvocationList();
            handlers?.Invoke(sender, e);
        }
        public void LateUpdateInvoke(object sender, FrameEventArgs e)
        {
            var handlers = _lateupdate.GetInvocationList();
            handlers?.Invoke(sender, e);
        }
        public void CompletedInvoke(object sender, FrameEventArgs e)
        {
            var handlers = _completed.GetInvocationList();
            handlers?.Invoke(sender, e);
        }
        public void CancledInvoke(object sender, FrameEventArgs e)
        {
            var handlers = _cancled.GetInvocationList();
            handlers?.Invoke(sender, e);
        }
        public void FinallyInvoke(object sender, FrameEventArgs e)
        {
            var handlers = _finally.GetInvocationList();
            handlers?.Invoke(sender, e);
        }

        public object Clone()
        {
            return DeepCopy();
        }
        public ITransitionEffect<TPriority> DeepCopy()
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
