using VeloxDev.MVVM;

namespace VeloxDev.Core.Test.MVVM;

/// <summary>
/// The strongly typed event surface — the same stages, with the argument and the result in their own types.
/// </summary>
[TestClass]
public class TypedCommandEventTests
{
    [TestMethod]
    public async Task ATypedSubscriber_SeesTheTypedArgumentOnEveryStage()
    {
        var command = new VeloxCommand<int, int>(static (parameter, _) => Task.FromResult(parameter * 2));
        var typed = (IVeloxCommandEvents<int, int>)command;

        var seen = new List<int>();
        typed.Created += e => seen.Add(e.TypedParameter);
        typed.Started += e => seen.Add(e.TypedParameter);
        typed.Completed += e => seen.Add(e.TypedParameter);
        typed.Exited += e => seen.Add(e.TypedParameter);

        var value = await command.ExecuteAsync(21, CancellationToken.None);

        Assert.AreEqual(42, value);
        Assert.HasCount(4, seen, "every stage has to reach the typed handler");
        Assert.IsTrue(seen.All(static p => p == 21), "and every stage carries the argument it was called with");
    }

    [TestMethod]
    public async Task TheTwoFaces_ReceiveTheSameStage()
    {
        // 一份副本两副面孔共用 —— 两个处理器看到的必须是同一次执行的同一个参数。
        var command = new VeloxCommand<int, int>(static (parameter, _) => Task.FromResult(parameter));
        var typed = (IVeloxCommandEvents<int, int>)command;

        object? boxedParameter = null;
        var typedParameter = 0;
        var both = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        command.Exited += e =>
        {
            boxedParameter = e.Parameter;
            if (typedParameter != 0)
            {
                both.TrySetResult(true);
            }
        };
        typed.Exited += e =>
        {
            typedParameter = e.TypedParameter;
            if (boxedParameter is not null)
            {
                both.TrySetResult(true);
            }
        };

        _ = command.ExecuteAsync(7, CancellationToken.None);
        await both.Task.WaitAsync(CommandTestKit.Timeout);

        Assert.AreEqual(7, typedParameter);
        Assert.AreEqual(7, boxedParameter, "the object?-shaped reading of the same instance still works");
    }

    [TestMethod]
    public async Task ATypedEventOnAGenericAccessorCommand_StillReachesItsHandler()
    {
        // 情形 2 的访问器命令也走同一条投递路径。
        var vm = new CommandMatrixGenericMethodViewModel();
        var command = vm.GetTaskOfTValueCommand<string>();
        var typed = (IVeloxCommandEvents<string, int>)command;

        var seen = 0;
        typed.Completed += _ => seen++;

        await command.ExecuteAsync("payload", CancellationToken.None).WaitAsync(CommandTestKit.Timeout);

        Assert.AreEqual(1, seen);
    }
}
