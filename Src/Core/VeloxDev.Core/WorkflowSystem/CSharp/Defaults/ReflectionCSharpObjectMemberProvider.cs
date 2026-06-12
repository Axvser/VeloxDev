using System.Collections;
using System.Reflection;

namespace VeloxDev.WorkflowSystem.CSharp;

public sealed class ReflectionCSharpObjectMemberProvider : ICSharpObjectMemberProvider
{
    public bool TryGetMembers(Type hostType, out CSharpObjectMembers members)
    {
        var values = new List<ValueMember>();
        var collections = new List<CollectionMember>();
        var methods = new List<MethodMember>();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;

        foreach (var property in hostType.GetProperties(flags))
        {
            if (property.GetIndexParameters().Length != 0) continue;

            if (CSharpObjectTypeTool.TryGetCollectionItemType(
                    property.PropertyType,
                    out var itemType))
            {
                if (property.GetGetMethod(false) is not null
                    || property.GetSetMethod(false) is not null)
                {
                    collections.Add(CreateCollection(
                        property.Name,
                        property.PropertyType,
                        itemType));
                }
                continue;
            }

            if (property.GetSetMethod(false) is not null
                && CSharpObjectTypeTool.IsScalarType(property.PropertyType))
            {
                values.Add(CreateValue(property.Name, property.PropertyType));
            }
        }

        foreach (var field in hostType.GetFields(flags))
        {
            if (field.IsInitOnly || field.IsLiteral) continue;

            if (CSharpObjectTypeTool.TryGetCollectionItemType(
                    field.FieldType,
                    out var itemType))
            {
                collections.Add(CreateCollection(
                    field.Name,
                    field.FieldType,
                    itemType));
                continue;
            }

            if (CSharpObjectTypeTool.IsScalarType(field.FieldType))
            {
                values.Add(CreateValue(field.Name, field.FieldType));
            }
        }

        foreach (var method in hostType.GetMethods(flags))
        {
            if (CSharpObjectMethodTool.TryCreateMember(
                    method,
                    out var methodMember))
            {
                methods.Add(methodMember);
            }
        }

        members = new CSharpObjectMembers(values, collections, methods);
        return true;
    }

    private static ValueMember CreateValue(string name, Type type)
        => new()
        {
            Path = name,
            ValueName = name,
            ValueType = CSharpObjectTypeTool.GetTypeName(type)
        };

    private static CollectionMember CreateCollection(
        string name,
        Type collectionType,
        Type itemType)
        => new()
        {
            Path = name,
            ValueName = name,
            CollectionType = CSharpObjectTypeTool.GetTypeName(collectionType),
            ValueType = CSharpObjectTypeTool.GetTypeName(itemType)
        };
}
