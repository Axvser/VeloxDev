using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.MVVM;

namespace VeloxDev.Core.Test.MVVM;

/// <summary>
/// One cell of the command signature matrix: the generated command, the name it was generated under, and the
/// argument a test should pass to it.
/// </summary>
/// <param name="id">The generated member's name.</param>
/// <param name="command">The generated command.</param>
/// <param name="parameter">The argument to pass, or <see langword="null"/> for a parameterless command.</param>
internal sealed class MatrixCase(string id, IVeloxCommand command, object? parameter)
{
    internal string Id { get; } = id;
    internal IVeloxCommand Command { get; } = command;
    internal object? Parameter { get; } = parameter;
}

/// <summary>
/// The concrete parameter type the matrix uses. Its <see cref="Id"/> is what a value-returning body hands back,
/// so a test can tell which body produced which value.
/// </summary>
/// <param name="id">The payload's value.</param>
/// <remarks>
/// Public because the generated command property is public and names this type — an inaccessible parameter
/// type would surface as CS0053 in the generated file.
/// </remarks>
public sealed class MatrixPayload(int id)
{
    internal int Id { get; } = id;
}
