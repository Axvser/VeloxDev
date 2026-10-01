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

    private static IVeloxCommand[] AllCommands(CommandSignatureViewModel vm) =>
    [
        vm.BothCommand, vm.ParameterOnlyCommand, vm.TokenOnlyCommand, vm.NothingCommand, vm.TaskOfTCommand,
        vm.VoidParameterCommand, vm.VoidNothingCommand,
        vm.VtBothCommand, vm.VtParameterOnlyCommand, vm.VtTokenOnlyCommand, vm.VtNothingCommand, vm.VtOfTCommand,
    ];

    // ExecuteAsync 只等到入队；要等「命令真的跑完」得看 Exited。
    private static async Task RunToCompletionAsync(IVeloxCommand command)
    {
        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        command.Exited += _ => exited.TrySetResult(true);
        await command.ExecuteAsync(null);
        await exited.Task.WaitAsync(CommandTestKit.Timeout);
    }
}
