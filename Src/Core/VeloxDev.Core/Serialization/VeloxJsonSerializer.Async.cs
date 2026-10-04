using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace VeloxDev.Serialization;

// 写读的异步链路。与同步链路一一对应，形状完全照抄，只把每条 token 调用换成 Async 版。
// 两条链路必须同义：生成器按入口产出一对读写器，VeloxJsonStreamingTests 会在两条路上跑同一批语料。
public static partial class VeloxJsonSerializer
{
    // ── 写 ────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Writes a value as a UTF-8 document into <paramref name="output"/>, asynchronously.</summary>
    /// <param name="output">Where the document is written. Left open.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="indented">Whether to lay the document out over lines.</param>
    /// <param name="excludedTypes">Member types to leave out, or <see langword="null"/> for all of them.</param>
    /// <returns>A task that completes when the document and the stream have been flushed.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// Every I/O is awaited: the writer buffers, and the handover to the <see cref="StreamWriter"/> — including
    /// the one its own full buffer forces — goes through the asynchronous path.
    /// </remarks>
    public static async Task WriteToAsync(
        Stream output,
        object value,
        bool indented = true,
        IReadOnlyCollection<Type>? excludedTypes = null)
    {
        if (output is null) throw new ArgumentNullException(nameof(output));
        if (value is null) throw new ArgumentNullException(nameof(value));

        using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 8192, leaveOpen: true);
        await WriteToAsync(writer, value, indented, excludedTypes).ConfigureAwait(false);
    }

    /// <summary>Writes a value as a document into <paramref name="output"/>, asynchronously.</summary>
    /// <param name="output">Where the document is written.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="indented">Whether to lay the document out over lines.</param>
    /// <param name="excludedTypes">Member types to leave out, or <see langword="null"/> for all of them.</param>
    /// <returns>A task that completes when the document has been written and flushed.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static async Task WriteToAsync(
        TextWriter output,
        object value,
        bool indented = true,
        IReadOnlyCollection<Type>? excludedTypes = null)
    {
        if (output is null) throw new ArgumentNullException(nameof(output));
        if (value is null) throw new ArgumentNullException(nameof(value));

        var previous = _excludedTypes;
        _excludedTypes = excludedTypes is { Count: > 0 } ? [.. excludedTypes] : null;

        try
        {
            var writer = new VeloxJsonWriter(output, indented, async: true);
            await WriteValueAsync(writer, value, null).ConfigureAwait(false);
            await writer.CompleteAsync().ConfigureAwait(false);
        }
        finally
        {
            _excludedTypes = previous;
        }
    }

    /// <summary>Writes one value asynchronously: a scalar, a container, or through its registered writer.</summary>
    /// <param name="writer">The document being built.</param>
    /// <param name="value">The value; <see langword="null"/> is written as the JSON literal.</param>
    /// <param name="declaredType">The member's declared type.</param>
    /// <returns>A task that completes when the value has been written.</returns>
    /// <exception cref="InvalidOperationException">The value's type has no registered writer.</exception>
    public static async Task WriteValueAsync(VeloxJsonWriter writer, object? value, Type? declaredType)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));

        if (value is null)
        {
            await writer.WriteNullAsync().ConfigureAwait(false);
            return;
        }

        if (await TryWriteScalarAsync(writer, value).ConfigureAwait(false)) return;

        // 生成了条目的类型优先：`SlotEnumerator<T>` 实现 IEnumerable，但契约一贯把它写成**对象**而不是
        // 数组（那条规则是 WritablePropertiesOnlyResolver 定下的，既有文档就是这么存的）。
        var type = value.GetType();
        if (VeloxJsonRegistry.WriterFor(type) is { } registered)
        {
            await registered.WriteAsync(writer, value, declaredType).ConfigureAwait(false);
            return;
        }

        // 容器没有生成的条目，它们是框架类型：形状由声明类型与值一起决定。
        if (value is IDictionary map)
        {
            var keyType = FirstTypeArgument(declaredType);
            await WriteMapAsync(
                writer, map,
                declaredType ?? value.GetType(),
                ValueTypeOf(declaredType) ?? typeof(object),
                keyType?.IsInterface == true).ConfigureAwait(false);
            return;
        }

        if (value is IEnumerable sequence && value is not string)
        {
            var elementType = ElementTypeOf(declaredType);

            await writer.WriteStartArrayAsync().ConfigureAwait(false);
            foreach (var item in sequence)
            {
                await writer.WriteNextElementAsync().ConfigureAwait(false);
                await WriteValueAsync(writer, item, elementType).ConfigureAwait(false);
            }
            await writer.WriteEndArrayAsync().ConfigureAwait(false);
            return;
        }

        throw MissingWriter(type);
    }

    private static async Task<bool> TryWriteScalarAsync(VeloxJsonWriter writer, object value)
    {
        switch (value)
        {
            case string text: await writer.WriteStringAsync(text).ConfigureAwait(false); return true;
            case bool flag: await writer.WriteBooleanAsync(flag).ConfigureAwait(false); return true;
            case int number: await writer.WriteInt32Async(number).ConfigureAwait(false); return true;
            case long number: await writer.WriteInt64Async(number).ConfigureAwait(false); return true;
            case double number: await writer.WriteDoubleAsync(number).ConfigureAwait(false); return true;
            case float number: await writer.WriteSingleAsync(number).ConfigureAwait(false); return true;
            case decimal number: await writer.WriteDecimalAsync(number).ConfigureAwait(false); return true;
            case byte number: await writer.WriteInt32Async(number).ConfigureAwait(false); return true;
            case short number: await writer.WriteInt32Async(number).ConfigureAwait(false); return true;
            case char character: await writer.WriteStringAsync(character.ToString()).ConfigureAwait(false); return true;
            case Guid id: await writer.WriteGuidAsync(id).ConfigureAwait(false); return true;
            case DateTime moment:
                await writer.WriteStringAsync(moment.ToString("O", CultureInfo.InvariantCulture)).ConfigureAwait(false);
                return true;
            case TimeSpan span: await writer.WriteStringAsync(span.ToString()).ConfigureAwait(false); return true;
            case Enum enumeration:
                // 枚举一律写它的底层整数 —— 既有文档就是这么写的，读回来时也会退化成整数。
                await writer.WriteInt64Async(Convert.ToInt64(enumeration, CultureInfo.InvariantCulture)).ConfigureAwait(false);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Writes a map as a JSON object, asynchronously.</summary>
    /// <param name="writer">The document being built.</param>
    /// <param name="map">The map.</param>
    /// <param name="declaredType">The map's declared type.</param>
    /// <param name="valueType">The type each value is written as.</param>
    /// <param name="interfaceKeys">Whether the map is keyed by an interface.</param>
    /// <returns>A task that completes when the map has been written.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static async Task WriteMapAsync(
        VeloxJsonWriter writer,
        IDictionary map,
        Type declaredType,
        Type valueType,
        bool interfaceKeys)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));
        if (map is null) throw new ArgumentNullException(nameof(map));

        if (interfaceKeys)
        {
            // 接口键：键写成它自己的引用 id，映射本身不拿 id —— 这是既有文档的形状。
            await writer.WriteStartObjectWithoutReferenceAsync().ConfigureAwait(false);
            foreach (var raw in map)
            {
                if (raw is not DictionaryEntry entry) continue;
                if (entry.Key is null) continue;

                await writer.WriteMemberNameAsync(
                    writer.GetOrAddReference(entry.Key).ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
                await WriteValueAsync(writer, entry.Value, valueType).ConfigureAwait(false);
            }
        }
        else
        {
            if (!await writer.WriteStartObjectAsync(map, declaredType).ConfigureAwait(false)) return;

            foreach (var raw in map)
            {
                if (raw is not DictionaryEntry entry) continue;
                if (entry.Key is null) continue;

                await writer.WriteMemberNameAsync(entry.Key.ToString() ?? string.Empty).ConfigureAwait(false);
                await WriteValueAsync(writer, entry.Value, valueType).ConfigureAwait(false);
            }
        }

        await writer.WriteEndObjectAsync().ConfigureAwait(false);
    }

    // ── 读 ────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Reads a document from a source that does not have to hold all of it, asynchronously.</summary>
    /// <typeparam name="T">The root type the document was written from.</typeparam>
    /// <param name="reader">The source. Read no further than the parser needs, and left open.</param>
    /// <returns>The graph, or <see langword="null"/> when the document held the JSON literal.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
    /// <remarks>Every refill is an awaited read, so no thread is held while the source produces bytes.</remarks>
    public static async Task<T?> DeserializeAsync<T>(TextReader reader) where T : class
    {
        if (reader is null) throw new ArgumentNullException(nameof(reader));

        return (T?)await ReadValueAsync(new VeloxJsonReader(reader), typeof(T), null).ConfigureAwait(false);
    }

    /// <summary>Reads a document from a source that does not have to hold all of it, asynchronously.</summary>
    /// <param name="reader">The source. Left open.</param>
    /// <param name="type">The root type.</param>
    /// <returns>The graph, or <see langword="null"/> when the document held the JSON literal.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static async Task<object?> DeserializeAsync(TextReader reader, Type type)
    {
        if (reader is null) throw new ArgumentNullException(nameof(reader));
        if (type is null) throw new ArgumentNullException(nameof(type));

        return await ReadValueAsync(new VeloxJsonReader(reader), type, null).ConfigureAwait(false);
    }

    /// <summary>Reads a UTF-8 document from a stream that does not have to hold all of it, asynchronously.</summary>
    /// <typeparam name="T">The root type the document was written from.</typeparam>
    /// <param name="stream">The source. Left open, and honouring a byte order mark.</param>
    /// <returns>The graph, or <see langword="null"/> when the document held the JSON literal.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    public static async Task<T?> DeserializeAsync<T>(Stream stream) where T : class
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));

        return (T?)await ReadValueAsync(new VeloxJsonReader(stream), typeof(T), null).ConfigureAwait(false);
    }

    /// <summary>Reads a UTF-8 document from a stream that does not have to hold all of it, asynchronously.</summary>
    /// <param name="stream">The source. Left open.</param>
    /// <param name="type">The root type.</param>
    /// <returns>The graph, or <see langword="null"/> when the document held the JSON literal.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static async Task<object?> DeserializeAsync(Stream stream, Type type)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        if (type is null) throw new ArgumentNullException(nameof(type));

        return await ReadValueAsync(new VeloxJsonReader(stream), type, null).ConfigureAwait(false);
    }

    /// <summary>Reads one value into the graph asynchronously, dispatching on the document's type name.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="declaredType">The member's declared type.</param>
    /// <param name="existing">The instance the member already holds, when there is one.</param>
    /// <returns>The value that was read or filled.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The document names, or implies, a type with no reader.</exception>
    public static async Task<object?> ReadValueAsync(VeloxJsonReader reader, Type declaredType, object? existing)
    {
        if (reader is null) throw new ArgumentNullException(nameof(reader));
        if (declaredType is null) throw new ArgumentNullException(nameof(declaredType));

        if (declaredType == typeof(object)) return await ReadUnknownAsync(reader).ConfigureAwait(false);

        if (await reader.NextIsNullAsync().ConfigureAwait(false)) { await reader.SkipValueAsync().ConfigureAwait(false); return null; }

        // 运行期才知道的标量（集合的元素、字典的值）：按声明类型读那一个原语。
        if (await TryReadScalarAsync(reader, declaredType).ConfigureAwait(false) is { } scalar) return scalar;

        // 容器里的容器。生成器为它见过的每个组合登记了构造方式 —— 理由见同步链路。
        if (VeloxJsonRegistry.ContainerFactoryFor(declaredType) is { } createContainer)
            return await ReadContainerAsync(reader, declaredType, existing, createContainer).ConfigureAwait(false);

        if (await reader.NextIsArrayAsync().ConfigureAwait(false) && !declaredType.IsArray)
            throw new InvalidOperationException(
                $"'{declaredType.FullName}' is not what a top-level array reads as. A collection member is filled " +
                "by the generated reader for the type that declares it; to read an array as a tree, use Parse.");

        return await ReadObjectValueAsync(reader, declaredType, existing).ConfigureAwait(false);
    }

    /// <summary>Reads one value whose declared type is <see cref="object"/>, asynchronously.</summary>
    /// <param name="reader">The document.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
    public static async Task<object?> ReadUnknownAsync(VeloxJsonReader reader)
    {
        if (reader is null) throw new ArgumentNullException(nameof(reader));

        if (await reader.NextIsNullAsync().ConfigureAwait(false)) { await reader.SkipValueAsync().ConfigureAwait(false); return null; }

        // 对象成员要靠文档里的类型名才落得下去 —— 这正是两个分支类型带侧信道的原因。
        if (await reader.NextIsObjectAsync().ConfigureAwait(false))
            return await ReadObjectValueAsync(reader, typeof(object), null).ConfigureAwait(false);

        if (await reader.NextIsArrayAsync().ConfigureAwait(false))
        {
            var list = new List<object?>();
            await reader.BeginArrayAsync().ConfigureAwait(false);
            while (await reader.NextElementAsync().ConfigureAwait(false))
                list.Add(await ReadUnknownAsync(reader).ConfigureAwait(false));
            await reader.FinishArrayAsync().ConfigureAwait(false);

            return list;
        }

        var text = await reader.ReadTextAsync().ConfigureAwait(false);
        if (bool.TryParse(text, out var flag)) return flag;
        if (long.TryParse(text, out var number)) return number;

        return text;
    }

    private static async Task<object?> TryReadScalarAsync(VeloxJsonReader reader, Type declaredType)
    {
        var underlying = Nullable.GetUnderlyingType(declaredType) ?? declaredType;

        if (underlying.IsEnum) return null;
        if (underlying == typeof(string)) return await reader.ReadStringAsync().ConfigureAwait(false);
        if (underlying == typeof(bool)) return await reader.ReadBooleanAsync().ConfigureAwait(false);
        if (underlying == typeof(int)) return await reader.ReadInt32Async().ConfigureAwait(false);
        if (underlying == typeof(long)) return await reader.ReadInt64Async().ConfigureAwait(false);
        if (underlying == typeof(double)) return await reader.ReadDoubleAsync().ConfigureAwait(false);
        if (underlying == typeof(float)) return await reader.ReadSingleAsync().ConfigureAwait(false);
        if (underlying == typeof(decimal)) return await reader.ReadDecimalAsync().ConfigureAwait(false);
        if (underlying == typeof(byte)) return (byte)await reader.ReadInt32Async().ConfigureAwait(false);
        if (underlying == typeof(short)) return (short)await reader.ReadInt32Async().ConfigureAwait(false);
        if (underlying == typeof(char)) return (await reader.ReadTextAsync().ConfigureAwait(false))[0];
        if (underlying == typeof(Guid)) return await reader.ReadGuidAsync().ConfigureAwait(false);
        if (underlying == typeof(DateTime))
            return DateTime.Parse(await reader.ReadTextAsync().ConfigureAwait(false), CultureInfo.InvariantCulture);
        if (underlying == typeof(TimeSpan))
            return TimeSpan.Parse(await reader.ReadTextAsync().ConfigureAwait(false), CultureInfo.InvariantCulture);

        return null;
    }

    private static async Task<object?> ReadObjectValueAsync(VeloxJsonReader reader, Type declaredType, object? existing)
    {
        var (opened, referenceId, typeName) = await reader.BeginObjectAsync().ConfigureAwait(false);
        if (!opened) return reader.ResolveReference(referenceId);

        var actualType = typeName is null
            ? declaredType
            : VeloxJsonRegistry.TypeOf(typeName) ?? declaredType;

        var registered = VeloxJsonRegistry.ReaderFor(actualType);
        if (registered is null && declaredType != typeof(object)) throw MissingReader(actualType, typeName);

        if (registered is null)
        {
            // 没有条目：按文档的形状读成普通容器。`object` 成员里放的就是这些。
            return await ReadByShapeAsync(reader, referenceId).ConfigureAwait(false);
        }

        var instance = existing is not null && actualType.IsInstanceOfType(existing)
            ? existing
            : registered.Create();

        // 先登记再读成员：文档里指向自己的引用要靠这张表才解得开。
        reader.RegisterReference(referenceId, instance);

        await registered.ReadAsync(reader, instance).ConfigureAwait(false);
        return instance;
    }

    private static async Task<object?> ReadByShapeAsync(VeloxJsonReader reader, int referenceId)
    {
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        reader.RegisterReference(referenceId, map);

        while (await reader.NextMemberAsync().ConfigureAwait(false))
        {
            // 形状未知，没有字面量可比，所以名字要物化成字符串才能当键。
            var name = await reader.ReadMemberNameAsync().ConfigureAwait(false);
            map[name] = await ReadUnknownAsync(reader).ConfigureAwait(false);
        }
        await reader.FinishObjectAsync().ConfigureAwait(false);

        return map;
    }

    /// <summary>Reads an array into <paramref name="target"/>, asynchronously.</summary>
    /// <param name="reader">The document being read.</param>
    /// <param name="target">The collection to fill.</param>
    /// <param name="elementType">The type each element is read as.</param>
    /// <returns>A task that completes when the array has been read.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static async Task ReadArrayAsync(VeloxJsonReader reader, IList target, Type elementType)
    {
        if (reader is null) throw new ArgumentNullException(nameof(reader));
        if (target is null) throw new ArgumentNullException(nameof(target));

        await reader.BeginArrayAsync().ConfigureAwait(false);
        while (await reader.NextElementAsync().ConfigureAwait(false))
        {
            target.Add(await ReadValueAsync(reader, elementType, null).ConfigureAwait(false));
        }
        await reader.FinishArrayAsync().ConfigureAwait(false);
    }

    /// <summary>Reads a map into <paramref name="target"/>, asynchronously.</summary>
    /// <param name="reader">The document being read.</param>
    /// <param name="target">The map to fill.</param>
    /// <param name="keyType">The map's key type.</param>
    /// <param name="valueType">The type each value is read as.</param>
    /// <param name="interfaceKeys">Whether the map is keyed by an interface.</param>
    /// <returns>A task that completes when the map has been read.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static async Task ReadMapAsync(
        VeloxJsonReader reader,
        IDictionary target,
        Type keyType,
        Type valueType,
        bool interfaceKeys)
    {
        if (reader is null) throw new ArgumentNullException(nameof(reader));
        if (target is null) throw new ArgumentNullException(nameof(target));

        await reader.BeginObjectAsync().ConfigureAwait(false);
        while (await reader.NextMemberAsync().ConfigureAwait(false))
        {
            // 成员名走异步面时不留原文（生成器那条链用的是同步的无参重载），所以这里用同步的一条取名字。
            var name = await reader.ReadMemberNameAsync().ConfigureAwait(false);

            var key = interfaceKeys
                ? reader.ResolveReference(int.Parse(name, CultureInfo.InvariantCulture))
                : ReadMapKey(name, keyType);

            if (key is null) { await reader.SkipValueAsync().ConfigureAwait(false); continue; }

            target[key] = await ReadValueAsync(reader, valueType, target.Contains(key) ? target[key] : null).ConfigureAwait(false);
        }
        await reader.FinishObjectAsync().ConfigureAwait(false);
    }

    private static async Task<object> ReadContainerAsync(
        VeloxJsonReader reader,
        Type declaredType,
        object? existing,
        Func<object> create)
    {
        var container = existing is not null && declaredType.IsInstanceOfType(existing) ? existing : create();

        if (container is IDictionary map)
        {
            var keyType = FirstTypeArgument(declaredType) ?? typeof(object);
            await ReadMapAsync(reader, map, keyType, ValueTypeOf(declaredType) ?? typeof(object), keyType.IsInterface).ConfigureAwait(false);
            return container;
        }

        await ReadArrayAsync(reader, (IList)container, ElementTypeOf(declaredType) ?? typeof(object)).ConfigureAwait(false);
        return container;
    }
}
