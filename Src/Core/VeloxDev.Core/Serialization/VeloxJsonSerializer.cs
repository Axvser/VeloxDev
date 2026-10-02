using System;
using System.IO;

namespace VeloxDev.Serialization;

/// <summary>
/// Turns a generated object graph into the archive JSON, and back.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is driven by <see cref="VeloxJsonRegistry"/>: a value is written by the writer its runtime
/// type registered, and read by the reader the document's <c>$type</c> names. A type that registered nothing
/// cannot appear in a document — there is no reflection behind this and no fallback that would need it.
/// </para>
/// <para>
/// The output is byte-for-byte the output the archive format has always had; see
/// <see cref="VeloxJsonWriter"/> for what that means in detail.
/// </para>
/// </remarks>
public static class VeloxJsonSerializer
{
    /// <summary>
    /// Writes a value as a document.
    /// </summary>
    /// <param name="value">The value to write.</param>
    /// <param name="indented">Whether to lay the document out over lines. The archive format does.</param>
    /// <returns>The document.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The value's type has no registered writer.</exception>
    public static string Serialize(object value, bool indented = true)
    {
        if (value is null) throw new ArgumentNullException(nameof(value));

        using var text = new StringWriter();
        WriteTo(text, value, indented);
        return text.ToString();
    }

    /// <summary>
    /// Writes a value as a document into <paramref name="output"/>.
    /// </summary>
    /// <param name="output">Where the document is written.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="indented">Whether to lay the document out over lines.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The value's type has no registered writer.</exception>
    public static void WriteTo(TextWriter output, object value, bool indented = true)
    {
        if (output is null) throw new ArgumentNullException(nameof(output));
        if (value is null) throw new ArgumentNullException(nameof(value));

        WriteValue(new VeloxJsonWriter(output, indented), value, null);
    }

    /// <summary>
    /// Writes one value: a scalar with its own primitive, a container as an array or an object, and anything
    /// else through the writer its runtime type registered.
    /// </summary>
    /// <param name="writer">The document being built.</param>
    /// <param name="value">The value; <see langword="null"/> is written as the JSON literal.</param>
    /// <param name="declaredType">
    /// The member's declared type, which decides whether the value's runtime type needs naming and what a
    /// container's elements are written as.
    /// </param>
    /// <exception cref="InvalidOperationException">The value's type has no registered writer.</exception>
    public static void WriteValue(VeloxJsonWriter writer, object? value, Type? declaredType)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));

        if (value is null)
        {
            writer.WriteNull();
            return;
        }

        if (TryWriteScalar(writer, value)) return;

        // 容器没有生成的条目，它们是框架类型：形状由声明类型与值一起决定。
        if (value is System.Collections.IDictionary map)
        {
            var keyType = FirstTypeArgument(declaredType);
            WriteMap(
                writer, map,
                declaredType ?? value.GetType(),
                ValueTypeOf(declaredType) ?? typeof(object),
                keyType?.IsInterface == true);
            return;
        }

        if (value is System.Collections.IEnumerable sequence && value is not string)
        {
            writer.WriteStartArray();
            foreach (var item in sequence)
            {
                writer.WriteNextElement();
                WriteValue(writer, item, ElementTypeOf(declaredType));
            }
            writer.WriteEndArray();
            return;
        }

        var type = value.GetType();
        var registered = VeloxJsonRegistry.WriterFor(type);
        if (registered is null) throw MissingWriter(type);

        registered.Write(writer, value, declaredType);
    }

    /// <summary>The first type argument of a declared type, or <see langword="null"/>.</summary>
    private static Type? FirstTypeArgument(Type? declaredType)
    {
        if (declaredType is null) return null;

        var arguments = declaredType.IsGenericType ? declaredType.GetGenericArguments() : null;
        return arguments is { Length: > 0 } ? arguments[0] : null;
    }

    /// <summary>The element type of a declared sequence, or <see langword="null"/> when it is not one.</summary>
    private static Type? ElementTypeOf(Type? declaredType)
    {
        if (declaredType is null) return null;
        if (declaredType.IsArray) return declaredType.GetElementType();

        return FirstTypeArgument(declaredType);
    }

    /// <summary>The value type of a declared map, or <see langword="null"/> when it is not one.</summary>
    private static Type? ValueTypeOf(Type? declaredType)
    {
        if (declaredType is null || !declaredType.IsGenericType) return null;

        var arguments = declaredType.GetGenericArguments();
        return arguments.Length == 2 ? arguments[1] : null;
    }

    /// <summary>
    /// Writes a value that is its own JSON spelling.
    /// </summary>
    /// <param name="writer">The document being built.</param>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> when the value was a scalar and has been written.</returns>
    private static bool TryWriteScalar(VeloxJsonWriter writer, object value)
    {
        switch (value)
        {
            case string text: writer.WriteString(text); return true;
            case bool flag: writer.WriteBoolean(flag); return true;
            case int number: writer.WriteInt32(number); return true;
            case long number: writer.WriteInt64(number); return true;
            case double number: writer.WriteDouble(number); return true;
            case float number: writer.WriteSingle(number); return true;
            case decimal number: writer.WriteDecimal(number); return true;
            case byte number: writer.WriteInt32(number); return true;
            case short number: writer.WriteInt32(number); return true;
            case char character: writer.WriteString(character.ToString()); return true;
            case Guid id: writer.WriteGuid(id); return true;
            case System.DateTime moment: writer.WriteString(moment.ToString("O", System.Globalization.CultureInfo.InvariantCulture)); return true;
            case System.TimeSpan span: writer.WriteString(span.ToString()); return true;
            case Enum enumeration:
                // 枚举一律写它的底层整数 —— 既有文档就是这么写的，读回来时也会退化成整数。
                writer.WriteInt64(Convert.ToInt64(enumeration, System.Globalization.CultureInfo.InvariantCulture));
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Reads a document back into a graph.
    /// </summary>
    /// <typeparam name="T">The root type the document was written from.</typeparam>
    /// <param name="json">The document.</param>
    /// <returns>The graph, or <see langword="null"/> when the document held the JSON literal.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The document names, or implies, a type with no reader.</exception>
    public static T? Deserialize<T>(string json) where T : class
    {
        if (json is null) throw new ArgumentNullException(nameof(json));

        return (T?)ReadValue(new VeloxJsonReader(json), typeof(T), null);
    }

    /// <summary>
    /// Reads a document back into a graph of a type known only at run time.
    /// </summary>
    /// <param name="json">The document.</param>
    /// <param name="type">The root type.</param>
    /// <returns>The graph, or <see langword="null"/> when the document held the JSON literal.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The document names, or implies, a type with no reader.</exception>
    public static object? Deserialize(string json, Type type)
    {
        if (json is null) throw new ArgumentNullException(nameof(json));
        if (type is null) throw new ArgumentNullException(nameof(type));

        return ReadValue(new VeloxJsonReader(json), type, null);
    }

    /// <summary>
    /// Reads one value whose declared type is <see cref="object"/>.
    /// </summary>
    /// <remarks>
    /// A member declared <see cref="object"/> has no static type to read by, so the token decides: a number
    /// comes back as a 64-bit integer, a string as itself, and an object through the type name the document
    /// carries. Reading an enum back as an integer is exactly what the previous serializer did, and the two
    /// branch types carry a type-name side channel because of it.
    /// </remarks>
    /// <param name="reader">The document being read.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
    public static object? ReadUnknown(VeloxJsonReader reader)
    {
        if (reader is null) throw new ArgumentNullException(nameof(reader));

        if (reader.NextIsNull()) { reader.SkipValue(); return null; }

        // 对象成员要靠文档里的类型名才落得下去 —— 这正是两个分支类型带侧信道的原因。
        if (reader.NextIsObject()) return ReadValue(reader, typeof(object), null);

        var text = reader.ReadText();
        if (bool.TryParse(text, out var flag)) return flag;
        if (long.TryParse(text, out var number)) return number;

        return text;
    }

    /// <summary>
    /// Reads a value into the graph, dispatching to the reader the document's type name picks.
    /// </summary>
    /// <param name="reader">The document being read.</param>
    /// <param name="declaredType">The member's declared type, used when the document names no type.</param>
    /// <param name="existing">
    /// The instance the member already holds, when there is one. The format fills that instance rather than
    /// replacing it, which is what lets a collapsed geometry object write itself back into the node it belongs
    /// to when reading finishes.
    /// </param>
    /// <returns>The value that was read or filled.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The document names, or implies, a type with no reader.</exception>
    public static object? ReadValue(VeloxJsonReader reader, Type declaredType, object? existing)
    {
        if (reader is null) throw new ArgumentNullException(nameof(reader));
        if (declaredType is null) throw new ArgumentNullException(nameof(declaredType));

        if (reader.NextIsNull()) { reader.SkipValue(); return null; }

        if (!reader.BeginObject(out var referenceId, out var typeName))
            return reader.ResolveReference(referenceId);

        var actualType = typeName is null
            ? declaredType
            : VeloxJsonRegistry.TypeOf(typeName) ?? declaredType;

        var registered = VeloxJsonRegistry.ReaderFor(actualType);
        if (registered is null) throw MissingReader(actualType, typeName);

        var instance = existing is not null && actualType.IsInstanceOfType(existing)
            ? existing
            : registered.Create();

        // 先登记再读成员：文档里指向自己的引用要靠这张表才解得开。
        reader.RegisterReference(referenceId, instance);

        registered.Read(reader, instance);
        return instance;
    }

    /// <summary>
    /// Reads an array into <paramref name="target"/>, which the generated reader supplies from the member.
    /// </summary>
    /// <param name="reader">The document being read.</param>
    /// <param name="target">The collection to fill; the format appends to what is already there.</param>
    /// <param name="elementType">The type each element is read as.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static void ReadArray(VeloxJsonReader reader, System.Collections.IList target, Type elementType)
    {
        if (reader is null) throw new ArgumentNullException(nameof(reader));
        if (target is null) throw new ArgumentNullException(nameof(target));

        reader.BeginArray();
        while (reader.NextElement())
        {
            target.Add(ReadValue(reader, elementType, null));
        }
        reader.FinishArray();
    }

    /// <summary>
    /// Reads a map into <paramref name="target"/>.
    /// </summary>
    /// <param name="reader">The document being read.</param>
    /// <param name="target">The map to fill.</param>
    /// <param name="keyType">The map's key type.</param>
    /// <param name="valueType">The type each value is read as.</param>
    /// <param name="interfaceKeys">
    /// Whether the map is keyed by an interface, in which case the document's property names are the reference
    /// ids of the key objects.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static void ReadMap(
        VeloxJsonReader reader,
        System.Collections.IDictionary target,
        Type keyType,
        Type valueType,
        bool interfaceKeys)
    {
        if (reader is null) throw new ArgumentNullException(nameof(reader));
        if (target is null) throw new ArgumentNullException(nameof(target));

        reader.BeginObject(out _, out _);
        while (reader.NextMember(out var name))
        {
            var key = interfaceKeys
                ? reader.ResolveReference(int.Parse(name, System.Globalization.CultureInfo.InvariantCulture))
                : ReadMapKey(name, keyType);

            if (key is null) { reader.SkipValue(); continue; }

            target[key] = ReadValue(reader, valueType, target.Contains(key) ? target[key] : null);
        }
        reader.FinishObject();
    }

    /// <summary>
    /// Turns a document property name back into a map key.
    /// </summary>
    /// <remarks>
    /// An enum key was written as its member name. A genuinely typed key comes back as the enum; a key declared
    /// <see cref="object"/> comes back as that string, which is the behaviour the enumerator's own rebuild
    /// assumes.
    /// </remarks>
    private static object? ReadMapKey(string name, Type keyType)
    {
        if (keyType == typeof(string) || keyType == typeof(object)) return name;
        if (keyType.IsEnum) return Enum.Parse(keyType, name, ignoreCase: true);

        return Convert.ChangeType(name, keyType, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The error a document naming a type with no reader produces.
    /// </summary>
    internal static InvalidOperationException MissingReader(Type type, string? writtenName)
        => new($"'{type.FullName}' has no registered JSON reader" +
               (writtenName is null ? "." : $" for the type name '{writtenName}' this document carries."));

    /// <summary>
    /// Writes a map as a JSON object, dispatching each value the way a member of its declared type would be.
    /// </summary>
    /// <param name="writer">The document being built.</param>
    /// <param name="map">The map, as the non-generic interface every generic dictionary implements.</param>
    /// <param name="declaredType">The map's declared type, for the object bookkeeping and the type name.</param>
    /// <param name="valueType">The type each value is written as.</param>
    /// <param name="interfaceKeys">
    /// Whether the map is keyed by an interface. Such a map is written with its keys' reference ids as property
    /// names and carries no bookkeeping of its own — that shape is what existing archives hold, so it is
    /// reproduced rather than improved on.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static void WriteMap(
        VeloxJsonWriter writer,
        System.Collections.IDictionary map,
        Type declaredType,
        Type valueType,
        bool interfaceKeys)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));
        if (map is null) throw new ArgumentNullException(nameof(map));

        if (interfaceKeys)
        {
            // 接口键：键写成它自己的引用 id，映射本身不拿 id —— 这是既有文档的形状。
            writer.WriteStartObjectWithoutReference();
            foreach (System.Collections.DictionaryEntry entry in map)
            {
                if (entry.Key is null) continue;

                writer.WriteMemberName(writer.GetOrAddReference(entry.Key).ToString(System.Globalization.CultureInfo.InvariantCulture));
                WriteValue(writer, entry.Value, valueType);
            }
        }
        else
        {
            if (!writer.WriteStartObject(map, declaredType)) return;

            foreach (System.Collections.DictionaryEntry entry in map)
            {
                if (entry.Key is null) continue;

                writer.WriteMemberName(entry.Key.ToString() ?? string.Empty);
                WriteValue(writer, entry.Value, valueType);
            }
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// The error a value with no writer produces. The message names the type and the two ways to fix it, because
    /// the closed world is the design rather than an accident.
    /// </summary>
    internal static InvalidOperationException MissingWriter(Type type)
        => new($"'{type.FullName}' has no registered JSON writer. A type takes part in the archive format when the " +
               "VeloxDev generator compiled a writer for it — which happens for a workflow ViewModel, or for a " +
               "type reachable from one. A type outside that closure cannot be written.");
}
