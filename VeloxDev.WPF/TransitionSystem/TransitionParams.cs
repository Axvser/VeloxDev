using System.Windows.Threading;
using VeloxDev.WPF.FrameworkSupport;
using VeloxDev.WPF.WeakTypes;

namespace VeloxDev.WPF.TransitionSystem
{
    public class FrameEventArgs : EventArgs
    {
        public bool Handled { get; set; } = false;
        public int CurrentFrameIndex { get; internal set; } = 0;
        public int MaxFrameIndex { get; internal set; } = 0;
    }

    /// <summary>
    /// ✨ Describe the transition effect
    /// <para>Static instance</para>
    /// <para>- <see cref="TransitionParams.Hover"/></para>
    /// <para>- <see cref="TransitionParams.Theme"/></para>
    /// <para>- <see cref="TransitionParams.Empty"/></para>
    /// <para>Effect parameter</para>
    /// <para>- <see cref="TransitionParams.FrameRate"/></para>
    /// <para>- <see cref="TransitionParams.Duration"/></para>
    /// <para>- <see cref="TransitionParams.Acceleration"/></para>
    /// <para>- <see cref="TransitionParams.IsAutoReverse"/></para>
    /// <para>- <see cref="TransitionParams.LoopTime"/></para>
    /// <para>- <see cref="TransitionParams.Priority"/></para>
    /// <para>- <see cref="TransitionParams.IsAsync"/></para>
    /// <para>Life cycle</para>
    /// <para>- <see cref="TransitionParams.Awaked"/></para>
    /// <para>- <see cref="TransitionParams.Start"/></para>
    /// <para>- <see cref="TransitionParams.Update"/></para>
    /// <para>- <see cref="TransitionParams.LateUpdate"/></para>
    /// <para>- <see cref="TransitionParams.Canceled"/></para>
    /// <para>- <see cref="TransitionParams.Completed"/></para>
    /// </summary>
    public sealed class TransitionParams : ICloneable
    {
        public TransitionParams() { }
        public TransitionParams(Action<TransitionParams>? action)
        {
            action?.Invoke(this);
        }

        public const int MAX_FPS = 165;
        public const int MIN_FPS = 1;

        public static int DefaultFrameRate { get; set; } = 60;
        public static DispatcherPriority DefaultPriority { get; set; } = DispatcherPriority.Render;
        public static TransitionParams Theme { get; set; } = new()
        {
            FrameRate = DefaultFrameRate,
            Duration = 0.5
        };
        public static TransitionParams Hover { get; set; } = new()
        {
            FrameRate = DefaultFrameRate,
            Duration = 0.3
        };
        public static TransitionParams Empty { get; } = new()
        {
            Duration = 0
        };

        public double DeltaTime => 1000.0 / XMath.Clamp(FrameRate, MIN_FPS, MAX_FPS);
        public double FrameCount => XMath.Clamp(Duration * XMath.Clamp(FrameRate, MIN_FPS, MAX_FPS), 1, int.MaxValue);

        private WeakDelegate<EventHandler<FrameEventArgs>> _awaked = new();
        private WeakDelegate<EventHandler<FrameEventArgs>> _start = new();
        private WeakDelegate<EventHandler<FrameEventArgs>> _update = new();
        private WeakDelegate<EventHandler<FrameEventArgs>> _lateupdate = new();
        private WeakDelegate<EventHandler<FrameEventArgs>> _cancled = new();
        private WeakDelegate<EventHandler<FrameEventArgs>> _completed = new();
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

        public bool IsAutoReverse { get; set; } = false;
        public int LoopTime { get; set; } = 0;
        public double Duration { get; set; } = 0;
        public int FrameRate { get; set; } = DefaultFrameRate;
        public double Acceleration { get; set; } = 0;
        public DispatcherPriority Priority { get; set; } = DefaultPriority;
        public bool IsAsync { get; set; } = false;

        internal TransitionParams DeepCopy()
        {
            var copy = new TransitionParams
            {
                _awaked = _awaked,
                _start = _start,
                _update = _update,
                _lateupdate = _lateupdate,
                _cancled = _cancled,
                _completed = _completed,
                IsAutoReverse = IsAutoReverse,
                LoopTime = LoopTime,
                Duration = Duration,
                FrameRate = FrameRate,
                Acceleration = Acceleration,
                Priority = Priority,
                IsAsync = IsAsync
            };
            return copy;
        }

        internal void AwakeInvoke(object sender, FrameEventArgs e)
        {
            var handlers = _awaked.GetInvocationList();
            handlers?.Invoke(sender, e);
        }
        internal void StartInvoke(object sender, FrameEventArgs e)
        {
            var handlers = _start.GetInvocationList();
            handlers?.Invoke(sender, e);
        }
        internal void UpdateInvoke(object sender, FrameEventArgs e)
        {
            var handlers = _update.GetInvocationList();
            handlers?.Invoke(sender, e);
        }
        internal void LateUpdateInvoke(object sender, FrameEventArgs e)
        {
            var handlers = _lateupdate.GetInvocationList();
            handlers?.Invoke(sender, e);
        }
        internal void CompletedInvoke(object sender, FrameEventArgs e)
        {
            var handlers = _completed.GetInvocationList();
            handlers?.Invoke(sender, e);
        }
        internal void CancledInvoke(object sender, FrameEventArgs e)
        {
            var handlers = _cancled.GetInvocationList();
            handlers?.Invoke(sender, e);
        }

        public object Clone()
        {
            return DeepCopy();
        }
    }
}
