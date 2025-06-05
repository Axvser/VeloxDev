using System.Collections.Generic;
using System.Text;

namespace VeloxDev.WPF.Generator.Factory
{
    public class DependencyPropertyFactory(string fullOwnerTypeName, string fullTypeName, string propertyName, string initialText) : IFactory
    {
        const string DP_NAMESPACE = "global::System.Windows.";
        const string DP_SUFFIX = "Property";
        const string RETRACT = "      ";

        public string FullOwnerTypeName { get; private set; } = fullOwnerTypeName;
        public string FullTypeName { get; private set; } = fullTypeName;
        public string PropertyName { get; private set; } = propertyName;
        public string DependencyPropertyName { get; private set; } = propertyName + DP_SUFFIX;
        public string InitialText { get; private set; } = initialText;

        public List<string> SetterBody { get; set; } = [];

        public string Generate()
        {
            var setterBody = new StringBuilder();
            for (int i = 0; i < SetterBody.Count; i++)
            {
                setterBody.AppendLine($"{SetterBody[i]}");
            }

            return $$"""
                {{RETRACT}}public {{FullTypeName}} {{PropertyName}}
                {{RETRACT}}{
                {{RETRACT}}    get => ({{FullTypeName}})GetValue({{DependencyPropertyName}});
                {{RETRACT}}    set => SetValue({{DependencyPropertyName}}, value);
                {{RETRACT}}}
                {{RETRACT}}public static readonly {{DP_NAMESPACE}}DependencyProperty {{DependencyPropertyName}} =
                {{RETRACT}}    {{DP_NAMESPACE}}DependencyProperty.Register(
                {{RETRACT}}        "{{PropertyName}}", 
                {{RETRACT}}        typeof({{FullTypeName}}), 
                {{RETRACT}}        typeof({{FullOwnerTypeName}}), 
                {{RETRACT}}        new {{DP_NAMESPACE}}PropertyMetadata({{InitialText}},_innerOn{{PropertyName}}Changed));
                {{RETRACT}}private static void _innerOn{{PropertyName}}Changed({{DP_NAMESPACE}}DependencyObject d, {{DP_NAMESPACE}}DependencyPropertyChangedEventArgs e)
                {{RETRACT}}{
                {{RETRACT}}   if(d is {{FullOwnerTypeName}} target)
                {{RETRACT}}   {
                {{RETRACT}}      var oldValue = ({{FullTypeName}})e.OldValue;
                {{RETRACT}}      var newValue = ({{FullTypeName}})e.NewValue;
                {{setterBody.ToString()}}
                {{RETRACT}}      target.On{{PropertyName}}Changed(oldValue,newValue);
                {{RETRACT}}   }
                {{RETRACT}}}
                {{RETRACT}}partial void On{{PropertyName}}Changed({{FullTypeName}} oldValue,{{FullTypeName}} newValue);
                """;
        }
    }
}
