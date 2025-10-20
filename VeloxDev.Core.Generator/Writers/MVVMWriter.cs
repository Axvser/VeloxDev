using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VeloxDev.Core.Generator.Base;
using static VeloxDev.Core.Generator.Base.Analizer;

namespace VeloxDev.Core.Generator.Writers
{
    public class MVVMWriter : WriterBase
    {
        private List<MVVMPropertyFactory> MVVMProperties { get; set; } = [];

        public override void Initialize(ClassDeclarationSyntax classDeclaration, INamedTypeSymbol namedTypeSymbol)
        {
            base.Initialize(classDeclaration, namedTypeSymbol);
            ReadMVVMConfig(namedTypeSymbol);
        }

        private void ReadMVVMConfig(INamedTypeSymbol symbol)
        {
            MVVMProperties =
            [
                .. symbol.GetMembers()
                    .OfType<IFieldSymbol>()
                    .Where(field => field.GetAttributes().Any(attr =>
                        attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                        NAMESPACE_VELOX_MVVM + ".VeloxPropertyAttribute"))
                    .Select(field => new MVVMFieldAnalizer(field))
                    .Select(analizer => new MVVMPropertyFactory(analizer, "public", false)
                    {
                        SetteringBody = [$"OnPropertyChanging(nameof({analizer.PropertyName}));"],
                        SetteredBody = [$"OnPropertyChanged(nameof({analizer.PropertyName}));"],
                    })
            ];
        }
        private bool HasWorkflowAttribute(INamedTypeSymbol symbol)
        {
            const string workflowNamespace = "VeloxDev.Core.WorkflowSystem";

            var attributes = symbol.GetAttributes();
            foreach (var attribute in attributes)
            {
                var attributeClass = attribute.AttributeClass;
                if (attributeClass == null) continue;

                // 检查是否属于Workflow特性
                if (attributeClass.ContainingNamespace?.ToDisplayString() == workflowNamespace &&
                    attributeClass.ContainingType?.Name == "ViewModel" &&
                    attributeClass.ContainingType?.ContainingType?.Name == "WorkflowBuilder")
                {
                    return true;
                }

                // 检查特性名称（简化版）
                var attributeName = attributeClass.Name;
                if (attributeName.Contains('`'))
                {
                    attributeName = attributeName.Substring(0, attributeName.IndexOf('`'));
                }

                if (attributeName is "TreeAttribute" or "NodeAttribute" or "SlotAttribute" or "LinkAttribute")
                {
                    return true;
                }
            }

            return false;
        }

        public override bool CanWrite() => MVVMProperties.Count > 0 || HasWorkflowAttribute(Symbol);

        public override string GetFileName()
        {
            if (Syntax == null || Symbol == null)
            {
                return string.Empty;
            }

            return $"{Syntax.Identifier.Text}_{Symbol.ContainingNamespace.ToDisplayString().Replace('.', '_')}_MVVM.g.cs";
        }

        public override string[] GenerateBaseInterfaces() =>
        [
            $"{NAMESPACE_SYSTEM_MVVM}.INotifyPropertyChanging",
            $"{NAMESPACE_SYSTEM_MVVM}.INotifyPropertyChanged"
        ];
        public override string[] GenerateBaseTypes() => [];

        public override string GenerateBody()
        {
            if (Syntax == null || Symbol == null || MVVMProperties.Count == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();

            // 生成事件和方法
            builder.AppendLine($$"""
                public event {{NAMESPACE_SYSTEM_MVVM}}.PropertyChangingEventHandler? PropertyChanging;
                public event {{NAMESPACE_SYSTEM_MVVM}}.PropertyChangedEventHandler? PropertyChanged;
                public void OnPropertyChanging(string propertyName)
                {
                    PropertyChanging?.Invoke(this, new {{NAMESPACE_SYSTEM_MVVM}}.PropertyChangingEventArgs(propertyName));
                }
                public void OnPropertyChanged(string propertyName)
                {
                    PropertyChanged?.Invoke(this, new {{NAMESPACE_SYSTEM_MVVM}}.PropertyChangedEventArgs(propertyName));
                }
            """);

            // 生成属性
            foreach (var factory in MVVMProperties)
            {
                builder.AppendLine(factory.Generate());
            }

            return builder.ToString();
        }
    }
}