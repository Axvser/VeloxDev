namespace VeloxDev.AI;

/// <summary>
/// Acts on one type on the Agent's behalf, without reflection.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart to <see cref="AIContextNode"/>. A node says what a type is; an accessor does things to it.
/// Splitting the two is what lets the tree stay data-only: type identity lives here, where a
/// <see cref="Type"/> is a literal the trimmer roots rather than a value carried through the tree.
/// </para>
/// <para>
/// Implementations are generated into the assembly that declares the type, one per concrete type, and register
/// themselves through <see cref="AIContextTreeRegistry.RegisterAccessor"/>. Every member name handled here was
/// known when the assembly was compiled, so an implementation is a switch over literal names and a cast — never
/// a lookup.
/// </para>
/// </remarks>
public interface IAIContextAccessor
{
    /// <summary>The full name of the type this accessor acts on. The registry keys on it.</summary>
    string TypeName { get; }

    /// <summary>The type itself, from a <c>typeof</c> literal.</summary>
    Type TargetType { get; }

    /// <summary>Whether <see cref="TargetType"/> can be constructed with no arguments.</summary>
    bool HasPublicParameterlessConstructor { get; }

    /// <summary>
    /// Creates a new instance of <see cref="TargetType"/>.
    /// </summary>
    /// <returns>The new instance.</returns>
    /// <exception cref="InvalidOperationException">
    /// <see cref="HasPublicParameterlessConstructor"/> is <see langword="false"/>.
    /// </exception>
    object Create();

    /// <summary>
    /// Reads one property or field.
    /// </summary>
    /// <param name="target">The instance to read from.</param>
    /// <param name="member">The member's name.</param>
    /// <param name="value">The value read, or <see langword="null"/> when the member is not readable here.</param>
    /// <returns><see langword="true"/> when the member exists and was read.</returns>
    bool TryGet(object target, string member, out object? value);

    /// <summary>
    /// Writes one property or field, converting <paramref name="value"/> to the member's own type.
    /// </summary>
    /// <param name="target">The instance to write to.</param>
    /// <param name="member">The member's name.</param>
    /// <param name="value">The value to write.</param>
    /// <returns><see langword="null"/> on success, or a message describing why the write was refused.</returns>
    string? Set(object target, string member, object? value);

    /// <summary>
    /// Executes one <c>ICommand</c> property.
    /// </summary>
    /// <param name="target">The instance holding the command.</param>
    /// <param name="commandName">The command property's name.</param>
    /// <param name="parameter">The parameter to pass, or <see langword="null"/>.</param>
    /// <param name="error"><see langword="null"/> on success, or a message describing why it did not run.</param>
    /// <returns><see langword="true"/> when the command ran.</returns>
    bool TryExecuteCommand(object target, string commandName, object? parameter, out string? error);

    /// <summary>
    /// Asks whether a command would run, without running it.
    /// </summary>
    /// <param name="target">The instance holding the command.</param>
    /// <param name="commandName">The command property's name.</param>
    /// <param name="parameter">The parameter that would be passed, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the command exists and reports it can run.</returns>
    /// <remarks>
    /// The answer is runtime state, so it cannot come from the tree the way the rest of a descriptor does. It is
    /// reported and never enforced — <see cref="TryExecuteCommand"/> does not consult it, matching the behaviour
    /// the reflection path has always had.
    /// </remarks>
    bool CanExecuteCommand(object target, string commandName, object? parameter);

    /// <summary>
    /// Invokes one method, matching an overload by name and argument count.
    /// </summary>
    /// <param name="target">The instance to invoke on.</param>
    /// <param name="method">The method's name.</param>
    /// <param name="args">The arguments, converted to the parameter types.</param>
    /// <param name="returnValue">The method's return value, or <see langword="null"/>.</param>
    /// <param name="error"><see langword="null"/> on success, or a message describing why it did not run.</param>
    /// <returns><see langword="true"/> when the method ran.</returns>
    bool TryInvoke(object target, string method, object?[] args, out object? returnValue, out string? error);

    /// <summary>
    /// Copies the scalar-valued properties two objects have in common, by name.
    /// </summary>
    /// <param name="source">The object to read from.</param>
    /// <param name="target">The object to write to.</param>
    /// <returns>The number of properties copied.</returns>
    int CopyScalarFrom(object source, object target);
}
