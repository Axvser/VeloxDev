using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VeloxDev.MVVM.Serialization;

public static class ComponentModelEx
{
    private static readonly JsonSerializerSettings settings = new()
    {
        Formatting = Formatting.Indented,
        TypeNameHandling = TypeNameHandling.Auto,
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        NullValueHandling = NullValueHandling.Include,
        DefaultValueHandling = DefaultValueHandling.Include,
        ContractResolver = new WritablePropertiesOnlyResolver(),
        Converters = [new DictionaryKeyConverter()]
    };

    internal static JsonSerializer CreateJsonSerializer()
        => JsonSerializer.Create(settings);

    private static string SerializeCore<T>(T workflow)
        where T : INotifyPropertyChanged
    {
        if (workflow == null)
            throw new ArgumentNullException(nameof(workflow), "Workflow object cannot be null for serialization");

        return JsonConvert.SerializeObject(workflow, settings);
    }

    private static bool TryDeserializeCore<T>(string json, out T? workflow)
        where T : INotifyPropertyChanged
    {
        try
        {
            workflow = JsonConvert.DeserializeObject<T>(json, settings);
            return workflow != null;
        }
        catch
        {
            workflow = default;
            return false;
        }
    }

    private static T DeserializeCore<T>(string json)
        where T : INotifyPropertyChanged
    {
        if (!TryDeserializeCore<T>(json, out var workflow) || workflow == null)
            throw new JsonSerializationException($"Deserialization of JSON to type {typeof(T).Name} resulted in null. The JSON may be invalid or incompatible with the target type.");

        return workflow;
    }

    internal static object? DeserializeToType(this JToken token, Type targetType)
    {
        if (token == null)
            throw new ArgumentNullException(nameof(token));
        if (targetType == null)
            throw new ArgumentNullException(nameof(targetType));

        return token.Type == JTokenType.Null
            ? null
            : token.ToObject(targetType, CreateJsonSerializer());
    }

    #region Synchronous Methods
    public static string Serialize<T>(this T workflow)
        where T : INotifyPropertyChanged
    {
        return SerializeCore(workflow);
    }

    public static bool TryDeserialize<T>(this string json, out T? workflow)
        where T : INotifyPropertyChanged
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("JSON string cannot be null or empty", nameof(json));

        return TryDeserializeCore(json, out workflow);
    }

    public static T Deserialize<T>(this string json)
        where T : INotifyPropertyChanged
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("JSON string cannot be null or empty", nameof(json));

        return DeserializeCore<T>(json);
    }
    #endregion

    #region Asynchronous Methods
    /// <summary>
    /// Asynchronously serializes a workflow object to JSON string
    /// </summary>
    public static Task<string> SerializeAsync<T>(this T workflow, CancellationToken cancellationToken = default)
        where T : INotifyPropertyChanged
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => SerializeCore(workflow), cancellationToken);
    }

    /// <summary>
    /// Asynchronously attempts to deserialize a JSON string to workflow object
    /// </summary>
    public static Task<(bool Success, T? Result)> TryDeserializeAsync<T>(this string json, CancellationToken cancellationToken = default)
        where T : INotifyPropertyChanged
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("JSON string cannot be null or empty", nameof(json));

        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() =>
        {
            var success = TryDeserializeCore<T>(json, out var result);
            return (success, result);
        }, cancellationToken);
    }

    /// <summary>
    /// Asynchronously deserializes a JSON string to workflow object with exception handling
    /// </summary>
    public static Task<T> DeserializeAsync<T>(this string json, CancellationToken cancellationToken = default)
        where T : INotifyPropertyChanged
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("JSON string cannot be null or empty", nameof(json));

        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => DeserializeCore<T>(json), cancellationToken);
    }
    #endregion

    #region Streaming Asynchronous Methods (netstandard2.0 compatible)
    public static byte[] SerializeToUtf8Bytes<T>(this T workflow)
        where T : INotifyPropertyChanged
    {
        return Encoding.UTF8.GetBytes(SerializeCore(workflow));
    }

    public static Task<byte[]> SerializeToUtf8BytesAsync<T>(this T workflow, CancellationToken cancellationToken = default)
        where T : INotifyPropertyChanged
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => workflow.SerializeToUtf8Bytes(), cancellationToken);
    }

    public static T DeserializeFromUtf8Bytes<T>(this byte[] utf8Json)
        where T : INotifyPropertyChanged
    {
        if (utf8Json == null)
            throw new ArgumentNullException(nameof(utf8Json), "UTF-8 JSON payload cannot be null");
        if (utf8Json.Length == 0)
            throw new ArgumentException("UTF-8 JSON payload cannot be empty", nameof(utf8Json));

        return DeserializeCore<T>(Encoding.UTF8.GetString(utf8Json));
    }

    public static Task<T> DeserializeFromUtf8BytesAsync<T>(this byte[] utf8Json, CancellationToken cancellationToken = default)
        where T : INotifyPropertyChanged
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => utf8Json.DeserializeFromUtf8Bytes<T>(), cancellationToken);
    }

    public static async Task SerializeToTextWriterAsync<T>(this T workflow, TextWriter writer, CancellationToken cancellationToken = default)
        where T : INotifyPropertyChanged
    {
        if (writer == null)
            throw new ArgumentNullException(nameof(writer), "Target writer cannot be null");

        cancellationToken.ThrowIfCancellationRequested();
        var json = await Task.Run(() => SerializeCore(workflow), cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync(json).ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public static async Task<T> DeserializeFromTextReaderAsync<T>(this TextReader reader, CancellationToken cancellationToken = default)
        where T : INotifyPropertyChanged
    {
        if (reader == null)
            throw new ArgumentNullException(nameof(reader), "Source reader cannot be null");

        cancellationToken.ThrowIfCancellationRequested();
        var json = await reader.ReadToEndAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return await Task.Run(() => DeserializeCore<T>(json), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously serializes a workflow object to a stream
    /// </summary>
    public static async Task SerializeToStreamAsync<T>(this T workflow, Stream stream, CancellationToken cancellationToken = default)
        where T : INotifyPropertyChanged
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream), "Target stream cannot be null");

        if (!stream.CanWrite)
            throw new InvalidOperationException("Target stream is not writable");

        using var streamWriter = new StreamWriter(stream, new UTF8Encoding(false), 1024, true);
        await workflow.SerializeToTextWriterAsync(streamWriter, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously deserializes a workflow object from a stream
    /// </summary>
    public static async Task<T> DeserializeFromStreamAsync<T>(this Stream stream, CancellationToken cancellationToken = default)
        where T : INotifyPropertyChanged
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream), "Source stream cannot be null");

        if (!stream.CanRead)
            throw new InvalidOperationException("Source stream is not readable");

        using var streamReader = new StreamReader(stream, Encoding.UTF8, true, 1024, true);
        return await streamReader.DeserializeFromTextReaderAsync<T>(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously attempts to deserialize a workflow object from a stream
    /// </summary>
    public static async Task<(bool Success, T? Result)> TryDeserializeFromStreamAsync<T>(this Stream stream, CancellationToken cancellationToken = default)
        where T : INotifyPropertyChanged
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream), "Source stream cannot be null");

        try
        {
            var result = await DeserializeFromStreamAsync<T>(stream, cancellationToken).ConfigureAwait(false);
            return (true, result);
        }
        catch
        {
            return (false, default(T));
        }
    }

    /// <summary>
    /// Alternative streaming method using Task-based approach (compatible with netstandard2.0)
    /// </summary>
    public static async Task StreamSerializeAsync<T>(this T workflow, Stream outputStream, CancellationToken cancellationToken = default)
        where T : INotifyPropertyChanged
    {
        await workflow.SerializeToStreamAsync(outputStream, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Alternative streaming deserialization method (compatible with netstandard2.0)
    /// </summary>
    public static async Task<T> StreamDeserializeAsync<T>(this Stream inputStream, CancellationToken cancellationToken = default)
        where T : INotifyPropertyChanged
    {
        return await inputStream.DeserializeFromStreamAsync<T>(cancellationToken).ConfigureAwait(false);
    }
    #endregion
}

internal sealed class DictionaryKeyConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return objectType.IsGenericType &&
               objectType.GetGenericTypeDefinition() == typeof(Dictionary<,>) &&
               objectType.GetGenericArguments()[0].IsInterface;
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value == null)
            throw new ArgumentNullException(nameof(value), "Dictionary value cannot be null during JSON serialization");

        if (value is not IDictionary dict)
            throw new ArgumentException($"Expected IDictionary but got {value.GetType().Name}", nameof(value));

        if (serializer.ReferenceResolver == null)
            throw new InvalidOperationException("JSON serializer ReferenceResolver is not configured");

        writer.WriteStartObject();
        foreach (var key in dict.Keys)
        {
            if (key == null)
                throw new JsonSerializationException("Dictionary key cannot be null during serialization");

            string refId = serializer.ReferenceResolver.GetReference(serializer, key);
            writer.WritePropertyName(refId);
            serializer.Serialize(writer, dict[key]);
        }
        writer.WriteEndObject();
    }

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        if (serializer.ReferenceResolver == null)
            throw new InvalidOperationException("JSON serializer ReferenceResolver is not configured");

        var valueType = objectType.GetGenericArguments()[1];
        var jo = Newtonsoft.Json.Linq.JObject.Load(reader);

        if (Activator.CreateInstance(objectType) is not IDictionary dict)
            throw new JsonSerializationException($"Failed to create dictionary instance of type {objectType.Name}");

        foreach (var prop in jo.Properties())
        {
            string refId = prop.Name;
            var keyObject = serializer.ReferenceResolver.ResolveReference(serializer, refId) ?? throw new JsonSerializationException($"Failed to resolve dictionary key reference: {refId}");
            var valueObject = prop.Value?.ToObject(valueType, serializer);
            dict.Add(keyObject, valueObject);
        }

        return dict;
    }

    public override bool CanWrite => true;
    public override bool CanRead => true;
}

internal class WritablePropertiesOnlyResolver : DefaultContractResolver
{
    protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
    {
        IList<JsonProperty> props = base.CreateProperties(type, memberSerialization);
        return [.. props.Where(p => p.Writable)];
    }
}