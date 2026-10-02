using System.Globalization;

namespace VeloxDev.AI;

/// <summary>
/// Converts the loosely typed values an Agent supplies into the types generated accessors assign to.
/// </summary>
/// <remarks>
/// <para>
/// Every entry point names its destination type in its signature. There is deliberately no
/// <c>To(object value, Type targetType)</c> overload: a conversion driven by a runtime <see cref="Type"/> is what
/// <see cref="AgentPropertyAccessor"/> does today, and it is exactly the shape that cannot be made trim-safe.
/// Leaving the overload out means a future caller cannot reintroduce it without noticing.
/// </para>
/// <para>
/// Failures throw — <see cref="InvalidCastException"/> or <see cref="FormatException"/> — rather than returning a
/// default. A default would write a wrong value the caller never mentioned; generated callers catch and turn the
/// exception into the refusal message their own contract returns.
/// </para>
/// </remarks>
public static class AIContextConvert
{
    /// <summary>Converts to <see cref="string"/>.</summary>
    /// <param name="value">The value to convert; <see langword="null"/> becomes <see langword="null"/>.</param>
    /// <returns>The value as text.</returns>
    public static string? ToText(object? value)
        => value is null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);

    /// <summary>Converts to <see cref="int"/>.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The value.</returns>
    /// <exception cref="InvalidCastException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static int ToInt32(object? value)
        => Convert.ToInt32(Require(value), CultureInfo.InvariantCulture);

    /// <summary>Converts to <see cref="long"/>.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The value.</returns>
    /// <exception cref="InvalidCastException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static long ToInt64(object? value)
        => Convert.ToInt64(Require(value), CultureInfo.InvariantCulture);

    /// <summary>Converts to <see cref="double"/>.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The value.</returns>
    /// <exception cref="InvalidCastException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static double ToDouble(object? value)
        => Convert.ToDouble(Require(value), CultureInfo.InvariantCulture);

    /// <summary>Converts to <see cref="decimal"/>.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The value.</returns>
    /// <exception cref="InvalidCastException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static decimal ToDecimal(object? value)
        => Convert.ToDecimal(Require(value), CultureInfo.InvariantCulture);

    /// <summary>Converts to <see cref="bool"/>.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The value.</returns>
    /// <exception cref="InvalidCastException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static bool ToBoolean(object? value)
        => Convert.ToBoolean(Require(value), CultureInfo.InvariantCulture);

    /// <summary>Converts to <see cref="byte"/>.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The value.</returns>
    /// <exception cref="InvalidCastException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static byte ToByte(object? value)
        => Convert.ToByte(Require(value), CultureInfo.InvariantCulture);

    /// <summary>Converts to <see cref="short"/>.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The value.</returns>
    /// <exception cref="InvalidCastException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static short ToInt16(object? value)
        => Convert.ToInt16(Require(value), CultureInfo.InvariantCulture);

    /// <summary>Converts to <see cref="char"/>.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The value.</returns>
    /// <exception cref="InvalidCastException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static char ToChar(object? value)
        => Convert.ToChar(Require(value), CultureInfo.InvariantCulture);

    /// <summary>Converts to <see cref="DateTime"/>.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The value.</returns>
    /// <exception cref="InvalidCastException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static DateTime ToDateTime(object? value)
        => Convert.ToDateTime(Require(value), CultureInfo.InvariantCulture);

    /// <summary>
    /// Converts to <see cref="TimeSpan"/> — from another span, or from its invariant text form.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The value.</returns>
    /// <exception cref="InvalidCastException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static TimeSpan ToTimeSpan(object? value)
        => Require(value) is TimeSpan span
            ? span
            : TimeSpan.Parse(Convert.ToString(Require(value), CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);

    /// <summary>
    /// Converts to <see cref="Guid"/> — from another id, or from its text form.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The value.</returns>
    /// <exception cref="InvalidCastException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static Guid ToGuid(object? value)
        => Require(value) is Guid guid
            ? guid
            : Guid.Parse(Convert.ToString(Require(value), CultureInfo.InvariantCulture)!);

    /// <summary>
    /// Converts to an enum, either from a member name or from its underlying number.
    /// </summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    /// <param name="value">A member name, or a value convertible to <typeparamref name="TEnum"/>'s underlying type.</param>
    /// <returns>The enum member.</returns>
    /// <remarks>
    /// Uses <c>typeof(<typeparamref name="TEnum"/>)</c> rather than a generic parse overload, which does not exist
    /// on the oldest framework this library targets. A <c>typeof</c> literal is a type token the trimmer roots, so
    /// the lookup stays safe on every target.
    /// </remarks>
    /// <exception cref="InvalidCastException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static TEnum ToEnum<TEnum>(object? value) where TEnum : struct, Enum
    {
        if (value is TEnum typed) return typed;

        var required = Require(value);

        if (required is string name)
            return (TEnum)Enum.Parse(typeof(TEnum), name, ignoreCase: true);

        return (TEnum)Enum.ToObject(typeof(TEnum), required);
    }

    private static object Require(object? value)
        => value ?? throw new InvalidCastException("A null value cannot be converted to a value type.");
}
