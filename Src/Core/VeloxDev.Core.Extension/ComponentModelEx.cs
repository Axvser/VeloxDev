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

/// <summary>
/// Per-call serialization options built via a Fluent API.
/// Construct an instance with <see cref="SerializationOptions.Create"/>,
/// chain the desired overrides, then pass it to the
/// <c>Serialize</c> / <c>Deserialize</c> / <c>TryDeserialize</c> overloads.
/// </summary>
public sealed class SerializationOptions
{
    internal Formatting? Formatting { get; private set; }
    internal TypeNameHandling? TypeNameHandling { get; private set; }
    internal NullValueHandling? NullValueHandling { get; private set; }
    internal DefaultValueHandling? DefaultValueHandling { get; private set; }
    private SerializationOptions() { }

    /// <summary>Creates a new blank options builder.</summary>
    public static SerializationOptions Create() => new();

    /// <summary>Produce indented (human-readable) JSON output.</summary>
    public SerializationOptions WithIndented() { Formatting = Newtonsoft.Json.Formatting.Indented; return this; }

    /// <summary>Produce compact JSON output (no extra whitespace).</summary>
    public SerializationOptions WithCompact() { Formatting = Newtonsoft.Json.Formatting.None; return this; }

    /// <summary>Override <see cref="Newtonsoft.Json.TypeNameHandling"/>.</summary>
    public SerializationOptions WithTypeNameHandling(TypeNameHandling value) { TypeNameHandling = value; return this; }

    /// <summary>Override <see cref="Newtonsoft.Json.NullValueHandling"/>.</summary>
    public SerializationOptions WithNullValueHandling(NullValueHandling value) { NullValueHandling = value; return this; }

    /// <summary>Override <see cref="Newtonsoft.Json.DefaultValueHandling"/>.</summary>
    public SerializationOptions WithDefaultValueHandling(DefaultValueHandling value) { DefaultValueHandling = value; return this; }

    }

public static class ComponentModelEx
{
    // Settings (and their ContractResolver) are cached: Newtonsoft.Json caches
    // JsonContract objects on the resolver instance, so re-creating the resolver
    // per call defeats the cache and causes the type system to be re-reflected
    // on every (de)serialization.
    private static readonly object _settingsGate = new();
    private static JsonSerializerSettings? _indentedSettingsCache;
    private static JsonSerializerSettings? _compactSettingsCache;

    private static JsonSerializerSettings BuildSettings(Formatting formatting) => new()
    {
        Formatting = formatting,
        TypeNameHandling = TypeNameHandling.Auto,
        PreserveReferencesHandling = PreserveReferencesHandling.Objects,
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        NullValueHandling = NullValueHandling.Include,
        DefaultValueHandling = DefaultValueHandling.Include,
        ContractResolver = new WritablePropertiesOnlyResolver(),
        Converters = [new DictionaryKeyConverter()]
    };

    // Both settings variants are cached so the resolver's contract cache is
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

    /// <summary>
    /// Applies a <see cref="SerializationOptions"/> on top of the global base settings,
    /// returning a fresh (non-cached) settings object for one-off use.
    /// When <paramref name="options"/> is null the cached IndentedSettings are returned.
    /// </summary>
    private static JsonSerializerSettings ResolveSettings(SerializationOptions? options)
    {
        if (options == null)
            return IndentedSettings;

        // Start from the current global indented baseline so defaults are consistent.
        var s = new JsonSerializerSettings
        {
            Formatting                   = options.Formatting           ?? Formatting.Indented,
            TypeNameHandling             = options.TypeNameHandling      ?? TypeNameHandling.Auto,
            PreserveReferencesHandling   = PreserveReferencesHandling.Objects,
            ReferenceLoopHandling        = ReferenceLoopHandling.Ignore,
            NullValueHandling            = options.NullValueHandling     ?? NullValueHandling.Include,
            DefaultValueHandling         = options.DefaultValueHandling  ?? DefaultValueHandling.Include,
            ContractResolver             = new WritablePropertiesOnlyResolver(),
            Converters                   = [new DictionaryKeyConverter()],
        };
        return s;
    }

    private static string SerializeCore<T>(T workflow, SerializationOptions? options = null)
        where T : INotifyPropertyChanged
    {
        if (workflow == null)
            throw new ArgumentNullException(nameof(workflow), "Workflow object cannot be null for serialization");

        return JsonConvert.SerializeObject(workflow, ResolveSettings(options));
    }

    private static bool TryDeserializeCore<T>(string json, out T? workflow, SerializationOptions? options = null)
        where T : INotifyPropertyChanged
    {
        try
        {
            workflow = JsonConvert.DeserializeObject<T>(json, ResolveSettings(options));
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
        var result = JsonConvert.DeserializeObject<T>(json, ResolveSettings(options));
        if (result == null)
            throw new JsonSerializationException($"Deserialization of JSON to type {typeof(T).Name} resulted in null. The JSON may be invalid or incompatible with the target type.");

        return result;
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
        var json = SerializeCore(workflow, options);
        cancellationToken.ThrowIfCancellationRequested();
        await writer.WriteAsync(json).ConfigureAwait(false);
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
    protected override JsonContract CreateContract(Type objectType)
    {
        // Types that implement IEnumerable but also have a default constructor
        // and writable properties (e.g. SlotEnumerator<T>) must be treated as
        // plain objects, not as JSON arrays.
        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(objectType)
            && objectType != typeof(string)
            && objectType.GetConstructor(Type.EmptyTypes) != null
            && !(objectType.IsArray)
            && !IsNativeCollection(objectType))
        {
            return base.CreateObjectContract(objectType);
        }

        return base.CreateContract(objectType);
    }

    protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
    {
        IList<JsonProperty> props = base.CreateProperties(type, memberSerialization);
        return [.. props.Where(p => p.Writable)];
    }

    // Returns true for BCL collection types that should keep their default
    // array/dictionary contract (List<T>, ObservableCollection<T>, Dictionary<,>, …).
    private static bool IsNativeCollection(Type type)
    {
        if (type.IsGenericType)
        {
            var def = type.GetGenericTypeDefinition();
            if (def == typeof(System.Collections.Generic.List<>)
                || def == typeof(System.Collections.ObjectModel.ObservableCollection<>)
                || def == typeof(System.Collections.Generic.Dictionary<,>)
                || def == typeof(System.Collections.Generic.HashSet<>)
                || def == typeof(System.Collections.Generic.Queue<>)
                || def == typeof(System.Collections.Generic.Stack<>))
                return true;
        }

        var ns = type.Namespace;
        return ns != null
            && (ns.StartsWith("System.Collections", StringComparison.Ordinal)
                || ns.StartsWith("System.Linq", StringComparison.Ordinal));
    }
}

// AllowListSerializationBinder intentionally removed.
// VeloxDev serialization targets local workflow files authored by the same
// application, not untrusted network payloads, so the default Newtonsoft.Json
// binder (DefaultSerializationBinder) is the right choice: it resolves every
// loaded type without any prefix restrictions, matching the 4x behaviour.