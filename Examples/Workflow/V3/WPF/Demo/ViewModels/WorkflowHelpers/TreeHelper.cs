using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.ViewModels.WorkflowHelpers
{
    public class TreeHelper : WorkflowHelper.ViewModel.Tree
    {
        public static readonly JsonSerializerSettings js_settings = new()
        {
            Formatting = Formatting.Indented,
            TypeNameHandling = TypeNameHandling.Auto,
            PreserveReferencesHandling = PreserveReferencesHandling.Objects,
            NullValueHandling = NullValueHandling.Include,
            DefaultValueHandling = DefaultValueHandling.Include,
            ContractResolver = new WritablePropertiesOnlyResolver(),
            Converters = [ new DictionaryKeyConverter() ]
        };

        public override bool ValidateConnection(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
        {
            // 可以替换默认的验证
            return base.ValidateConnection(sender, receiver);
        }
    }

    class DictionaryKeyConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType.IsGenericType &&
                   objectType.GetGenericTypeDefinition() == typeof(Dictionary<,>) &&
                   objectType.GetGenericArguments()[0].IsInterface;
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            var dict = (System.Collections.IDictionary)value;

            writer.WriteStartObject();

            foreach (var key in dict.Keys)
            {
                // ⭐ 关键：使用 JSON.NET 自己的引用 ID
                string refId = serializer.ReferenceResolver.GetReference(serializer, key);

                writer.WritePropertyName(refId);
                serializer.Serialize(writer, dict[key]);
            }

            writer.WriteEndObject();
        }

        public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            var keyType = objectType.GetGenericArguments()[0];
            var valueType = objectType.GetGenericArguments()[1];

            var jo = Newtonsoft.Json.Linq.JObject.Load(reader);
            var dict = (System.Collections.IDictionary)Activator.CreateInstance(objectType)!;

            foreach (var prop in jo.Properties())
            {
                string refId = prop.Name;

                // ⭐ 使用 JSON.NET 引用解析系统恢复真实对象
                var keyObject = serializer.ReferenceResolver.ResolveReference(serializer, refId);

                var valueObject = prop.Value.ToObject(valueType, serializer);

                dict.Add(keyObject, valueObject);
            }

            return dict;
        }

        public override bool CanWrite => true;
        public override bool CanRead => true;
    }


    class WritablePropertiesOnlyResolver : DefaultContractResolver
    {
        protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
        {
            IList<JsonProperty> props = base.CreateProperties(type, memberSerialization);
            return [.. props.Where(p => p.Writable)];
        }
    }
}
