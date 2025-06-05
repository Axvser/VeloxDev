using Microsoft.CodeAnalysis;
using System.Linq;

namespace VeloxDev.WPF.Generator
{
    public sealed class FieldRoslyn
    {
        private const string NAME_OBS = "ObservableAttribute";

        internal FieldRoslyn(IFieldSymbol fieldSymbol)
        {
            Symbol = fieldSymbol;
            TypeName = fieldSymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            FieldName = fieldSymbol.Name;
            PropertyName = GetPropertyNameFromFieldName(fieldSymbol.Name);
            ReadObservableParams(fieldSymbol);
        }

        public IFieldSymbol Symbol { get; private set; }
        public string TypeName { get; private set; } = string.Empty;
        public string FieldName { get; private set; } = string.Empty;
        public string PropertyName { get; private set; } = string.Empty;
        public int SetterValidation { get; private set; } = 0;

        private static string GetPropertyNameFromFieldName(string fieldName)
        {
            if (fieldName.StartsWith("_"))
            {
                return char.ToUpper(fieldName[1]) + fieldName.Substring(2);
            }
            else
            {
                return char.ToUpper(fieldName[0]) + fieldName.Substring(1);
            }
        }
        private void ReadObservableParams(IFieldSymbol fieldSymbol)
        {
            var attributeData = fieldSymbol.GetAttributes().FirstOrDefault(ad => ad.AttributeClass?.Name == NAME_OBS);
            if (attributeData != null)
            {
                SetterValidation = (int)attributeData.ConstructorArguments[0].Value!;
            }
        }
    }
}
