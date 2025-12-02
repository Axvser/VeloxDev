using System.Threading;
using System.Threading.Tasks;
using VeloxDev.Core.MVVM;

namespace Demo.ViewModels;

/* 不需要继承任何类，也不需要显示声明接口 */
/* 提示：你可以继承其它类，但是请不要与 MVVM 相关，因为此工具集已经生成了完整的 MVVM 支持，需要避免与其它工具产生冲突 */
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

    /* 无阻中断 */
    private void FreeCommand()
    {
        MinusCommand.Lock();   // 进入锁定状态，阻止新的命令触发但不会中断当前执行中的命令

        MinusCommand.Interrupt();    // 中断当前命令
        MinusCommand.Clear();        // 中断当前命令和正在排队的所有命令

        MinusCommand.UnLock(); // 解除锁定
    }

    /* 可等待中断 */
    private async Task FreeCommandAsync()
    {
        MinusCommand.Lock();   // 进入锁定状态，阻止新的命令触发但不会中断当前执行中的命令

        await MinusCommand.InterruptAsync();    // 中断当前命令
        await MinusCommand.ClearAsync(); // 中断当前命令和正在排队的所有命令

        MinusCommand.UnLock(); // 解除锁定
    }
}