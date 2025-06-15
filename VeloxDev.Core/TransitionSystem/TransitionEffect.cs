using VeloxDev.Core.Interfaces.TransitionSystem;
using VeloxDev.Core.WeakTypes;

namespace VeloxDev.Core.TransitionSystem
{
    /// <summary>
    /// <para>---</para>
    /// ✨ ⌈ 核心 ⌋ 过渡解释器
    /// <para>解释 : </para>
    /// <para>在不同平台实现过渡系统时，您仅需一个此核心的具体实现就能用于描述过渡效果的细节</para>
    /// </summary>
    /// <typeparam name="TTransitionEffectCore">您在具体框架对TransitionEffectCore的实现类</typeparam>
    /// <typeparam name="TPriorityCore">在不同框架中，使用不同的结构来表示UI更新操作的优先级</typeparam>
    public abstract class TransitionEffectCore<TTransitionEffectCore, TPriorityCore> : TransitionEffectCore, ITransitionEffect<TTransitionEffectCore, TPriorityCore>
        where TTransitionEffectCore : TransitionEffectCore<TTransitionEffectCore, TPriorityCore>, new()
    {
#pragma warning disable CS8618
        public virtual TPriorityCore Priority { get; set; }
#pragma warning restore CS8618

        public TTransitionEffectCore Clone()
        {
            var copy = new TTransitionEffectCore()
            {
                _awaked = _awaked.Clone(),
                _start = _start.Clone(),
                _update = _update.Clone(),
                _lateupdate = _lateupdate.Clone(),
                _cancled = _cancled.Clone(),
                _completed = _completed.Clone(),
                _finally = _finally.Clone(),
                IsAutoReverse = IsAutoReverse,
                LoopTime = LoopTime,
                Duration = Duration,
                FPS = FPS,
                EaseCalculator = EaseCalculator,
                Priority = Priority,
            };
            return copy;
        }
    }

    /// <summary>
    /// <para>---</para>
    /// ✨ ⌈ 核心 ⌋ 过渡解释器
    /// <para>解释 : </para>
    /// <para>在不同平台实现过渡系统时，您仅需一个此核心的具体实现就能用于描述过渡效果的细节</para>
    /// </summary>
    /// <typeparam name="TTransitionEffectCore">您在具体框架对TransitionEffectCore的实现类</typeparam>
    public abstract class TransitionEffectCore<TTransitionEffectCore> : TransitionEffectCore, ITransitionEffect<TTransitionEffectCore>
        where TTransitionEffectCore : TransitionEffectCore<TTransitionEffectCore>, new()
    {
        public TTransitionEffectCore Clone()
        {
            var copy = new TTransitionEffectCore()
            {
                _awaked = _awaked.Clone(),
                _start = _start.Clone(),
                _update = _update.Clone(),
                _lateupdate = _lateupdate.Clone(),
                _cancled = _cancled.Clone(),
                _completed = _completed.Clone(),
                _finally = _finally.Clone(),
                IsAutoReverse = IsAutoReverse,
                LoopTime = LoopTime,
                Duration = Duration,
                FPS = FPS,
                EaseCalculator = EaseCalculator
            };
            return copy;
        }
    }

    /// <summary>
    /// <para>---</para>
    /// ✨ ⌈ 核心 ⌋ 过渡解释器
    /// <para>解释 : </para>
    /// <para>在不同平台实现过渡系统时，您仅需一个此核心的具体实现就能用于描述过渡效果的细节</para>
    /// </summary>
    public abstract class TransitionEffectCore : ITransitionEffectCore
    {
        protected WeakDelegate<EventHandler<FrameEventArgs>> _awaked = new();
        protected WeakDelegate<EventHandler<FrameEventArgs>> _start = new();
        protected WeakDelegate<EventHandler<FrameEventArgs>> _update = new();
        protected WeakDelegate<EventHandler<FrameEventArgs>> _lateupdate = new();
        protected WeakDelegate<EventHandler<FrameEventArgs>> _cancled = new();
        protected WeakDelegate<EventHandler<FrameEventArgs>> _completed = new();
        protected WeakDelegate<EventHandler<FrameEventArgs>> _finally = new();

        public virtual int FPS { get; set; } = 60;
        public virtual TimeSpan Duration { get; set; } = TimeSpan.FromMilliseconds(0);
        public virtual bool IsAutoReverse { get; set; } = false;
        public virtual int LoopTime { get; set; } = 0;
        public virtual IEaseCalculator EaseCalculator { get; set; } = Eases.Default;


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
    }
}
