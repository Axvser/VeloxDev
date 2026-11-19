using VeloxDev.MVVM;

namespace VeloxDev.Core.Test.MVVM;

/// <summary>
/// Every cell of the command signature matrix, executed.
/// <para>
/// Generation is asserted by compilation: the fixtures name every generated member, so a cell that stopped being
/// generated would not build. These tests cover the other half — that each one runs, and that the ones with a
/// return value hand it back.
/// </para>
/// </summary>
[TestClass]
public class CommandMatrixTests
{
    [TestMethod]
    public async Task EveryParameterShape_RunsExactlyOnce()
    {
        var vm = new CommandMatrixViewModel();

        foreach (var cell in vm.Cases)
        {
            await RunToCompletionAsync(cell.Command, cell.Parameter);
        }

        CollectionAssert.AreEquivalent(
            vm.Cases.Select(static cell => cell.Id).ToArray(),
            vm.Ran,
            "every generated command has to reach its body exactly once");
    }

    [TestMethod]
    public async Task EveryMethodTypeParameterShape_RunsExactlyOnce()
    {
        // 泛型方法的命令只能经访问器拿，所以这一格一格格点名。
        var vm = new CommandMatrixGenericMethodViewModel();

        IVeloxCommand[] commands =
        [
            vm.GetVoidValueCommand<string>(), vm.GetTaskValueCommand<string>(),
            vm.GetTaskValueTokenCommand<string>(), vm.GetTaskOfTValueCommand<string>(),
            vm.GetTaskOfTValueTokenCommand<string>(), vm.GetValueTaskValueCommand<string>(),
            vm.GetValueTaskValueTokenCommand<string>(), vm.GetValueTaskOfTValueCommand<string>(),
            vm.GetValueTaskOfTValueTokenCommand<string>(),
        ];

        foreach (var command in commands)
        {
            await RunToCompletionAsync(command, "payload");
        }

        Assert.HasCount(9, vm.Ran, "every closed accessor has to run its own body");
    }

    [TestMethod]
    public async Task EveryClassTypeParameterShape_RunsExactlyOnce()
    {
        var vm = new CommandMatrixClassViewModel<string>();

        IVeloxCommand[] commands =
        [
            vm.VoidValueCommand, vm.TaskValueCommand, vm.TaskValueTokenCommand,
            vm.TaskOfTValueCommand, vm.TaskOfTValueTokenCommand, vm.ValueTaskValueCommand,
            vm.ValueTaskValueTokenCommand, vm.ValueTaskOfTValueCommand, vm.ValueTaskOfTValueTokenCommand,
        ];

        foreach (var command in commands)
        {
            await RunToCompletionAsync(command, "payload");
        }

        Assert.HasCount(9, vm.Ran, "the class's type parameter must reach every generated property");
    }

    [TestMethod]
    public async Task AGenericAccessor_GivesEachClosedTypeItsOwnCommand()
    {
        var vm = new CommandMatrixGenericMethodViewModel();

        var forString = vm.GetTaskValueCommand<string>();
        var forUri = vm.GetTaskValueCommand<Uri>();

        Assert.AreNotSame((object)forString, (object)forUri,
            "each closed type argument owns its own queue, lock and body closure");

        await RunToCompletionAsync(forString, "a");
        await RunToCompletionAsync(forUri, new Uri("https://example.invalid/"));

        Assert.HasCount(2, vm.Ran);
    }

    [TestMethod]
    public async Task ATypedValueReturningCommand_DeliversTheSameValueAsTheBoxedChannel()
    {
        var vm = new CommandMatrixViewModel();
        IVeloxCommand<MatrixPayload, int> typed = vm.TaskOfTConcreteCommand;
        var payload = new MatrixPayload(3);

        var typedValue = await typed.ExecuteAsync(payload, CancellationToken.None);

        // 装箱通道不在 IVeloxCommand<TP,TR> 上（2-arity 不继承 IVeloxCommandResult），要强转到它。
        var boxedValue = await ((IVeloxCommandResult)typed).ExecuteAsync(payload, CancellationToken.None);

        Assert.AreEqual(boxedValue, typedValue,
            "the typed channel and the boxed one must be the same value from the same body");
        Assert.AreNotEqual(0, typedValue, "and it must be the value the body returned, not a default");
    }

    [TestMethod]
    public async Task AValueTaskReturningCommand_DeliversItsValue()
    {
        var vm = new CommandMatrixViewModel();
        IVeloxCommand<MatrixPayload, int> typed = vm.ValueTaskOfTConcreteCommand;

        var value = await typed.ExecuteAsync(new MatrixPayload(3), CancellationToken.None);

        Assert.AreNotEqual(0, value);
    }

    [TestMethod]
    public void TheSynchronousExecuteWithOut_DeliversTheSameValueAsTheAwait()
    {
        var vm = new CommandMatrixViewModel();
        IVeloxCommand<MatrixPayload, int> typed = vm.TaskOfTConcreteCommand;
        var payload = new MatrixPayload(3);

        typed.Execute(payload, out int fromOut);

        Assert.AreNotEqual(0, fromOut);
        Assert.AreEqual(typed.ExecuteAsync(payload, CancellationToken.None).GetAwaiter().GetResult(), fromOut,
            "the blocking entry point and the awaitable one must agree");
    }

    [TestMethod]
    public async Task ACommandWithoutAReturnValue_YieldsNull()
    {
        var vm = new CommandMatrixViewModel();

        Assert.IsNull(await vm.TaskConcreteCommand.ExecuteAsync(new MatrixPayload(1), CancellationToken.None));

        // 零形参的命令是非强类型的，属性是裸 IVeloxCommand —— 结果通道要经独立接口取。
        Assert.IsNull(await ((IVeloxCommandResult)vm.VoidNoneCommand).ExecuteAsync(null, CancellationToken.None));
    }

    [TestMethod]
    public async Task AFailedBody_ThrowsTheSameExceptionInstance()
    {
        var boom = new InvalidTimeZoneException("matrix");
        var command = new VeloxCommand<string, int>((_, _) => Task.FromException<int>(boom));

        var thrown = await Assert.ThrowsAsync<InvalidTimeZoneException>(
            () => command.ExecuteAsync("x", CancellationToken.None));

        Assert.AreSame(boom, thrown, "the original instance must be rethrown, not wrapped");
    }

    [TestMethod]
    public async Task ARefusedCall_ThrowsInsteadOfReturningDefault()
    {
        var command = new VeloxCommand<string, int>((_, _) => Task.FromResult(1));
        command.Lock();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => command.ExecuteAsync("x", CancellationToken.None));
    }

    [TestMethod]
    public async Task AnInterruptedExecution_ThrowsOperationCanceled()
    {
        // 矩阵里的命令体都是立刻返回的，打断不了 —— 这里用一个真正停在 token 上的体。
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new VeloxCommand<string, int>(async (value, ct) =>
        {
            _ = value;
            started.TrySetResult(true);
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        });

        var wait = command.ExecuteAsync("x", CancellationToken.None);
        await started.Task.WaitAsync(CommandTestKit.Timeout);
        await command.InterruptAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => wait);
    }

    private static async Task RunToCompletionAsync(IVeloxCommand command, object? parameter)
    {
        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        command.Exited += _ => exited.TrySetResult(true);

        await command.ExecuteAsync(parameter);
        await exited.Task.WaitAsync(CommandTestKit.Timeout);
    }
}
