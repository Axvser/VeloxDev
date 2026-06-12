using System.Reflection;

namespace VeloxDev.WorkflowSystem.CSharp;

internal sealed class CSharpObjectMemberAccessor
{
    private readonly object owner;
    private readonly MemberInfo member;

    private CSharpObjectMemberAccessor(object owner, MemberInfo member)
    {
        this.owner = owner;
        this.member = member;
        ValueType = GetMemberType(member);
    }

    internal Type ValueType { get; }

    internal object? GetValue()
        => GetMemberValue(member, owner);

    internal void SetValue(object? value)
        => SetMemberValue(member, owner, value);

    internal static CSharpObjectMemberAccessor Resolve(
        object host,
        string path,
        bool createIntermediates)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "Member path cannot be empty.",
                nameof(path));
        }

        var segments = path.Split('.');
        object current = host;
        for (var index = 0; index < segments.Length - 1; index++)
        {
            var intermediate = FindMember(current.GetType(), segments[index]);
            var value = GetMemberValue(intermediate, current);
            if (value is null && createIntermediates)
            {
                var valueType = GetMemberType(intermediate);
                value = Activator.CreateInstance(valueType)
                    ?? throw new InvalidOperationException(
                        $"Unable to create path segment '{segments[index]}' ({valueType.FullName}).");
                SetMemberValue(intermediate, current, value);
            }

            current = value
                ?? throw new InvalidOperationException(
                    $"Path segment '{segments[index]}' in '{path}' is null.");
        }

        var member = FindMember(
            current.GetType(),
            segments[segments.Length - 1]);
        return new CSharpObjectMemberAccessor(current, member);
    }

    private static MemberInfo FindMember(Type type, string name)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
        var property = type.GetProperty(name, flags);
        if (property is not null && property.GetIndexParameters().Length == 0)
        {
            return property;
        }

        var field = type.GetField(name, flags);
        return field
            ?? throw new MissingMemberException(type.FullName, name);
    }

    private static Type GetMemberType(MemberInfo member)
        => member switch
        {
            PropertyInfo property => property.PropertyType,
            FieldInfo field => field.FieldType,
            _ => throw new NotSupportedException(
                $"Member '{member.Name}' is not a field or property.")
        };

    private static object? GetMemberValue(MemberInfo member, object owner)
        => member switch
        {
            PropertyInfo property when property.GetGetMethod(false) is not null
                => property.GetValue(owner),
            FieldInfo field => field.GetValue(owner),
            _ => throw new InvalidOperationException(
                $"Member '{member.Name}' is not readable.")
        };

    private static void SetMemberValue(
        MemberInfo member,
        object owner,
        object? value)
    {
        switch (member)
        {
            case PropertyInfo property
                when property.GetSetMethod(false) is not null:
                property.SetValue(owner, value);
                return;
            case FieldInfo field when !field.IsInitOnly && !field.IsLiteral:
                field.SetValue(owner, value);
                return;
            default:
                throw new InvalidOperationException(
                    $"Member '{member.Name}' is not writable.");
        }
    }
}
