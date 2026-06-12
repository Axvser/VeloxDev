using System.Collections;
using System.ComponentModel;
using System.Globalization;

namespace VeloxDev.WorkflowSystem.CSharp;

internal static class CSharpObjectTypeTool
{
    internal static Type? ResolveType(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return null;

        var type = Type.GetType(fullName, false);
        if (type is not null) return type;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                type = assembly.GetType(fullName, false);
                if (type is not null) return type;
            }
            catch
            {
            }
        }

        return null;
    }

    internal static string GetTypeName(Type type)
        => type.FullName ?? type.AssemblyQualifiedName ?? type.Name;

    internal static string FormatValue(object? value)
        => value is null
            ? string.Empty
            : Convert.ToString(value, CultureInfo.InvariantCulture)
                ?? string.Empty;

    internal static bool IsScalarType(Type type)
    {
        var actualType = Nullable.GetUnderlyingType(type) ?? type;
        return actualType.IsPrimitive
            || actualType.IsEnum
            || actualType == typeof(string)
            || actualType == typeof(decimal)
            || actualType == typeof(DateTime)
            || actualType == typeof(DateTimeOffset)
            || actualType == typeof(TimeSpan)
            || actualType == typeof(Guid)
            || actualType == typeof(Uri)
            || actualType == typeof(Type);
    }

    internal static bool CanConvertFromString(Type type)
    {
        var actualType = Nullable.GetUnderlyingType(type) ?? type;
        if (IsScalarType(actualType)) return true;

        try
        {
            var converter = TypeDescriptor.GetConverter(actualType);
            return converter.CanConvertFrom(typeof(string))
                || typeof(IConvertible).IsAssignableFrom(actualType);
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryGetCollectionItemType(
        Type collectionType,
        out Type itemType)
    {
        if (collectionType == typeof(string))
        {
            itemType = typeof(object);
            return false;
        }

        if (collectionType.IsArray)
        {
            itemType = collectionType.GetElementType() ?? typeof(object);
            return true;
        }

        var enumerableType = collectionType
            .GetInterfaces()
            .Concat(new[] { collectionType })
            .FirstOrDefault(candidate =>
                candidate.IsGenericType
                && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        if (enumerableType is not null)
        {
            itemType = enumerableType.GetGenericArguments()[0];
            return true;
        }

        if (typeof(IEnumerable).IsAssignableFrom(collectionType))
        {
            itemType = typeof(object);
            return true;
        }

        itemType = typeof(object);
        return false;
    }

    internal static bool CanAcceptNull(Type type)
        => !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;

    internal static bool IsRuntimeValueCompatible(
        object? value,
        Type targetType)
    {
        if (value is null) return CanAcceptNull(targetType);
        var actualType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        return actualType.IsInstanceOfType(value);
    }

    internal static bool IsTypeCompatible(Type sourceType, Type targetType)
    {
        var sourceNullable = Nullable.GetUnderlyingType(sourceType);
        var targetNullable = Nullable.GetUnderlyingType(targetType);
        if (sourceNullable is not null
            && targetType.IsValueType
            && targetNullable is null)
        {
            return false;
        }

        var actualSource = sourceNullable ?? sourceType;
        var actualTarget = targetNullable ?? targetType;
        return actualTarget.IsAssignableFrom(actualSource);
    }

    internal static string GetSimpleTypeName(string typeName)
        => ResolveType(typeName) is { } type
            ? GetSimpleTypeName(type)
            : typeName.Split('.').LastOrDefault() ?? typeName;

    internal static string GetSimpleTypeName(Type type)
    {
        var nullableType = Nullable.GetUnderlyingType(type);
        if (nullableType is not null)
        {
            return $"{GetSimpleTypeName(nullableType)}?";
        }

        if (type.IsArray)
        {
            return $"{GetSimpleTypeName(type.GetElementType()!)}[]";
        }

        if (type.IsGenericType)
        {
            var name = type.Name;
            var tick = name.IndexOf('`');
            if (tick >= 0) name = name.Substring(0, tick);
            return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(GetSimpleTypeName))}>";
        }

        return Type.GetTypeCode(type) switch
        {
            TypeCode.Boolean => "bool",
            TypeCode.Byte => "byte",
            TypeCode.Char => "char",
            TypeCode.Decimal => "decimal",
            TypeCode.Double => "double",
            TypeCode.Int16 => "short",
            TypeCode.Int32 => "int",
            TypeCode.Int64 => "long",
            TypeCode.SByte => "sbyte",
            TypeCode.Single => "float",
            TypeCode.String => "string",
            TypeCode.UInt16 => "ushort",
            TypeCode.UInt32 => "uint",
            TypeCode.UInt64 => "ulong",
            _ when type == typeof(object) => "object",
            _ when type == typeof(void) => "void",
            _ => type.Name
        };
    }
}
