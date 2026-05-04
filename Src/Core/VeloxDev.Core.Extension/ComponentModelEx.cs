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
    // Default binder restricts polymorphic deserialization to types coming from
    // assemblies whose name starts with "VeloxDev" or core BCL prefixes. This
    // mitigates the well-known TypeNameHandling.Auto attack surface where a
    // crafted JSON file could otherwise instantiate arbitrary types.
    // Consumers can opt in to a stricter (or different) policy via
    // ConfigureSerializationBinder.
    private static ISerializationBinder _serializationBinder = new AllowListSerializationBinder();

    // Settings (and their ContractResolver) are cached: Newtonsoft.Json caches
    // JsonContract objects on the resolver instance, so re-creating the resolver
    // per call defeats the cache and causes the type system to be re-reflected
    // on every (de)serialization. We invalidate the cache only when the binder
    // is replaced via ConfigureSerializationBinder.
    private static readonly object _settingsGate = new();
    private static JsonSerializerSettings? _indentedSettingsCache;
    private static JsonSerializerSettings? _compactSettingsCache;

    /// <summary>
    /// Replace the default <see cref="ISerializationBinder"/> used for all
    /// (de)serialization. Provide a stricter binder for security-sensitive
    /// scenarios (loading user files, network payloads, etc.).
    /// </summary>
    public static void ConfigureSerializationBinder(ISerializationBinder binder)
    {
        if (binder == null)
            throw new ArgumentNullException(nameof(binder));

        lock (_settingsGate)
        {
            _serializationBinder = binder;
            _indentedSettingsCache = null;
            _compactSettingsCache = null;
        }
    }

    private static JsonSerializerSettings BuildSettings(Formatting formatting) => new()
    {
        Formatting = formatting,
        TypeNameHandling = TypeNameHandling.Auto,
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        NullValueHandling = NullValueHandling.Include,
        DefaultValueHandling = DefaultValueHandling.Include,
        ContractResolver = new WritablePropertiesOnlyResolver(),
        SerializationBinder = _serializationBinder,
        Converters = [new DictionaryKeyConverter()]
    };

    // Indented for string-facing APIs (human-readable / diff-friendly), compact
    // for stream/byte APIs (file IO, network, browser downloads) where size and
    // parse time matter. Both are cached so the resolver's contract cache is
    // reused across calls.
    private static JsonSerializerSettings IndentedSettings
    {
        get
        {
            var cached = _indentedSettingsCache;
            if (cached is not null)
                return cached;

            lock (_settingsGate)
            {
                return _indentedSettingsCache ??= BuildSettings(Formatting.Indented);
            }
        }
    }

    private static JsonSerializerSettings CompactSettings
    {
        get
        {
            var cached = _compactSettingsCache;
            if (cached is not null)
                return cached;

            lock (_settingsGate)
            {
                return _compactSettingsCache ??= BuildSettings(Formatting.None);
            }
        }
    }

    internal static JsonSerializer CreateJsonSerializer()
        => JsonSerializer.Create(IndentedSettings);

    private static string SerializeCore<T>(T workflow, Formatting formatting = Formatting.Indented)
        where T : INotifyPropertyChanged
    {
        if (workflow == null)
            throw new ArgumentNullException(nameof(workflow), "Workflow object cannot be null for serialization");

        return JsonConvert.SerializeObject(workflow, formatting == Formatting.None ? CompactSettings : IndentedSettings);
    }

    private static bool TryDeserializeCore<T>(string json, out T? workflow)
        where T : INotifyPropertyChanged
    {
        try
        {
            workflow = JsonConvert.DeserializeObject<T>(json, IndentedSettings);
            return workflow != null;
        }
        // Only swallow JSON-shape failures. Real faults (OOM, cancellation,
        // stack overflow) must propagate.
        catch (JsonException)
        {
            workflow = default;
            return false;
        }
        catch (IOException)
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
        // Try-pattern contract: never throw on bad input. Caller is expected
        // to branch on the boolean result.
        if (string.IsNullOrWhiteSpace(json))
        {
            workflow = default;
            return false;
        }

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
        // Compact formatting for byte/file payloads: ~2x smaller than indented
        // and proportionally faster to parse on reload.
        return Encoding.UTF8.GetBytes(SerializeCore(workflow, Formatting.None));
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
        if (workflow == null)
            throw new ArgumentNullException(nameof(workflow));

        cancellationToken.ThrowIfCancellationRequested();

        // Serialize on a worker thread when available, but write to the
        // caller-owned TextWriter on the calling thread. JsonTextWriter is
        // not safe to dispose on a different thread than the underlying writer
        // (especially when the writer wraps a FileStream opened with
        // useAsync:true), so we fully build the JSON first and then push it
        // through the writer with a single asynchronous write.
        var json = await Task.Run(() => SerializeCore(workflow, Formatting.None), cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        await writer.WriteAsync(json).ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);
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
        if (workflow == null)
            throw new ArgumentNullException(nameof(workflow));

        cancellationToken.ThrowIfCancellationRequested();

        // Build the UTF-8 payload off the UI thread, then write it in one
        // async call. This avoids two prior failure modes:
        //   1. StreamWriter wrapping a FileStream(useAsync:true) being flushed
        //      on a worker thread caused 0-byte files when the underlying
        //      stream had already been truncated by FileMode.Create.
        //   2. Browser write streams (Avalonia.Browser) reject Seek/SetLength
        //      and any second-pass writes; a single WriteAsync is the only
        //      portable shape.
        var payload = await Task.Run(() => workflow.SerializeToUtf8Bytes(), cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        await stream.WriteAsync(payload, 0, payload.Length, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously deserializes a workflow object from a stream.
    /// </summary>
    /// <remarks>
    /// On Avalonia.Browser (WASM), the underlying <see cref="StreamReader.ReadToEndAsync()"/>
    /// path may fall back to synchronous reads on JS-backed streams and block the UI thread.
    /// Callers on browser targets should buffer the source stream into a
    /// <see cref="MemoryStream"/> via <c>CopyToAsync</c> first, then pass that buffer here.
    /// </remarks>
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

/// <summary>
/// Default <see cref="ISerializationBinder"/> that limits TypeNameHandling.Auto
/// deserialization to a safe set of assemblies. By default it accepts types
/// whose assembly simple name starts with <c>VeloxDev</c> plus common BCL
/// collection assemblies. This blocks the well-known polymorphic-deserialization
/// gadget surface where untrusted JSON could otherwise instantiate arbitrary
/// types from the loaded process.
/// </summary>
public sealed class AllowListSerializationBinder : ISerializationBinder
{
    private static readonly string[] DefaultAllowedAssemblyPrefixes =
    [
        "VeloxDev",
        "System.Private.CoreLib",
        "mscorlib",
        "System.Collections",
        "System.ObjectModel",
    ];

    private readonly string[] _allowedAssemblyPrefixes;

    public AllowListSerializationBinder()
        : this(DefaultAllowedAssemblyPrefixes) { }

    public AllowListSerializationBinder(IEnumerable<string> allowedAssemblyPrefixes)
    {
        if (allowedAssemblyPrefixes == null)
            throw new ArgumentNullException(nameof(allowedAssemblyPrefixes));

        _allowedAssemblyPrefixes = allowedAssemblyPrefixes.ToArray();
    }

    public Type BindToType(string? assemblyName, string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            throw new JsonSerializationException("Empty type name in JSON $type metadata.");

        var assemblySimpleName = assemblyName?.Split(',')[0]?.Trim() ?? string.Empty;
        var allowed = _allowedAssemblyPrefixes.Any(prefix =>
            assemblySimpleName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        if (!allowed)
            throw new JsonSerializationException(
                $"Type '{typeName}' from assembly '{assemblySimpleName}' is not in the deserialization allow-list.");

        var qualifiedName = string.IsNullOrEmpty(assemblyName)
            ? typeName
            : $"{typeName}, {assemblyName}";

        var type = Type.GetType(qualifiedName, throwOnError: false);
        if (type == null)
            throw new JsonSerializationException($"Could not resolve type '{qualifiedName}'.");

        return type;
    }

    public void BindToName(Type serializedType, out string? assemblyName, out string? typeName)
    {
        assemblyName = serializedType.Assembly.GetName().Name;
        typeName = serializedType.FullName;
    }
}