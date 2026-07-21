using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VeloxDev.Generators.Base;
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
        private bool _generatePropertyChangingEvent;
        private bool _generatePropertyChangedEvent;
        private bool _addNotifyPropertyChangingInterface;
        private bool _addNotifyPropertyChangedInterface;
        private string _propertyChangingMethodDeclaration = string.Empty;
        private string _propertyChangedMethodDeclaration = string.Empty;
        private bool _hasBaseCollectionNotificationInfrastructure = false;
        private bool _hasBaseWorkflowSlotInfrastructure = false;

        public override void Initialize(ClassDeclarationSyntax classDeclaration, INamedTypeSymbol namedTypeSymbol)
        {
            base.Initialize(classDeclaration, namedTypeSymbol);

            _isWorkflowComponentOrBase = HasWorkflowAttributeInHierarchy(namedTypeSymbol);
            _isWorkflowNodeComponentOrBase = HasWorkflowNodeAttributeInHierarchy(namedTypeSymbol);
            IsWorkflowComponent = HasWorkflowAttribute(namedTypeSymbol);
            ConfigurePropertyNotificationInfrastructure(namedTypeSymbol);
            _hasBaseCollectionNotificationInfrastructure = CheckBaseClassForCollectionInfrastructure(namedTypeSymbol);
            _hasBaseWorkflowSlotInfrastructure = CheckBaseClassForWorkflowSlotInfrastructure(namedTypeSymbol);
            ReadMVVMConfig(namedTypeSymbol);
            ReadAutoProperties(namedTypeSymbol);
        }

        // ── Framework-aware detection ──
        private SetterMode DetectSetterMode(INamedTypeSymbol symbol)
        {
            // Check for CommunityToolkit.Mvvm (ObservableObject / ObservableValidator / [ObservableObject])
            if (HasAttributeInHierarchy(symbol, "global::CommunityToolkit.Mvvm.ComponentModel.ObservableObjectAttribute"))
                return SetterMode.FrameworkSetProperty;

            // Check for Prism (BindableBase)
            if (ImplementsInterface(symbol, "global::System.ComponentModel.INotifyPropertyChanged"))
            {
                // BindableBase has SetProperty<T>(ref T, T, string) — check via method presence
                var bindableBase = symbol.BaseType;
                while (bindableBase != null && bindableBase.SpecialType != SpecialType.System_Object)
                {
                    var fullName = bindableBase.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    if (fullName == "global::Prism.Mvvm.BindableBase")
                        return SetterMode.FrameworkSetProperty;
                    
                    // Also match generic pattern: any base with SetProperty(ref T, T, string)
                    var hasSetProp = bindableBase.GetMembers("SetProperty")
                        .OfType<IMethodSymbol>()
                        .Any(m => !m.IsStatic && m.Parameters.Length >= 2 &&
                                  m.Parameters[0].RefKind == RefKind.Ref);
                    if (hasSetProp)
                        return SetterMode.FrameworkSetProperty;

                    bindableBase = bindableBase.BaseType;
                }
            }

            // Check for ReactiveUI (ReactiveObject)
            if (ImplementsInterface(symbol, "global::ReactiveUI.IReactiveObject"))
                return SetterMode.FrameworkRaiseAndSet;

            // Check for Caliburn.Micro (PropertyChangedBase)
            var baseType = symbol.BaseType;
            while (baseType != null && baseType.SpecialType != SpecialType.System_Object)
            {
                var hasNotifyOf = baseType.GetMembers("NotifyOfPropertyChange")
                    .OfType<IMethodSymbol>()
                    .Any(m => !m.IsStatic && m.Parameters.Length == 1 &&
                              m.Parameters[0].Type.SpecialType == SpecialType.System_String);
                if (hasNotifyOf)
                    return SetterMode.FrameworkNotifyOfPropertyChange;
                baseType = baseType.BaseType;
            }

            return SetterMode.Default;
        }

        private void ReadMVVMConfig(INamedTypeSymbol symbol)
        {
            var setterMode = DetectSetterMode(symbol);
            MVVMProperties =
            [
                .. symbol.GetMembers()
                    .OfType<IFieldSymbol>()
                    .Where(field => field.GetAttributes().Any(attr =>
                        attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                        NAMESPACE_VELOX_MVVM + ".VeloxPropertyAttribute"))
                    .Where(field => ShouldGenerateFieldProperty(symbol, field))
                    .Select(field =>
                    {
                        var analizer = new MVVMFieldAnalizer(field);
                        var factory = new MVVMPropertyFactory(analizer, "public", false)
                        {
                            FrameworkSetterMode = setterMode,
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
            var setterMode = DetectSetterMode(symbol);
            AutoProperties =
            [
                .. symbol.GetMembers()
                    .OfType<IPropertySymbol>()
                    .Where(property => property.GetAttributes().Any(attr =>
                        attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                        NAMESPACE_VELOX_MVVM + ".VeloxPropertyAttribute"))
                    .Where(ShouldGeneratePartialProperty)
                    .Select(property =>
                    {
                        var analizer = new MVVMPropertyAnalizer(property);
                        var factory = new MVVMPropertyFactory(analizer, "public", false)
                        {
                            FrameworkSetterMode = setterMode,
                            SetteringBody = analizer.HasSetter ? [$"OnPropertyChanging(nameof({analizer.PropertyName}));"] : [],
                            SetteredBody = analizer.HasSetter ? [$"OnPropertyChanged(nameof({analizer.PropertyName}));"] : [],
                        };
                        ConfigureWorkflowSlotProperty(factory, property, property.Type);
                        return factory;
                    })
            ];
        }

        private bool ShouldGenerateFieldProperty(INamedTypeSymbol symbol, IFieldSymbol field)
        {
            if (HasCompetingPropertyGeneratorAttribute(field))
                return false;

            var propertyName = new MVVMFieldAnalizer(field).PropertyName;
            var current = symbol;
            while (current != null && current.SpecialType != SpecialType.System_Object)
            {
                if (current.GetMembers(propertyName).OfType<IPropertySymbol>().Any())
                    return false;

                current = current.BaseType;
            }

            return true;
        }

        private bool ShouldGeneratePartialProperty(IPropertySymbol property)
        {
            if (HasCompetingPropertyGeneratorAttribute(property))
                return false;

            return property.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax())
                .OfType<PropertyDeclarationSyntax>()
                .Any(syntax => syntax.Modifiers.Any(modifier =>
                    modifier.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword)));
        }

        private static bool HasCompetingPropertyGeneratorAttribute(ISymbol symbol)
        {
            foreach (var attribute in symbol.GetAttributes())
            {
                var attributeClass = attribute.AttributeClass;
                if (attributeClass == null)
                    continue;

                var fullName = attributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var isSupportedFramework =
                    fullName.StartsWith("global::CommunityToolkit.Mvvm.") ||
                    fullName.StartsWith("global::ReactiveUI.") ||
                    fullName.StartsWith("global::Prism.") ||
                    fullName.StartsWith("global::Caliburn.Micro.");

                if (!isSupportedFramework)
                    continue;

                if (attributeClass.Name is "ObservablePropertyAttribute" or "ReactiveAttribute")
                    return true;
            }

            return false;
        }

        private void ConfigurePropertyNotificationInfrastructure(INamedTypeSymbol symbol)
        {
            var toolkitObservableObject = HasAttributeInHierarchy(
                symbol,
                "global::CommunityToolkit.Mvvm.ComponentModel.ObservableObjectAttribute");
            var toolkitChangedOnly = HasAttributeInHierarchy(
                symbol,
                "global::CommunityToolkit.Mvvm.ComponentModel.INotifyPropertyChangedAttribute");
            var baseVeloxInfrastructure =
                HasWorkflowAttributeInHierarchy(symbol.BaseType) ||
                HasVeloxPropertyInHierarchy(symbol.BaseType);

            var plannedChangingInfrastructure = toolkitObservableObject || baseVeloxInfrastructure;
            var plannedChangedInfrastructure =
                toolkitObservableObject ||
                toolkitChangedOnly ||
                baseVeloxInfrastructure;

            var changingEvent = FindEventInHierarchy(
                symbol,
                "PropertyChanging",
                "global::System.ComponentModel.PropertyChangingEventHandler");
            var changedEvent = FindEventInHierarchy(
                symbol,
                "PropertyChanged",
                "global::System.ComponentModel.PropertyChangedEventHandler");

            _generatePropertyChangingEvent = changingEvent == null && !plannedChangingInfrastructure;
            _generatePropertyChangedEvent = changedEvent == null && !plannedChangedInfrastructure;

            var workflowProvidesNotificationContracts = _isWorkflowComponentOrBase;
            _addNotifyPropertyChangingInterface =
                !workflowProvidesNotificationContracts &&
                !plannedChangingInfrastructure &&
                !ImplementsInterface(symbol, "global::System.ComponentModel.INotifyPropertyChanging");
            _addNotifyPropertyChangedInterface =
                !workflowProvidesNotificationContracts &&
                !plannedChangedInfrastructure &&
                !ImplementsInterface(symbol, "global::System.ComponentModel.INotifyPropertyChanged");

            var baseProvidesWorkflowMethods =
                HasWorkflowAttributeInHierarchy(symbol.BaseType) ||
                (symbol.BaseType != null &&
                 ImplementsInterface(symbol.BaseType, "global::VeloxDev.WorkflowSystem.IWorkflowViewModel"));
            var requiresPublicWorkflowMethods = IsWorkflowComponent && !baseProvidesWorkflowMethods;

            _propertyChangingMethodDeclaration = BuildNotificationMethodDeclaration(
                symbol,
                isChanging: true,
                plannedStringMethod: plannedChangingInfrastructure,
                eventSymbol: changingEvent,
                generateEvent: _generatePropertyChangingEvent,
                requiresPublicWorkflowMethod: requiresPublicWorkflowMethods);
            _propertyChangedMethodDeclaration = BuildNotificationMethodDeclaration(
                symbol,
                isChanging: false,
                plannedStringMethod: plannedChangedInfrastructure,
                eventSymbol: changedEvent,
                generateEvent: _generatePropertyChangedEvent,
                requiresPublicWorkflowMethod: requiresPublicWorkflowMethods);
        }

        private string BuildNotificationMethodDeclaration(
            INamedTypeSymbol symbol,
            bool isChanging,
            bool plannedStringMethod,
            IEventSymbol? eventSymbol,
            bool generateEvent,
            bool requiresPublicWorkflowMethod)
        {
            var methodName = isChanging ? "OnPropertyChanging" : "OnPropertyChanged";
            var eventName = isChanging ? "PropertyChanging" : "PropertyChanged";
            var eventArgsType = isChanging
                ? "global::System.ComponentModel.PropertyChangingEventArgs"
                : "global::System.ComponentModel.PropertyChangedEventArgs";
            var stringMethod = FindMethodInHierarchy(symbol, methodName, SpecialType.System_String);

            if (stringMethod != null || plannedStringMethod)
            {
                if (!requiresPublicWorkflowMethod)
                    return string.Empty;

                if (plannedStringMethod ||
                    (stringMethod != null &&
                     SymbolEqualityComparer.Default.Equals(stringMethod.ContainingType, symbol)))
                {
                    if (stringMethod?.DeclaredAccessibility == Accessibility.Public)
                        return string.Empty;

                    return
                        $"void {NAMESPACE_VELOX_IWORKFLOW}.IWorkflowViewModel.{methodName}(string propertyName) => {methodName}(propertyName);";
                }

                if (stringMethod?.DeclaredAccessibility == Accessibility.Public)
                    return string.Empty;

                return $"public new void {methodName}(string propertyName) => base.{methodName}(propertyName);";
            }

            var body = BuildNotificationMethodBody(
                symbol,
                isChanging,
                eventName,
                eventArgsType,
                eventSymbol,
                generateEvent);
            var methodModifier = symbol.IsSealed ? string.Empty : "virtual ";

            return $$"""
                public {{methodModifier}}void {{methodName}}(string propertyName)
                {
                    {{body}}
                }
                """;
        }

        private string BuildNotificationMethodBody(
            INamedTypeSymbol symbol,
            bool isChanging,
            string eventName,
            string eventArgsType,
            IEventSymbol? eventSymbol,
            bool generateEvent)
        {
            if (generateEvent ||
                (eventSymbol != null &&
                 SymbolEqualityComparer.Default.Equals(eventSymbol.ContainingType, symbol)))
            {
                return $"{eventName}?.Invoke(this, new {eventArgsType}(propertyName));";
            }

            var methodName = isChanging ? "OnPropertyChanging" : "OnPropertyChanged";
            var eventArgsMethod = FindMethodInHierarchy(symbol, methodName, eventArgsType);

            // Check framework-specific forwarding names FIRST.
            // For Caliburn.Micro: prefer NotifyOfPropertyChange over OnPropertyChanged(PropertyChangedEventArgs)
            // to respect IsNotifying check. For Prism: prefer RaisePropertyChanged for consistency.
            var forwardingNames = isChanging
                ? new[] { "RaisePropertyChanging", "NotifyOfPropertyChanging" }
                : new[] { "RaisePropertyChanged", "NotifyOfPropertyChange" };
            foreach (var forwardingName in forwardingNames)
            {
                if (FindMethodInHierarchy(symbol, forwardingName, SpecialType.System_String) != null)
                    return $"{forwardingName}(propertyName);";
            }

            // Fall back to EventArgs-based method if available (e.g. CommunityToolkit.Mvvm ObservableObject)
            if (eventArgsMethod != null)
                return $"{methodName}(new {eventArgsType}(propertyName));";

            // ReactiveUI uses extension methods
            if (ImplementsInterface(symbol, "global::ReactiveUI.IReactiveObject"))
            {
                var extensionMethod = isChanging ? "RaisePropertyChanging" : "RaisePropertyChanged";
                return $"global::ReactiveUI.IReactiveObjectExtensions.{extensionMethod}(this, propertyName);";
            }

            return string.Empty;
        }

        private static bool HasAttributeInHierarchy(INamedTypeSymbol symbol, string fullyQualifiedName)
        {
            var current = symbol;
            while (current != null && current.SpecialType != SpecialType.System_Object)
            {
                if (current.GetAttributes().Any(attribute =>
                        attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                        fullyQualifiedName))
                {
                    return true;
                }

                current = current.BaseType;
            }

            return false;
        }

        private static bool HasVeloxPropertyInHierarchy(INamedTypeSymbol? symbol)
        {
            var current = symbol;
            while (current != null && current.SpecialType != SpecialType.System_Object)
            {
                if (current.GetMembers().Any(member =>
                        member.GetAttributes().Any(attribute =>
                            attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                            NAMESPACE_VELOX_MVVM + ".VeloxPropertyAttribute")))
                {
                    return true;
                }

                current = current.BaseType;
            }

            return false;
        }

        private static bool ImplementsInterface(INamedTypeSymbol symbol, string fullyQualifiedName)
        {
            return symbol.AllInterfaces.Any(@interface =>
                @interface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                fullyQualifiedName);
        }

        private IEventSymbol? FindEventInHierarchy(
            INamedTypeSymbol symbol,
            string eventName,
            string eventHandlerType)
        {
            var current = symbol;
            while (current != null && current.SpecialType != SpecialType.System_Object)
            {
                var eventSymbol = current.GetMembers(eventName)
                    .OfType<IEventSymbol>()
                    .FirstOrDefault(candidate =>
                        !candidate.IsStatic &&
                        candidate.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                        eventHandlerType &&
                        IsAccessibleFromTarget(candidate, symbol));
                if (eventSymbol != null)
                    return eventSymbol;

                current = current.BaseType;
            }

            return null;
        }

        private IMethodSymbol? FindMethodInHierarchy(
            INamedTypeSymbol symbol,
            string methodName,
            SpecialType parameterType)
        {
            return FindMethodInHierarchy(symbol, methodName, parameter =>
                parameter.Type.SpecialType == parameterType);
        }

        private IMethodSymbol? FindMethodInHierarchy(
            INamedTypeSymbol symbol,
            string methodName,
            string parameterType)
        {
            return FindMethodInHierarchy(symbol, methodName, parameter =>
                parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                parameterType);
        }

        private IMethodSymbol? FindMethodInHierarchy(
            INamedTypeSymbol symbol,
            string methodName,
            System.Func<IParameterSymbol, bool> parameterMatches)
        {
            var current = symbol;
            while (current != null && current.SpecialType != SpecialType.System_Object)
            {
                var method = current.GetMembers(methodName)
                    .OfType<IMethodSymbol>()
                    .FirstOrDefault(candidate =>
                        !candidate.IsStatic &&
                        candidate.ReturnsVoid &&
                        candidate.Parameters.Length == 1 &&
                        parameterMatches(candidate.Parameters[0]) &&
                        IsAccessibleFromTarget(candidate, symbol));
                if (method != null)
                    return method;

                current = current.BaseType;
            }

            return null;
        }

        private static bool IsAccessibleFromTarget(ISymbol member, INamedTypeSymbol target)
        {
            if (SymbolEqualityComparer.Default.Equals(member.ContainingType, target))
                return true;

            return member.DeclaredAccessibility switch
            {
                Accessibility.Public => true,
                Accessibility.Protected => true,
                Accessibility.ProtectedOrInternal => true,
                Accessibility.Internal =>
                    SymbolEqualityComparer.Default.Equals(member.ContainingAssembly, target.ContainingAssembly),
                Accessibility.ProtectedAndInternal =>
                    SymbolEqualityComparer.Default.Equals(member.ContainingAssembly, target.ContainingAssembly),
                _ => false
            };
        }

        private void ConfigureWorkflowSlotProperty(MVVMPropertyFactory factory, ISymbol memberSymbol, ITypeSymbol typeSymbol)
        {
            // Re-evaluate workflow-class recognition based on the member's declaring type
            var ownerType = memberSymbol?.ContainingType as INamedTypeSymbol;
            var isWorkflowComponentOrBase = HasWorkflowAttributeInHierarchy(ownerType);
            var isWorkflowNodeOrBase = HasWorkflowNodeAttributeInHierarchy(ownerType);

            // Check if this is a SlotEnumerator<TSlot> field on a Node
            if (isWorkflowComponentOrBase && isWorkflowNodeOrBase && IsSlotEnumeratorType(typeSymbol))
            {
                factory.UseSlotEnumeratorLifecycle = true;
                factory.UseWorkflowSlotLifecycle = false;
                factory.UseWorkflowSlotAutoCreation = false;
                factory.UseWorkflowSlotCollectionLifecycle = false;
                return;
            }

            factory.UseWorkflowSlotLifecycle = isWorkflowComponentOrBase &&
                                              isWorkflowNodeOrBase &&
                                              IsWorkflowSlotViewModelType(typeSymbol);

            // Lazy auto-creation in getter is no longer generated; slots are created in InitializeWorkflow
            factory.UseWorkflowSlotAutoCreation = false;

            // Collection lifecycle for slot collections is replaced by SlotEnumerator<TSlot>
            factory.UseWorkflowSlotCollectionLifecycle = false;
        }

        private bool IsSlotEnumeratorType(ITypeSymbol typeSymbol)
        {
            if (typeSymbol is not INamedTypeSymbol namedType) return false;
            if (!namedType.IsGenericType) return false;
            var original = namedType.OriginalDefinition;
            var fullName = original.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return fullName.Contains("SlotEnumerator") &&
                   (fullName.StartsWith("global::VeloxDev.WorkflowSystem.SlotEnumerator") ||
                    original.Name == "SlotEnumerator");
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

            var slotBaseSymbol = ResolveTypeByMetadataName(Symbol.ContainingAssembly, "VeloxDev.WorkflowSystem.SlotDefaultViewModel");
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
        private static ITypeSymbol? GetCollectionItemTypeSymbol(ITypeSymbol typeSymbol)
        {
            // Check IEnumerable<T> to get T's type symbol
            if (typeSymbol is INamedTypeSymbol namedType &&
                namedType.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T &&
                namedType.TypeArguments.Length > 0)
            {
                return namedType.TypeArguments[0];
            }

            var enumerableInterface = typeSymbol.AllInterfaces
                .FirstOrDefault(i => i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T);

            return enumerableInterface?.TypeArguments.Length > 0 ? enumerableInterface.TypeArguments[0] : null;
        }

        private static bool IsGenericCollectionType(ITypeSymbol typeSymbol)
        {
            // Check if the type implements ICollection<T>
            if (typeSymbol is INamedTypeSymbol namedType &&
                namedType.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.ICollection<T>")
            {
                return true;
            }

            return typeSymbol.AllInterfaces.Any(i =>
                i.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.ICollection<T>");
        }
        private bool CheckBaseClassForCollectionInfrastructure(INamedTypeSymbol? symbol)
        {
            if (symbol == null || symbol.SpecialType == SpecialType.System_Object)
                return false;

            if (HasCollectionInfrastructureMethods(symbol))
                return true;

            return CheckBaseClassForCollectionInfrastructure(symbol.BaseType);
        }

        private bool CheckBaseClassForWorkflowSlotInfrastructure(INamedTypeSymbol? symbol)
        {
            if (symbol == null || symbol.SpecialType == SpecialType.System_Object)
                return false;

            if (HasWorkflowSlotInfrastructureMethods(symbol))
                return true;

            return CheckBaseClassForWorkflowSlotInfrastructure(symbol.BaseType);
        }

        private bool HasWorkflowSlotInfrastructureMethods(INamedTypeSymbol symbol)
        {
            var methods = symbol.GetMembers().OfType<IMethodSymbol>().ToList();
            return methods.Any(m => m.Name == "OnWorkflowSlotAdded" && !m.IsStatic && m.Parameters.Length == 1) &&
                   methods.Any(m => m.Name == "OnWorkflowSlotRemoved" && !m.IsStatic && m.Parameters.Length == 1) &&
                   methods.Any(m => m.Name == "CreateWorkflowSlot" && !m.IsStatic && m.IsGenericMethod);
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

        public override bool CanWrite() => MVVMProperties.Count > 0 || AutoProperties.Count > 0 || IsWorkflowComponent;

        public override string GetFileName()
        {
            if (Syntax == null || Symbol == null) return string.Empty;
            var namespaceName = Symbol.ContainingNamespace.IsGlobalNamespace
                ? "Global"
                : Symbol.ContainingNamespace.ToDisplayString().Replace('.', '_');
            return $"{Syntax.Identifier.Text}_{namespaceName}_MVVM.g.cs";
        }

        public override string[] GenerateBaseInterfaces()
        {
            var interfaces = new List<string>();
            if (_addNotifyPropertyChangingInterface)
                interfaces.Add($"{NAMESPACE_SYSTEM_MVVM}.INotifyPropertyChanging");
            if (_addNotifyPropertyChangedInterface)
                interfaces.Add($"{NAMESPACE_SYSTEM_MVVM}.INotifyPropertyChanged");
            return [.. interfaces];
        }

        public override string[] GenerateBaseTypes() => [];

        public override string GenerateBody()
        {
            if (Syntax == null || Symbol == null) return string.Empty;

            var builder = new StringBuilder();

            if (_generatePropertyChangingEvent)
                builder.AppendLine($"public event {NAMESPACE_SYSTEM_MVVM}.PropertyChangingEventHandler? PropertyChanging;");
            if (_generatePropertyChangedEvent)
                builder.AppendLine($"public event {NAMESPACE_SYSTEM_MVVM}.PropertyChangedEventHandler? PropertyChanged;");
            if (!string.IsNullOrWhiteSpace(_propertyChangingMethodDeclaration))
                builder.AppendLine(_propertyChangingMethodDeclaration);
            if (!string.IsNullOrWhiteSpace(_propertyChangedMethodDeclaration))
                builder.AppendLine(_propertyChangedMethodDeclaration);

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

            if (!_hasBaseWorkflowSlotInfrastructure && !IsWorkflowComponent &&
                MVVMProperties.Concat(AutoProperties).Any(p => p.UseWorkflowSlotLifecycle))
            {
                bool isSealed = Symbol.IsSealed;
                string methodModifier = isSealed ? "" : "virtual ";

                builder.AppendLine($$"""
                        public {{methodModifier}}T CreateWorkflowSlot<T>() where T : class, {{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel
                        {
                            try
                            {
                                if (global::System.Activator.CreateInstance(typeof(T), true) is T created)
                                {
                                    return created;
                                }
                            }
                            catch
                            {
                            }
                            return (T)(object)new {{NAMESPACE_VELOX_WORKFLOW}}.SlotDefaultViewModel();
                        }
                        protected {{methodModifier}}void OnWorkflowSlotAdded({{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel slot)
                        {
                            CreateSlotCommand.Execute(slot);
                        }
                        protected {{methodModifier}}void OnWorkflowSlotRemoved({{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel slot)
                        {
                            slot?.DeleteCommand.Execute(null);
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




