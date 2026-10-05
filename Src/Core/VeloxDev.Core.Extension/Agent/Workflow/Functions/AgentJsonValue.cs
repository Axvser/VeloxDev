using System;
using System.Globalization;
using VeloxDev.Serialization;

namespace VeloxDev.AI.Workflow.Functions;

/// <summary>
/// Turns an already-parsed JSON value into a CLR value of a type that is only known at run time —
/// the conversion the tools need when they hand the model's argument to a command or a property setter.
/// </summary>
/// <remarks>
/// <para>
/// It exists for one gap. An enum is deliberately absent from the archive engine's run-time scalar table
/// (<see cref="VeloxJsonSerializer.ReadValue"/>'s untyped path): a generated reader turns an enum member
/// back with a cast, and the engine has no metadata of its own to do that with. That is fine for a document
/// the generator wrote, and fatal for a tool argument — <c>{"CompileMode":"Static"}</c>, which
/// <c>PatchNodeProperties</c> is documented to accept, would otherwise reach the engine as a bare string
/// and fail on <c>Expect('{')</c>.
/// </para>
/// <para>
/// So the enum case is answered here, where the target <see cref="Type"/> is in hand, and everything else
/// keeps going to the archive serializer. The two spellings the tool surface actually produces are both
/// accepted: a member <b>name</b> (what <c>AppendScalarProperties</c> and <c>WorkflowStateTracker</c> write
/// when they report a component) and the <b>underlying integer</b> (what the archive writes).
/// </para>
/// </remarks>
internal static class AgentJsonValue
{
    /// <summary>
    /// Converts a parsed JSON value to <paramref name="targetType"/>.
    /// </summary>
    /// <param name="value">The parsed value; <see langword="null"/> and the JSON literal both become <see langword="null"/>.</param>
    /// <param name="targetType">The type the caller needs, as the generated accessor declared it.</param>
    /// <returns>The converted value.</returns>
    /// <exception cref="InvalidOperationException">
    /// The value cannot be read as <paramref name="targetType"/> — an enum that is neither a member name nor
    /// a number, or a type the archive format has no reader for.
    /// </exception>
    public static object? Convert(VeloxJsonValue? value, Type targetType)
    {
        if (value is null || value.IsNull) return null;

        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (underlying.IsEnum)
            return EnumFrom(value, underlying);

        return VeloxJsonSerializer.Deserialize(value.ToJson(), targetType);
    }

    /// <summary>Reads an enum member from its name or its underlying integer.</summary>
    private static object EnumFrom(VeloxJsonValue value, Type enumType)
    {
        if (value is not VeloxJsonScalar scalar || scalar.Text is not { Length: > 0 } text)
            throw new InvalidOperationException(
                $"'{enumType.FullName}' reads from an enum member name or its underlying number, not from {Describe(value)}.");

        // 裸数字 = 归档的写法；带引号的数字两种都收（Enum.Parse 也认）。
        if (scalar.IsNumber
            && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            return Enum.ToObject(enumType, number);
        }

        try
        {
            return Enum.Parse(enumType, text, ignoreCase: true);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException($"'{text}' is not a member of '{enumType.FullName}'.", ex);
        }
    }

    /// <summary>The JSON shape, for an error message a model can act on.</summary>
    private static string Describe(VeloxJsonValue value) => value switch
    {
        VeloxJsonArray => "an array",
        VeloxJsonObject => "an object",
        _ => "a value of another shape",
    };
}
