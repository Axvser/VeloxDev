using Newtonsoft.Json;
using Newtonsoft.Json.Mutualization;
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
        private static readonly JsonMutualizerSettings settings = new()
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
        public static string Mutualize<T>(this T workflow)
            where T : IWorkflowTreeViewModel
        {
            return JsonConvert.MutualizeObject(workflow, settings);
        }

        public static bool TryDeMutualize<T>(this string json, out T? workflow)
            where T : IWorkflowTreeViewModel
        {
            try
            {
                workflow = JsonConvert.DeMutualizeObject<T>(json, settings);
                return workflow != null;
            }
            catch (Exception ex)
            {
                throw new JsonMutualizationException($"Failed to deMutualize JSON to type {typeof(T).Name}. Error: {ex.Message}", ex);
            }
        }
        #endregion

        #region Asynchronous Methods
        /// <summary>
        /// Asynchronously Mutualizes a workflow object to JSON string
        /// </summary>
        public static async Task<string> MutualizeAsync<T>(this T workflow, CancellationToken cancellationToken = default)
            where T : IWorkflowTreeViewModel
        {
            if (workflow == null)
                throw new ArgumentNullException(nameof(workflow), "Workflow object cannot be null for Mutualization");

            return await Task.Run(() => JsonConvert.MutualizeObject(workflow, settings), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Asynchronously attempts to deMutualize a JSON string to workflow object
        /// </summary>
        public static async Task<(bool Success, T? Result)> TryDeMutualizeAsync<T>(this string json, CancellationToken cancellationToken = default)
            where T : IWorkflowTreeViewModel
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("JSON string cannot be null or empty", nameof(json));

            try
            {
                var result = await Task.Run(() => JsonConvert.DeMutualizeObject<T>(json, settings), cancellationToken).ConfigureAwait(false);
                return (result != null, result);
            }
            catch
            {
                return (false, default(T));
            }
        }

        /// <summary>
        /// Asynchronously deMutualizes a JSON string to workflow object with exception handling
        /// </summary>
        public static async Task<T> DeMutualizeAsync<T>(this string json, CancellationToken cancellationToken = default)
            where T : IWorkflowTreeViewModel
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("JSON string cannot be null or empty", nameof(json));

            var result = await Task.Run(() => JsonConvert.DeMutualizeObject<T>(json, settings), cancellationToken).ConfigureAwait(false) ?? throw new JsonMutualizationException($"DeMutualization of JSON to type {typeof(T).Name} resulted in null. The JSON may be invalid or incompatible with the target type.");
            return result;
        }
        #endregion

        #region Streaming Asynchronous Methods (netstandard2.0 compatible)
        /// <summary>
        /// Asynchronously Mutualizes a workflow object to a stream
        /// </summary>
        public static async Task MutualizeToStreamAsync<T>(this T workflow, Stream stream, CancellationToken cancellationToken = default)
            where T : IWorkflowTreeViewModel
        {
            if (workflow == null)
                throw new ArgumentNullException(nameof(workflow), "Workflow object cannot be null for Mutualization");

            if (stream == null)
                throw new ArgumentNullException(nameof(stream), "Target stream cannot be null");

            if (!stream.CanWrite)
                throw new InvalidOperationException("Target stream is not writable");

            // Use proper StreamWriter constructor for netstandard2.0
            using var streamWriter = new StreamWriter(stream, Encoding.UTF8, 1024, true);
            using var jsonWriter = new JsonTextWriter(streamWriter);
            var Mutualizer = JsonMutualizer.Create(settings);

            await Task.Run(() => Mutualizer.Mutualize(jsonWriter, workflow), cancellationToken).ConfigureAwait(false);
            await streamWriter.FlushAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Asynchronously deMutualizes a workflow object from a stream
        /// </summary>
        public static async Task<T> DeMutualizeFromStreamAsync<T>(this Stream stream, CancellationToken cancellationToken = default)
            where T : IWorkflowTreeViewModel
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream), "Source stream cannot be null");

            if (!stream.CanRead)
                throw new InvalidOperationException("Source stream is not readable");

            // Use proper StreamReader constructor for netstandard2.0
            using var streamReader = new StreamReader(stream, Encoding.UTF8, true, 1024, true);
            using var jsonReader = new JsonTextReader(streamReader);
            var Mutualizer = JsonMutualizer.Create(settings);

            var result = await Task.Run(() => Mutualizer.DeMutualize<T>(jsonReader), cancellationToken).ConfigureAwait(false) ?? throw new JsonMutualizationException($"DeMutualization from stream to type {typeof(T).Name} resulted in null. The stream content may be invalid or incompatible with the target type.");
            return result;
        }

        /// <summary>
        /// Asynchronously attempts to deMutualize a workflow object from a stream
        /// </summary>
        public static async Task<(bool Success, T? Result)> TryDeMutualizeFromStreamAsync<T>(this Stream stream, CancellationToken cancellationToken = default)
            where T : IWorkflowTreeViewModel
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream), "Source stream cannot be null");

            try
            {
                var result = await DeMutualizeFromStreamAsync<T>(stream, cancellationToken).ConfigureAwait(false);
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
        public static async Task StreamMutualizeAsync<T>(this T workflow, Stream outputStream, CancellationToken cancellationToken = default)
            where T : IWorkflowTreeViewModel
        {
            if (workflow == null)
                throw new ArgumentNullException(nameof(workflow));
            if (outputStream == null)
                throw new ArgumentNullException(nameof(outputStream));

            var json = await MutualizeAsync(workflow, cancellationToken).ConfigureAwait(false);
            var jsonBytes = Encoding.UTF8.GetBytes(json);

            await outputStream.WriteAsync(jsonBytes, 0, jsonBytes.Length, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Alternative streaming deMutualization method (compatible with netstandard2.0)
        /// </summary>
        public static async Task<T> StreamDeMutualizeAsync<T>(this Stream inputStream, CancellationToken cancellationToken = default)
            where T : IWorkflowTreeViewModel
        {
            if (inputStream == null)
                throw new ArgumentNullException(nameof(inputStream));

            using var memoryStream = new MemoryStream();
            await inputStream.CopyToAsync(memoryStream, 81920, cancellationToken).ConfigureAwait(false);
            var json = Encoding.UTF8.GetString(memoryStream.ToArray());
            return await DeMutualizeAsync<T>(json, cancellationToken).ConfigureAwait(false);
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

        public override void WriteJson(JsonWriter writer, object? value, JsonMutualizer Mutualizer)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value), "Dictionary value cannot be null during JSON Mutualization");

            if (value is not IDictionary dict)
                throw new ArgumentException($"Expected IDictionary but got {value.GetType().Name}", nameof(value));

            if (Mutualizer.ReferenceResolver == null)
                throw new InvalidOperationException("JSON Mutualizer ReferenceResolver is not configured");

            writer.WriteStartObject();
            foreach (var key in dict.Keys)
            {
                if (key == null)
                    throw new JsonMutualizationException("Dictionary key cannot be null during Mutualization");

                string refId = Mutualizer.ReferenceResolver.GetReference(Mutualizer, key);
                writer.WritePropertyName(refId);
                Mutualizer.Mutualize(writer, dict[key]);
            }
            writer.WriteEndObject();
        }

        public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonMutualizer Mutualizer)
        {
            if (Mutualizer.ReferenceResolver == null)
                throw new InvalidOperationException("JSON Mutualizer ReferenceResolver is not configured");

            var valueType = objectType.GetGenericArguments()[1];
            var jo = Newtonsoft.Json.Linq.JObject.Load(reader);

            if (Activator.CreateInstance(objectType) is not IDictionary dict)
                throw new JsonMutualizationException($"Failed to create dictionary instance of type {objectType.Name}");

            foreach (var prop in jo.Properties())
            {
                string refId = prop.Name;
                var keyObject = Mutualizer.ReferenceResolver.ResolveReference(Mutualizer, refId) ?? throw new JsonMutualizationException($"Failed to resolve dictionary key reference: {refId}");
                var valueObject = prop.Value?.ToObject(valueType, Mutualizer);
                dict.Add(keyObject, valueObject);
            }

            return dict;
        }

        public override bool CanWrite => true;
        public override bool CanRead => true;
    }

    internal class WritablePropertiesOnlyResolver : DefaultContractResolver
    {
        protected override IList<JsonProperty> CreateProperties(Type type, MemberMutualization memberMutualization)
        {
            IList<JsonProperty> props = base.CreateProperties(type, memberMutualization);
            return [.. props.Where(p => p.Writable)];
        }
    }
}