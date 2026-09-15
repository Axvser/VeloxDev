using VeloxDev.TimeLine;

namespace VeloxDev.TransitionSystem.Abstractions;

/// <summary>Reports a run's degraded and failed stages: a Debug line, and an event when someone is listening.</summary>
/// <remarks>
/// 两个通道都不向调用方抛异常。每个 stage 每个实例最多报一次——它们大多是每帧都会发生的事。
/// </remarks>
internal sealed class TransitionDiagnostics(ITransitionEffectCore effect, object target, TimeLineEventArgs? run = null)
{
    private HashSet<string>? _warned;
    private HashSet<string>? _reported;

    /// <summary>Reports an escaped exception. False when this stage was already reported.</summary>
    public bool Error(string stage, Exception exception)
    {
        _reported ??= [];
        if (!_reported.Add(stage)) return false;

        Raise(new TransitionEventArgs
        {
            Stage = stage,
            Message = exception.Message,
            Exception = exception,
        }, isError: true);
        return true;
    }

    /// <summary>Reports a stage the run degrades through and carries on from.</summary>
    public void Warn(string stage, string message)
    {
        _warned ??= [];
        if (!_warned.Add(stage)) return;

        Raise(new TransitionEventArgs { Stage = stage, Message = message }, isError: false);
    }

    private void Raise(TransitionEventArgs args, bool isError)
    {
        if (isError) effect.InvokeError(target, args);
        else effect.InvokeWarn(target, args);

        // 处理器把 Handled 置起来，就是要求终止这一趟。
        if (args.Handled && run is not null) run.Handled = true;
    }
}
