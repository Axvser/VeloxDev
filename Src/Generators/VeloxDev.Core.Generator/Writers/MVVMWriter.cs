using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static VeloxDev.Generators.Base.Analizer;

namespace VeloxDev.Generators.Writers
{
    public class MVVMWriter : WriterBase
    {
        private List<MVVMPropertyFactory> MVVMProperties { get; set; } = [];
        private List<MVVMPropertyFactory> AutoProperties { get; set; } = [];
        private bool IsWorkflowComponent { get; set; } = false;
        private bool _isWorkflowComponentOrBase = false;
        private bool _isWorkflowNodeComponentOrBase = false;
        private bool _hasBasePropertyNotificationInfrastructure = false;
        private bool _hasBaseCollectionNotificationInfrastructure = false;

        public override void Initialize(ClassDeclarationSyntax classDeclaration, INamedTypeSymbol namedTypeSymbol)
        {
            base.Initialize(classDeclaration, namedTypeSymbol);

            if (namedTypeSymbol.BaseType != null)
            {
                _hasBasePropertyNotificationInfrastructure = CheckBaseClassForPropertyInfrastructure(namedTypeSymbol.BaseType);
                _hasBaseCollectionNotificationInfrastructure = CheckBaseClassForCollectionInfrastructure(namedTypeSymbol.BaseType);
            }

            _isWorkflowComponentOrBase = HasWorkflowAttributeInHierarchy(namedTypeSymbol);
            _isWorkflowNodeComponentOrBase = HasWorkflowNodeAttributeInHierarchy(namedTypeSymbol);
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
                    .Select(field =>
                    {
                        var analizer = new MVVMFieldAnalizer(field);
                        var factory = new MVVMPropertyFactory(analizer, "public", false)
                        {
                            SetteringBody = [$"OnPropertyChanging(nameof({analizer.PropertyName}));"],
                            SetteredBody = [$"OnPropertyChanged(nameof({analizer.PropertyName}));"],
                        };
                        ConfigureWorkflowSlotProperty(factory, field, field.Type);
                        return factory;
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
                    .Select(property =>
                    {
                        var analizer = new MVVMPropertyAnalizer(property);
                        var factory = new MVVMPropertyFactory(analizer, "public", false)
                        {
                            SetteringBody = analizer.HasSetter ? [$"OnPropertyChanging(nameof({analizer.PropertyName}));"] : [],
                            SetteredBody = analizer.HasSetter ? [$"OnPropertyChanged(nameof({analizer.PropertyName}));"] : [],
                        };
                        ConfigureWorkflowSlotProperty(factory, property, property.Type);
                        return factory;
                    })
            ];
        }

        private void ConfigureWorkflowSlotProperty(MVVMPropertyFactory factory, ISymbol memberSymbol, ITypeSymbol typeSymbol)
        {
            // Re-evaluate workflow-class recognition based on the member's declaring type
            var ownerType = memberSymbol?.ContainingType as INamedTypeSymbol;
            var isWorkflowComponentOrBase = HasWorkflowAttributeInHierarchy(ownerType);
            var isWorkflowNodeOrBase = HasWorkflowNodeAttributeInHierarchy(ownerType);

            factory.UseWorkflowSlotLifecycle = isWorkflowComponentOrBase &&
                                              isWorkflowNodeOrBase &&
                                              IsWorkflowSlotViewModelType(typeSymbol);

            if (!factory.UseWorkflowSlotLifecycle || factory.IsNullable)
            {
                factory.UseWorkflowSlotAutoCreation = false;
                return;
            }

            factory.UseWorkflowSlotAutoCreation = CanAutoCreateWorkflowSlot(typeSymbol);
        }

        private bool CanAutoCreateWorkflowSlot(ITypeSymbol typeSymbol)
        {
            return CanInstantiateWorkflowSlotType(typeSymbol) || CanUseWorkflowSlotFallback(typeSymbol);
        }

        private bool CanInstantiateWorkflowSlotType(ITypeSymbol typeSymbol)
        {
            if (typeSymbol is not INamedTypeSymbol namedType)
                return false;

            if (namedType.TypeKind == TypeKind.Interface || namedType.IsAbstract)
                return false;

            if (namedType.TypeKind == TypeKind.TypeParameter)
                return false;

            if (namedType.IsValueType)
                return true;

            if (namedType.InstanceConstructors.Length == 0)
                return true;

            return namedType.InstanceConstructors.Any(ctor => !ctor.IsStatic && ctor.Parameters.Length == 0);
        }

        private bool CanUseWorkflowSlotFallback(ITypeSymbol typeSymbol)
        {
            if (Symbol == null)
                return false;

            var slotBaseSymbol = ResolveTypeByMetadataName(Symbol.ContainingAssembly, "VeloxDev.WorkflowSystem.SlotViewModelBase");
            if (slotBaseSymbol == null)
                return false;

            return IsAssignableFrom(typeSymbol, slotBaseSymbol);
        }

        private static INamedTypeSymbol? ResolveTypeByMetadataName(IAssemblySymbol assembly, string metadataName)
        {
            return assembly.GetTypeByMetadataName(metadataName)
                ?? assembly.Modules.SelectMany(module => module.ReferencedAssemblySymbols)
                    .Select(reference => reference.GetTypeByMetadataName(metadataName))
                    .FirstOrDefault(type => type != null);
        }

        private static bool IsAssignableFrom(ITypeSymbol destinationType, ITypeSymbol sourceType)
        {
            var comparer = SymbolEqualityComparer.Default;

            if (comparer.Equals(destinationType, sourceType))
                return true;

            if (destinationType.SpecialType == SpecialType.System_Object)
                return true;

            if (destinationType.TypeKind == TypeKind.Interface)
            {
                return sourceType.AllInterfaces.Any(i => comparer.Equals(i, destinationType));
            }

            var current = sourceType.BaseType;
            while (current != null)
            {
                if (comparer.Equals(current, destinationType))
                    return true;

                current = current.BaseType;
            }

            return false;
        }

        private bool HasWorkflowAttribute(INamedTypeSymbol symbol)
        {
            foreach (var attribute in symbol.GetAttributes())
            {
                var attributeClass = attribute.AttributeClass;
                if (attributeClass == null) continue;

                var fullName = attributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (fullName.Contains("WorkflowBuilder") || fullName.Contains("VeloxDev.WorkflowSystem"))
                    return true;

                var name = attributeClass.Name;
                if (name.Contains('`')) name = name.Substring(0, name.IndexOf('`'));
                if (name is "TreeAttribute" or "NodeAttribute" or "SlotAttribute" or "LinkAttribute")
                    return true;
            }
            return false;
        }

        private bool HasWorkflowAttributeInHierarchy(INamedTypeSymbol? symbol)
        {
            if (symbol == null || symbol.SpecialType == SpecialType.System_Object)
                return false;

            // 首先检查显式的工作流特性
            if (HasWorkflowAttribute(symbol)) return true;

            // 其次检查类型及其基类是否实现了任一工作流接口（Tree/Node/Slot/Link）
            INamedTypeSymbol? treeInterface = null;
            INamedTypeSymbol? nodeInterface = null;
            INamedTypeSymbol? slotInterface = null;
            INamedTypeSymbol? linkInterface = null;

            if (Symbol != null)
            {
                treeInterface = ResolveTypeByMetadataName(Symbol.ContainingAssembly, "VeloxDev.WorkflowSystem.IWorkflowTreeViewModel");
                nodeInterface = ResolveTypeByMetadataName(Symbol.ContainingAssembly, "VeloxDev.WorkflowSystem.IWorkflowNodeViewModel");
                slotInterface = ResolveTypeByMetadataName(Symbol.ContainingAssembly, "VeloxDev.WorkflowSystem.IWorkflowSlotViewModel");
                linkInterface = ResolveTypeByMetadataName(Symbol.ContainingAssembly, "VeloxDev.WorkflowSystem.IWorkflowLinkViewModel");
            }

            var comparer = SymbolEqualityComparer.Default;
            var current = symbol;
            while (current != null && current.SpecialType != SpecialType.System_Object)
            {
                if (treeInterface != null && current.AllInterfaces.Any(i => comparer.Equals(i, treeInterface))) return true;
                if (nodeInterface != null && current.AllInterfaces.Any(i => comparer.Equals(i, nodeInterface))) return true;
                if (slotInterface != null && current.AllInterfaces.Any(i => comparer.Equals(i, slotInterface))) return true;
                if (linkInterface != null && current.AllInterfaces.Any(i => comparer.Equals(i, linkInterface))) return true;

                // 回退到字符串比较，以防解析失败
                if (current.AllInterfaces.Any(i =>
                    i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).StartsWith(NAMESPACE_VELOX_IWORKFLOW)))
                {
                    return true;
                }

                current = current.BaseType;
            }

            // 继续递归检查基类上的特性（保持原有行为）
            return HasWorkflowAttributeInHierarchy(symbol.BaseType);
        }
        private bool HasWorkflowNodeAttributeInHierarchy(INamedTypeSymbol? symbol)
        {
            if (symbol == null || symbol.SpecialType == SpecialType.System_Object)
                return false;

            var current = symbol;
            while (current != null && current.SpecialType != SpecialType.System_Object)
            {
                // 1) 显式 Node 特性
                if (HasWorkflowNodeAttribute(current)) return true;

                // 2) 检查当前类型是否直接实现了 IWorkflowNodeViewModel（接口名匹配或符号匹配）
                foreach (var iface in current.Interfaces)
                {
                    var ifaceName = iface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    if (ifaceName.EndsWith("IWorkflowNodeViewModel") || iface.Name == "IWorkflowNodeViewModel")
                        return true;
                }

                // 3) 检查当前类型或其基类实现的所有接口（防止显式基类接口未出现在 Interfaces 列表）
                foreach (var iface in current.AllInterfaces)
                {
                    var ifaceName = iface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    if (ifaceName.EndsWith("IWorkflowNodeViewModel") || iface.Name == "IWorkflowNodeViewModel")
                        return true;
                }

                current = current.BaseType;
            }

            return false;
        }
        private bool HasWorkflowNodeAttribute(INamedTypeSymbol symbol)
        {
            foreach (var attribute in symbol.GetAttributes())
            {
                var attributeClass = attribute.AttributeClass;
                if (attributeClass == null) continue;

                var fullName = attributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (fullName.Contains("WorkflowBuilder.NodeAttribute"))
                    return true;

                var name = attributeClass.Name;
                if (name.Contains('`')) name = name.Substring(0, name.IndexOf('`'));
                if (name is "NodeAttribute")
                    return true;
            }

            return false;
        }

        private bool IsWorkflowSlotViewModelType(ITypeSymbol typeSymbol)
        {
            // 1) 尝试解析 SlotAttribute 符号以进行符号比较
            INamedTypeSymbol? slotAttributeSymbol = null;
            if (Symbol != null)
            {
                slotAttributeSymbol = ResolveTypeByMetadataName(Symbol.ContainingAssembly, "WorkflowBuilder.SlotAttribute")
                                      ?? ResolveTypeByMetadataName(Symbol.ContainingAssembly, "VeloxDev.WorkflowSystem.SlotAttribute");
            }

            var comparer = SymbolEqualityComparer.Default;

            if (typeSymbol is INamedTypeSymbol namedType)
            {
                var current = namedType;
                while (current != null)
                {
                    foreach (var attr in current.GetAttributes())
                    {
                        var attrClass = attr.AttributeClass;
                        if (attrClass == null) continue;

                        if (slotAttributeSymbol != null)
                        {
                            if (comparer.Equals(attrClass, slotAttributeSymbol)) return true;
                        }
                        else
                        {
                            var fullAttrName = attrClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                            if (fullAttrName.Contains("WorkflowBuilder.SlotAttribute") || attrClass.Name.StartsWith("SlotAttribute"))
                                return true;
                        }
                    }

                    current = current.BaseType;
                }
            }

            // 2) 检查是否实现了 IWorkflowSlotViewModel 接口（使用符号比较优先）
            INamedTypeSymbol? slotInterface = null;
            if (Symbol != null)
            {
                slotInterface = ResolveTypeByMetadataName(Symbol.ContainingAssembly, "VeloxDev.WorkflowSystem.IWorkflowSlotViewModel");
            }

            if (slotInterface != null)
            {
                if (comparer.Equals(typeSymbol, slotInterface)) return true;
                if (typeSymbol.AllInterfaces.Any(i => comparer.Equals(i, slotInterface))) return true;
            }
            else
            {
                var fullName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (fullName == NAMESPACE_VELOX_IWORKFLOW + ".IWorkflowSlotViewModel")
                    return true;

                if (typeSymbol.AllInterfaces.Any(i => i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == NAMESPACE_VELOX_IWORKFLOW + ".IWorkflowSlotViewModel"))
                    return true;
            }

            return false;
        }
        private bool CheckBaseClassForPropertyInfrastructure(INamedTypeSymbol? symbol)
        {
            if (symbol == null || symbol.SpecialType == SpecialType.System_Object)
                return false;

            if (HasPropertyInfrastructureMethods(symbol))
                return true;

            // 1. 检查 [VeloxProperty]
            if (symbol.GetMembers().Any(m => m.GetAttributes().Any(a =>
                    a.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).EndsWith("VeloxPropertyAttribute") == true)))
                return true;

            // 2. 检查工作流特性
            if (HasWorkflowAttribute(symbol)) return true;

            // 3. 检查主流框架
            if (HasMainstreamFrameworkFeatures(symbol)) return true;

            return CheckBaseClassForPropertyInfrastructure(symbol.BaseType);
        }

        private bool CheckBaseClassForCollectionInfrastructure(INamedTypeSymbol? symbol)
        {
            if (symbol == null || symbol.SpecialType == SpecialType.System_Object)
                return false;

            if (HasCollectionInfrastructureMethods(symbol))
                return true;

            return CheckBaseClassForCollectionInfrastructure(symbol.BaseType);
        }

        private bool HasPropertyInfrastructureMethods(INamedTypeSymbol symbol)
        {
            var methods = symbol.GetMembers().OfType<IMethodSymbol>().ToList();

            return methods.Any(method =>
                       method.Name == "OnPropertyChanging" &&
                       !method.IsStatic &&
                       method.Parameters.Length == 1 &&
                       method.Parameters[0].Type.SpecialType == SpecialType.System_String) &&
                   methods.Any(method =>
                       method.Name == "OnPropertyChanged" &&
                       !method.IsStatic &&
                       method.Parameters.Length == 1 &&
                       method.Parameters[0].Type.SpecialType == SpecialType.System_String);
        }

        private bool HasCollectionInfrastructureMethods(INamedTypeSymbol symbol)
        {
            return symbol.GetMembers().OfType<IMethodSymbol>().Any(method =>
                method.Name == "OnCollectionChanged" &&
                !method.IsStatic &&
                method.IsGenericMethod &&
                method.TypeParameters.Length == 1 &&
                method.Parameters.Length == 4 &&
                method.Parameters[0].Type.SpecialType == SpecialType.System_String);
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

            // 仅在基类未生成属性通知基础设施时生成事件和方法
            if (!_hasBasePropertyNotificationInfrastructure)
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

            if (!_hasBaseCollectionNotificationInfrastructure && MVVMProperties.Concat(AutoProperties).Any(property => property.IsNotifyCollectionChanged))
            {
                bool isSealed = Symbol.IsSealed;
                string methodModifier = isSealed ? "" : "virtual ";

                builder.AppendLine($$"""
                        protected {{methodModifier}}void OnCollectionChanged<T>(string propertyName, global::System.Collections.Specialized.NotifyCollectionChangedEventArgs e, global::System.Collections.Generic.IEnumerable<T>? oldItems, global::System.Collections.Generic.IEnumerable<T>? newItems)
                        {
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




