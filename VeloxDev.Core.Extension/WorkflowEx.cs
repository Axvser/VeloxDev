using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.Core.Interfaces.WorkflowSystem;

namespace VeloxDev.Core.Extension
{
    public static class WorkflowEx
    {
        private static readonly JsonSerializerSettings settings = new()
        {
            Formatting = Formatting.Indented,
            TypeNameHandling = TypeNameHandling.Auto,
            PreserveReferencesHandling = PreserveReferencesHandling.Objects,
            NullValueHandling = NullValueHandling.Include,
            DefaultValueHandling = DefaultValueHandling.Include,
            ContractResolver = new WritablePropertiesOnlyResolver(),
            Converters = [new DictionaryKeyConverter()]
        };

        #region Synchronous Methods
        public static string Serialize<T>(this T workflow)
            where T : IWorkflowTreeViewModel
        {
            return JsonConvert.SerializeObject(workflow, settings);
        }

        public static bool TryDeserialize<T>(this string json, out T? workflow)
            where T : IWorkflowTreeViewModel
        {
            try
            {
                workflow = JsonConvert.DeserializeObject<T>(json, settings);
                return workflow != null;
            }
            catch (Exception ex)
            {
                throw new JsonSerializationException($"Failed to deserialize JSON to type {typeof(T).Name}. Error: {ex.Message}", ex);
            }
        }
        #endregion

        #region Asynchronous Methods
        /// <summary>
        /// Asynchronously serializes a workflow object to JSON string
        /// </summary>
        public static async Task<string> SerializeAsync<T>(this T workflow, CancellationToken cancellationToken = default)
            where T : IWorkflowTreeViewModel
        {
            if (workflow == null)
                throw new ArgumentNullException(nameof(workflow), "Workflow object cannot be null for serialization");

            return await Task.Run(() => JsonConvert.SerializeObject(workflow, settings), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Asynchronously attempts to deserialize a JSON string to workflow object
        /// </summary>
        public static async Task<(bool Success, T? Result)> TryDeserializeAsync<T>(this string json, CancellationToken cancellationToken = default)
            where T : IWorkflowTreeViewModel
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("JSON string cannot be null or empty", nameof(json));

            try
            {
                var result = await Task.Run(() => JsonConvert.DeserializeObject<T>(json, settings), cancellationToken).ConfigureAwait(false);
                return (result != null, result);
            }
            catch
            {
                return (false, default(T));
            }
        }

        /// <summary>
        /// Asynchronously deserializes a JSON string to workflow object with exception handling
        /// </summary>
        public static async Task<T> DeserializeAsync<T>(this string json, CancellationToken cancellationToken = default)
            where T : IWorkflowTreeViewModel
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("JSON string cannot be null or empty", nameof(json));

            var result = await Task.Run(() => JsonConvert.DeserializeObject<T>(json, settings), cancellationToken).ConfigureAwait(false) ?? throw new JsonSerializationException($"Deserialization of JSON to type {typeof(T).Name} resulted in null. The JSON may be invalid or incompatible with the target type.");
            return result;
        }
        #endregion

        #region Streaming Asynchronous Methods (netstandard2.0 compatible)
        /// <summary>
        /// Asynchronously serializes a workflow object to a stream
        /// </summary>
        public static async Task SerializeToStreamAsync<T>(this T workflow, Stream stream, CancellationToken cancellationToken = default)
            where T : IWorkflowTreeViewModel
        {
            if (workflow == null)
                throw new ArgumentNullException(nameof(workflow), "Workflow object cannot be null for serialization");

            if (stream == null)
                throw new ArgumentNullException(nameof(stream), "Target stream cannot be null");

            if (!stream.CanWrite)
                throw new InvalidOperationException("Target stream is not writable");

            // Use proper StreamWriter constructor for netstandard2.0
            using var streamWriter = new StreamWriter(stream, Encoding.UTF8, 1024, true);
            using var jsonWriter = new JsonTextWriter(streamWriter);
            var serializer = JsonSerializer.Create(settings);

            await Task.Run(() => serializer.Serialize(jsonWriter, workflow), cancellationToken).ConfigureAwait(false);
            await streamWriter.FlushAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Asynchronously deserializes a workflow object from a stream
        /// </summary>
        public static async Task<T> DeserializeFromStreamAsync<T>(this Stream stream, CancellationToken cancellationToken = default)
            where T : IWorkflowTreeViewModel
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream), "Source stream cannot be null");

            if (!stream.CanRead)
                throw new InvalidOperationException("Source stream is not readable");

            // Use proper StreamReader constructor for netstandard2.0
            using var streamReader = new StreamReader(stream, Encoding.UTF8, true, 1024, true);
            using var jsonReader = new JsonTextReader(streamReader);
            var serializer = JsonSerializer.Create(settings);

            var result = await Task.Run(() => serializer.Deserialize<T>(jsonReader), cancellationToken).ConfigureAwait(false) ?? throw new JsonSerializationException($"Deserialization from stream to type {typeof(T).Name} resulted in null. The stream content may be invalid or incompatible with the target type.");
            return result;
        }

        /// <summary>
        /// Asynchronously attempts to deserialize a workflow object from a stream
        /// </summary>
        public static async Task<(bool Success, T? Result)> TryDeserializeFromStreamAsync<T>(this Stream stream, CancellationToken cancellationToken = default)
            where T : IWorkflowTreeViewModel
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
            where T : IWorkflowTreeViewModel
        {
            if (workflow == null)
                throw new ArgumentNullException(nameof(workflow));
            if (outputStream == null)
                throw new ArgumentNullException(nameof(outputStream));

            var json = await SerializeAsync(workflow, cancellationToken).ConfigureAwait(false);
            var jsonBytes = Encoding.UTF8.GetBytes(json);

            await outputStream.WriteAsync(jsonBytes, 0, jsonBytes.Length, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Alternative streaming deserialization method (compatible with netstandard2.0)
        /// </summary>
        public static async Task<T> StreamDeserializeAsync<T>(this Stream inputStream, CancellationToken cancellationToken = default)
            where T : IWorkflowTreeViewModel
        {
            if (inputStream == null)
                throw new ArgumentNullException(nameof(inputStream));

            using var memoryStream = new MemoryStream();
            await inputStream.CopyToAsync(memoryStream, 81920, cancellationToken).ConfigureAwait(false);
            var json = Encoding.UTF8.GetString(memoryStream.ToArray());
            return await DeserializeAsync<T>(json, cancellationToken).ConfigureAwait(false);
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
}