namespace VeloxDev.TransitionSystem.Abstractions;

// 汇报一趟运行里降级与失败的 stage：写一行 Debug，并在有人监听时发一个事件。
// 两个通道都不向调用方抛异常。每个 stage 每个实例最多报一次——它们大多是每帧都会发生的事。
internal sealed class TransitionDiagnostics(ITransitionEffectCore effect, object target, TransitionEventArgs? run = null)
{
    private HashSet<WarnStage>? _warned;
    private HashSet<ErrorStage>? _reported;

    // 汇报一个逃逸出来的异常。该 stage 已经报过时返回 false。
    public bool Error(ErrorStage stage, Exception exception)
    {
        _reported ??= [];
        if (!_reported.Add(stage)) return false;

        Raise(new TransitionEventArgs<ErrorStage, Exception> { Stage = stage, Value = exception }, effect.InvokeError);
        return true;
    }

    // 汇报一个运行降级后继续的 stage。
    public void Warn(WarnStage stage, string message)
    {
        _warned ??= [];
        if (!_warned.Add(stage)) return;

        Raise(new TransitionEventArgs<WarnStage, string> { Stage = stage, Value = message }, effect.InvokeWarn);
    }

    private void Raise<TArgs>(TArgs args, Action<object, TArgs> invoke) where TArgs : TransitionEventArgs
    {
        // 先把这一趟的位置抄上去，处理器才看得到。诊断是替解释器发的，手上那个 run 就是它的 Args 实例。
        if (run is not null)
        {
            args.Loop = run.Loop;
            args.Cycle = run.Cycle;
        }

        invoke(target, args);

        // 处理器把 Handled 置起来，就是要求终止这一趟。
        if (args.Handled && run is not null) run.Handled = true;
    }
}
