using VeloxDev.MVVM;

namespace VeloxDev.Core.Test.MVVM;

/// <summary>
/// The type argument comes from the containing class, so the generated property can name it.
/// </summary>
/// <typeparam name="T">The type the command body receives.</typeparam>
public partial class TypedGenericViewModel<T>
{
    internal List<T> Seen { get; } = [];

    [VeloxCommand]
    private Task StoreAsync(T value)
    {
        Seen.Add(value);
        return Task.CompletedTask;
    }
}

/// <summary>
/// A typed parameter with a validator. The validator's signature is generated from the parameter type, so this
/// type only compiles if <c>CanExecuteFilterCommand</c> is declared with <c>string</c> and not <c>object?</c>.
/// </summary>
public partial class ValidatedTypedCommandViewModel
{
    internal bool Allow { get; set; } = true;

    [VeloxCommand(canValidate: true)]
    private Task FilterAsync(string query)
    {
        _ = query;
        return Task.CompletedTask;
    }

    // 形参名跟随源方法：FilterAsync(string query) ⇒ query。写成别的名字是 CS8826。
    // 必须自己 null 检查：强类型校验器仍然经 ICommand.CanExecute(object?) 可达，框架会带 null 调它。
    private partial bool CanExecuteFilterCommand(string query) => Allow && query is not null && query.Length > 0;
}

/// <summary>
/// A validator on a value-type parameter. <c>CanExecute(null)</c> has to answer false instead of throwing —
/// the argument cannot be unboxed, and the throw would land on whatever thread the framework asks from.
/// </summary>
public partial class ValidatedValueTypeCommandViewModel
{
    internal List<int> Seen { get; } = [];

    [VeloxCommand(canValidate: true)]
    private void NotifyCount(int count) => Seen.Add(count);

    private partial bool CanExecuteNotifyCountCommand(int count) => count > 0;
}

/// <summary>
/// The type argument comes from the method, which a property cannot carry — the generator emits
/// <c>GetStoreCommand&lt;T&gt;()</c> instead.
/// </summary>
public partial class GenericMethodCommandViewModel
{
    internal List<string> Seen { get; } = [];

    [VeloxCommand]
    private Task StoreAsync<T>(T value) where T : class
    {
        Seen.Add(value.ToString() ?? string.Empty);
        return Task.CompletedTask;
    }
}

/// <summary>
/// A generic method with a validator: the generated declaration is itself a generic partial method carrying the
/// same constraint, so this type only compiles if that emission is right.
/// </summary>
public partial class ValidatedGenericMethodCommandViewModel
{
    [VeloxCommand(canValidate: true)]
    private Task StoreAsync<T>(T value) where T : class
    {
        _ = value;
        return Task.CompletedTask;
    }

    // 形参名跟随源方法：StoreAsync<T>(T value) ⇒ value。
    private partial bool CanExecuteStoreCommand<T>(T value) where T : class => value is not null;
}

/// <summary>
/// A payload for the interface-contract fixture below; the point is only that it is a concrete type.
/// </summary>
public sealed class MovePayload
{
    /// <summary>The value the body records.</summary>
    public int Value { get; init; }
}

/// <summary>
/// Declares the command property the way the workflow view-model interfaces do — as the untyped
/// <see cref="IVeloxCommand"/>. A generated <c>IVeloxCommand&lt;T&gt;</c> property would not implement this
/// (CS0738), so the build passing is the assertion.
/// </summary>
public interface IHasMoveCommand
{
    /// <summary>The untyped command property the generated one has to satisfy.</summary>
    IVeloxCommand MoveCommand { get; }
}

/// <summary>
/// Implements <see cref="IHasMoveCommand"/> while its command body takes a concrete type — the case where the
/// generator has to fall back to the untyped property type.
/// </summary>
public partial class MoveCommandHolderViewModel : IHasMoveCommand
{
    internal List<int> Seen { get; } = [];

    [VeloxCommand]
    private Task MoveAsync(MovePayload payload)
    {
        Seen.Add(payload.Value);
        return Task.CompletedTask;
    }
}
