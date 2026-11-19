using System.Threading;
using System.Threading.Tasks;
using VeloxDev.MVVM;

namespace VeloxDev.Core.Test.MVVM;

/// <summary>
/// The <c>canValidate</c> hook across every parameter shape, plus the attribute's other knobs.
/// </summary>
/// <remarks>
/// The validator's declared parameter type follows the command's, and its declared <em>name</em> follows the
/// source method's own parameter — so a fixture that gets either wrong does not compile, which is the assertion.
/// </remarks>
public partial class CommandValidatorMatrixViewModel
{
    internal List<string> Ran { get; } = [];

    /// <summary>What every validator consults, so a test can flip executability without touching the commands.</summary>
    internal bool Allow { get; set; } = true;

    // ── 零个前导形参：没有源形参名可抄，校验器只能是 parameter ──

    [VeloxCommand(canValidate: true)]
    private void None() => Ran.Add(nameof(None));

    private partial bool CanExecuteNoneCommand(object? parameter) => Allow;

    // ── 仅 token：token 是尾随形参而不是前导形参，所以仍然叫 parameter ──

    [VeloxCommand(canValidate: true)]
    private Task TokenOnly(CancellationToken ct)
    {
        _ = ct;
        Ran.Add(nameof(TokenOnly));
        return Task.CompletedTask;
    }

    private partial bool CanExecuteTokenOnlyCommand(object? parameter) => Allow;

    // ── object? 前导形参：校验器形参名跟随源形参 value ──

    [VeloxCommand(canValidate: true)]
    private Task ObjectOnly(object? value)
    {
        _ = value;
        Ran.Add(nameof(ObjectOnly));
        return Task.CompletedTask;
    }

    private partial bool CanExecuteObjectOnlyCommand(object? value) => Allow;

    [VeloxCommand(canValidate: true)]
    private Task ObjectAndToken(object? value, CancellationToken ct)
    {
        _ = value;
        _ = ct;
        Ran.Add(nameof(ObjectAndToken));
        return Task.CompletedTask;
    }

    private partial bool CanExecuteObjectAndTokenCommand(object? value) => Allow;

    // ── 具体类型：校验器拿到强类型形参 ──

    [VeloxCommand(canValidate: true)]
    private Task Concrete(MatrixPayload payload)
    {
        _ = payload;
        Ran.Add(nameof(Concrete));
        return Task.CompletedTask;
    }

    private partial bool CanExecuteConcreteCommand(MatrixPayload payload) => Allow && payload.Id > 0;

    [VeloxCommand(canValidate: true)]
    private Task ConcreteAndToken(MatrixPayload payload, CancellationToken ct)
    {
        _ = payload;
        _ = ct;
        Ran.Add(nameof(ConcreteAndToken));
        return Task.CompletedTask;
    }

    private partial bool CanExecuteConcreteAndTokenCommand(MatrixPayload payload) => Allow && payload.Id > 0;

    // ── 返回值与校验是正交的两件事：值返回命令同样可以带校验器 ──

    [VeloxCommand(canValidate: true)]
    private Task<int> ConcreteWithValue(MatrixPayload payload)
    {
        Ran.Add(nameof(ConcreteWithValue));
        return Task.FromResult(payload.Id);
    }

    private partial bool CanExecuteConcreteWithValueCommand(MatrixPayload payload) => Allow && payload.Id > 0;

    // ── 属性上的三个开关 ──

    /// <summary>An explicit name replaces the auto-derived one entirely.</summary>
    [VeloxCommand(name: "Renamed", canValidate: false)]
    private Task OriginalName()
    {
        Ran.Add(nameof(OriginalName));
        return Task.CompletedTask;
    }

    /// <summary>Two executions may run at once, which is what makes a probe able to observe overlap.</summary>
    [VeloxCommand(semaphore: 2, canValidate: false)]
    private async Task Concurrent()
    {
        if (Interlocked.Increment(ref _concurrentStarted) == 2)
        {
            _bothConcurrentStarted.TrySetResult(true);
        }

        await _releaseConcurrent.Task;
    }

    private readonly TaskCompletionSource<bool> _bothConcurrentStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _releaseConcurrent = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _concurrentStarted;

    /// <summary>Completes once two <see cref="Concurrent"/> executions are in flight at the same time.</summary>
    internal Task BothConcurrentStarted => _bothConcurrentStarted.Task;

    /// <summary>Lets both parked executions finish.</summary>
    internal void ReleaseConcurrent() => _releaseConcurrent.TrySetResult(true);
}

/// <summary>
/// The validator on a command whose parameter is the <em>containing class's</em> type parameter — the one shape
/// where both the command and its validator carry a type argument from the class.
/// </summary>
/// <typeparam name="T">The type the command body receives.</typeparam>
public partial class CommandValidatorClassViewModel<T>
{
    internal List<string> Ran { get; } = [];

    [VeloxCommand(canValidate: true)]
    private Task Accept(T value)
    {
        _ = value;
        Ran.Add(nameof(Accept));
        return Task.CompletedTask;
    }

    private partial bool CanExecuteAcceptCommand(T value) => value is not null;
}

/// <summary>
/// The validator on a <em>generic method</em> command that also takes a token — the accessor form, where the
/// validator has to carry the same type parameter and constraint as the accessor itself.
/// </summary>
public partial class CommandValidatorGenericMethodViewModel
{
    internal List<string> Ran { get; } = [];

    [VeloxCommand(canValidate: true)]
    private Task StoreWithToken<T>(T value, CancellationToken ct)
    {
        _ = value;
        _ = ct;
        Ran.Add(nameof(StoreWithToken));
        return Task.CompletedTask;
    }

    private partial bool CanExecuteStoreWithTokenCommand<T>(T value) => value is not null;
}
