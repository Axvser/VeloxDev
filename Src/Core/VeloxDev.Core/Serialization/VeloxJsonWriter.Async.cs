using System;
using System.IO;
using System.Threading.Tasks;

namespace VeloxDev.Serialization;

// 异步面：与同步面同形，区别只在「缓冲交给 TextWriter」这一步是 await 的。
//
// 造型逻辑一行都没有重复 —— *Async 方法只是先腾出空间（必要时 await 刷一次），再调同步体追加。
// 因此两条路不可能产出差一个字节的文档。
public sealed partial class VeloxJsonWriter
{
    // 缓冲满了就先交出去。**在追加之前**做：这样缓冲的增长上限是「一个 token」，而不是整份文档。
    private async Task MakeRoomAsync()
    {
        if (_buffered >= FlushThreshold) await FlushBufferAsync().ConfigureAwait(false);
    }

    // 把缓冲异步交给输出。不额外 Flush —— StreamWriter 自己的缓冲满了会走异步写，
    // 每一次真正落到流上的 I/O 因此都是 async 的；收尾的那次刷由 CompleteAsync 负责。
    internal async Task FlushBufferAsync()
    {
        if (_buffered == 0) return;

        await _writer.WriteAsync(_buffer, 0, _buffered).ConfigureAwait(false);
        _buffered = 0;
    }

    /// <summary>Flushes what is buffered and then the underlying writer.</summary>
    /// <returns>A task that completes when both have been flushed.</returns>
    public async Task CompleteAsync()
    {
        await FlushBufferAsync().ConfigureAwait(false);
        await _writer.FlushAsync().ConfigureAwait(false);
    }

    /// <summary>Asynchronously starts writing an object, or writes a reference to one already written.</summary>
    /// <param name="instance">The object.</param>
    /// <param name="declaredType">The member's declared type, or <see langword="null"/> for the value's own.</param>
    /// <returns>
    /// <see langword="true"/> when the caller must write the members and then call
    /// <see cref="WriteEndObjectAsync"/>; <see langword="false"/> when a reference was emitted instead.
    /// </returns>
    public async Task<bool> WriteStartObjectAsync(object instance, Type? declaredType)
    {
        await MakeRoomAsync().ConfigureAwait(false);
        return WriteStartObject(instance, declaredType);
    }

    /// <summary>Asynchronously starts an object that is not itself reference-tracked.</summary>
    public async Task WriteStartObjectWithoutReferenceAsync()
    {
        await MakeRoomAsync().ConfigureAwait(false);
        WriteStartObjectWithoutReference();
    }

    /// <summary>Asynchronously ends an object.</summary>
    public Task WriteEndObjectAsync() => WriteShapeAsync(WriteEndObject);

    /// <summary>Asynchronously starts an array.</summary>
    public Task WriteStartArrayAsync() => WriteShapeAsync(WriteStartArray);

    /// <summary>Asynchronously moves to the next array element.</summary>
    public Task WriteNextElementAsync() => WriteShapeAsync(WriteNextElement);

    /// <summary>Asynchronously ends an array.</summary>
    public Task WriteEndArrayAsync() => WriteShapeAsync(WriteEndArray);

    /// <summary>Asynchronously writes a property name.</summary>
    /// <param name="name">The member's name, exactly as the contract reports it.</param>
    public Task WriteMemberNameAsync(string name)
    {
        if (name is null) throw new ArgumentNullException(nameof(name));

        return WriteShapeAsync(() => WriteMemberName(name));
    }

    /// <summary>Asynchronously writes a string value.</summary>
    /// <param name="value">The value; <see langword="null"/> is written as the JSON literal.</param>
    public Task WriteStringAsync(string? value) => WriteShapeAsync(() => WriteString(value));

    /// <summary>Asynchronously writes the JSON <c>null</c> literal.</summary>
    public Task WriteNullAsync() => WriteShapeAsync(WriteNull);

    /// <summary>Asynchronously writes a boolean.</summary>
    /// <param name="value">The value.</param>
    public Task WriteBooleanAsync(bool value) => WriteShapeAsync(() => WriteBoolean(value));

    /// <summary>Asynchronously writes a 32-bit integer.</summary>
    /// <param name="value">The value.</param>
    public Task WriteInt32Async(int value) => WriteShapeAsync(() => WriteInt32(value));

    /// <summary>Asynchronously writes a 64-bit integer.</summary>
    /// <param name="value">The value.</param>
    public Task WriteInt64Async(long value) => WriteShapeAsync(() => WriteInt64(value));

    /// <summary>Asynchronously writes a double with the archive format's spelling rules.</summary>
    /// <param name="value">The value.</param>
    public Task WriteDoubleAsync(double value) => WriteShapeAsync(() => WriteDouble(value));

    /// <summary>Asynchronously writes a single-precision value.</summary>
    /// <param name="value">The value.</param>
    public Task WriteSingleAsync(float value) => WriteShapeAsync(() => WriteSingle(value));

    /// <summary>Asynchronously writes a decimal.</summary>
    /// <param name="value">The value.</param>
    public Task WriteDecimalAsync(decimal value) => WriteShapeAsync(() => WriteDecimal(value));

    /// <summary>Asynchronously writes a globally unique identifier.</summary>
    /// <param name="value">The value.</param>
    public Task WriteGuidAsync(Guid value) => WriteShapeAsync(() => WriteGuid(value));

    /// <summary>Asynchronously writes a value that already carries its own JSON spelling.</summary>
    /// <param name="json">A complete JSON token.</param>
    public Task WriteRawValueAsync(string json) => WriteShapeAsync(() => WriteRawValue(json));

    // 所有「只追加、不返回结果」的写法都收敛到这里：先腾地方，再追加。
    private async Task WriteShapeAsync(Action write)
    {
        await MakeRoomAsync().ConfigureAwait(false);
        write();
    }
}
