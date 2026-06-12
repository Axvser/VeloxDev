using System.Collections;

namespace VeloxDev.WorkflowSystem.CSharp;

internal static class CSharpObjectCollectionTool
{
    internal static void Apply(
        object host,
        CollectionMember member,
        object? conversionParameter,
        IReadOnlyList<ICSharpObjectValueConverter> converters)
    {
        var accessor = CSharpObjectMemberAccessor.Resolve(
            host,
            member.Path,
            true);
        if (!CSharpObjectTypeTool.TryGetCollectionItemType(
                accessor.ValueType,
                out var itemType))
        {
            itemType = CSharpObjectTypeTool.ResolveType(member.ValueType)
                ?? throw new InvalidOperationException(
                    $"Unable to resolve collection item type '{member.ValueType}'.");
        }

        var ordered = member.Items
            .Select((item, order) => new
            {
                Item = item,
                Order = order,
                Index = item.Index >= 0 ? item.Index : order
            })
            .OrderBy(entry => entry.Index)
            .ToArray();
        if (ordered.GroupBy(entry => entry.Index).Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException(
                $"Collection path '{member.Path}' contains duplicate indices.");
        }

        var length = ordered.Length == 0
            ? 0
            : ordered.Max(entry => entry.Index) + 1;
        var values = new object?[length];
        for (var index = 0; index < length; index++)
        {
            values[index] = GetDefaultValue(itemType);
        }

        foreach (var entry in ordered)
        {
            values[entry.Index] = CSharpObjectConversionTool.ConvertValue(
                entry.Item.Value,
                itemType,
                conversionParameter,
                converters);
        }

        if (accessor.ValueType.IsArray)
        {
            var array = Array.CreateInstance(itemType, values.Length);
            for (var index = 0; index < values.Length; index++)
            {
                array.SetValue(values[index], index);
            }

            accessor.SetValue(array);
            return;
        }

        var collection = accessor.GetValue();
        if (collection is IList list
            && !list.IsReadOnly
            && !list.IsFixedSize)
        {
            list.Clear();
            foreach (var value in values) list.Add(value);
            return;
        }

        if (collection is not null
            && TryPopulateGenericCollection(collection, itemType, values))
        {
            return;
        }

        var replacement = CreateCollection(accessor.ValueType, itemType);
        if (replacement is IList replacementList)
        {
            foreach (var value in values) replacementList.Add(value);
        }
        else if (!TryPopulateGenericCollection(
                     replacement,
                     itemType,
                     values))
        {
            throw new InvalidOperationException(
                $"Collection type '{accessor.ValueType.FullName}' cannot be populated.");
        }

        accessor.SetValue(replacement);
    }

    private static object? GetDefaultValue(Type type)
        => type.IsValueType ? Activator.CreateInstance(type) : null;

    private static bool TryPopulateGenericCollection(
        object collection,
        Type itemType,
        IEnumerable<object?> values)
    {
        var collectionInterface = typeof(ICollection<>).MakeGenericType(itemType);
        if (!collectionInterface.IsInstanceOfType(collection)) return false;

        var clear = collectionInterface.GetMethod("Clear");
        var add = collectionInterface.GetMethod("Add");
        if (clear is null || add is null) return false;

        clear.Invoke(collection, null);
        foreach (var value in values)
        {
            add.Invoke(collection, new[] { value });
        }

        return true;
    }

    private static object CreateCollection(
        Type collectionType,
        Type itemType)
    {
        if (!collectionType.IsInterface && !collectionType.IsAbstract)
        {
            return Activator.CreateInstance(collectionType)
                ?? throw new InvalidOperationException(
                    $"Unable to create collection '{collectionType.FullName}'.");
        }

        var listType = typeof(List<>).MakeGenericType(itemType);
        if (!collectionType.IsAssignableFrom(listType))
        {
            throw new InvalidOperationException(
                $"Collection interface '{collectionType.FullName}' cannot use '{listType.FullName}'.");
        }

        return Activator.CreateInstance(listType)!;
    }
}
