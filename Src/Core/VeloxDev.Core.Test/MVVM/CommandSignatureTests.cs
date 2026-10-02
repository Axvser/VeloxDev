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

    [TestMethod]
    public void ATypedParameter_ProducesAStronglyTypedCommand()
    {
        var vm = new CommandSignatureViewModel();

        // 不带 cast 的赋值就是断言：属性若还是非强类型的 IVeloxCommand，这几行编译不过。
        IVeloxCommand<string> text = vm.TypedStringCommand;
        IVeloxCommand<string> textWithToken = vm.TypedStringWithTokenCommand;
        IVeloxCommand<int> number = vm.TypedNumberCommand;
        IVeloxCommand<string> fromVoid = vm.TypedVoidCommand;

        Assert.IsNotNull(text);
        Assert.IsNotNull(textWithToken);
        Assert.IsNotNull(number);
        Assert.IsNotNull(fromVoid);
    }

    [TestMethod]
    public async Task ATypedParameterOnlyBody_GetsNoCancellationTokenSource()
    {
        var vm = new CommandSignatureViewModel();

        var fromTask = await CaptureStartedSourceAsync(vm.TypedStringCommand);
        var fromValueTask = await CaptureStartedSourceAsync(vm.TypedNumberCommand);

        Assert.IsNull(fromTask, "a typed body that cannot observe a token needs no source either");
        Assert.IsNull(fromValueTask,
            "the typed ValueTask form has to reach the same null-token entry point as the typed Task form");
    }

    [TestMethod]
    public async Task ATypeArgumentFromTheContainingClass_IsCarriedByTheProperty()
    {
        var vm = new TypedGenericViewModel<string>();

        IVeloxCommand<string> typed = vm.StoreCommand;
        await RunToCompletionAsync(typed, "a");

        CollectionAssert.AreEqual(new[] { "a" }, vm.Seen);
    }

    [TestMethod]
    public void ATypedValidator_IsDeclaredWithTheParameterType()
    {
        var vm = new ValidatedTypedCommandViewModel();
        IVeloxCommand<string> typed = vm.FilterCommand;

        Assert.IsTrue(typed.CanExecute("ok"), "the validator receives the query, not a boxed argument");
        Assert.IsFalse(typed.CanExecute(""), "and its answer is what the command reports");
    }

    [TestMethod]
    public async Task ACommandSatisfyingAnUntypedInterfaceProperty_KeepsTheUntypedType()
    {
        // MoveCommand 的参数是具体类型，但接口把属性声明为非强类型 —— 生成器必须让属性退回去，
        // 否则这里 CS0738，整个测试工程编译不过。
        var vm = new MoveCommandHolderViewModel();
        IHasMoveCommand contract = vm;

        await RunToCompletionAsync(contract.MoveCommand, new MovePayload { Value = 7 });

        CollectionAssert.AreEqual(new[] { 7 }, vm.Seen);
    }

    [TestMethod]
    public async Task AGenericMethod_GivesOneCommandPerTypeArgument()
    {
        var vm = new GenericMethodCommandViewModel();

        IVeloxCommand<string> forString = vm.GetStoreCommand<string>();
        IVeloxCommand<Uri> forUri = vm.GetStoreCommand<Uri>();

        Assert.AreNotSame((object)forString, (object)forUri, "each closed type argument owns its own command, queue and lock");

        await RunToCompletionAsync(forString, "a");
        await RunToCompletionAsync(forUri, new Uri("https://example.invalid/"));

        CollectionAssert.AreEqual(new[] { "a", "https://example.invalid/" }, vm.Seen);
    }

    [TestMethod]
    public void AGenericMethodWithAValidator_UsesTheGenericValidator()
    {
        var vm = new ValidatedGenericMethodCommandViewModel();

        // 约束与类型参数都被搬运到了 CanExecuteStoreCommand<T> 上 —— 这个类型能编译就是断言。
        Assert.IsTrue(vm.GetStoreCommand<string>().CanExecute("ok"));
        Assert.IsFalse(vm.GetStoreCommand<Uri>().CanExecute(null!), "and the validator's answer is what it reports");
    }

    [TestMethod]
    public void CanExecuteWithANullArgument_AnswersFalseForAValueTypeParameter()
    {
        // ICommand.CanExecute(null) 是常态，不是异常路径 —— WPF 在应用按钮模板时会带着 null 调一次。
        // 值类型的 (T)value 在那种情况下会抛，所以这里必须答 false 而不是把异常扔给调用方。
        var vm = new ValidatedValueTypeCommandViewModel();
        IVeloxCommand<int> typed = vm.NotifyCountCommand;

        Assert.IsFalse(typed.CanExecute(null!), "a null argument can never satisfy a value-typed command");
        Assert.IsTrue(typed.CanExecute(1));
        Assert.IsFalse(typed.CanExecute(0), "and the validator still decides the real cases");
    }

    [TestMethod]
    public void CanExecuteWithANullArgument_ReachesAReferenceTypeValidator()
    {
        // 引用类型的 T 则原样把 null 交给校验器 —— 所以校验器自己必须 null 检查。
        // 这正是「强类型是类型信息、不是保证」在实践中的样子。
        var vm = new ValidatedTypedCommandViewModel();
        IVeloxCommand<string> typed = vm.FilterCommand;

        Assert.IsFalse(typed.CanExecute(null!));
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
        // 强转是运行期的。属性虽然已经强类型了，但 IVeloxCommand<T> 派生自 IVeloxCommand，
        // 基接口的 object? 重载始终可达 —— 传错类型仍然编译通过，仍然只在运行期失败。
        // 这条钉住的正是那个代价，也是「强类型只是类型信息，不是编译期保证」的现场证据。
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
