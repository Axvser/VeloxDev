using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.MVVM;

namespace VeloxDev.Core.Test.MVVM;

/// <summary>
/// The matrix where the parameter type is the <em>containing class's</em> type parameter, so the generated
/// property carries it: <c>IVeloxCommand&lt;T, ...&gt;</c>.
/// </summary>
/// <typeparam name="T">The type every command body in this fixture receives.</typeparam>
public partial class CommandMatrixClassViewModel<T>
{
    internal List<string> Ran { get; } = [];

    [VeloxCommand]
    private void VoidValue(T value)
    {
        Ran.Add(nameof(VoidValue));
    }

    [VeloxCommand]
    private Task TaskValue(T value)
    {
        Ran.Add(nameof(TaskValue));
        return Task.CompletedTask;
    }

    [VeloxCommand]
    private Task TaskValueToken(T value, CancellationToken ct)
    {
        Ran.Add(nameof(TaskValueToken));
        return Task.CompletedTask;
    }

    [VeloxCommand]
    private Task<int> TaskOfTValue(T value)
    {
        Ran.Add(nameof(TaskOfTValue));
        return Task.FromResult(4);
    }

    [VeloxCommand]
    private Task<int> TaskOfTValueToken(T value, CancellationToken ct)
    {
        Ran.Add(nameof(TaskOfTValueToken));
        return Task.FromResult(5);
    }

    [VeloxCommand]
    private ValueTask ValueTaskValue(T value)
    {
        Ran.Add(nameof(ValueTaskValue));
        return default;
    }

    [VeloxCommand]
    private ValueTask ValueTaskValueToken(T value, CancellationToken ct)
    {
        Ran.Add(nameof(ValueTaskValueToken));
        return default;
    }

    [VeloxCommand]
    private ValueTask<int> ValueTaskOfTValue(T value)
    {
        Ran.Add(nameof(ValueTaskOfTValue));
        return new ValueTask<int>(8);
    }

    [VeloxCommand]
    private ValueTask<int> ValueTaskOfTValueToken(T value, CancellationToken ct)
    {
        Ran.Add(nameof(ValueTaskOfTValueToken));
        return new ValueTask<int>(9);
    }
}
