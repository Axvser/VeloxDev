using VeloxDev.MVVM;

namespace VeloxDev.Core.Test.MVVM;

/// <summary>
/// The <c>canValidate</c> hook, across every parameter shape, and the attribute's other knobs.
/// <para>
/// Generation is asserted by compilation — each fixture names its own generated member, so a shape that stopped
/// being generated would not build. These tests cover the behaviour: that the validator is actually consulted,
/// and that the knobs reach the generated command.
/// </para>
/// </summary>
[TestClass]
public class CommandValidatorTests
{
    [TestMethod]
    public async Task EveryValidatorShape_IsGeneratedAndConsulted()
    {
        var vm = new CommandValidatorMatrixViewModel();
        var payload = new MatrixPayload(1);

        // 每一格：(命令, 实参)。生成由编译保证，这里断言校验器真的被问过。
        (IVeloxCommand Command, object? Parameter)[] cells =
        [
            (vm.NoneCommand, null),
            (vm.TokenOnlyCommand, null),
            (vm.ObjectOnlyCommand, "boxed"),
            (vm.ObjectAndTokenCommand, "boxed"),
            (vm.ConcreteCommand, payload),
            (vm.ConcreteAndTokenCommand, payload),
            (vm.ConcreteWithValueCommand, payload),
        ];

        foreach (var (command, parameter) in cells)
        {
            Assert.IsTrue(command.CanExecute(parameter), "the validator allows when Allow is true");
        }

        vm.Allow = false;

        foreach (var (command, parameter) in cells)
        {
            Assert.IsFalse(command.CanExecute(parameter), "and it is consulted, not bypassed");
        }

        // 允许之后每一格都真的跑得起来。
        vm.Allow = true;

        foreach (var (command, parameter) in cells)
        {
            await RunToCompletionAsync(command, parameter);
        }

        Assert.HasCount(cells.Length, vm.Ran, "every shape has to reach its body exactly once");
    }

    [TestMethod]
    public void AValidatorOnAConcreteParameter_SeesTheTypedArgument()
    {
        var vm = new CommandValidatorMatrixViewModel();

        Assert.IsTrue(vm.ConcreteCommand.CanExecute(new MatrixPayload(1)));
        Assert.IsFalse(vm.ConcreteCommand.CanExecute(new MatrixPayload(0)),
            "the rejection comes from the payload, not from Allow");
    }

    [TestMethod]
    public void AValidatorOnAValueReturningCommand_IsStillConsulted()
    {
        var vm = new CommandValidatorMatrixViewModel();
        IVeloxCommand<MatrixPayload, int> typed = vm.ConcreteWithValueCommand;

        Assert.IsTrue(typed.CanExecute(new MatrixPayload(2)));
        Assert.IsFalse(typed.CanExecute(new MatrixPayload(0)));
    }

    [TestMethod]
    public async Task AValidatorOnAClassTypeParameter_IsConsulted()
    {
        var vm = new CommandValidatorClassViewModel<string>();

        Assert.IsTrue(vm.AcceptCommand.CanExecute("value"));
        Assert.IsFalse(vm.AcceptCommand.CanExecute(null!));

        await RunToCompletionAsync(vm.AcceptCommand, "value");
        Assert.HasCount(1, vm.Ran);
    }

    [TestMethod]
    public async Task AValidatorOnAGenericMethodWithAToken_IsConsulted()
    {
        var vm = new CommandValidatorGenericMethodViewModel();

        Assert.IsTrue(vm.GetStoreWithTokenCommand<string>().CanExecute("value"));
        Assert.IsFalse(vm.GetStoreWithTokenCommand<string>().CanExecute(null!));

        await RunToCompletionAsync(vm.GetStoreWithTokenCommand<string>(), "value");
        Assert.HasCount(1, vm.Ran);
    }

    [TestMethod]
    public async Task AnExplicitName_ReplacesTheAutoDerivedOne()
    {
        var vm = new CommandValidatorMatrixViewModel();

        // 生成的是 RenamedCommand；按方法名推导的 OriginalNameCommand 根本不存在，能编译就是断言。
        await RunToCompletionAsync(vm.RenamedCommand, null);

        Assert.HasCount(1, vm.Ran);
        Assert.AreEqual("OriginalName", vm.Ran[0],
            "the command is named by the attribute, the body by the method");
    }

    [TestMethod]
    public async Task ASemaphoreOfTwo_LetsTwoExecutionsRunAtOnce()
    {
        var vm = new CommandValidatorMatrixViewModel();

        _ = vm.ConcurrentCommand.ExecuteAsync(null);
        _ = vm.ConcurrentCommand.ExecuteAsync(null);

        // 两条都起来了才可能完成 —— 容量为 1 时第二条会排队，这个 await 会超时。
        await vm.BothConcurrentStarted.WaitAsync(CommandTestKit.Timeout);

        vm.ReleaseConcurrent();
    }

    private static async Task RunToCompletionAsync(IVeloxCommand command, object? parameter)
    {
        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        command.Exited += _ => exited.TrySetResult(true);

        await command.ExecuteAsync(parameter);
        await exited.Task.WaitAsync(CommandTestKit.Timeout);
    }
}
