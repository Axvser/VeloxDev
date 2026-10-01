using VeloxDev.MVVM;

namespace VeloxDev.Core.Test.MVVM;

/// <summary>
/// Every signature the attribute documents must produce a command that compiles AND runs. This exercises the
/// generator end to end — the generated members are compiled into this assembly and then executed — rather
/// than asserting on generated text, which would pass even if the emitted code did not compile.
/// </summary>
[TestClass]
public class CommandSignatureTests
{
    [TestMethod]
    public async Task EveryDocumentedSignature_ProducesARunnableCommand()
    {
        var vm = new CommandSignatureViewModel();

        foreach (var command in AllCommands(vm))
        {
            await RunToCompletionAsync(command);
        }

        CollectionAssert.AreEquivalent(
            new[]
            {
                "BothAsync", "ParameterOnlyAsync", "TokenOnlyAsync", "NothingAsync", "TaskOfTAsync",
                "VoidParameter", "VoidNothing",
                "VtBothAsync", "VtParameterOnlyAsync", "VtTokenOnlyAsync", "VtNothingAsync", "VtOfTAsync",
            },
            vm.Ran,
            "every generated command has to reach its body exactly once");
    }

    [TestMethod]
    public void EveryDocumentedSignature_ProducesANonNullCommand()
    {
        var vm = new CommandSignatureViewModel();

        Assert.IsTrue(AllCommands(vm).All(c => c is not null));
    }

    [TestMethod]
    public async Task AValueTaskBody_ReallyReceivesTheCancellationToken()
    {
        var vm = new CancellableValueTaskViewModel();

        _ = vm.WorkCommand.ExecuteAsync(null);
        await vm.Started.Task.WaitAsync(CommandTestKit.Timeout);

        await vm.WorkCommand.InterruptAsync();

        Assert.IsTrue(await vm.CancellationObserved.Task.WaitAsync(CommandTestKit.Timeout),
            "a ValueTask body must still be cancellable - accepting the token is the whole point");
    }

    [TestMethod]
    public async Task ANestedClassInsideAGenericOuterClass_StillGenerates()
    {
        var inner = new GenericOuter<int>.Inner();

        await RunToCompletionAsync(inner.RunCommand);

        Assert.IsTrue(inner.Ran, "the outer class's type parameter must survive into the generated partial");
    }

    [TestMethod]
    public async Task AViewModelWithoutANamespace_StillGenerates()
    {
        // 全局命名空间曾让生成器整个崩掉（见该视图模型文件顶部的说明）。
        var vm = new GlobalNamespaceCommandViewModel();

        await RunToCompletionAsync(vm.RunCommand);

        Assert.IsTrue(vm.Ran, "a class in the global namespace must generate a working command");
    }

    [TestMethod]
    public async Task AParameterOnlyBody_GetsNoCancellationTokenSource_ForEitherReturnType()
    {
        var vm = new CommandSignatureViewModel();

        var fromTask = await CaptureStartedSourceAsync(vm.ParameterOnlyCommand);
        var fromValueTask = await CaptureStartedSourceAsync(vm.VtParameterOnlyCommand);

        Assert.IsNull(fromTask, "a body that cannot observe a token needs no source");
        Assert.IsNull(fromValueTask,
            "the ValueTask form must match the Task form - its thunk is a one-parameter lambda precisely so that it binds the same entry point");
    }

    private static async Task<CancellationTokenSource?> CaptureStartedSourceAsync(IVeloxCommand command)
    {
        CancellationTokenSource? captured = null;
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        command.Started += e =>
        {
            captured = e.Cts;
            started.TrySetResult(true);
        };

        _ = command.ExecuteAsync(null);
        await started.Task.WaitAsync(CommandTestKit.Timeout);

        return captured;
    }

    private static IVeloxCommand[] AllCommands(CommandSignatureViewModel vm) =>
    [
        vm.BothCommand, vm.ParameterOnlyCommand, vm.TokenOnlyCommand, vm.NothingCommand, vm.TaskOfTCommand,
        vm.VoidParameterCommand, vm.VoidNothingCommand,
        vm.VtBothCommand, vm.VtParameterOnlyCommand, vm.VtTokenOnlyCommand, vm.VtNothingCommand, vm.VtOfTCommand,
    ];

    // ExecuteAsync 只等到入队；要等「命令真的跑完」得看 Exited。
    [TestMethod]
    public async Task ASingleTypedParameter_IsUnpackedAndHandedToTheBody()
    {
        var vm = new CommandSignatureViewModel();

        await RunToCompletionAsync(vm.TypedStringCommand, "hello");
        await RunToCompletionAsync(vm.TypedStringWithTokenCommand, "world");
        await RunToCompletionAsync(vm.TypedNumberCommand, 42);
        await RunToCompletionAsync(vm.TypedVoidCommand, "void");

        CollectionAssert.AreEqual(new[] { "hello", "world", "42", "void" }, vm.TypedSeen,
            "every return type must unpack the same way, and void must too");
    }

    [TestMethod]
    public async Task ATypedParameterOfTheWrongType_FailsTheExecutionInsteadOfSilentlyDoingNothing()
    {
        // 强转是运行期的 —— 这条钉住的正是它的代价：传错类型不会静默，但也不是编译错误。
        var vm = new CommandSignatureViewModel();

        var completion = await vm.TypedStringCommand.ExecuteAndWaitAsync(42);

        Assert.AreEqual(CommandOutcome.Failed, completion.Outcome);
        Assert.IsInstanceOfType<InvalidCastException>(completion.Exception);
        Assert.IsEmpty(vm.TypedSeen, "the body must not run with a bogus value");
    }

    private static async Task RunToCompletionAsync(IVeloxCommand command, object? parameter = null)
    {
        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        command.Exited += _ => exited.TrySetResult(true);
        await command.ExecuteAsync(parameter);
        await exited.Task.WaitAsync(CommandTestKit.Timeout);
    }
}
