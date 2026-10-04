using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VeloxDev.Serialization;

/// <summary>
/// Per-call serialization options built via a Fluent API.
/// Construct an instance with <see cref="Create"/>,
/// chain the desired overrides, then pass it to the
/// <c>Serialize</c> / <c>Deserialize</c> / <c>TryDeserialize</c> overloads.
/// </summary>
public sealed class SerializationOptions
{
    internal VeloxJsonFormat? Formatting { get; private set; }
    private SerializationOptions() { }

    /// <summary>Creates a new blank options builder.</summary>
    public static SerializationOptions Create() => new();

    /// <summary>Produce indented (human-readable) JSON output.</summary>
    public SerializationOptions WithIndented() { Formatting = VeloxJsonFormat.Indented; return this; }

    /// <summary>Produce compact JSON output (no extra whitespace).</summary>
    public SerializationOptions WithCompact() { Formatting = VeloxJsonFormat.Compact; return this; }

    /// <summary>
    /// Omits every property whose <b>declared</b> type is one of <paramref name="types"/>, everywhere in the graph.
    /// </summary>
    /// <param name="types">The declared property types to drop. Exact matches only.</param>
    /// <remarks>
    /// The use it exists for: serializing a compiled graph without dragging in the tree it came from. A node's
    /// <c>Parent</c> is a writable property of type <c>IWorkflowTreeViewModel</c>, so the reference graph reaches the
    /// whole document from any single node. Unlike a name-based rule this cannot hit a slot's parent (which is its
    /// node and is forward-needed), and it is inert for the tree's own round trip because that path passes no
    /// exclusions.
    /// </remarks>
    public SerializationOptions WithExcludedPropertyTypes(params Type[] types)
    {
        ExcludedPropertyTypes = types is { Length: > 0 } ? types : null;
        return this;
    }

    internal Type[]? ExcludedPropertyTypes { get; private set; }

    }

/// <summary>Serialization for any view model, backed by VeloxDev's own serializer.</summary>
public static class ViewModelSerializer
{
    // 三个核心入口都走 VeloxDev 自己的序列化器：格式逐字节一致，但不再依赖运行期反射，
    // 因此裁剪与 NativeAOT 下都成立。
    private static string SerializeCore<T>(T workflow, SerializationOptions? options = null)
        where T : INotifyPropertyChanged
    {
        if (workflow == null)
            throw new ArgumentNullException(nameof(workflow), "Workflow object cannot be null for serialization");

        return VeloxJsonSerializer.Serialize(workflow, options?.Formatting != VeloxJsonFormat.Compact, options?.ExcludedPropertyTypes);
    }

    private static bool TryDeserializeCore<T>(string json, out T? workflow, SerializationOptions? options = null)
        where T : INotifyPropertyChanged
    {
        try
        {
            workflow = (T?)VeloxJsonSerializer.Deserialize(json, typeof(T));
            return workflow != null;
        }
        catch
        {
            workflow = default;
            return false;
        }
    }

    private static T DeserializeCore<T>(string json, SerializationOptions? options = null)
        where T : INotifyPropertyChanged
    {
        var result = (T?)VeloxJsonSerializer.Deserialize(json, typeof(T));
        if (result == null)
            throw new InvalidOperationException($"Deserialization of JSON to type {typeof(T).Name} resulted in null. The JSON may be invalid or incompatible with the target type.");

        return result;
    }

    /// <summary>Reads one JSON tree into an instance of <paramref name="targetType"/>.</summary>
    /// <param name="value">The tree.</param>
    /// <param name="targetType">The type to read into; it must be one the generator carried.</param>
    /// <returns>The instance, or <see langword="null"/> when the tree is the JSON literal.</returns>
    public static object? DeserializeToType(this VeloxJsonValue value, Type targetType)
    {
        if (value == null)
            throw new ArgumentNullException(nameof(value));
        if (targetType == null)
            throw new ArgumentNullException(nameof(targetType));

        return value.IsNull ? null : VeloxJsonSerializer.Deserialize(value.ToJson(), targetType);
    }

    #region Synchronous Methods
    /// <summary>Serializes a workflow object to a JSON string using default settings.</summary>
    public static string Serialize<T>(this T workflow)
        where T : INotifyPropertyChanged
        => SerializeCore(workflow);

    /// <summary>Serializes a workflow object to a JSON string with per-call option overrides.</summary>
    public static string Serialize<T>(this T workflow, SerializationOptions options)
        where T : INotifyPropertyChanged
        => SerializeCore(workflow, options);

    /// <summary>
    /// Attempts to deserialize a JSON string using default settings.
    /// Returns <c>false</c> for null/empty/malformed input without throwing.
    /// </summary>
    public static bool TryDeserialize<T>(this string json, out T? workflow)
        where T : INotifyPropertyChanged
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            workflow = default;
            return false;
        }

        return TryDeserializeCore(json, out workflow);
    }

    /// <summary>
    /// Attempts to deserialize a JSON string with per-call option overrides.
    /// Returns <c>false</c> for null/empty/malformed input without throwing.
    /// </summary>
    public static bool TryDeserialize<T>(this string json, SerializationOptions options, out T? workflow)
        where T : INotifyPropertyChanged
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            workflow = default;
            return false;
        }

        return TryDeserializeCore(json, out workflow, options);
    }

    /// <summary>Deserializes a JSON string using default settings.</summary>
    public static T Deserialize<T>(this string json)
        where T : INotifyPropertyChanged
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("JSON string cannot be null or empty", nameof(json));

        return DeserializeCore<T>(json);
    }

    /// <summary>Deserializes a JSON string with per-call option overrides.</summary>
    public static T Deserialize<T>(this string json, SerializationOptions options)
        where T : INotifyPropertyChanged
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("JSON string cannot be null or empty", nameof(json));

        return DeserializeCore<T>(json, options);
    }
    #endregion

    #region Asynchronous Methods
    /// <summary>Asynchronously serializes a workflow object to a JSON string.</summary>
    public static Task<string> SerializeAsync<T>(this T workflow, CancellationToken cancellationToken = default)
        where T : INotifyPropertyChanged
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(SerializeCore(workflow));
    }

    /// <summary>Asynchronously serializes a workflow object to a JSON string with per-call option overrides.</summary>
    public static Task<string> SerializeAsync<T>(this T workflow, SerializationOptions options, CancellationToken cancellationToken = default)
        where T : INotifyPropertyChanged
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(SerializeCore(workflow, options));
    }

    /// <summary>Asynchronously deserializes a JSON string to a workflow object.</summary>
    public static Task<T> DeserializeAsync<T>(this string json, CancellationToken cancellationToken = default)
        where T : INotifyPropertyChanged
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("JSON string cannot be null or empty", nameof(json));

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(DeserializeCore<T>(json));
    }

    /// <summary>Asynchronously deserializes a JSON string with per-call option overrides.</summary>
    public static Task<T> DeserializeAsync<T>(this string json, SerializationOptions options, CancellationToken cancellationToken = default)
        where T : INotifyPropertyChanged
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("JSON string cannot be null or empty", nameof(json));

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(DeserializeCore<T>(json, options));
    }
    #endregion

    #region Streaming Methods (netstandard2.0 compatible)

    /// <summary>Serializes a workflow object into a <see cref="TextWriter"/>, a piece at a time.</summary>
    /// <param name="workflow">The object to write.</param>
    /// <param name="writer">Where the document is written.</param>
    /// <param name="options">Optional per-call overrides.</param>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// No whole-document string is built; memory is bounded by the writer's own buffering. Use this rather than
    /// <see cref="Serialize{T}(T)"/> when the document is large enough that holding it twice matters.
    /// </remarks>
    public static void SerializeToTextWriter<T>(this T workflow, TextWriter writer, SerializationOptions? options = null)
        where T : INotifyPropertyChanged
    {
        if (writer == null)
            throw new ArgumentNullException(nameof(writer), "Target writer cannot be null");
        if (workflow == null)
            throw new ArgumentNullException(nameof(workflow));

        VeloxJsonSerializer.WriteTo(writer, workflow, options?.Formatting != VeloxJsonFormat.Compact, options?.ExcludedPropertyTypes);
        writer.Flush();
    }

    /// <summary>Serializes a workflow object into a UTF-8 stream, a piece at a time.</summary>
    /// <param name="workflow">The object to write.</param>
    /// <param name="stream">Where the document is written. Left open.</param>
    /// <param name="options">Optional per-call overrides.</param>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The stream is not writable.</exception>
    public static void SerializeToStream<T>(this T workflow, Stream stream, SerializationOptions? options = null)
        where T : INotifyPropertyChanged
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream), "Target stream cannot be null");
        if (!stream.CanWrite)
            throw new InvalidOperationException("Target stream is not writable");
        if (workflow == null)
            throw new ArgumentNullException(nameof(workflow));

        VeloxJsonSerializer.WriteTo(stream, workflow, options?.Formatting != VeloxJsonFormat.Compact, options?.ExcludedPropertyTypes);
    }

    /// <summary>Deserializes a workflow object from a <see cref="TextReader"/>, a piece at a time.</summary>
    /// <typeparam name="T">The root type.</typeparam>
    /// <param name="reader">The document. Left open.</param>
    /// <returns>The restored object, or <see langword="null"/> when the document held the JSON literal.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
    /// <remarks>Peak memory is the reader's buffer plus the graph, not the whole document.</remarks>
    public static T? DeserializeFromTextReader<T>(this TextReader reader)
        where T : INotifyPropertyChanged
    {
        if (reader == null)
            throw new ArgumentNullException(nameof(reader), "Source reader cannot be null");

        return (T?)VeloxJsonSerializer.Deserialize(reader, typeof(T));
    }

    /// <summary>Deserializes a workflow object from a UTF-8 stream, a piece at a time.</summary>
    /// <typeparam name="T">The root type.</typeparam>
    /// <param name="stream">The document. Left open.</param>
    /// <returns>The restored object, or <see langword="null"/> when the document held the JSON literal.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The stream is not readable.</exception>
    public static T? DeserializeFromStream<T>(this Stream stream)
        where T : INotifyPropertyChanged
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream), "Source stream cannot be null");
        if (!stream.CanRead)
            throw new InvalidOperationException("Source stream is not readable");

        return (T?)VeloxJsonSerializer.Deserialize(stream, typeof(T));
    }

    /// <summary>Serializes a workflow object to a UTF-8 byte array.</summary>
    public static byte[] SerializeToUtf8Bytes<T>(this T workflow, SerializationOptions? options = null)
        where T : INotifyPropertyChanged
        => Encoding.UTF8.GetBytes(SerializeCore(workflow, options));

    /// <summary>Asynchronously serializes a workflow object to a UTF-8 byte array.</summary>
    public static Task<byte[]> SerializeToUtf8BytesAsync<T>(this T workflow, SerializationOptions? options = null, CancellationToken cancellationToken = default)
        where T : INotifyPropertyChanged
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(workflow.SerializeToUtf8Bytes(options));
    }

    /// <summary>Deserializes a workflow object from a UTF-8 byte array.</summary>
    public static T DeserializeFromUtf8Bytes<T>(this byte[] utf8Json, SerializationOptions? options = null)
        where T : INotifyPropertyChanged
    {
        if (utf8Json == null)
            throw new ArgumentNullException(nameof(utf8Json), "UTF-8 JSON payload cannot be null");
        if (utf8Json.Length == 0)
            throw new ArgumentException("UTF-8 JSON payload cannot be empty", nameof(utf8Json));

        return DeserializeCore<T>(Encoding.UTF8.GetString(utf8Json), options);
    }

    /// <summary>Asynchronously deserializes a workflow object from a UTF-8 byte array.</summary>
    public static Task<T> DeserializeFromUtf8BytesAsync<T>(this byte[] utf8Json, SerializationOptions? options = null, CancellationToken cancellationToken = default)
        where T : INotifyPropertyChanged
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(utf8Json.DeserializeFromUtf8Bytes<T>(options));
    }

    /// <summary>Asynchronously serializes a workflow object to a <see cref="TextWriter"/>.</summary>
    public static async Task SerializeToTextWriterAsync<T>(this T workflow, TextWriter writer, SerializationOptions? options = null, CancellationToken cancellationToken = default)
        where T : INotifyPropertyChanged
    {
        if (writer == null)
            throw new ArgumentNullException(nameof(writer), "Target writer cannot be null");
        if (workflow == null)
            throw new ArgumentNullException(nameof(workflow));

        cancellationToken.ThrowIfCancellationRequested();

        // 逐段写进 writer，不再先造一整份字符串 —— 这条路上内存有界。
        VeloxJsonSerializer.WriteTo(writer, workflow, options?.Formatting != VeloxJsonFormat.Compact, options?.ExcludedPropertyTypes);
        await writer.FlushAsync().ConfigureAwait(false);
    }

    /// <summary>Asynchronously deserializes a workflow object from a <see cref="TextReader"/>.</summary>
    public static async Task<T> DeserializeFromTextReaderAsync<T>(this TextReader reader, SerializationOptions? options = null, CancellationToken cancellationToken = default)
        where T : INotifyPropertyChanged
    {
        if (reader == null)
            throw new ArgumentNullException(nameof(reader), "Source reader cannot be null");

        cancellationToken.ThrowIfCancellationRequested();
        var json = await reader.ReadToEndAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return DeserializeCore<T>(json, options);
    }

    /// <summary>
    /// Asynchronously serializes a workflow object to a stream.
    /// </summary>
    public static async Task SerializeToStreamAsync<T>(this T workflow, Stream stream, SerializationOptions? options = null, CancellationToken cancellationToken = default)
        where T : INotifyPropertyChanged
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream), "Target stream cannot be null");
        if (!stream.CanWrite)
            throw new InvalidOperationException("Target stream is not writable");
        if (workflow == null)
            throw new ArgumentNullException(nameof(workflow));

        cancellationToken.ThrowIfCancellationRequested();
        using var streamWriter = new StreamWriter(stream, new UTF8Encoding(false), 1024, true);
        await workflow.SerializeToTextWriterAsync(streamWriter, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously deserializes a workflow object from a stream.
    /// </summary>
    public static async Task<T> DeserializeFromStreamAsync<T>(this Stream stream, SerializationOptions? options = null, CancellationToken cancellationToken = default)
        where T : INotifyPropertyChanged
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream), "Source stream cannot be null");
        if (!stream.CanRead)
            throw new InvalidOperationException("Source stream is not readable");

        using var streamReader = new StreamReader(stream, Encoding.UTF8, true, 1024, true);
        return await streamReader.DeserializeFromTextReaderAsync<T>(options, cancellationToken).ConfigureAwait(false);
    }

    #endregion
}
