using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.MVVM;

namespace VeloxDev.Core.Test.MVVM;

/// <summary>
/// Every runnable cell of the signature matrix whose parameter type involves no type parameter: five return
/// types by six parameter shapes, minus the three <c>void</c> + token shapes the generator refuses.
/// </summary>
/// <remarks>
/// The <see cref="Cases"/> list is the generation assertion. It names every generated member, so a cell that
/// stopped being generated would make this fixture fail to compile rather than fail a string comparison.
/// </remarks>
public partial class CommandMatrixViewModel
{
    internal List<string> Ran { get; } = [];

    internal IReadOnlyList<MatrixCase> Cases { get; }

    public CommandMatrixViewModel()
    {
        Cases =
        [
            new MatrixCase(nameof(VoidNone), VoidNoneCommand, null),
            new MatrixCase(nameof(VoidObject), VoidObjectCommand, "boxed"),
            new MatrixCase(nameof(VoidConcrete), VoidConcreteCommand, new MatrixPayload(7)),
            new MatrixCase(nameof(TaskNone), TaskNoneCommand, null),
            new MatrixCase(nameof(TaskObject), TaskObjectCommand, "boxed"),
            new MatrixCase(nameof(TaskObjectToken), TaskObjectTokenCommand, "boxed"),
            new MatrixCase(nameof(TaskToken), TaskTokenCommand, null),
            new MatrixCase(nameof(TaskConcrete), TaskConcreteCommand, new MatrixPayload(7)),
            new MatrixCase(nameof(TaskConcreteToken), TaskConcreteTokenCommand, new MatrixPayload(7)),
            new MatrixCase(nameof(TaskOfTNone), TaskOfTNoneCommand, null),
            new MatrixCase(nameof(TaskOfTObject), TaskOfTObjectCommand, "boxed"),
            new MatrixCase(nameof(TaskOfTObjectToken), TaskOfTObjectTokenCommand, "boxed"),
            new MatrixCase(nameof(TaskOfTToken), TaskOfTTokenCommand, null),
            new MatrixCase(nameof(TaskOfTConcrete), TaskOfTConcreteCommand, new MatrixPayload(7)),
            new MatrixCase(nameof(TaskOfTConcreteToken), TaskOfTConcreteTokenCommand, new MatrixPayload(7)),
            new MatrixCase(nameof(ValueTaskNone), ValueTaskNoneCommand, null),
            new MatrixCase(nameof(ValueTaskObject), ValueTaskObjectCommand, "boxed"),
            new MatrixCase(nameof(ValueTaskObjectToken), ValueTaskObjectTokenCommand, "boxed"),
            new MatrixCase(nameof(ValueTaskToken), ValueTaskTokenCommand, null),
            new MatrixCase(nameof(ValueTaskConcrete), ValueTaskConcreteCommand, new MatrixPayload(7)),
            new MatrixCase(nameof(ValueTaskConcreteToken), ValueTaskConcreteTokenCommand, new MatrixPayload(7)),
            new MatrixCase(nameof(ValueTaskOfTNone), ValueTaskOfTNoneCommand, null),
            new MatrixCase(nameof(ValueTaskOfTObject), ValueTaskOfTObjectCommand, "boxed"),
            new MatrixCase(nameof(ValueTaskOfTObjectToken), ValueTaskOfTObjectTokenCommand, "boxed"),
            new MatrixCase(nameof(ValueTaskOfTToken), ValueTaskOfTTokenCommand, null),
            new MatrixCase(nameof(ValueTaskOfTConcrete), ValueTaskOfTConcreteCommand, new MatrixPayload(7)),
            new MatrixCase(nameof(ValueTaskOfTConcreteToken), ValueTaskOfTConcreteTokenCommand, new MatrixPayload(7)),
        ];
    }

    [VeloxCommand]
    private void VoidNone()
    {
        Ran.Add(nameof(VoidNone));
    }

    [VeloxCommand]
    private void VoidObject(object? value)
    {
        Ran.Add(nameof(VoidObject));
    }

    [VeloxCommand]
    private void VoidConcrete(MatrixPayload payload)
    {
        Ran.Add(nameof(VoidConcrete));
    }

    [VeloxCommand]
    private Task TaskNone()
    {
        Ran.Add(nameof(TaskNone));
        return Task.CompletedTask;
    }

    [VeloxCommand]
    private Task TaskObject(object? value)
    {
        Ran.Add(nameof(TaskObject));
        return Task.CompletedTask;
    }

    [VeloxCommand]
    private Task TaskObjectToken(object? value, CancellationToken ct)
    {
        Ran.Add(nameof(TaskObjectToken));
        return Task.CompletedTask;
    }

    [VeloxCommand]
    private Task TaskToken(CancellationToken ct)
    {
        Ran.Add(nameof(TaskToken));
        return Task.CompletedTask;
    }

    [VeloxCommand]
    private Task TaskConcrete(MatrixPayload payload)
    {
        Ran.Add(nameof(TaskConcrete));
        return Task.CompletedTask;
    }

    [VeloxCommand]
    private Task TaskConcreteToken(MatrixPayload payload, CancellationToken ct)
    {
        Ran.Add(nameof(TaskConcreteToken));
        return Task.CompletedTask;
    }

    [VeloxCommand]
    private Task<int> TaskOfTNone()
    {
        Ran.Add(nameof(TaskOfTNone));
        return Task.FromResult(10);
    }

    [VeloxCommand]
    private Task<int> TaskOfTObject(object? value)
    {
        Ran.Add(nameof(TaskOfTObject));
        return Task.FromResult(11);
    }

    [VeloxCommand]
    private Task<int> TaskOfTObjectToken(object? value, CancellationToken ct)
    {
        Ran.Add(nameof(TaskOfTObjectToken));
        return Task.FromResult(12);
    }

    [VeloxCommand]
    private Task<int> TaskOfTToken(CancellationToken ct)
    {
        Ran.Add(nameof(TaskOfTToken));
        return Task.FromResult(13);
    }

    [VeloxCommand]
    private Task<int> TaskOfTConcrete(MatrixPayload payload)
    {
        Ran.Add(nameof(TaskOfTConcrete));
        return Task.FromResult(14);
    }

    [VeloxCommand]
    private Task<int> TaskOfTConcreteToken(MatrixPayload payload, CancellationToken ct)
    {
        Ran.Add(nameof(TaskOfTConcreteToken));
        return Task.FromResult(15);
    }

    [VeloxCommand]
    private ValueTask ValueTaskNone()
    {
        Ran.Add(nameof(ValueTaskNone));
        return default;
    }

    [VeloxCommand]
    private ValueTask ValueTaskObject(object? value)
    {
        Ran.Add(nameof(ValueTaskObject));
        return default;
    }

    [VeloxCommand]
    private ValueTask ValueTaskObjectToken(object? value, CancellationToken ct)
    {
        Ran.Add(nameof(ValueTaskObjectToken));
        return default;
    }

    [VeloxCommand]
    private ValueTask ValueTaskToken(CancellationToken ct)
    {
        Ran.Add(nameof(ValueTaskToken));
        return default;
    }

    [VeloxCommand]
    private ValueTask ValueTaskConcrete(MatrixPayload payload)
    {
        Ran.Add(nameof(ValueTaskConcrete));
        return default;
    }

    [VeloxCommand]
    private ValueTask ValueTaskConcreteToken(MatrixPayload payload, CancellationToken ct)
    {
        Ran.Add(nameof(ValueTaskConcreteToken));
        return default;
    }

    [VeloxCommand]
    private ValueTask<int> ValueTaskOfTNone()
    {
        Ran.Add(nameof(ValueTaskOfTNone));
        return new ValueTask<int>(22);
    }

    [VeloxCommand]
    private ValueTask<int> ValueTaskOfTObject(object? value)
    {
        Ran.Add(nameof(ValueTaskOfTObject));
        return new ValueTask<int>(23);
    }

    [VeloxCommand]
    private ValueTask<int> ValueTaskOfTObjectToken(object? value, CancellationToken ct)
    {
        Ran.Add(nameof(ValueTaskOfTObjectToken));
        return new ValueTask<int>(24);
    }

    [VeloxCommand]
    private ValueTask<int> ValueTaskOfTToken(CancellationToken ct)
    {
        Ran.Add(nameof(ValueTaskOfTToken));
        return new ValueTask<int>(25);
    }

    [VeloxCommand]
    private ValueTask<int> ValueTaskOfTConcrete(MatrixPayload payload)
    {
        Ran.Add(nameof(ValueTaskOfTConcrete));
        return new ValueTask<int>(26);
    }

    [VeloxCommand]
    private ValueTask<int> ValueTaskOfTConcreteToken(MatrixPayload payload, CancellationToken ct)
    {
        Ran.Add(nameof(ValueTaskOfTConcreteToken));
        return new ValueTask<int>(27);
    }
}
