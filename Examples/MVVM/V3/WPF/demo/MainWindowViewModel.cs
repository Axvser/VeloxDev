using VeloxDev.Core.MVVM;

namespace Demo;

public partial class MainWindowViewModel
{
    /* 快速生成你的属性 */
    [VeloxProperty] private int _index = 0;
    [VeloxProperty] private string _greeting = $"current index: 0";

    /* 属性回调 */
    partial void OnIndexChanged(int oldValue, int newValue)
    {
        MinusCommand.Notify(); // 通知 MinusCommand 的可执行性需要更新
    }

    /* 一个默认的 Command，名字自动截取，无可用性验证，排队执行 */
    [VeloxCommand(name: "Auto", canValidate: false, semaphore: 1)]
    private Task Plus(object? sender, CancellationToken ct)
    {
        Index++;
        Greeting = $"current index: {Index}";
        return Task.CompletedTask;
    }

    /* 开启可用性验证 */
    [VeloxCommand(canValidate: true)]
    private Task Minus(object? sender, CancellationToken ct)
    {
        Index--;
        Greeting = $"current index: {Index}";
        return Task.CompletedTask;
    }
    /* 此时必须实现此分部方法 */
    private partial bool CanExecuteMinusCommand(object? parameter)
    {
        return _index > 0;
    }

    /* 同步中断 */
    private void FreeCommand()
    {
        MinusCommand.Lock();      // 进入锁定状态，阻止新的命令触发但不会中断当前执行中的命令
        
        MinusCommand.Clear();     // 清理当前命令和正在排队的所有命令
        MinusCommand.Interrupt(); // 中断当前命令，保留排队中的任务
        MinusCommand.Continue();  // 尝试继续执行排队中的任务

        MinusCommand.UnLock();    // 解除锁定
    }
    
    /* 异步中断 */
    private async Task FreeCommandAsync()
    {
        await MinusCommand.LockAsync();      // 进入锁定状态，阻止新的命令触发但不会中断当前执行中的命令

        await MinusCommand.ClearAsync();     // 清理当前命令和正在排队的所有命令
        await MinusCommand.InterruptAsync(); // 中断当前命令，保留排队中的任务
        await MinusCommand.ContinueAsync();  // 尝试继续执行排队中的任务

        await MinusCommand.UnLockAsync();    // 解除锁定
    }
}