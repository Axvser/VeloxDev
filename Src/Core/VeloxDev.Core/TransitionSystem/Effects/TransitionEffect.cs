using System.Diagnostics;
using VeloxDev.Threading;
using VeloxDev.WeakTypes;

namespace VeloxDev.TransitionSystem.Abstractions;

/// <summary>The transition effect skeleton, parameterized by its priority type.</summary>
public class TransitionEffectCore<TPriorityCore> : TransitionEffectCore, ITransitionEffect<TPriorityCore>
{
    /// <inheritdoc />
    public virtual TPriorityCore Priority { get; set; } = default!;

    /// <summary>Returns a copy of this effect.</summary>
    public new ITransitionEffect<TPriorityCore> Clone()
    {
        var copy = new TransitionEffectCore<TPriorityCore>()
        {
            _awaked = _awaked.Clone(),
            _start = _start.Clone(),
            _update = _update.Clone(),
            _lateupdate = _lateupdate.Clone(),
            _cancled = _cancled.Clone(),
            _completed = _completed.Clone(),
            _finally = _finally.Clone(),
            _warn = _warn.Clone(),
            _error = _error.Clone(),
            IsAutoReverse = IsAutoReverse,
            LoopTime = LoopTime,
            Duration = Duration,
            FPS = FPS,
            Ease = Ease,
            Priority = Priority,
        };
        return copy;
    }
}

/// <summary>The default transition effect, carrying no priority.</summary>
public class TransitionEffectCore : ITransitionEffectCore, ITransitionEffect<NonPriority>
{
    /// <summary>Always <c>default</c>: this effect carries no priority.</summary>
    NonPriority ITransitionEffect<NonPriority>.Priority { get; set; }

    ITransitionEffect<NonPriority> ITransitionEffect<NonPriority>.Clone() => (ITransitionEffect<NonPriority>)Clone();

    /// <summary>The handlers behind <see cref="Awaked"/>.</summary>
    protected WeakDelegate<EventHandler<TransitionEventArgs>> _awaked = new();
    /// <summary>The handlers behind <see cref="Start"/>.</summary>
    protected WeakDelegate<EventHandler<TransitionEventArgs>> _start = new();
    /// <summary>The handlers behind <see cref="Update"/>.</summary>
    protected WeakDelegate<EventHandler<TransitionEventArgs>> _update = new();
    /// <summary>The handlers behind <see cref="LateUpdate"/>.</summary>
    protected WeakDelegate<EventHandler<TransitionEventArgs>> _lateupdate = new();
    /// <summary>The handlers behind <see cref="Canceled"/>.</summary>
    protected WeakDelegate<EventHandler<TransitionEventArgs>> _cancled = new();
    /// <summary>The handlers behind <see cref="Completed"/>.</summary>
    protected WeakDelegate<EventHandler<TransitionEventArgs>> _completed = new();
    /// <summary>The handlers behind <see cref="Finally"/>.</summary>
    protected WeakDelegate<EventHandler<TransitionEventArgs>> _finally = new();
    /// <summary>The handlers behind <see cref="Warn"/>.</summary>
    protected WeakDelegate<EventHandler<TransitionEventArgs<WarnStage, string>>> _warn = new();
    /// <summary>The handlers behind <see cref="Error"/>.</summary>
    protected WeakDelegate<EventHandler<TransitionEventArgs<ErrorStage, Exception>>> _error = new();

    /// <inheritdoc />
    public virtual int FPS { get; set; } = 60;

    /// <inheritdoc />
    public virtual TimeSpan Duration { get; set; } = TimeSpan.FromMilliseconds(0);

    /// <inheritdoc />
    public virtual bool IsAutoReverse { get; set; } = false;

    /// <inheritdoc />
    public virtual int LoopTime { get; set; } = 0;

    /// <inheritdoc />
    public virtual IEaseCalculator Ease { get; set; } = Eases.Default;

    /// <inheritdoc />
    public virtual event EventHandler<TransitionEventArgs> Awaked
    {
        add => _awaked.AddHandler(value);
        remove => _awaked.RemoveHandler(value);
    }
    /// <inheritdoc />
    public virtual event EventHandler<TransitionEventArgs> Start
    {
        add => _start.AddHandler(value);
        remove => _start.RemoveHandler(value);
    }
    /// <inheritdoc />
    public virtual event EventHandler<TransitionEventArgs> Update
    {
        add => _update.AddHandler(value);
        remove => _update.RemoveHandler(value);
    }
    /// <inheritdoc />
    public virtual event EventHandler<TransitionEventArgs> LateUpdate
    {
        add => _lateupdate.AddHandler(value);
        remove => _lateupdate.RemoveHandler(value);
    }
    /// <inheritdoc />
    public virtual event EventHandler<TransitionEventArgs> Canceled
    {
        add => _cancled.AddHandler(value);
        remove => _cancled.RemoveHandler(value);
    }
    /// <inheritdoc />
    public virtual event EventHandler<TransitionEventArgs> Completed
    {
        add => _completed.AddHandler(value);
        remove => _completed.RemoveHandler(value);
    }
    /// <inheritdoc />
    public virtual event EventHandler<TransitionEventArgs> Finally
    {
        add => _finally.AddHandler(value);
        remove => _finally.RemoveHandler(value);
    }
    /// <inheritdoc />
    public virtual event EventHandler<TransitionEventArgs<WarnStage, string>> Warn
    {
        add => _warn.AddHandler(value);
        remove => _warn.RemoveHandler(value);
    }
    /// <inheritdoc />
    public virtual event EventHandler<TransitionEventArgs<ErrorStage, Exception>> Error
    {
        add => _error.AddHandler(value);
        remove => _error.RemoveHandler(value);
    }

    /// <inheritdoc />
    public virtual void InvokeAwake(object sender, TransitionEventArgs e)
    {
        _awaked.GetInvocationList()?.Invoke(sender, e);
    }
    /// <inheritdoc />
    public virtual void InvokeStart(object sender, TransitionEventArgs e)
    {
        _start.GetInvocationList()?.Invoke(sender, e);
    }
    /// <inheritdoc />
    public virtual void InvokeUpdate(object sender, TransitionEventArgs e)
    {
        _update.GetInvocationList()?.Invoke(sender, e);
    }
    /// <inheritdoc />
    public virtual void InvokeLateUpdate(object sender, TransitionEventArgs e)
    {
        _lateupdate.GetInvocationList()?.Invoke(sender, e);
    }
    /// <inheritdoc />
    public virtual void InvokeCompleted(object sender, TransitionEventArgs e)
    {
        _completed.GetInvocationList()?.Invoke(sender, e);
    }
    /// <inheritdoc />
    public virtual void InvokeCancled(object sender, TransitionEventArgs e)
    {
        _cancled.GetInvocationList()?.Invoke(sender, e);
    }
    /// <inheritdoc />
    public virtual void InvokeFinally(object sender, TransitionEventArgs e)
    {
        _finally.GetInvocationList()?.Invoke(sender, e);
    }

    /// <inheritdoc />
    public virtual void InvokeWarn(object sender, TransitionEventArgs<WarnStage, string> e)
    {
        Debug.WriteLine($"[VeloxDev.Transition] warn @{e.Stage}: {e.Value}");
        _warn.GetInvocationList()?.Invoke(sender, e);
    }

    /// <inheritdoc />
    public virtual void InvokeError(object sender, TransitionEventArgs<ErrorStage, Exception> e)
    {
        // 不用 Debug.Fail：它在无交互宿主里会直接终止进程，而那正是这条通道要防的事。
        Debug.WriteLine($"[VeloxDev.Transition] error @{e.Stage}: {e.Value}");
        _error.GetInvocationList()?.Invoke(sender, e);
    }

    /// <inheritdoc />
    public ITransitionEffectCore Clone()
    {
        var copy = new TransitionEffectCore()
        {
            _awaked = _awaked.Clone(),
            _start = _start.Clone(),
            _update = _update.Clone(),
            _lateupdate = _lateupdate.Clone(),
            _cancled = _cancled.Clone(),
            _completed = _completed.Clone(),
            _finally = _finally.Clone(),
            _warn = _warn.Clone(),
            _error = _error.Clone(),
            IsAutoReverse = IsAutoReverse,
            LoopTime = LoopTime,
            Duration = Duration,
            FPS = FPS,
            Ease = Ease
        };
        return copy;
    }
}
