using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static VeloxDev.Core.Generator.Base.Analizer;

namespace VeloxDev.Core.Generator.Writers
{
    public class MVVMWriter : WriterBase
    {
        private List<MVVMPropertyFactory> MVVMProperties { get; set; } = [];
        private List<MVVMPropertyFactory> AutoProperties { get; set; } = [];
        private bool IsWorkflowComponent { get; set; } = false;
        private bool _isBaseClassMvvmGenerated = false;

        public override void Initialize(ClassDeclarationSyntax classDeclaration, INamedTypeSymbol namedTypeSymbol)
        {
            base.Initialize(classDeclaration, namedTypeSymbol);

            if (namedTypeSymbol.BaseType != null)
            {
                _isBaseClassMvvmGenerated = CheckBaseClassForMvvmInfrastructure(namedTypeSymbol.BaseType);
            }

            ReadMVVMConfig(namedTypeSymbol);
            ReadAutoProperties(namedTypeSymbol);
            IsWorkflowComponent = HasWorkflowAttribute(namedTypeSymbol);
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

        private void ReadAutoProperties(INamedTypeSymbol symbol)
        {
            AutoProperties =
            [
                .. symbol.GetMembers()
                    .OfType<IPropertySymbol>()
                    .Where(property => property.GetAttributes().Any(attr =>
                        attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                        NAMESPACE_VELOX_MVVM + ".VeloxPropertyAttribute"))
                    .Select(property => new MVVMPropertyAnalizer(property))
                    .Select(analizer => new MVVMPropertyFactory(analizer, "public", false)
                    {
                        SetteringBody = analizer.HasSetter ? [$"OnPropertyChanging(nameof({analizer.PropertyName}));"] : [],
                        SetteredBody = analizer.HasSetter ? [$"OnPropertyChanged(nameof({analizer.PropertyName}));"] : [],
                    })
            ];
        }

        private bool HasWorkflowAttribute(INamedTypeSymbol symbol)
        {
            foreach (var attribute in symbol.GetAttributes())
            {
                var attributeClass = attribute.AttributeClass;
                if (attributeClass == null) continue;

                var fullName = attributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (fullName.Contains("WorkflowBuilder.ViewModel") || fullName.Contains("VeloxDev.Core.WorkflowSystem"))
                    return true;

                var name = attributeClass.Name;
                if (name.Contains('`')) name = name.Substring(0, name.IndexOf('`'));
                if (name is "TreeAttribute" or "NodeAttribute" or "SlotAttribute" or "LinkAttribute")
                    return true;
            }
            return false;
        }

        private bool CheckBaseClassForMvvmInfrastructure(INamedTypeSymbol? symbol)
        {
            if (symbol == null || symbol.SpecialType == SpecialType.System_Object)
                return false;

            // 1. 检查 [VeloxProperty]
            if (symbol.GetMembers().Any(m => m.GetAttributes().Any(a =>
                    a.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).EndsWith("VeloxPropertyAttribute") == true)))
                return true;

            // 2. 检查工作流特性
            if (HasWorkflowAttribute(symbol)) return true;

            // 3. 检查主流框架
            if (HasMainstreamFrameworkFeatures(symbol)) return true;

            return CheckBaseClassForMvvmInfrastructure(symbol.BaseType);
        }

        private bool HasMainstreamFrameworkFeatures(INamedTypeSymbol symbol)
        {
            var fullName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            // 检查基类名称
            if (fullName.Contains("CommunityToolkit.Mvvm.ComponentModel.ObservableObject") ||
                fullName.Contains("CommunityToolkit.Mvvm.ComponentModel.ObservableValidator") ||
                fullName.Contains("Prism.Mvvm.BindableBase") ||
                fullName.Contains("ReactiveUI.ReactiveObject") ||
                fullName.Contains("Caliburn.Micro.PropertyChangedBase"))
                return true;

            // 检查特性
            foreach (var attr in symbol.GetAttributes())
            {
                var attrName = attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (attrName is null || string.IsNullOrEmpty(attrName)) continue;

                if (attrName.Contains("CommunityToolkit.Mvvm.ComponentModel.ObservablePropertyAttribute") ||
                    attrName.Contains("CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedForAttribute") ||
                    attrName.Contains("Prism.Mvvm") ||
                    attrName.Contains("ReactiveUI"))
                    return true;
            }

            return false;
        }

        public override bool CanWrite() => MVVMProperties.Count > 0 || AutoProperties.Count > 0 || IsWorkflowComponent;

        public override string GetFileName()
        {
            if (Syntax == null || Symbol == null) return string.Empty;
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
            if (Syntax == null || Symbol == null) return string.Empty;

            var builder = new StringBuilder();

            // 仅在基类未生成 MVVM 基础设施时生成事件和方法
            if (!_isBaseClassMvvmGenerated)
            {
                // 检查类是否为 sealed
                bool isSealed = Symbol.IsSealed;

                // 如果是 sealed，不能使用 virtual；否则使用 virtual 以支持多态
                string methodModifier = isSealed ? "" : "virtual ";

                builder.AppendLine($$"""
                        public event {{NAMESPACE_SYSTEM_MVVM}}.PropertyChangingEventHandler? PropertyChanging;
                        public event {{NAMESPACE_SYSTEM_MVVM}}.PropertyChangedEventHandler? PropertyChanged;
                        
                        public {{methodModifier}}void OnPropertyChanging(string propertyName)
                        {
                            PropertyChanging?.Invoke(this, new {{NAMESPACE_SYSTEM_MVVM}}.PropertyChangingEventArgs(propertyName));
                        }
                        public {{methodModifier}}void OnPropertyChanged(string propertyName)
                        {
                            PropertyChanged?.Invoke(this, new {{NAMESPACE_SYSTEM_MVVM}}.PropertyChangedEventArgs(propertyName));
                        }
                    """);
            }

            foreach (var factory in AutoProperties)
            {
                var field = factory.GenerateFieldDeclaration();
                if (!string.IsNullOrEmpty(field)) builder.AppendLine(field);
            }

            foreach (var factory in MVVMProperties.Concat(AutoProperties))
            {
                builder.AppendLine(factory.Generate());
            }

            return builder.ToString();
        }
    }
}