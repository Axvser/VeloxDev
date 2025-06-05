using System.Collections.Generic;
using System.Text;

namespace VeloxDev.WPF.Generator.Factory
{
    public class PropertyFactory : IFactory
    {
        const string RETRACT = "      ";

        public PropertyFactory(FieldRoslyn fieldRoslyn, string modifies, bool isView)
        {
            Modifies = modifies;
            FullTypeName = fieldRoslyn.TypeName;
            SourceName = fieldRoslyn.FieldName;
            PropertyName = fieldRoslyn.PropertyName;
            IsView = isView;
        }

        public PropertyFactory(string modifies, string fullTypeName, string sourceName, string propertyName, bool isView)
        {
            Modifies = modifies;
            FullTypeName = fullTypeName;
            SourceName = sourceName;
            PropertyName = propertyName;
            IsView = isView;
        }

        public string Modifies { get; private set; }
        public string FullTypeName { get; private set; }
        public string SourceName { get; private set; }
        public string PropertyName { get; private set; }

        public List<string> SetteringBody { get; set; } = [];
        public List<string> SetteredBody { get; set; } = [];
        public List<string> AttributeBody { get; set; } = [];

        private bool IsView { get; set; }

        public string GenerateViewModel()
        {
            var setteringBody = new StringBuilder();
            for (int i = 0; i < SetteringBody.Count; i++)
            {
                if (i == SetteringBody.Count - 1)
                {
                    setteringBody.Append($"{RETRACT}       {SetteringBody[i]}");
                }
                else
                {
                    setteringBody.AppendLine($"{RETRACT}       {SetteringBody[i]}");
                }
            }

            var setteredBody = new StringBuilder();
            for (int i = 0; i < SetteredBody.Count; i++)
            {
                if (i == SetteredBody.Count - 1)
                {
                    setteredBody.Append($"{RETRACT}       {SetteredBody[i]}");
                }
                else
                {
                    setteredBody.AppendLine($"{RETRACT}       {SetteredBody[i]}");
                }
            }

            return $$"""
                {{RETRACT}}{{Modifies}} {{FullTypeName}} {{PropertyName}}
                {{RETRACT}}{
                {{RETRACT}}    get => {{SourceName}}; 
                {{RETRACT}}    set 
                {{RETRACT}}    { 
                {{RETRACT}}       var old = {{SourceName}}; 
                {{RETRACT}}       On{{PropertyName}}Changing(old,value); 
                {{setteringBody.ToString()}}
                {{RETRACT}}       {{SourceName}} = value; 
                {{RETRACT}}       On{{PropertyName}}Changed(old,value); 
                {{setteredBody.ToString()}} 
                {{RETRACT}}    } 
                {{RETRACT}}}
                {{RETRACT}}partial void On{{PropertyName}}Changing({{FullTypeName}} oldValue,{{FullTypeName}} newValue); 
                {{RETRACT}}partial void On{{PropertyName}}Changed({{FullTypeName}} oldValue,{{FullTypeName}} newValue); 
                """;
        }
        public string Generate()
        {
            return IsView ? GenerateProxy() : GenerateViewModel();
        }
        public string GenerateProxy()
        {
            var attributeBody = new StringBuilder();
            for (int i = 0; i < AttributeBody.Count; i++)
            {
                if (i == AttributeBody.Count - 1)
                {
                    attributeBody.Append($"{RETRACT}[{AttributeBody[i]}]");
                }
                else
                {
                    attributeBody.AppendLine($"{RETRACT}[{AttributeBody[i]}]");
                }
            }

            return $$"""

                {{attributeBody}}
                {{RETRACT}}{{Modifies}} {{FullTypeName}} {{PropertyName}}
                {{RETRACT}}{
                {{RETRACT}}    get => {{SourceName}};
                {{RETRACT}}    set => {{SourceName}} = value;
                {{RETRACT}}}
                """;
        }
    }
}
