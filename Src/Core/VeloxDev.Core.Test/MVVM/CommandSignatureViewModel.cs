using System.Globalization;
using System.Threading;
using VeloxDev.MVVM;

namespace VeloxDev.Core.Test.MVVM;

/// <summary>
/// One method per signature <see cref="VeloxCommandAttribute"/> documents, so that the generator's
/// coverage is judged by whether this type compiles and its commands actually run — not by reading
/// generated text.
/// <para>
/// <c>ValueTask</c> / <c>ValueTask&lt;T&gt;</c> are the shapes that used to be rejected outright:
/// <c>CommandWriter.ParseConstructorType</c> only matched the literal <c>Task</c> / <c>Task&lt;</c>,
/// so the writer emitted <c>new VeloxCommand(command: FooAsync, …)</c> and the method group could not
/// bind to any constructor — the generated file did not compile.
/// </para>
/// </summary>
public partial class CommandSignatureViewModel
{
    /// <summary>Body names that actually ran, in order.</summary>
    internal List<string> Ran { get; } = [];

    // ---- Task ----

    [VeloxCommand]
    private Task BothAsync(object? parameter, CancellationToken ct)
    {
        _ = parameter;
        _ = ct;
        Ran.Add(nameof(BothAsync));
        return Task.CompletedTask;
    }

    [VeloxCommand]
    private Task ParameterOnlyAsync(object? parameter)
    {
        _ = parameter;
        Ran.Add(nameof(ParameterOnlyAsync));
        return Task.CompletedTask;
    }

    [VeloxCommand]
    private Task TokenOnlyAsync(CancellationToken ct)
    {
        _ = ct;
        Ran.Add(nameof(TokenOnlyAsync));
        return Task.CompletedTask;
    }

    [VeloxCommand]
    private Task NothingAsync()
    {
        Ran.Add(nameof(NothingAsync));
        return Task.CompletedTask;
    }

    [VeloxCommand]
    private Task<int> TaskOfTAsync(object? parameter)
    {
        _ = parameter;
        Ran.Add(nameof(TaskOfTAsync));
        return Task.FromResult(1);
    }

    // ---- void ----

    [VeloxCommand]
    private void VoidParameter(object? parameter)
    {
        _ = parameter;
        Ran.Add(nameof(VoidParameter));
    }

    [VeloxCommand]
    private void VoidNothing() => Ran.Add(nameof(VoidNothing));

    // ---- ValueTask：这一组是本轮新增覆盖 ----

    [VeloxCommand]
    private ValueTask VtBothAsync(object? parameter, CancellationToken ct)
    {
        _ = parameter;
        _ = ct;
        Ran.Add(nameof(VtBothAsync));
        return default;
    }

    [VeloxCommand]
    private ValueTask VtParameterOnlyAsync(object? parameter)
    {
        _ = parameter;
        Ran.Add(nameof(VtParameterOnlyAsync));
        return default;
    }

    [VeloxCommand]
    private ValueTask VtTokenOnlyAsync(CancellationToken ct)
    {
        _ = ct;
        Ran.Add(nameof(VtTokenOnlyAsync));
        return default;
    }

    [VeloxCommand]
    private ValueTask VtNothingAsync()
    {
        Ran.Add(nameof(VtNothingAsync));
        return default;
    }

    [VeloxCommand]
    private ValueTask<int> VtOfTAsync(object? parameter)
    {
        _ = parameter;
        Ran.Add(nameof(VtOfTAsync));
        return new ValueTask<int>(1);
    }

    // ---- 单个非 object? 形参：命令参数在 thunk 里被强转后交给命令体 ----
    // （刻意不写进 Ran：上面那条「每个签名各跑一遍」的用例不传参数，这里需要带着实参断言。）

    /// <summary>每一次强转后命令体实际收到的值，按执行顺序。</summary>
    internal List<string?> TypedSeen { get; } = [];

    [VeloxCommand]
    private Task TypedStringAsync(string value)
    {
        TypedSeen.Add(value);
        return Task.CompletedTask;
    }

    [VeloxCommand]
    private Task TypedStringWithTokenAsync(string value, CancellationToken ct)
    {
        _ = ct;
        TypedSeen.Add(value);
        return Task.CompletedTask;
    }

    [VeloxCommand]
    private ValueTask TypedNumberAsync(int number)
    {
        TypedSeen.Add(number.ToString(CultureInfo.InvariantCulture));
        return default;
    }

    [VeloxCommand]
    private void TypedVoidAsync(string value) => TypedSeen.Add(value);
}

/// <summary>
/// A <c>ValueTask</c> body that parks until cancelled, so the token's reachability can be asserted rather
/// than assumed. Kept separate from <see cref="CommandSignatureViewModel"/> because that type's commands
/// must all complete on their own.
/// </summary>
public partial class CancellableValueTaskViewModel
{
    internal TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal TaskCompletionSource<bool> CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    [VeloxCommand]
    private async ValueTask WorkAsync(CancellationToken ct)
    {
        Started.TrySetResult(true);

        try
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            CancellationObserved.TrySetResult(true);
            throw;
        }
    }
}
