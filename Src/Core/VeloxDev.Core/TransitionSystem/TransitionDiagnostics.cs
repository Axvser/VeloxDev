using VeloxDev.TimeLine;

namespace VeloxDev.TransitionSystem.Abstractions;

// 汇报一趟运行里降级与失败的 stage：写一行 Debug，并在有人监听时发一个事件。
// 两个通道都不向调用方抛异常。每个 stage 每个实例最多报一次——它们大多是每帧都会发生的事。
internal sealed class TransitionDiagnostics(ITransitionEffectCore effect, object target, TimeLineEventArgs? run = null)
{
    private HashSet<string>? _warned;
    private HashSet<string>? _reported;

    // 汇报一个逃逸出来的异常。该 stage 已经报过时返回 false。
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

    // 汇报一个运行降级后继续的 stage。
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
