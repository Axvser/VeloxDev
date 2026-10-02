using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.MVVM;

namespace VeloxDev.Core.Test.MVVM;

/// <summary>
/// The matrix where the parameter type is a <em>method</em> type parameter, so the generator emits
/// <c>Get{Name}Command&lt;T&gt;()</c> accessors rather than properties — the type argument is only in scope there.
/// Each closed <c>T</c> gets its own command, with its own queue and lock.
/// </summary>
public partial class CommandMatrixGenericMethodViewModel
{
    internal List<string> Ran { get; } = [];

    [VeloxCommand]
    private void VoidValue<T>(T value)
    {
        Ran.Add(nameof(VoidValue));
    }

    [VeloxCommand]
    private Task TaskValue<T>(T value)
    {
        Ran.Add(nameof(TaskValue));
        return Task.CompletedTask;
    }

    [VeloxCommand]
    private Task TaskValueToken<T>(T value, CancellationToken ct)
    {
        Ran.Add(nameof(TaskValueToken));
        return Task.CompletedTask;
    }

    [VeloxCommand]
    private Task<int> TaskOfTValue<T>(T value)
    {
        Ran.Add(nameof(TaskOfTValue));
        return Task.FromResult(4);
    }

    [VeloxCommand]
    private Task<int> TaskOfTValueToken<T>(T value, CancellationToken ct)
    {
        Ran.Add(nameof(TaskOfTValueToken));
        return Task.FromResult(5);
    }

    [VeloxCommand]
    private ValueTask ValueTaskValue<T>(T value)
    {
        Ran.Add(nameof(ValueTaskValue));
        return default;
    }

    [VeloxCommand]
    private ValueTask ValueTaskValueToken<T>(T value, CancellationToken ct)
    {
        Ran.Add(nameof(ValueTaskValueToken));
        return default;
    }

    [VeloxCommand]
    private ValueTask<int> ValueTaskOfTValue<T>(T value)
    {
        Ran.Add(nameof(ValueTaskOfTValue));
        return new ValueTask<int>(8);
    }

    [VeloxCommand]
    private ValueTask<int> ValueTaskOfTValueToken<T>(T value, CancellationToken ct)
    {
        Ran.Add(nameof(ValueTaskOfTValueToken));
        return new ValueTask<int>(9);
    }
}
