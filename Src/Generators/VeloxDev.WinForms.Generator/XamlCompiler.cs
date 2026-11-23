using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace VeloxDev.WinForms.Generator;

/// <summary>
/// Compiles one XAML additional file into a private <c>InitializeComponent</c> for its <c>x:Class</c>.
/// </summary>
/// <remarks>
/// Supported subset:
/// <list type="bullet">
/// <item>VeloxDev visual types written unprefixed and resolved against the visual backend (the root must derive
/// from <c>VisualElement</c>); local types through any <c>clr-namespace:</c> prefix such as <c>local:</c>. A legacy
/// <c>v:</c> prefix still means the visual namespaces.</item>
/// <item>Direct child controls added to <c>Controls</c>.</item>
/// <item>Property elements for scalar values, <c>Resources</c>, <c>Controls</c> and list properties.</item>
/// <item>String, numeric, boolean, enum, color, padding, thickness, size, point and time-span literals.</item>
/// <item><c>{Binding Path, Mode}</c> and <c>{StaticResource Key}</c>.</item>
/// <item><c>DataTemplate</c> resources (inline or <c>x:Key</c>) whose body is a <c>VisualElement</c>.</item>
/// </list>
/// Anything else is reported as a diagnostic and skipped; there is no runtime XAML parser to fall back to.
/// </remarks>
internal sealed class XamlCompiler
{
    private readonly SourceProductionContext _production;
    private readonly string _filePath;
    private readonly string? _text;
    private readonly Compilation _compilation;

    private readonly CodeBuilder _initialize = new();
    private readonly CodeBuilder _helpers = new();
    private readonly CodeBuilder _fields = new();
    private readonly HashSet<string> _declaredNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _templateNames = new(StringComparer.Ordinal);
    private readonly HashSet<XObject> _reportedSources = [];
    private readonly List<string> _deferredEvents = [];

    private CodeBuilder _current;
    private INamedTypeSymbol _owner = null!;
    private XElement? _rootElement;
    private string? _rootScope;
    private string? _scopeExpression;
    private int _variableCounter;
    private int _templateDepth;

    public XamlCompiler(SourceProductionContext production, string filePath, string? text, Compilation compilation)
    {
        _production = production;
        _filePath = filePath;
        _text = text;
        _compilation = compilation;
        _current = _initialize;
    }

    public void Run()
    {
        if (string.IsNullOrWhiteSpace(_text))
        {
            return;
        }

        XDocument document;
        try
        {
            using var reader = new StringReader(_text);
            document = XDocument.Load(reader, LoadOptions.SetLineInfo);
        }
        catch (XmlException exception)
        {
            Report(Diagnostics.ParseError, null, exception.Message);
            return;
        }

        var root = document.Root;
        if (root is null)
        {
            return;
        }

        _rootElement = root;

        var classAttribute = root.Attribute(XName.Get("Class", SyntaxHelpers.XamlNamespace));
        if (classAttribute is null || string.IsNullOrWhiteSpace(classAttribute.Value))
        {
            Report(Diagnostics.MissingClass, root);
            return;
        }

        var owner = _compilation.GetTypeByMetadataName(classAttribute.Value.Trim());
        if (owner is null)
        {
            Report(Diagnostics.ClassNotFound, classAttribute, classAttribute.Value);
            return;
        }

        if (!SyntaxHelpers.IsPartial(owner))
        {
            Report(Diagnostics.NotPartial, classAttribute, owner.Name);
            return;
        }

        if (!IsVisualElement(owner))
        {
            Report(Diagnostics.UnsupportedRoot, root, root.Name.LocalName, owner.Name);
            return;
        }

        // ResolveElementType needs the owning type to resolve unprefixed local names, including the root itself.
        _owner = owner;

        var rootType = ResolveElementType(root);
        if (rootType is not null && !CanAssign(owner, rootType))
        {
            Report(Diagnostics.UnsupportedRoot, root, root.Name.LocalName, owner.Name);
        }

        EmitRoot(root);

        var existing = owner.GetMembers("InitializeComponent");
        if (existing.Length > 0)
        {
            var location = existing
                .SelectMany(static member => member.Locations)
                .FirstOrDefault(static location => location.IsInSource)
                ?? GetLocation(root);

            ReportAt(Diagnostics.InitializeComponentAlreadyDeclared, location, owner.Name);
            return;
        }

        var members = new CodeBuilder();
        var fields = _fields.ToString().TrimEnd('\n');
        if (fields.Length > 0)
        {
            members.Raw(fields);
            members.Line();
        }

        members.Open("private void InitializeComponent()");
        members.Raw(_initialize.ToString().TrimEnd('\n'));
        members.Close();

        var helpers = _helpers.ToString().TrimEnd('\n');
        if (helpers.Length > 0)
        {
            members.Line();
            members.Raw(helpers);
        }

        var code = "// <auto-generated/>\n#nullable enable\n" + SyntaxHelpers.WrapInPartialType(owner, members.ToString());
        var hint = $"Xaml.{SyntaxHelpers.SanitizeIdentifier(Path.GetFileNameWithoutExtension(_filePath))}.{SyntaxHelpers.StableSuffix(_filePath)}.g.cs";
        _production.AddSource(hint, SourceText.From(code, Encoding.UTF8));
    }

    private void EmitRoot(XElement root)
    {
        _rootScope = null;

        foreach (var element in root.Elements())
        {
            if (IsResourcesProperty(element))
            {
                EmitResources(element, "this", _owner);
            }
        }

        ApplyAttributes(root, _owner, "this", null);
        ProcessChildren(root, _owner, "this", null);

        foreach (var line in _deferredEvents)
        {
            _current.Line(line);
        }

        _deferredEvents.Clear();
    }

    private void ProcessChildren(XElement element, ITypeSymbol elementType, string elementExpression, string? hostExpression)
    {
        var parentIsVisual = IsVisualElement(elementType);

        foreach (var child in element.Elements())
        {
            if (IsResourcesProperty(child))
            {
                continue;
            }

            if (IsPropertyElement(child))
            {
                EmitPropertyElement(child, elementType, elementExpression, hostExpression);
                continue;
            }

            var type = ResolveElementType(child);
            if (type is null)
            {
                if (!IsReported(child))
                {
                    Report(Diagnostics.UnknownType, child, child.Name.LocalName);
                }

                continue;
            }

            if (parentIsVisual)
            {
                if (!IsVisualElement(type))
                {
                    Report(
                        Diagnostics.UnsupportedConstruct,
                        child,
                        $"'{elementType.Name}' is a visual element and cannot host '{type.Name}'; only visual elements can be children.");
                    continue;
                }

                var visualExpression = EmitControl(child, type, hostExpression: null);
                var items = FindItemsCollection(elementType);
                if (items is not null)
                {
                    _current.Line($"{elementExpression}.{items.Name}.Add({visualExpression});");
                    continue;
                }

                // Single-content containers (visual UserControl/ContentControl/ScrollViewer) expose Content;
                // assigning it lets their own measure/arrange treat the child as the content slot.
                var contentProperty = FindProperty(elementType, "Content");
                if (contentProperty?.SetMethod is not null
                    && !IsCollection(contentProperty.Type)
                    && CanAssign(type, contentProperty.Type))
                {
                    _current.Line($"{elementExpression}.Content = {visualExpression};");
                    continue;
                }

                _current.Line($"{elementExpression}.Add({visualExpression});");
                continue;
            }

            if (IsVisualElement(type))
            {
                Report(
                    Diagnostics.UnsupportedConstruct,
                    child,
                    $"The visual element '{type.Name}' cannot be added to '{elementType.Name}'; host visual elements under a visual root instead.");
                continue;
            }

            if (!SyntaxHelpers.DerivesFrom(type, SyntaxHelpers.ControlMetadataName))
            {
                var collection = FindItemsCollection(elementType);
                if (collection is null)
                {
                    Report(Diagnostics.UnknownType, child, child.Name.LocalName);
                    continue;
                }

                var objectExpression = EmitXamlObject(child, type, hostExpression);
                if (objectExpression is not null)
                {
                    _current.Line($"{elementExpression}.{collection.Name}.Add({objectExpression});");
                }

                continue;
            }

            var expression = EmitControl(child, type, hostExpression);
            if (SyntaxHelpers.DerivesFrom(elementType, SyntaxHelpers.ControlMetadataName))
            {
                _current.Line($"{elementExpression}.Controls.Add({expression});");
            }
            else
            {
                Report(
                    Diagnostics.UnsupportedConstruct,
                    child,
                    $"'{elementType.Name}' is not a Control and cannot host child controls.");
            }
        }
    }

    private string EmitControl(XElement element, ITypeSymbol type, string? hostExpression)
    {
        var name = GetXName(element);
        var inTemplate = _templateDepth > 0;
        string expression;

        if (!inTemplate
            && !string.IsNullOrEmpty(name)
            && SyntaxFacts.IsValidIdentifier(name)
            && !_declaredNames.Contains(name)
            && !HasMember(_owner, name))
        {
            expression = name;
            _declaredNames.Add(name);
            _fields.Line($"private {SyntaxHelpers.TypeName(type)} {name} = null!;");
        }
        else
        {
            if (!inTemplate && !string.IsNullOrEmpty(name))
            {
                Report(Diagnostics.DuplicateName, element, name, _owner.Name);
            }

            expression = NextVariable();
        }

        _current.Line($"var {expression} = new {SyntaxHelpers.TypeName(type)}();");

        if (!inTemplate && !string.IsNullOrEmpty(name) && expression == name)
        {
            _current.Line($"this.{name} = {expression};");
        }

        if (!string.IsNullOrEmpty(name)
            && _scopeExpression is not null
            && (inTemplate || expression == name))
        {
            if (IsVisualElement(type))
            {
                // 视觉模板的 x:Name 是模板部件，不是字段；每实例一个 VisualNameScope，GetTemplateChild 从这里取。
                _current.Line($"{_scopeExpression}.Register({SyntaxHelpers.Quote(name!)}, {expression});");
            }
            else
            {
                _current.Line(
                    $"{SyntaxHelpers.Fqn("XamlRuntime")}.RegisterName({_scopeExpression}, {SyntaxHelpers.Quote(name!)}, {expression});");
            }
        }

        if (!string.IsNullOrEmpty(name))
        {
            // WPF 的 x:Name 同时就是 FrameworkElement.Name；这里也让 x:Name 写原生 Name，
            // 这样 PART_* 只需写一次，按 Control.Name 解析的附着行为也能找到它。
            // 显式的 Name="..." 属性仍在 ApplyAttributes 里应用，后写覆盖前者。
            _current.Line($"{expression}.Name = {SyntaxHelpers.Quote(name!)};");
        }

        foreach (var child in element.Elements())
        {
            if (IsResourcesProperty(child))
            {
                EmitResources(child, expression, type);
            }
        }

        string? childHost = null;
        ApplyAttributes(element, type, expression, childHost);
        ProcessChildren(element, type, expression, childHost);
        EmitTextContent(element, type, expression);
        return expression;
    }

    // 返回 null 表示这个对象无法生成（已报诊断）：调用方要跳过赋值，不能把一个未声明的变量写进代码。
    private string? EmitXamlObject(XElement element, ITypeSymbol type, string? hostExpression)
    {
        // 视觉元素必须走 EmitControl：只有它生成 x:Name 字段并递归用 Add 挂子项。
        if (IsVisualElement(type))
        {
            return EmitControl(element, type, hostExpression);
        }

        if (type.IsAbstract
            || type is not INamedTypeSymbol named
            || named.IsGenericType
            || !HasPublicParameterlessConstructor(named))
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                element,
                $"'{type.Name}' needs a public parameterless constructor to be declared in XAML.");
            return null;
        }

        var variable = NextVariable();
        _current.Line($"var {variable} = new {SyntaxHelpers.TypeName(type)}();");
        ApplyAttributes(element, type, variable, hostExpression);

        foreach (var child in element.Elements())
        {
            if (IsPropertyElement(child))
            {
                EmitObjectPropertyElement(child, type, variable, hostExpression);
                continue;
            }

            var childType = ResolveElementType(child);
            if (childType is null)
            {
                if (!IsReported(child))
                {
                    Report(Diagnostics.UnknownType, child, child.Name.LocalName);
                }

                continue;
            }

            var collection = FindItemsCollection(type);
            if (collection is null)
            {
                Report(
                    Diagnostics.UnsupportedConstruct,
                    child,
                    $"'{type.Name}' has no item collection to receive '{child.Name.LocalName}'.");
                continue;
            }

            var childExpression = EmitXamlObject(child, childType, hostExpression);
            if (childExpression is not null)
            {
                _current.Line($"{variable}.{collection.Name}.Add({childExpression});");
            }
        }

        EmitTextContent(element, type, variable);
        return variable;
    }

    private void EmitObjectPropertyElement(
        XElement propertyElement,
        ITypeSymbol ownerType,
        string ownerExpression,
        string? hostExpression)
    {
        var fullName = propertyElement.Name.LocalName;
        var memberName = fullName.Substring(fullName.LastIndexOf('.') + 1);
        var property = FindProperty(ownerType, memberName);
        if (property is null)
        {
            Report(Diagnostics.UnknownMember, propertyElement, ownerType.Name, memberName);
            return;
        }

        if (IsCollection(property.Type))
        {
            var children = propertyElement.Elements().ToArray();
            if (children.Length == 0)
            {
                return;
            }

            foreach (var child in children)
            {
                if (IsPropertyElement(child))
                {
                    continue;
                }

                var childType = ResolveElementType(child);
                if (childType is null)
                {
                    if (!IsReported(child))
                    {
                        Report(Diagnostics.UnknownType, child, child.Name.LocalName);
                    }

                    continue;
                }

                var childExpression = EmitXamlObject(child, childType, hostExpression);
                if (childExpression is not null)
                {
                    _current.Line($"{ownerExpression}.{property.Name}.Add({childExpression});");
                }
            }

            return;
        }

        var content = propertyElement.Elements().FirstOrDefault(child => !IsPropertyElement(child));
        if (content is not null)
        {
            var contentType = ResolveElementType(content);
            if (contentType is null)
            {
                if (!IsReported(content))
                {
                    Report(Diagnostics.UnknownType, content, content.Name.LocalName);
                }

                return;
            }

            var contentExpression = EmitXamlObject(content, contentType, hostExpression);
            if (contentExpression is not null)
            {
                _current.Line($"{ownerExpression}.{property.Name} = {contentExpression};");
            }

            return;
        }

        var literal = CreateLiteralExpression(property.Type, propertyElement.Value, propertyElement);
        if (literal is not null)
        {
            _current.Line($"{ownerExpression}.{property.Name} = {literal};");
        }
    }

    private static IPropertySymbol? FindItemsCollection(ITypeSymbol type)
    {
        foreach (var name in new[] { "Items", "DropDownItems" })
        {
            var property = FindProperty(type, name);
            if (property is not null && IsCollection(property.Type))
            {
                return property;
            }
        }

        return null;
    }

    /// <summary>
    /// Writes an element's text body to its content property.
    /// </summary>
    /// <remarks>
    /// WPF and Avalonia authors write <c>&lt;Button&gt;OK&lt;/Button&gt;</c> rather than a <c>Content</c> attribute.
    /// The text body is accepted only when the element has no child elements; mixed content stays unsupported.
    /// </remarks>
    private void EmitTextContent(XElement element, ITypeSymbol type, string expression)
    {
        if (element.Elements().Any())
        {
            return;
        }

        var text = element.Value;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var property = FindProperty(type, "Content") ?? FindProperty(type, "Text");
        if (property is null || property.SetMethod is null)
        {
            return;
        }

        if (!LiteralFactory.TryCreate(property.Type, text.Trim(), out var valueExpression, out _))
        {
            return;
        }

        _current.Line($"{expression}.{property.Name} = {valueExpression};");
    }

    private void ApplyAttributes(XElement element, ITypeSymbol type, string targetExpression, string? hostExpression)
    {
        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
            {
                continue;
            }

            if (attribute.Name.NamespaceName == SyntaxHelpers.XamlNamespace)
            {
                // x:Name / x:Class / x:Key 由编译器消费，其余 x: 属性不在子集内。
                continue;
            }

            var memberName = attribute.Name.LocalName;
            var isVisual = IsVisualElement(type);

            if (memberName == "Style")
            {
                if (isVisual)
                {
                    EmitVisualStyleAttribute(attribute, targetExpression);
                    continue;
                }

                if (!IsXamlControl(type)
                    && SyntaxHelpers.DerivesFrom(type, SyntaxHelpers.ControlMetadataName))
                {
                    EmitNativeStyleAttribute(attribute, targetExpression);
                    continue;
                }
            }

            if (memberName.IndexOf('.') >= 0)
            {
                if (isVisual)
                {
                    EmitVisualAttachedAttribute(attribute, targetExpression);
                    continue;
                }

                if (!SyntaxHelpers.DerivesFrom(type, SyntaxHelpers.ControlMetadataName))
                {
                    Report(
                        Diagnostics.UnsupportedConstruct,
                        attribute,
                        $"'{type.Name}' is not a System.Windows.Forms.Control and cannot receive attached properties.");
                    continue;
                }

                EmitAttachedAttribute(attribute, targetExpression, hostExpression);
                continue;
            }

            var property = FindProperty(type, memberName);
            if (memberName == "ContextMenu"
                && !IsXamlControl(type)
                && SyntaxHelpers.DerivesFrom(type, SyntaxHelpers.ControlMetadataName))
            {
                property = FindProperty(type, "ContextMenuStrip") ?? property;
            }

            if (property is not null)
            {
                if (isVisual)
                {
                    EmitVisualPropertyValue(attribute, targetExpression, property);
                    continue;
                }

                EmitPropertyValue(attribute, targetExpression, property, attribute.Value, hostExpression);
                continue;
            }

            var eventSymbol = FindEvent(type, memberName);
            if (eventSymbol is not null)
            {
                // A real WinForms event always wins: XAML KeyDown/KeyUp/MouseDown/Click bind natively.
                EmitEvent(attribute, targetExpression, eventSymbol, attribute.Value);
                continue;
            }

            if (!IsXamlControl(type)
                && SyntaxHelpers.DerivesFrom(type, SyntaxHelpers.ControlMetadataName)
                && TryGetBridgedInputEvent(memberName, out var addMethod, out var bridgedArgsType))
            {
                EmitBridgedInputEvent(
                    attribute,
                    targetExpression,
                    memberName,
                    addMethod,
                    bridgedArgsType,
                    attribute.Value);
                continue;
            }

            Report(Diagnostics.UnknownMember, attribute, type.Name, memberName);
        }
    }

    private static readonly Dictionary<string, (string ArgsType, string AddMethod)> BridgedInputEvents = new(StringComparer.Ordinal)
    {
        ["PointerPressed"] = ("VeloxDev.WinForms.Xaml.PointerPressedEventArgs", "AddPointerPressed"),
        ["PointerMoved"] = ("VeloxDev.WinForms.Xaml.PointerMovedEventArgs", "AddPointerMoved"),
        ["PointerReleased"] = ("VeloxDev.WinForms.Xaml.PointerReleasedEventArgs", "AddPointerReleased"),
        ["PointerEntered"] = ("VeloxDev.WinForms.Xaml.PointerEnteredEventArgs", "AddPointerEntered"),
        ["PointerExited"] = ("VeloxDev.WinForms.Xaml.PointerExitedEventArgs", "AddPointerExited"),
        ["PointerWheelChanged"] = ("VeloxDev.WinForms.Xaml.PointerWheelEventArgs", "AddPointerWheelChanged"),
        ["KeyInputDown"] = ("VeloxDev.WinForms.Xaml.KeyInputEventArgs", "AddKeyInputDown"),
        ["KeyInputUp"] = ("VeloxDev.WinForms.Xaml.KeyInputEventArgs", "AddKeyInputUp"),
    };

    private static bool TryGetBridgedInputEvent(string name, out string addMethod, out string argsType)
    {
        if (BridgedInputEvents.TryGetValue(name, out var value))
        {
            addMethod = value.AddMethod;
            argsType = value.ArgsType;
            return true;
        }

        addMethod = string.Empty;
        argsType = string.Empty;
        return false;
    }

    private void EmitBridgedInputEvent(
        XObject source,
        string targetExpression,
        string eventName,
        string addMethod,
        string argsTypeName,
        string handler)
    {
        var methods = FindHandlerMethods(_owner, handler);
        if (methods.Count == 0)
        {
            Report(Diagnostics.MissingEventHandler, source, handler, _owner.Name);
            return;
        }

        if (methods.Count > 1)
        {
            Report(Diagnostics.AmbiguousEventHandler, source, handler, _owner.Name);
            return;
        }

        var argsType = _compilation.GetTypeByMetadataName(argsTypeName);
        if (argsType is not null
            && !ValidateBridgedHandlerSignature(source, methods[0], argsType, eventName, handler))
        {
            return;
        }

        _current.Line($"{SyntaxHelpers.Fqn("XamlInput")}.{addMethod}({targetExpression}, this.{handler});");
    }

    private bool ValidateBridgedHandlerSignature(
        XObject source,
        IMethodSymbol method,
        ITypeSymbol argsType,
        string eventName,
        string handlerName)
    {
        if (method.ReturnsVoid
            && method.Parameters.Length == 2
            && method.Parameters[0].RefKind == RefKind.None
            && method.Parameters[1].RefKind == RefKind.None
            && method.Parameters[0].Type.SpecialType == SpecialType.System_Object
            && AcceptsType(method.Parameters[1].Type, argsType))
        {
            return true;
        }

        Report(Diagnostics.InvalidEventHandlerSignature, source, handlerName, eventName);
        return false;
    }

    private void EmitNativeStyleAttribute(XAttribute attribute, string targetExpression)
    {
        if (!TryReadStaticResourceKey(attribute.Value, out var key))
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                attribute,
                "A native control Style supports only {StaticResource Key}.");
            return;
        }

        if (_rootScope is null)
        {
            Report(Diagnostics.UnsupportedConstruct, attribute, "A native control Style needs a Control scope.");
            return;
        }

        _current.Line(
            $"{SyntaxHelpers.Fqn("XamlRuntime")}.ApplyStyleResource({targetExpression}, {_rootScope}, {SyntaxHelpers.Quote(key)});");
    }

    private void EmitPropertyValue(
        XObject source,
        string targetExpression,
        IPropertySymbol property,
        string rawValue,
        string? hostExpression)
    {
        if (property.SetMethod is null)
        {
            Report(Diagnostics.UnsupportedConstruct, source, $"'{property.Name}' is read-only in XAML.");
            return;
        }

        if (TryParseMarkup(rawValue, out var extension, out var arguments, out var positional))
        {
            switch (extension)
            {
                case "x:Null":
                case "Null":
                    _current.Line($"{targetExpression}.{property.Name} = null;");
                    return;

                case "Binding":
                    EmitBinding(source, targetExpression, property, arguments, positional, hostExpression);
                    return;

                case "TemplateBinding":
                    EmitBinding(source, targetExpression, property, arguments, positional, hostExpression, templateBinding: true);
                    return;

                case "StaticResource":
                    EmitStaticResource(source, targetExpression, property, arguments, positional, hostExpression);
                    return;

                case "DynamicResource":
                case "x:DynamicResource":
                    EmitDynamicResourceProperty(source, targetExpression, property, arguments, positional, hostExpression);
                    return;

                case "x:Static":
                    var staticExpression = CreateStaticExpression(
                        source,
                        positional ?? (arguments.TryGetValue("Member", out var staticMember) ? staticMember : null));
                    if (staticExpression is not null)
                    {
                        _current.Line($"{targetExpression}.{property.Name} = {staticExpression};");
                    }

                    return;

                default:
                    Report(Diagnostics.UnsupportedConstruct, source, $"Markup extension '{extension}' is not supported.");
                    return;
            }
        }

        var expression = CreateLiteralExpression(property.Type, rawValue, source);
        if (expression is null)
        {
            return;
        }

        _current.Line($"{targetExpression}.{property.Name} = {expression};");
    }

    private string? CreateLiteralExpression(ITypeSymbol type, string rawValue, XObject source)
    {
        if (LiteralFactory.TryCreate(type, rawValue, out var expression, out _))
        {
            return expression;
        }

        if (type.SpecialType is SpecialType.System_Object or SpecialType.System_String)
        {
            return SyntaxHelpers.Quote(UnescapeMarkup(rawValue));
        }

        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
        {
            return SyntaxHelpers.Quote(UnescapeMarkup(rawValue));
        }

        // 媒体原语由运行时统一解析（颜色/渐变/路径/点列表），生成器不需要认识每一种字面量。
        if (IsRuntimeConvertedType(type))
        {
            var runtimeType = RuntimeTypeName(type);
            return $"({SyntaxHelpers.TypeName(type)}){SyntaxHelpers.Fqn("XamlRuntime")}.ConvertValue("
                + $"{SyntaxHelpers.Quote(UnescapeMarkup(rawValue))}, typeof({runtimeType}))";
        }

        Report(Diagnostics.UnsupportedValue, source, rawValue, type.ToDisplayString());
        return $"{SyntaxHelpers.Fqn("XamlRuntime")}.ConvertValue({SyntaxHelpers.Quote(UnescapeMarkup(rawValue))}, typeof({SyntaxHelpers.TypeName(type)}))";
    }

    private static string RuntimeTypeName(ITypeSymbol type)
        => SyntaxHelpers.TypeName(type.WithNullableAnnotation(NullableAnnotation.NotAnnotated));

    private static bool IsRuntimeConvertedType(ITypeSymbol type)
    {
        // 可空引用类型的 ToDisplayString 会带 '?'（Brush?），先去掉注解再比较。
        var normalized = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
        return normalized.ToDisplayString() == "System.Drawing.PointF[]"
            || SyntaxHelpers.DerivesFrom(normalized, "VeloxDev.WinForms.Xaml.Brush")
            || SyntaxHelpers.DerivesFrom(normalized, "VeloxDev.WinForms.Xaml.Geometry")
            || SyntaxHelpers.DerivesFrom(normalized, "VeloxDev.WinForms.Xaml.ImageSource");
    }

    private void EmitBinding(
        XObject source,
        string targetExpression,
        IPropertySymbol property,
        Dictionary<string, string> arguments,
        string? positional,
        string? hostExpression,
        bool templateBinding = false)
    {
        var bindingExpression = BuildBindingExpression(
            source,
            property.Type,
            arguments,
            positional,
            templateBinding,
            allowTwoWay: true);
        if (bindingExpression is null)
        {
            return;
        }

        if (TryGetDependencyPropertyReference(property, out var dependencyProperty))
        {
            _current.Line($"{targetExpression}.SetBinding({dependencyProperty}, {bindingExpression});");
            return;
        }

        // 非 Control 对象（KeyBinding / MouseBinding / 自定义动作）没有 DP 管线，走宿主 DataContext 的 CLR 属性绑定。
        if (!SyntaxHelpers.DerivesFrom(property.ContainingType, SyntaxHelpers.ControlMetadataName))
        {
            if (hostExpression is null)
            {
                Report(
                    Diagnostics.UnsupportedConstruct,
                    source,
                    $"A binding on '{property.ContainingType.Name}.{property.Name}' needs a Control to host its data context.");
                return;
            }

            _current.Line(
                $"{SyntaxHelpers.Fqn("XamlRuntime")}.SetObjectBinding({hostExpression}, {targetExpression}, "
                + $"{SyntaxHelpers.Quote(property.Name)}, {bindingExpression});");
            return;
        }

        if (hostExpression is null)
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                source,
                $"A native binding on '{property.Name}' needs a Control root to host it.");
            return;
        }

        _current.Line(
            $"{hostExpression}.SetNativeBinding({targetExpression}, {SyntaxHelpers.Quote(property.Name)}, {bindingExpression});");
    }

    private string? BuildBindingExpression(
        XObject source,
        ITypeSymbol? targetType,
        Dictionary<string, string> arguments,
        string? positional,
        bool templateBinding,
        bool allowTwoWay)
    {
        var path = arguments.TryGetValue("Path", out var explicitPath) ? explicitPath : positional ?? string.Empty;
        var mode = arguments.TryGetValue("Mode", out var explicitMode) ? explicitMode : "OneWay";

        if (mode is not ("OneWay" or "TwoWay" or "OneTime"))
        {
            Report(Diagnostics.UnsupportedValue, source, mode, targetType?.ToDisplayString() ?? "binding");
            return null;
        }

        if (!allowTwoWay && mode == "TwoWay")
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                source,
                "TwoWay is not supported for attached values on native controls; the binding behaves as OneWay.");
            mode = "OneWay";
        }

        var initializers = new List<string>();
        if (mode != "OneWay")
        {
            initializers.Add($"Mode = {SyntaxHelpers.Fqn("BindingMode")}.{mode}");
        }

        if (arguments.TryGetValue("ElementName", out var elementName) && !string.IsNullOrEmpty(elementName))
        {
            initializers.Add($"ElementName = {SyntaxHelpers.Quote(elementName)}");
        }

        if (arguments.TryGetValue("RelativeSource", out var relativeSource))
        {
            var relativeExpression = CreateRelativeSourceExpression(source, relativeSource);
            if (relativeExpression is null)
            {
                return null;
            }

            initializers.Add($"RelativeSource = {relativeExpression}");
        }

        if (arguments.TryGetValue("Source", out var sourceValue))
        {
            var sourceExpression = CreateBindingValueExpression(source, sourceValue);
            if (sourceExpression is null)
            {
                return null;
            }

            initializers.Add($"Source = {sourceExpression}");
        }

        if (arguments.TryGetValue("Converter", out var converterValue))
        {
            var converterExpression = CreateBindingValueExpression(source, converterValue);
            if (converterExpression is null)
            {
                return null;
            }

            initializers.Add($"Converter = ({SyntaxHelpers.Fqn("IValueConverter")}){converterExpression}");
        }

        if (arguments.TryGetValue("ConverterParameter", out var converterParameter))
        {
            var parameterExpression = CreateBindingValueExpression(source, converterParameter);
            if (parameterExpression is null)
            {
                return null;
            }

            initializers.Add($"ConverterParameter = {parameterExpression}");
        }

        if (arguments.TryGetValue("StringFormat", out var stringFormat))
        {
            // XAML 的 "{}" 前缀是转义：只有格式串自己要以 "{" 开头时才写它，不能原样交给 string.Format。
            initializers.Add($"StringFormat = {SyntaxHelpers.Quote(UnescapeMarkup(stringFormat))}");
        }

        if (arguments.TryGetValue("FallbackValue", out var fallbackValue))
        {
            var fallbackExpression = CreateBindingValueExpression(source, fallbackValue);
            if (fallbackExpression is null)
            {
                return null;
            }

            initializers.Add($"FallbackValue = {fallbackExpression}");
        }

        if (arguments.TryGetValue("TargetNullValue", out var targetNullValue))
        {
            var targetNullExpression = CreateBindingValueExpression(source, targetNullValue);
            if (targetNullExpression is null)
            {
                return null;
            }

            initializers.Add($"TargetNullValue = {targetNullExpression}");
        }

        if (arguments.TryGetValue("UpdateSourceTrigger", out var updateTrigger))
        {
            if (updateTrigger is not ("Default" or "PropertyChanged" or "LostFocus" or "Explicit"))
            {
                Report(Diagnostics.UnsupportedValue, source, updateTrigger, "UpdateSourceTrigger");
                return null;
            }

            initializers.Add($"UpdateSourceTrigger = {SyntaxHelpers.Fqn("UpdateSourceTrigger")}.{updateTrigger}");
        }

        var typeName = templateBinding ? SyntaxHelpers.Fqn("TemplateBinding") : SyntaxHelpers.Fqn("Binding");
        var expression = $"new {typeName}({SyntaxHelpers.Quote(path)})";
        if (initializers.Count > 0)
        {
            expression += $" {{ {string.Join(", ", initializers)} }}";
        }

        return expression;
    }

    private string? CreateBindingValueExpression(XObject source, string rawValue)
    {
        if (TryParseMarkup(rawValue, out var extension, out var arguments, out var positional))
        {
            switch (extension)
            {
                case "x:Null":
                case "Null":
                    return "null";

                case "StaticResource":
                    var key = arguments.TryGetValue("Key", out var explicitKey) ? explicitKey : positional;
                    if (string.IsNullOrEmpty(key))
                    {
                        Report(Diagnostics.UnsupportedResource, source, "A StaticResource reference needs a key.");
                        return null;
                    }

                    if (IsVisualElement(_owner))
                    {
                        return $"{VisualFqn("VisualResourceLookup")}.Get<object>(this, {SyntaxHelpers.Quote(key!)})";
                    }

                    if (_rootScope is null)
                    {
                        Report(Diagnostics.UnsupportedConstruct, source, "A StaticResource reference needs a Control scope.");
                        return null;
                    }

                    return $"{SyntaxHelpers.Fqn("XamlRuntime")}.GetResource<object>({_rootScope}, {SyntaxHelpers.Quote(key!)})";

                case "x:Static":
                    var memberPath = positional
                        ?? (arguments.TryGetValue("Member", out var member) ? member : null);
                    return CreateStaticExpression(source, memberPath);

                default:
                    Report(Diagnostics.UnsupportedConstruct, source, $"Markup extension '{extension}' is not supported in a binding value.");
                    return null;
            }
        }

        return SyntaxHelpers.Quote(UnescapeMarkup(rawValue));
    }

    private string? CreateStaticExpression(XObject source, string? memberPath)
    {
        if (string.IsNullOrWhiteSpace(memberPath))
        {
            Report(Diagnostics.UnsupportedValue, source, memberPath ?? string.Empty, "x:Static member");
            return null;
        }

        var text = memberPath!.Trim();
        for (var dot = text.LastIndexOf('.'); dot > 0; dot = text.LastIndexOf('.', dot - 1))
        {
            var typeName = text.Substring(0, dot);
            var memberName = text.Substring(dot + 1);
            var type = ResolveTypeReference(typeName, GetContextElement(source));
            if (type is null)
            {
                continue;
            }

            var member = FindStaticMember(type, memberName);
            if (member is null)
            {
                Report(Diagnostics.UnknownMember, source, type.Name, memberName);
                return null;
            }

            return $"{SyntaxHelpers.TypeName(type)}.{member}";
        }

        Report(Diagnostics.UnknownType, source, text);
        return null;
    }

    private static string? FindStaticMember(ITypeSymbol type, string memberName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(memberName))
            {
                if (member is IFieldSymbol field && field.IsStatic && !field.IsImplicitlyDeclared)
                {
                    return field.Name;
                }

                if (member is IPropertySymbol property && property.IsStatic && property.GetMethod is not null)
                {
                    return property.Name;
                }
            }
        }

        return null;
    }

    private string? CreateRelativeSourceExpression(XObject source, string rawValue)
    {
        var text = rawValue.Trim();
        string mode;
        Dictionary<string, string> arguments = new(StringComparer.OrdinalIgnoreCase);
        string? positional;

        if (TryParseMarkup(text, out var extension, out var parsedArguments, out var parsedPositional))
        {
            if (extension is not ("RelativeSource" or "x:RelativeSource"))
            {
                Report(
                    Diagnostics.UnsupportedConstruct,
                    source,
                    "RelativeSource supports only {RelativeSource Self}, {RelativeSource TemplatedParent} or {RelativeSource FindAncestor, AncestorType=...}.");
                return null;
            }

            arguments = parsedArguments;
            positional = parsedPositional;
            mode = positional ?? (arguments.TryGetValue("Mode", out var explicitMode) ? explicitMode : "Self");
        }
        else
        {
            positional = text;
            mode = text;
        }

        if (string.Equals(mode, "Self", StringComparison.OrdinalIgnoreCase))
        {
            return $"{SyntaxHelpers.Fqn("RelativeSource")}.Self";
        }

        if (string.Equals(mode, "TemplatedParent", StringComparison.OrdinalIgnoreCase))
        {
            return $"{SyntaxHelpers.Fqn("RelativeSource")}.TemplatedParent";
        }

        if (!string.Equals(mode, "FindAncestor", StringComparison.OrdinalIgnoreCase))
        {
            Report(Diagnostics.UnsupportedValue, source, mode, "RelativeSourceMode");
            return null;
        }

        if (!arguments.TryGetValue("AncestorType", out var ancestorTypeText))
        {
            Report(Diagnostics.UnsupportedConstruct, source, "A FindAncestor RelativeSource needs an AncestorType.");
            return null;
        }

        var ancestorType = ResolveTypeFromMarkup(source, ancestorTypeText);
        if (ancestorType is null)
        {
            return null;
        }

        var level = 1;
        if (arguments.TryGetValue("AncestorLevel", out var levelText)
            && !int.TryParse(levelText, NumberStyles.Integer, CultureInfo.InvariantCulture, out level))
        {
            Report(Diagnostics.UnsupportedValue, source, levelText, "System.Int32");
            return null;
        }

        return $"new {SyntaxHelpers.Fqn("RelativeSource")}({SyntaxHelpers.Fqn("RelativeSourceMode")}.FindAncestor) "
            + $"{{ AncestorType = typeof({SyntaxHelpers.TypeName(ancestorType)}), AncestorLevel = {Math.Max(1, level)} }}";
    }

    private ITypeSymbol? ResolveTypeFromMarkup(XObject source, string rawValue)
    {
        var text = rawValue.Trim();
        if (TryParseMarkup(text, out var extension, out var arguments, out var positional))
        {
            if (extension is not ("x:Type" or "Type"))
            {
                Report(Diagnostics.UnsupportedValue, source, text, "Type");
                return null;
            }

            text = arguments.TryGetValue("TypeName", out var explicitType) ? explicitType : positional ?? string.Empty;
        }

        var type = ResolveTypeReference(text, GetContextElement(source));
        if (type is null)
        {
            Report(Diagnostics.UnknownType, source, text);
        }

        return type;
    }

    private XElement GetContextElement(XObject source)
        => source as XElement ?? (source as XAttribute)?.Parent ?? _rootElement!;

    private void EmitStaticResource(
        XObject source,
        string targetExpression,
        IPropertySymbol property,
        Dictionary<string, string> arguments,
        string? positional,
        string? hostExpression)
    {
        var key = arguments.TryGetValue("Key", out var explicitKey) ? explicitKey : positional;
        if (string.IsNullOrEmpty(key))
        {
            Report(Diagnostics.UnsupportedResource, source, "A StaticResource reference needs a key.");
            return;
        }

        if (IsVisualElement(_owner))
        {
            if (property.Name == "Style")
            {
                _current.Line(
                    $"{targetExpression}.Style = {VisualFqn("VisualResourceLookup")}"
                    + $".Get<{VisualFqn("VisualStyle")}>(this, {SyntaxHelpers.Quote(key!)});");
                return;
            }

            _current.Line(
                $"{targetExpression}.{property.Name} = {VisualFqn("VisualResourceLookup")}"
                + $".Get<{SyntaxHelpers.TypeName(property.Type)}>(this, {SyntaxHelpers.Quote(key!)});");
            return;
        }

        if (_rootScope is null)
        {
            Report(Diagnostics.UnsupportedConstruct, source, "A StaticResource reference needs a Control scope.");
            return;
        }

        if (property.Name == "Style")
        {
            _current.Line($"{targetExpression}.ApplyStyleResource({_rootScope}, {SyntaxHelpers.Quote(key!)});");
            return;
        }

        _current.Line(
            $"{targetExpression}.{property.Name} = {SyntaxHelpers.Fqn("XamlRuntime")}.GetResource<{SyntaxHelpers.TypeName(property.Type)}>({_rootScope}, {SyntaxHelpers.Quote(key!)});");
    }

    private void EmitDynamicResourceProperty(
        XObject source,
        string targetExpression,
        IPropertySymbol property,
        Dictionary<string, string> arguments,
        string? positional,
        string? hostExpression)
    {
        var key = arguments.TryGetValue("Key", out var explicitKey) ? explicitKey : positional;
        if (string.IsNullOrEmpty(key))
        {
            Report(Diagnostics.UnsupportedResource, source, "A DynamicResource reference needs a key.");
            return;
        }

        if (hostExpression is null)
        {
            Report(Diagnostics.UnsupportedConstruct, source, "A DynamicResource reference needs a Control scope.");
            return;
        }

        if (!TryGetDependencyPropertyReference(property, out var dependencyProperty))
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                source,
                $"A DynamicResource reference needs a dependency property; '{property.ContainingType.Name}.{property.Name}' is a CLR property.");
            return;
        }

        _current.Line(
            $"{SyntaxHelpers.Fqn("XamlRuntime")}.SetDynamicResource({hostExpression}, {targetExpression}, "
            + $"{dependencyProperty}, {SyntaxHelpers.Quote(key!)});");
    }

    private void EmitAttachedAttribute(XAttribute attribute, string targetExpression, string? hostExpression)
    {
        var localName = attribute.Name.LocalName;
        var dot = localName.IndexOf('.');
        if (dot <= 0 || dot == localName.Length - 1 || localName.IndexOf('.', dot + 1) >= 0)
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                attribute,
                $"Attached XAML attribute '{localName}' must be written as 'Owner.Property' or 'prefix:Owner.Property'.");
            return;
        }

        var namespaceName = attribute.Name.NamespaceName;
        var clrNamespace = GetClrNamespace(namespaceName);
        if (clrNamespace is null)
        {
            // Unprefixed attributes carry no XML namespace and name a VeloxDev owner; a legacy `v:` prefix means
            // the same. Explicit clr-namespace prefixes (local: / behaviors:) keep resolving as written.
            if (namespaceName.Length == 0
                || (attribute.Parent is XElement ownerElement
                    && GetPrefixForNamespace(ownerElement, namespaceName) == "v"))
            {
                clrNamespace = SyntaxHelpers.RuntimeNamespace;
            }
        }

        if (clrNamespace is null)
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                attribute,
                "An attached XAML attribute needs a clr-namespace prefix (for example xmlns:local=\"clr-namespace:My.Namespace\" or xmlns:native=\"clr-namespace:System.Windows.Forms;assembly=System.Windows.Forms\").");
            return;
        }

        var ownerName = localName.Substring(0, dot);
        var propertyName = localName.Substring(dot + 1);
        var ownerType = string.Equals(clrNamespace, SyntaxHelpers.RuntimeNamespace, StringComparison.Ordinal)
            ? ResolveVisualAdapterType(ownerName, attribute)
            : _compilation.GetTypeByMetadataName(clrNamespace + "." + ownerName);
        if (ownerType is null)
        {
            Report(Diagnostics.UnknownType, attribute, ownerName);
            return;
        }

        if (!TryGetAttachedPropertyReference(ownerType, propertyName, out var dependencyProperty, out var valueType))
        {
            Report(Diagnostics.UnknownAttachedProperty, attribute, ownerType.Name, propertyName);
            return;
        }

        var rawValue = attribute.Value;
        if (TryParseMarkup(rawValue, out var extension, out var arguments, out var positional))
        {
            switch (extension)
            {
                case "x:Null":
                case "Null":
                    _current.Line($"{SyntaxHelpers.Fqn("XamlRuntime")}.SetAttachedValue({targetExpression}, {dependencyProperty}, null);");
                    return;

                case "Binding":
                    EmitAttachedBinding(attribute, targetExpression, dependencyProperty, arguments, positional, hostExpression);
                    return;

                case "StaticResource":
                    EmitAttachedStaticResource(attribute, targetExpression, dependencyProperty, arguments, positional);
                    return;

                case "DynamicResource":
                case "x:DynamicResource":
                    EmitAttachedDynamicResource(attribute, targetExpression, dependencyProperty, arguments, positional, hostExpression);
                    return;

                case "x:Static":
                    var attachedStatic = CreateStaticExpression(
                        attribute,
                        positional ?? (arguments.TryGetValue("Member", out var attachedMember) ? attachedMember : null));
                    if (attachedStatic is not null)
                    {
                        _current.Line(
                            $"{SyntaxHelpers.Fqn("XamlRuntime")}.SetAttachedValue({targetExpression}, {dependencyProperty}, {attachedStatic});");
                    }

                    return;

                default:
                    Report(
                        Diagnostics.UnsupportedConstruct,
                        attribute,
                        $"Markup extension '{extension}' is not supported on an attached property.");
                    return;
            }
        }

        var valueExpression = CreatePropertyValueExpression(attribute, dependencyProperty, valueType, rawValue);
        if (valueExpression is not null)
        {
            _current.Line($"{SyntaxHelpers.Fqn("XamlRuntime")}.SetAttachedValue({targetExpression}, {dependencyProperty}, {valueExpression});");
        }
    }

    // 视觉元素的附加属性由生成器写依赖属性本身：值类型在生成期已知，直接 Emit SetValue(Owner.XxxProperty, value)。
    // 运行时的 XamlRuntime.SetAttachedValue 只接受 System.Windows.Forms.Control，不能用于 VisualElement。
    private void EmitVisualAttachedAttribute(XAttribute attribute, string targetExpression)
    {
        var localName = attribute.Name.LocalName;
        var dot = localName.IndexOf('.');
        if (dot <= 0 || dot == localName.Length - 1 || localName.IndexOf('.', dot + 1) >= 0)
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                attribute,
                $"Attached XAML attribute '{localName}' must be written as 'Owner.Property' or 'prefix:Owner.Property'.");
            return;
        }

        var ownerName = localName.Substring(0, dot);
        var propertyName = localName.Substring(dot + 1);
        var clrNamespace = GetClrNamespace(attribute.Name.NamespaceName);

        // 视觉根的附加属性也允许无前缀（Grid.Row / Canvas.Left）；owner 按视觉层优先解析。
        // 显式 clr-namespace 仍然优先，local: 等用户前缀不受影响。
        var ownerType = clrNamespace is not null
            ? _compilation.GetTypeByMetadataName(clrNamespace + "." + ownerName)
            : ResolveVisualAdapterType(ownerName, attribute);
        if (ownerType is null)
        {
            Report(Diagnostics.UnknownType, attribute, ownerName);
            return;
        }

        if (!TryGetAttachedPropertyReference(ownerType, propertyName, out var dependencyProperty, out var valueType))
        {
            Report(Diagnostics.UnknownAttachedProperty, attribute, ownerType.Name, propertyName);
            return;
        }

        if (TryParseMarkup(attribute.Value, out var extension, out var arguments, out var positional))
        {
            switch (extension)
            {
                case "x:Null":
                case "Null":
                    _current.Line($"{targetExpression}.SetValue({dependencyProperty}, null);");
                    return;

                case "Binding":
                case "TemplateBinding":
                    var bindingExpression = BuildBindingExpression(
                        attribute,
                        valueType,
                        arguments,
                        positional,
                        templateBinding: extension == "TemplateBinding",
                        allowTwoWay: true);
                    if (bindingExpression is not null)
                    {
                        _current.Line(
                            $"{SyntaxHelpers.Fqn("VisualBindingOperations")}.Bind({targetExpression}, {dependencyProperty}, {bindingExpression});");
                    }

                    return;

                case "StaticResource":
                    var key = arguments.TryGetValue("Key", out var explicitKey) ? explicitKey : positional;
                    if (string.IsNullOrEmpty(key))
                    {
                        Report(Diagnostics.UnsupportedResource, attribute, "A StaticResource reference needs a key.");
                        return;
                    }

                    var resourceType = valueType is null ? "object" : SyntaxHelpers.TypeName(valueType);
                    _current.Line(
                        $"{targetExpression}.SetValue({dependencyProperty}, "
                        + $"{VisualFqn("VisualResourceLookup")}.Get<{resourceType}>(this, {SyntaxHelpers.Quote(key!) }));");
                    return;

                case "DynamicResource":
                case "x:DynamicResource":
                    Report(
                        Diagnostics.UnsupportedConstruct,
                        attribute,
                        "DynamicResource is not implemented on the visual backend; use StaticResource.");
                    return;

                case "x:Static":
                    var staticExpression = CreateStaticExpression(
                        attribute,
                        positional ?? (arguments.TryGetValue("Member", out var member) ? member : null));
                    if (staticExpression is not null)
                    {
                        _current.Line($"{targetExpression}.SetValue({dependencyProperty}, {staticExpression});");
                    }

                    return;

                default:
                    Report(
                        Diagnostics.UnsupportedConstruct,
                        attribute,
                        $"Markup extension '{extension}' is not supported on a visual attached property.");
                    return;
            }
        }

        var valueExpression = valueType is null
            ? $"{SyntaxHelpers.Fqn("XamlRuntime")}.ConvertValue({SyntaxHelpers.Quote(UnescapeMarkup(attribute.Value))}, {dependencyProperty}.PropertyType)"
            : CreateLiteralExpression(valueType, attribute.Value, attribute);
        if (valueExpression is null)
        {
            return;
        }

        _current.Line($"{targetExpression}.SetValue({dependencyProperty}, {valueExpression});");
    }

    private void EmitAttachedBinding(
        XObject source,
        string targetExpression,
        string dependencyProperty,
        Dictionary<string, string> arguments,
        string? positional,
        string? hostExpression)
    {
        if (hostExpression is null)
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                source,
                "An attached binding needs a Control in the tree to host its data context.");
            return;
        }

        var bindingExpression = BuildBindingExpression(
            source,
            targetType: null,
            arguments,
            positional,
            templateBinding: false,
            allowTwoWay: false);
        if (bindingExpression is null)
        {
            return;
        }

        _current.Line(
            $"{hostExpression}.SetAttachedBinding({targetExpression}, {dependencyProperty}, {bindingExpression});");
    }

    private void EmitAttachedStaticResource(
        XObject source,
        string targetExpression,
        string dependencyProperty,
        Dictionary<string, string> arguments,
        string? positional)
    {
        var key = arguments.TryGetValue("Key", out var explicitKey) ? explicitKey : positional;
        if (string.IsNullOrEmpty(key))
        {
            Report(Diagnostics.UnsupportedResource, source, "A StaticResource reference needs a key.");
            return;
        }

        if (_rootScope is null)
        {
            Report(Diagnostics.UnsupportedConstruct, source, "A StaticResource reference needs a Control scope.");
            return;
        }

        _current.Line(
            $"{SyntaxHelpers.Fqn("XamlRuntime")}.SetAttachedValue({targetExpression}, {dependencyProperty}, "
            + $"{SyntaxHelpers.Fqn("XamlRuntime")}.GetResource<object>({_rootScope}, {SyntaxHelpers.Quote(key!)}));");
    }

    private void EmitAttachedDynamicResource(
        XObject source,
        string targetExpression,
        string dependencyProperty,
        Dictionary<string, string> arguments,
        string? positional,
        string? hostExpression)
    {
        var key = arguments.TryGetValue("Key", out var explicitKey) ? explicitKey : positional;
        if (string.IsNullOrEmpty(key))
        {
            Report(Diagnostics.UnsupportedResource, source, "A DynamicResource reference needs a key.");
            return;
        }

        if (hostExpression is null)
        {
            Report(Diagnostics.UnsupportedConstruct, source, "A DynamicResource reference needs a Control scope.");
            return;
        }

        _current.Line(
            $"{SyntaxHelpers.Fqn("XamlRuntime")}.SetDynamicResource({hostExpression}, {targetExpression}, "
            + $"{dependencyProperty}, {SyntaxHelpers.Quote(key!)});");
    }

    private void EmitTriggers(XElement triggersElement, ITypeSymbol ownerType, string ownerExpression)
    {
        if (!IsXamlControl(ownerType))
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                triggersElement,
                $"'{ownerType.Name}' cannot own triggers because it is not a VeloxDev.WinForms.Xaml.Controls.Control.");
            return;
        }

        foreach (var child in triggersElement.Elements())
        {
            if (!IsTriggerElement(child))
            {
                Report(
                    Diagnostics.UnsupportedConstruct,
                    child,
                    "The Triggers collection only accepts Trigger, DataTrigger or MultiTrigger elements.");
                continue;
            }

            EmitTrigger(child, ownerType, ownerExpression);
        }
    }

    private void EmitTrigger(XElement triggerElement, ITypeSymbol ownerType, string collectionOwnerExpression)
    {
        switch (triggerElement.Name.LocalName)
        {
            case "DataTrigger":
                EmitDataTrigger(triggerElement, ownerType, collectionOwnerExpression);
                break;

            case "MultiTrigger":
                EmitMultiTrigger(triggerElement, ownerType, collectionOwnerExpression);
                break;

            default:
                EmitPropertyTrigger(triggerElement, ownerType, collectionOwnerExpression);
                break;
        }
    }

    private void EmitPropertyTrigger(XElement triggerElement, ITypeSymbol ownerType, string collectionOwnerExpression)
    {
        var propertyPath = triggerElement.Attribute("Property")?.Value;
        if (string.IsNullOrWhiteSpace(propertyPath))
        {
            Report(Diagnostics.UnsupportedConstruct, triggerElement, "A Trigger needs a Property attribute.");
            return;
        }

        if (!TryResolveControlProperty(ownerType, propertyPath!.Trim(), triggerElement, out var dependencyProperty, out var valueType))
        {
            return;
        }

        var triggerVariable = NextVariable();
        _current.Line($"var {triggerVariable} = new {SyntaxHelpers.Fqn("Trigger")}({dependencyProperty});");

        var valueAttribute = triggerElement.Attribute("Value");
        if (valueAttribute is not null)
        {
            var valueExpression = CreatePropertyValueExpression(valueAttribute, dependencyProperty, valueType, valueAttribute.Value);
            if (valueExpression is null)
            {
                return;
            }

            _current.Line($"{triggerVariable}.Value = {valueExpression};");
        }

        EmitTriggerChildren(triggerElement, ownerType, triggerVariable);
        _current.Line($"{collectionOwnerExpression}.Triggers.Add({triggerVariable});");
    }

    private void EmitDataTrigger(XElement triggerElement, ITypeSymbol ownerType, string collectionOwnerExpression)
    {
        string? path = null;
        var bindingAttribute = triggerElement.Attribute("Binding");
        if (bindingAttribute is not null)
        {
            if (!TryParseMarkup(bindingAttribute.Value, out var extension, out var arguments, out var positional)
                || extension != "Binding")
            {
                Report(
                    Diagnostics.UnsupportedConstruct,
                    bindingAttribute,
                    "A DataTrigger Binding supports only a {Binding Path} expression.");
                return;
            }

            path = arguments.TryGetValue("Path", out var explicitPath) ? explicitPath : positional;
        }

        if (string.IsNullOrEmpty(path))
        {
            path = triggerElement.Attribute("Path")?.Value;
        }

        if (string.IsNullOrEmpty(path))
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                triggerElement,
                "A DataTrigger needs a Binding or Path attribute.");
            return;
        }

        var triggerVariable = NextVariable();
        _current.Line(
            $"var {triggerVariable} = new {SyntaxHelpers.Fqn("DataTrigger")} {{ Binding = new {SyntaxHelpers.Fqn("Binding")}({SyntaxHelpers.Quote(path!)}) }};");

        var valueAttribute = triggerElement.Attribute("Value");
        if (valueAttribute is not null)
        {
            var valueExpression = IsNullMarkup(valueAttribute.Value)
                ? "null"
                : SyntaxHelpers.Quote(UnescapeMarkup(valueAttribute.Value));
            _current.Line($"{triggerVariable}.Value = {valueExpression};");
        }

        EmitTriggerChildren(triggerElement, ownerType, triggerVariable);
        _current.Line($"{collectionOwnerExpression}.Triggers.Add({triggerVariable});");
    }

    private void EmitMultiTrigger(XElement triggerElement, ITypeSymbol ownerType, string collectionOwnerExpression)
    {
        var triggerVariable = NextVariable();
        _current.Line($"var {triggerVariable} = new {SyntaxHelpers.Fqn("MultiTrigger")}();");

        var sawCondition = false;
        foreach (var child in triggerElement.Elements())
        {
            if (ChildMemberName(child) == "Conditions")
            {
                foreach (var condition in child.Elements())
                {
                    if (condition.Name.LocalName != "Condition")
                    {
                        Report(
                            Diagnostics.UnsupportedConstruct,
                            condition,
                            "The Conditions collection only accepts Condition elements.");
                        continue;
                    }

                    sawCondition = true;
                    EmitCondition(condition, ownerType, triggerVariable);
                }

                continue;
            }

            EmitTriggerMember(child, ownerType, triggerVariable);
        }

        if (!sawCondition)
        {
            Report(Diagnostics.UnsupportedConstruct, triggerElement, "A MultiTrigger needs at least one Condition.");
        }

        _current.Line($"{collectionOwnerExpression}.Triggers.Add({triggerVariable});");
    }

    private bool EmitCondition(XElement condition, ITypeSymbol ownerType, string multiTriggerVariable)
    {
        var propertyPath = condition.Attribute("Property")?.Value;
        if (string.IsNullOrWhiteSpace(propertyPath))
        {
            Report(Diagnostics.UnsupportedConstruct, condition, "A Condition needs a Property attribute.");
            return false;
        }

        if (!TryResolveControlProperty(
            ownerType,
            propertyPath!.Trim(),
            condition,
            out var dependencyProperty,
            out var valueType))
        {
            return false;
        }

        var valueAttribute = condition.Attribute("Value");
        var valueExpression = valueAttribute is null
            ? "null"
            : CreatePropertyValueExpression(valueAttribute, dependencyProperty, valueType, valueAttribute.Value);

        if (valueExpression is null)
        {
            return false;
        }

        _current.Line(
            $"{multiTriggerVariable}.Conditions.Add(new {SyntaxHelpers.Fqn("Condition")}({dependencyProperty}, {valueExpression}));");
        return true;
    }

    private void EmitTriggerChildren(XElement triggerElement, ITypeSymbol ownerType, string triggerVariable)
    {
        foreach (var child in triggerElement.Elements())
        {
            EmitTriggerMember(child, ownerType, triggerVariable);
        }
    }

    private void EmitTriggerMember(XElement child, ITypeSymbol ownerType, string triggerVariable)
    {
        switch (ChildMemberName(child))
        {
            case "Setters":
                EmitTriggerSetters(child, ownerType, triggerVariable);
                break;

            case "EnterAction":
                EmitTriggerAction(child, ownerType, triggerVariable, isEnter: true);
                break;

            case "ExitAction":
                EmitTriggerAction(child, ownerType, triggerVariable, isEnter: false);
                break;

            default:
                Report(
                    Diagnostics.UnsupportedConstruct,
                    child,
                    $"'{child.Name.LocalName}' is not a supported trigger member.");
                break;
        }
    }

    private static bool IsTriggerElement(XElement element)
        => element.Name.LocalName is "Trigger" or "DataTrigger" or "MultiTrigger";

    private static bool IsNullMarkup(string value)
        => value.Trim() is "{x:Null}" or "{Null}";

    private static string UnescapeMarkup(string value)
        => value.StartsWith("{}", StringComparison.Ordinal) ? value.Substring(2) : value;

    private void EmitTriggerSetters(XElement settersElement, ITypeSymbol ownerType, string triggerVariable)
        => EmitSetters(settersElement, ownerType, $"{triggerVariable}.Setters");

    private void EmitSetters(XElement settersElement, ITypeSymbol ownerType, string setterCollectionExpression)
    {
        foreach (var child in settersElement.Elements())
        {
            if (child.Name.LocalName != "Setter")
            {
                Report(Diagnostics.UnsupportedConstruct, child, "The Setters collection only accepts Setter elements.");
                continue;
            }

            EmitSetter(child, ownerType, setterCollectionExpression);
        }
    }

    private void EmitSetter(XElement setterElement, ITypeSymbol ownerType, string setterCollectionExpression)
    {
        var propertyPath = setterElement.Attribute("Property")?.Value;
        if (string.IsNullOrWhiteSpace(propertyPath))
        {
            Report(Diagnostics.UnsupportedConstruct, setterElement, "A Setter needs a Property attribute.");
            return;
        }

        // 模板触发器的 Setter 指向命名部件时，Property 属于部件的类型，不属于模板的 TargetType。
        var propertyOwner = ownerType;
        var targetTypeAttribute = setterElement.Attribute("TargetType");
        if (targetTypeAttribute is not null)
        {
            propertyOwner = ResolveTypeFromMarkup(targetTypeAttribute, targetTypeAttribute.Value);
            if (propertyOwner is null)
            {
                return;
            }
        }

        if (!TryResolveControlProperty(propertyOwner, propertyPath!.Trim(), setterElement, out var dependencyProperty, out var valueType))
        {
            return;
        }

        var valueAttribute = setterElement.Attribute("Value");
        if (valueAttribute is null)
        {
            _current.Line(
                $"{setterCollectionExpression}.Add({WithTargetName($"new {SyntaxHelpers.Fqn("Setter")}({dependencyProperty}, null)", setterElement)});");
            return;
        }

        if (TryParseMarkup(valueAttribute.Value, out var extension, out var arguments, out var positional))
        {
            if (extension is "Binding" or "TemplateBinding")
            {
                var bindingExpression = BuildBindingExpression(
                    valueAttribute,
                    valueType,
                    arguments,
                    positional,
                    templateBinding: extension == "TemplateBinding",
                    allowTwoWay: true);
                if (bindingExpression is null)
                {
                    return;
                }

                _current.Line(
                    $"{setterCollectionExpression}.Add({WithTargetName($"new {SyntaxHelpers.Fqn("Setter")}({dependencyProperty}) {{ Binding = {bindingExpression} }}", setterElement)});");
                return;
            }

            if (extension == "StaticResource")
            {
                var key = arguments.TryGetValue("Key", out var explicitKey) ? explicitKey : positional;
                if (string.IsNullOrEmpty(key))
                {
                    Report(Diagnostics.UnsupportedResource, setterElement, "A StaticResource reference needs a key.");
                    return;
                }

                if (_rootScope is null)
                {
                    Report(Diagnostics.UnsupportedConstruct, setterElement, "A StaticResource reference needs a Control scope.");
                    return;
                }

                _current.Line(
                    $"{setterCollectionExpression}.Add({WithTargetName($"new {SyntaxHelpers.Fqn("Setter")}({dependencyProperty}, " + $"{SyntaxHelpers.Fqn("XamlRuntime")}.GetResource<object>({_rootScope}, {SyntaxHelpers.Quote(key!)}))", setterElement)});");
                return;
            }

            if (extension is "DynamicResource" or "x:DynamicResource")
            {
                var key = arguments.TryGetValue("Key", out var explicitKey) ? explicitKey : positional;
                if (string.IsNullOrEmpty(key))
                {
                    Report(Diagnostics.UnsupportedResource, setterElement, "A DynamicResource reference needs a key.");
                    return;
                }

                if (_rootScope is null)
                {
                    Report(Diagnostics.UnsupportedConstruct, setterElement, "A DynamicResource reference needs a Control scope.");
                    return;
                }

                // Setter 本身是数据对象，拿不到活动控件；把它变成对资源源的绑定，由既有 setter-binding 通道跟随替换。
                var provider = $"{SyntaxHelpers.Fqn("XamlRuntime")}.CreateDynamicResourceProvider({_rootScope}, {SyntaxHelpers.Quote(key!)})";
                var dynamicSetter =
                    $"new {SyntaxHelpers.Fqn("Setter")}({dependencyProperty}, new {SyntaxHelpers.Fqn("Binding")}({SyntaxHelpers.Quote("Value")}) "
                    + $"{{ Source = {provider} }})";
                _current.Line($"{setterCollectionExpression}.Add({WithTargetName(dynamicSetter, setterElement)});");
                return;
            }

            if (extension is "x:Null" or "Null")
            {
                _current.Line(
                    $"{setterCollectionExpression}.Add({WithTargetName($"new {SyntaxHelpers.Fqn("Setter")}({dependencyProperty}, null)", setterElement)});");
                return;
            }

            if (extension == "x:Static")
            {
                var staticExpression = CreateStaticExpression(
                    valueAttribute,
                    positional ?? (arguments.TryGetValue("Member", out var member) ? member : null));
                if (staticExpression is null)
                {
                    return;
                }

                _current.Line(
                    $"{setterCollectionExpression}.Add({WithTargetName($"new {SyntaxHelpers.Fqn("Setter")}({dependencyProperty}, {staticExpression})", setterElement)});");
                return;
            }

            Report(
                Diagnostics.UnsupportedConstruct,
                valueAttribute,
                $"Markup extension '{extension}' is not supported in a Setter value.");
            return;
        }

        var valueExpression = CreatePropertyValueExpression(valueAttribute, dependencyProperty, valueType, valueAttribute.Value);
        if (valueExpression is null)
        {
            return;
        }

        _current.Line(
            $"{setterCollectionExpression}.Add({WithTargetName($"new {SyntaxHelpers.Fqn("Setter")}({dependencyProperty}, {valueExpression})", setterElement)});");
    }

    private static string WithTargetName(string setterExpression, XElement setterElement)
    {
        var targetName = setterElement.Attribute("TargetName")?.Value;
        if (string.IsNullOrWhiteSpace(targetName))
        {
            return setterExpression;
        }

        var assignment = $"TargetName = {SyntaxHelpers.Quote(targetName!)}";
        return setterExpression.EndsWith(" }", StringComparison.Ordinal)
            ? setterExpression.Substring(0, setterExpression.Length - 2) + $", {assignment} }}"
            : setterExpression + $" {{ {assignment} }}";
    }

    private void EmitTriggerAction(XElement actionElement, ITypeSymbol ownerType, string triggerVariable, bool isEnter)
    {
        var actions = actionElement.Elements().ToArray();
        if (actions.Length == 0)
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                actionElement,
                "An EnterAction or ExitAction needs at least one action element.");
            return;
        }

        var variables = new List<string>();
        foreach (var action in actions)
        {
            var variable = EmitActionElement(action, ownerType);
            if (variable is not null)
            {
                variables.Add(variable);
            }
        }

        if (variables.Count == 0)
        {
            return;
        }

        var member = isEnter ? "EnterAction" : "ExitAction";
        if (variables.Count == 1)
        {
            _current.Line($"{triggerVariable}.{member} = {variables[0]};");
            return;
        }

        var groupVariable = NextVariable();
        _current.Line($"var {groupVariable} = new {SyntaxHelpers.Fqn("TriggerActionGroup")}();");
        foreach (var variable in variables)
        {
            _current.Line($"{groupVariable}.Actions.Add({variable});");
        }

        _current.Line($"{triggerVariable}.{member} = {groupVariable};");
    }

    private string? EmitActionElement(XElement action, ITypeSymbol ownerType)
    {
        if (action.Name.LocalName == "TransitionAction")
        {
            return EmitTransitionAction(action, ownerType);
        }

        if (action.Name.LocalName == "KeyFrameAnimation")
        {
            return EmitKeyFrameAnimation(action, ownerType);
        }

        return EmitCustomTriggerAction(action);
    }

    private string? EmitTransitionAction(XElement action, ITypeSymbol ownerType)
    {
        var propertyPath = action.Attribute("Property")?.Value;
        if (string.IsNullOrWhiteSpace(propertyPath))
        {
            Report(Diagnostics.UnsupportedConstruct, action, "A TransitionAction needs a Property attribute.");
            return null;
        }

        if (!TryResolveControlProperty(ownerType, propertyPath!.Trim(), action, out var dependencyProperty, out var valueType))
        {
            return null;
        }

        var toAttribute = action.Attribute("To");
        var toExpression = toAttribute is null
            ? "null"
            : CreatePropertyValueExpression(toAttribute, dependencyProperty, valueType, toAttribute.Value);

        if (toExpression is null)
        {
            return null;
        }

        var effectParts = new List<string>();
        var durationAttribute = action.Attribute("Duration");
        if (durationAttribute is not null)
        {
            if (!TimeSpan.TryParse(durationAttribute.Value, CultureInfo.InvariantCulture, out _))
            {
                Report(
                    Diagnostics.UnsupportedValue,
                    durationAttribute,
                    durationAttribute.Value,
                    "System.TimeSpan");
                return null;
            }

            var duration = $"global::System.TimeSpan.Parse({SyntaxHelpers.Quote(durationAttribute.Value)}, global::System.Globalization.CultureInfo.InvariantCulture)";
            effectParts.Add($"Duration = {duration}");
        }

        var easeAttribute = action.Attribute("Ease");
        if (easeAttribute is not null)
        {
            if (TryMapEase(easeAttribute.Value, out var ease))
            {
                effectParts.Add($"Ease = {ease}");
            }
            else
            {
                Report(Diagnostics.UnsupportedValue, easeAttribute, easeAttribute.Value, "Ease");
            }
        }

        var loopAttribute = action.Attribute("LoopTime");
        if (loopAttribute is not null)
        {
            if (!int.TryParse(loopAttribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var loop))
            {
                Report(Diagnostics.UnsupportedValue, loopAttribute, loopAttribute.Value, "System.Int32");
                return null;
            }

            effectParts.Add($"LoopTime = {loop}");
        }

        var reverseAttribute = action.Attribute("IsAutoReverse");
        if (reverseAttribute is not null)
        {
            if (!bool.TryParse(reverseAttribute.Value, out var reverse))
            {
                Report(Diagnostics.UnsupportedValue, reverseAttribute, reverseAttribute.Value, "System.Boolean");
                return null;
            }

            effectParts.Add($"IsAutoReverse = {(reverse ? "true" : "false")}");
        }

        var effect = effectParts.Count > 0
            ? $" Effect = new global::VeloxDev.TransitionSystem.TransitionEffect {{ {string.Join(", ", effectParts)} }},"
            : string.Empty;

        var actionVariable = NextVariable();
        _current.Line(
            $"var {actionVariable} = new {SyntaxHelpers.Fqn("TransitionAction")} "
            + $"{{ Property = {dependencyProperty}, To = {toExpression},{effect} }};");
        return actionVariable;
    }

    private string? EmitKeyFrameAnimation(XElement action, ITypeSymbol ownerType)
    {
        var propertyPath = action.Attribute("Property")?.Value;
        if (string.IsNullOrWhiteSpace(propertyPath))
        {
            Report(Diagnostics.UnsupportedConstruct, action, "A KeyFrameAnimation needs a Property attribute.");
            return null;
        }

        if (!TryResolveControlProperty(ownerType, propertyPath!.Trim(), action, out var dependencyProperty, out var valueType))
        {
            return null;
        }

        var effectParts = new List<string>();
        var durationAttribute = action.Attribute("Duration");
        if (durationAttribute is not null)
        {
            if (!TimeSpan.TryParse(durationAttribute.Value, CultureInfo.InvariantCulture, out _))
            {
                Report(Diagnostics.UnsupportedValue, durationAttribute, durationAttribute.Value, "System.TimeSpan");
                return null;
            }

            effectParts.Add(
                $"Duration = global::System.TimeSpan.Parse({SyntaxHelpers.Quote(durationAttribute.Value)}, global::System.Globalization.CultureInfo.InvariantCulture)");
        }

        var easeAttribute = action.Attribute("Ease");
        if (easeAttribute is not null && TryMapEase(easeAttribute.Value, out var ease))
        {
            effectParts.Add($"Ease = {ease}");
        }
        else if (easeAttribute is not null)
        {
            Report(Diagnostics.UnsupportedValue, easeAttribute, easeAttribute.Value, "Ease");
        }

        var loopAttribute = action.Attribute("LoopTime");
        if (loopAttribute is not null)
        {
            if (!int.TryParse(loopAttribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var loop))
            {
                Report(Diagnostics.UnsupportedValue, loopAttribute, loopAttribute.Value, "System.Int32");
                return null;
            }

            effectParts.Add($"LoopTime = {loop}");
        }

        var reverseAttribute = action.Attribute("IsAutoReverse");
        if (reverseAttribute is not null)
        {
            if (!bool.TryParse(reverseAttribute.Value, out var reverse))
            {
                Report(Diagnostics.UnsupportedValue, reverseAttribute, reverseAttribute.Value, "System.Boolean");
                return null;
            }

            effectParts.Add($"IsAutoReverse = {(reverse ? "true" : "false")}");
        }

        var frames = action.Elements().Where(child => child.Name.LocalName == "KeyFrame").ToArray();
        if (frames.Length == 0)
        {
            Report(Diagnostics.UnsupportedConstruct, action, "A KeyFrameAnimation needs at least one KeyFrame element.");
            return null;
        }

        var actionVariable = NextVariable();
        var initializer = $"Property = {dependencyProperty}";
        if (effectParts.Count > 0)
        {
            initializer += ", " + string.Join(", ", effectParts);
        }

        _current.Line(
            $"var {actionVariable} = new {SyntaxHelpers.Fqn("KeyFrameAnimation")} {{ {initializer} }};");

        foreach (var frame in action.Elements())
        {
            if (frame.Name.LocalName != "KeyFrame")
            {
                Report(Diagnostics.UnsupportedConstruct, frame, "A KeyFrameAnimation accepts only KeyFrame children.");
                continue;
            }

            var offsetAttribute = frame.Attribute("Offset");
            if (offsetAttribute is null)
            {
                Report(Diagnostics.UnsupportedConstruct, frame, "A KeyFrame needs an Offset attribute.");
                continue;
            }

            if (!double.TryParse(offsetAttribute.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var offset))
            {
                Report(Diagnostics.UnsupportedValue, frame, offsetAttribute.Value, "System.Double");
                continue;
            }

            var valueAttribute = frame.Attribute("Value");
            if (valueAttribute is null)
            {
                Report(Diagnostics.UnsupportedConstruct, frame, "A KeyFrame needs a Value attribute.");
                continue;
            }

            var valueExpression = CreatePropertyValueExpression(frame, dependencyProperty, valueType, valueAttribute.Value);
            if (valueExpression is null)
            {
                continue;
            }

            _current.Line(
                $"{actionVariable}.KeyFrames.Add(new {SyntaxHelpers.Fqn("KeyFrame")} "
                + $"{{ Offset = {offset.ToString("R", CultureInfo.InvariantCulture)}D, Value = {valueExpression} }});");
        }

        return actionVariable;
    }


    private string? EmitCustomTriggerAction(XElement action)
    {
        var type = ResolveElementType(action);
        if (type is null)
        {
            if (!IsReported(action))
            {
                Report(Diagnostics.UnknownType, action, action.Name.LocalName);
            }

            return null;
        }

        if (!SyntaxHelpers.DerivesFrom(type, "VeloxDev.WinForms.Xaml.TriggerAction"))
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                action,
                $"'{type.Name}' does not derive from VeloxDev.WinForms.Xaml.TriggerAction.");
            return null;
        }

        if (type is not INamedTypeSymbol namedType || namedType.IsGenericType)
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                action,
                $"'{type.Name}' is not a usable trigger action type.");
            return null;
        }

        if (type.IsAbstract || !HasPublicParameterlessConstructor(namedType))
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                action,
                $"'{type.Name}' needs a public parameterless constructor.");
            return null;
        }

        if (action.HasElements)
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                action,
                "Nested content is not supported in a trigger action.");
            return null;
        }

        var actionVariable = NextVariable();
        _current.Line($"var {actionVariable} = new {SyntaxHelpers.TypeName(type)}();");

        foreach (var attribute in action.Attributes())
        {
            if (attribute.IsNamespaceDeclaration || attribute.Name.NamespaceName == SyntaxHelpers.XamlNamespace)
            {
                continue;
            }

            var name = attribute.Name.LocalName;
            if (name.IndexOf('.') >= 0)
            {
                Report(
                    Diagnostics.UnsupportedConstruct,
                    attribute,
                    "Attached properties are not supported on a trigger action.");
                continue;
            }

            var property = FindProperty(type, name);
            if (property is null)
            {
                Report(Diagnostics.UnknownMember, attribute, type.Name, name);
                continue;
            }

            if (property.IsStatic
                || property.SetMethod is null
                || property.SetMethod.DeclaredAccessibility != Accessibility.Public)
            {
                Report(
                    Diagnostics.UnsupportedConstruct,
                    attribute,
                    $"'{name}' is not a public writable instance property.");
                continue;
            }

            EmitCustomActionProperty(actionVariable, property, attribute);
        }

        return actionVariable;
    }

    private void EmitCustomActionProperty(string actionVariable, IPropertySymbol property, XAttribute attribute)
    {
        var raw = attribute.Value;
        if (TryParseMarkup(raw, out var extension, out var arguments, out var positional))
        {
            if (extension is "x:Null" or "Null")
            {
                var isNullable = property.Type is INamedTypeSymbol
                {
                    OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
                };

                if (property.Type.IsValueType && !isNullable)
                {
                    Report(Diagnostics.UnsupportedValue, attribute, raw, property.Type.ToDisplayString());
                    return;
                }

                _current.Line($"{actionVariable}.{property.Name} = null;");
                return;
            }

            if (extension == "StaticResource")
            {
                var key = arguments.TryGetValue("Key", out var explicitKey) ? explicitKey : positional;
                if (string.IsNullOrEmpty(key))
                {
                    Report(Diagnostics.UnsupportedResource, attribute, "A StaticResource reference needs a key.");
                    return;
                }

                if (_rootScope is null)
                {
                    Report(Diagnostics.UnsupportedConstruct, attribute, "A StaticResource reference needs a Control scope.");
                    return;
                }

                _current.Line(
                    $"{actionVariable}.{property.Name} = {SyntaxHelpers.Fqn("XamlRuntime")}.GetResource<{SyntaxHelpers.TypeName(property.Type)}>({_rootScope}, {SyntaxHelpers.Quote(key!)});");
                return;
            }

            if (extension is "DynamicResource" or "x:DynamicResource")
            {
                Report(
                    Diagnostics.UnsupportedConstruct,
                    attribute,
                    $"A DynamicResource reference cannot be applied to the trigger action property "
                    + $"'{property.ContainingType.Name}.{property.Name}'; it needs a dependency property.");
                return;
            }

            Report(
                Diagnostics.UnsupportedConstruct,
                attribute,
                $"Markup extension '{extension}' is not supported on a trigger action property.");
            return;
        }

        if (!LiteralFactory.TryCreate(property.Type, raw, out var valueExpression, out _))
        {
            Report(Diagnostics.UnsupportedValue, attribute, raw, property.Type.ToDisplayString());
            valueExpression =
                $"({SyntaxHelpers.TypeName(property.Type)}){SyntaxHelpers.Fqn("XamlRuntime")}.ConvertValue({SyntaxHelpers.Quote(raw)}, typeof({RuntimeTypeName(property.Type)}))!";
        }

        _current.Line($"{actionVariable}.{property.Name} = {valueExpression};");
    }

    private static bool HasPublicParameterlessConstructor(INamedTypeSymbol type)
        => type.InstanceConstructors.Any(
            constructor => constructor.Parameters.Length == 0
                && constructor.DeclaredAccessibility == Accessibility.Public);

    private static bool TryMapEase(string value, out string expression)
    {
        var normalized = value
            .Replace(".", string.Empty)
            .Replace("-", string.Empty)
            .Replace(" ", string.Empty)
            .ToLowerInvariant();

        if (normalized is "linear" or "default" or "none")
        {
            expression = "global::VeloxDev.TransitionSystem.Eases.Default";
            return true;
        }

        var families = new (string Key, string Name)[]
        {
            ("sine", "Sine"),
            ("quad", "Quad"),
            ("cubic", "Cubic"),
            ("quart", "Quart"),
            ("quint", "Quint"),
            ("expo", "Expo"),
            ("circ", "Circ"),
            ("back", "Back"),
            ("elastic", "Elastic"),
            ("bounce", "Bounce"),
        };

        foreach (var family in families)
        {
            if (normalized == family.Key + "in")
            {
                expression = $"global::VeloxDev.TransitionSystem.Eases.{family.Name}.In";
                return true;
            }

            if (normalized == family.Key + "out")
            {
                expression = $"global::VeloxDev.TransitionSystem.Eases.{family.Name}.Out";
                return true;
            }

            if (normalized == family.Key + "inout")
            {
                expression = $"global::VeloxDev.TransitionSystem.Eases.{family.Name}.InOut";
                return true;
            }
        }

        expression = string.Empty;
        return false;
    }

    private bool TryResolveControlProperty(
        ITypeSymbol ownerType,
        string propertyPath,
        XObject source,
        out string dependencyProperty,
        out ITypeSymbol? valueType)
    {
        dependencyProperty = string.Empty;
        valueType = null;

        var dot = propertyPath.LastIndexOf('.');
        if (dot < 0)
        {
            var property = FindProperty(ownerType, propertyPath);
            if (property is not null && TryGetDependencyPropertyReference(property, out dependencyProperty))
            {
                valueType = property.Type;
                return true;
            }

            var field = FindStaticField(
                ownerType,
                propertyPath + "Property",
                SyntaxHelpers.DependencyPropertyMetadataName);

            if (field is not null)
            {
                dependencyProperty = $"{SyntaxHelpers.TypeName(ownerType)}.{field.Name}";
                return true;
            }

            Report(Diagnostics.UnknownMember, source, ownerType.Name, propertyPath);
            return false;
        }

        var ownerName = propertyPath.Substring(0, dot);
        var prefix = ownerName.LastIndexOf(':');
        if (prefix >= 0)
        {
            ownerName = ownerName.Substring(prefix + 1);
        }

        var propertyName = propertyPath.Substring(dot + 1);
        if (ownerName.Length == 0 || propertyName.Length == 0)
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                source,
                $"'{propertyPath}' is not a valid property path; use 'Property' or 'Owner.Property'.");
            return false;
        }

        var owner = ResolveOwnerType(ownerType, ownerName, source);
        if (owner is null)
        {
            Report(Diagnostics.UnknownType, source, ownerName);
            return false;
        }

        if (!TryGetAttachedPropertyReference(owner, propertyName, out dependencyProperty, out valueType))
        {
            Report(Diagnostics.UnknownAttachedProperty, source, owner.Name, propertyName);
            return false;
        }

        return true;
    }

    private string? CreatePropertyValueExpression(
        XObject source,
        string dependencyProperty,
        ITypeSymbol? valueType,
        string? rawValue)
    {
        if (rawValue is null)
        {
            return "null";
        }

        if (rawValue.StartsWith("{}", StringComparison.Ordinal))
        {
            rawValue = rawValue.Substring(2);
        }

        if (TryParseMarkup(rawValue, out var dynamicExtension, out _, out _)
            && dynamicExtension is "DynamicResource" or "x:DynamicResource")
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                source,
                "A DynamicResource reference is not supported in this position; it needs a dependency-property target.");
            return null;
        }

        if (valueType is not null && LiteralFactory.TryCreate(valueType, rawValue, out var typed, out _))
        {
            return typed;
        }

        if (valueType is not null && !IsRuntimeConvertedType(valueType))
        {
            Report(Diagnostics.UnsupportedValue, source, rawValue, valueType.ToDisplayString());
        }

        return $"{SyntaxHelpers.Fqn("XamlRuntime")}.ConvertValue({SyntaxHelpers.Quote(rawValue)}, {dependencyProperty}.PropertyType)";
    }

    private ITypeSymbol? ResolveOwnerType(ITypeSymbol controlType, string ownerName, XObject source)
    {
        var controlNamespace = controlType.ContainingNamespace;
        if (controlNamespace is { IsGlobalNamespace: false })
        {
            var candidate = _compilation.GetTypeByMetadataName(controlNamespace.ToDisplayString() + "." + ownerName);
            if (candidate is not null)
            {
                return candidate;
            }
        }

        var qualified = _compilation.GetTypeByMetadataName(ownerName);
        if (qualified is not null)
        {
            return qualified;
        }

        var sourceUri = source switch
        {
            XAttribute attribute => attribute.Name.NamespaceName,
            XElement element => element.Name.NamespaceName,
            _ => string.Empty,
        };

        var sourceNamespace = GetClrNamespace(sourceUri);
        if (sourceNamespace is null && IsVNamespace(source, sourceUri))
        {
            sourceNamespace = SyntaxHelpers.RuntimeNamespace;
        }

        if (sourceNamespace is null)
        {
            // 样式 setter 的 Property="Grid.Row" 没有标记前缀；目标类型的命名空间也不一定和 owner 相同
            // （形状在 .Controls.Shapes 里），所以最后按根类型对应的命名空间组找一次。
            var adapterNamespaces = IsVisualElement(_owner)
                ? new[]
                {
                    SyntaxHelpers.VisualControlsNamespace,
                    SyntaxHelpers.VisualShapesNamespace,
                    SyntaxHelpers.VisualsNamespace,
                }
                : new[]
                {
                    SyntaxHelpers.ControlsNamespace,
                    SyntaxHelpers.ShapesNamespace,
                    SyntaxHelpers.RuntimeNamespace,
                };
            foreach (var adapterNamespace in adapterNamespaces)
            {
                var candidate = _compilation.GetTypeByMetadataName(adapterNamespace + "." + ownerName);
                if (candidate is not null)
                {
                    return candidate;
                }
            }

            return null;
        }

        if (string.Equals(sourceNamespace, SyntaxHelpers.RuntimeNamespace, StringComparison.Ordinal))
        {
            return ResolveVisualAdapterType(ownerName, source);
        }

        return _compilation.GetTypeByMetadataName(sourceNamespace + "." + ownerName);
    }

    private static string ChildMemberName(XElement element)
    {
        var name = element.Name.LocalName;
        var dot = name.LastIndexOf('.');
        return dot >= 0 ? name.Substring(dot + 1) : name;
    }

    private static IFieldSymbol? FindStaticField(ITypeSymbol type, string name, string fieldTypeMetadataName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var field = current.GetMembers(name)
                .OfType<IFieldSymbol>()
                .FirstOrDefault(candidate => candidate.IsStatic && candidate.Type.ToDisplayString() == fieldTypeMetadataName);

            if (field is not null)
            {
                return field;
            }
        }

        return null;
    }

    private static IMethodSymbol? FindStaticMethod(ITypeSymbol type, string name, int parameterCount)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var method = current.GetMembers(name)
                .OfType<IMethodSymbol>()
                .FirstOrDefault(candidate => candidate.IsStatic && candidate.Parameters.Length == parameterCount);

            if (method is not null)
            {
                return method;
            }
        }

        return null;
    }

    private static bool TryGetAttachedAttributeName(IMethodSymbol method, out string name)
    {
        foreach (var attribute in method.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != "VeloxDev.WinForms.Xaml.XamlAttachedPropertyAttribute")
            {
                continue;
            }

            SyntaxHelpers.TryGetAttributeName(
                attribute,
                method.Name.Length > 3 ? method.Name.Substring(3) : method.Name,
                out name);
            return true;
        }

        name = string.Empty;
        return false;
    }

    private static bool TryGetAttachedPropertyReference(
        ITypeSymbol ownerType,
        string propertyName,
        out string dependencyProperty,
        out ITypeSymbol? valueType)
    {
        valueType = null;

        var getter = FindStaticMethod(ownerType, "Get" + propertyName, parameterCount: 1);
        var setter = FindStaticMethod(ownerType, "Set" + propertyName, parameterCount: 2);

        if (getter is null && setter is null)
        {
            // The accessors need not be named Get{Property}/Set{Property}: [XamlAttachedProperty("Column")]
            // may sit on GetGridColumn/SetGridColumn.
            foreach (var method in ownerType.GetMembers().OfType<IMethodSymbol>())
            {
                if (!method.IsStatic
                    || !TryGetAttachedAttributeName(method, out var declaredName)
                    || declaredName != propertyName)
                {
                    continue;
                }

                if (!method.ReturnsVoid && method.Parameters.Length == 1 && getter is null)
                {
                    getter = method;
                }
                else if (method.ReturnsVoid && method.Parameters.Length == 2 && setter is null)
                {
                    setter = method;
                }
            }
        }

        if (getter is not null)
        {
            valueType = getter.ReturnType;
        }
        else if (setter is not null)
        {
            valueType = setter.Parameters[1].Type;
        }

        var field = FindStaticField(ownerType, propertyName + "Property", SyntaxHelpers.DependencyPropertyMetadataName);
        if (field is not null)
        {
            dependencyProperty = $"{SyntaxHelpers.TypeName(ownerType)}.{field.Name}";
            return true;
        }

        // [XamlAttachedProperty] accessors are partial; their generated {Property}Property field is not part of the
        // compilation this generator sees, so resolve it from the runtime registry instead.
        if (getter is not null || setter is not null)
        {
            dependencyProperty =
                $"{SyntaxHelpers.Fqn("XamlRuntime")}.RequireAttached(typeof({SyntaxHelpers.TypeName(ownerType)}), {SyntaxHelpers.Quote(propertyName)})";
            return true;
        }

        dependencyProperty = string.Empty;
        return false;
    }

    private void EmitEvent(XObject source, string targetExpression, IEventSymbol eventSymbol, string handler)
    {
        var methods = FindHandlerMethods(_owner, handler);
        if (methods.Count == 0)
        {
            Report(Diagnostics.MissingEventHandler, source, handler, _owner.Name);
            return;
        }

        if (methods.Count > 1)
        {
            Report(Diagnostics.AmbiguousEventHandler, source, handler, _owner.Name);
            return;
        }

        var invoke = eventSymbol.Type.GetMembers("Invoke").OfType<IMethodSymbol>().FirstOrDefault();
        if (invoke is not null
            && !ValidateEventHandlerSignature(source, methods[0], invoke, eventSymbol.Name, handler))
        {
            return;
        }

        var line = $"{targetExpression}.{eventSymbol.Name} += this.{handler};";

        // 初始化期间的事件可能在后面的 x:Name 字段赋值之前触发；根方法里的订阅统一放到
        // 所有子元素建好之后。模板工厂里的局部变量不跨方法，保持原位。
        if (_templateDepth == 0)
        {
            _deferredEvents.Add(line);
        }
        else
        {
            _current.Line(line);
        }
    }

    private bool ValidateEventHandlerSignature(
        XObject source,
        IMethodSymbol method,
        IMethodSymbol invoke,
        string eventName,
        string handlerName)
    {
        if (method.ReturnsVoid && method.Parameters.Length == invoke.Parameters.Length)
        {
            var valid = true;
            for (var index = 0; index < invoke.Parameters.Length; index++)
            {
                var handlerParameter = method.Parameters[index];
                var eventParameter = invoke.Parameters[index];
                if (handlerParameter.RefKind != RefKind.None
                    || eventParameter.RefKind != RefKind.None
                    || !AcceptsType(handlerParameter.Type, eventParameter.Type))
                {
                    valid = false;
                    break;
                }
            }

            if (valid)
            {
                return true;
            }
        }

        Report(Diagnostics.InvalidEventHandlerSignature, source, handlerName, eventName);
        return false;
    }

    private static bool AcceptsType(ITypeSymbol handlerType, ITypeSymbol valueType)
    {
        if (SymbolEqualityComparer.Default.Equals(handlerType, valueType))
        {
            return true;
        }

        if (handlerType.SpecialType == SpecialType.System_Object)
        {
            return true;
        }

        for (var current = valueType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(handlerType, current))
            {
                return true;
            }
        }

        return false;
    }

    private static List<IMethodSymbol> FindHandlerMethods(ITypeSymbol type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var methods = current.GetMembers(name).OfType<IMethodSymbol>().ToList();
            if (methods.Count > 0)
            {
                return methods;
            }
        }

        return [];
    }

    private void EmitPropertyElement(
        XElement propertyElement,
        ITypeSymbol ownerType,
        string ownerExpression,
        string? hostExpression)
    {
        var fullName = propertyElement.Name.LocalName;
        var memberName = fullName.Substring(fullName.LastIndexOf('.') + 1);
        var isVisual = IsVisualElement(ownerType);

        if (memberName == "Resources")
        {
            EmitResources(propertyElement, ownerExpression, ownerType);
            return;
        }

        if (isVisual && memberName == "Template")
        {
            var templateElement = propertyElement.Elements()
                .FirstOrDefault(child => ChildMemberName(child) is "ControlTemplate" or "VisualControlTemplate");
            if (templateElement is not null)
            {
                var template = EmitVisualControlTemplate(templateElement, key: null);
                if (template is not null)
                {
                    _current.Line($"{ownerExpression}.Template = {template};");
                }

                return;
            }
        }

        // DataTemplate 属性元素没有公共无参构造，不能走通用 EmitXamlObject；ItemTemplate 与 ContentTemplate
        // 都把它编成一个返回 VisualElement 的私有工厂，与模板资源同形。只有属性确实是 DataTemplate 时才接管。
        var templateProperty = isVisual && memberName is ("ItemTemplate" or "ContentTemplate")
            ? FindProperty(ownerType, memberName)
            : null;
        if (templateProperty?.Type.Name == "DataTemplate")
        {
            var templateElement = propertyElement.Elements()
                .FirstOrDefault(child => ChildMemberName(child) == "DataTemplate");
            if (templateElement is not null)
            {
                EmitVisualTemplateProperty(templateElement, ownerExpression, memberName);
                return;
            }
        }

        if (memberName == "Controls")
        {
            if (isVisual)
            {
                Report(
                    Diagnostics.UnsupportedConstruct,
                    propertyElement,
                    "A visual element adds children with 'Add'; a Controls property element is not supported.");
                return;
            }

            ProcessChildren(propertyElement, ownerType, ownerExpression, hostExpression);
            return;
        }

        if (memberName == "Triggers")
        {
            EmitTriggers(propertyElement, ownerType, ownerExpression);
            return;
        }

        var property = FindProperty(ownerType, memberName);
        if (memberName == "ContextMenu"
            && !IsXamlControl(ownerType)
            && SyntaxHelpers.DerivesFrom(ownerType, SyntaxHelpers.ControlMetadataName))
        {
            property = FindProperty(ownerType, "ContextMenuStrip") ?? property;
        }

        if (property is null)
        {
            Report(Diagnostics.UnknownMember, propertyElement, ownerType.Name, memberName);
            return;
        }

        if (property.SetMethod is null && !IsCollection(property.Type))
        {
            Report(Diagnostics.UnsupportedConstruct, propertyElement, $"'{property.Name}' is read-only in XAML.");
            return;
        }

        if (IsCollection(property.Type) && propertyElement.Elements().Any())
        {
            foreach (var child in propertyElement.Elements())
            {
                if (IsPropertyElement(child))
                {
                    continue;
                }

                var childType = ResolveElementType(child);
                if (childType is null)
                {
                    if (!IsReported(child))
                    {
                        Report(Diagnostics.UnknownType, child, child.Name.LocalName);
                    }

                    continue;
                }

                var childExpression = EmitXamlObject(child, childType, hostExpression);
                if (childExpression is not null)
                {
                    _current.Line($"{ownerExpression}.{property.Name}.Add({childExpression});");
                }
            }

            return;
        }

        var content = propertyElement.Elements().FirstOrDefault(child => !IsPropertyElement(child));
        if (content is not null)
        {
            var contentType = ResolveElementType(content);
            if (contentType is null)
            {
                if (!IsReported(content))
                {
                    Report(Diagnostics.UnknownType, content, content.Name.LocalName);
                }

                return;
            }

            var contentExpression = EmitXamlObject(content, contentType, hostExpression);
            if (contentExpression is not null)
            {
                _current.Line($"{ownerExpression}.{property.Name} = {contentExpression};");
            }

            return;
        }

        var literal = CreateLiteralExpression(property.Type, propertyElement.Value, propertyElement);
        if (literal is not null)
        {
            _current.Line($"{ownerExpression}.{property.Name} = {literal};");
        }
    }

    private void EmitResources(XElement propertyElement, string ownerExpression, ITypeSymbol ownerType)
    {
        if (IsVisualElement(ownerType))
        {
            EmitVisualResources(propertyElement, ownerExpression, ownerType);
            return;
        }

        if (!IsXamlControl(ownerType))
        {
            Report(
                Diagnostics.UnsupportedResource,
                propertyElement,
                $"'{ownerType.Name}' cannot own resources because it is not a VeloxDev.WinForms.Xaml.Controls.Control.");
            return;
        }

        var deferredStyleLinks = new List<string>();
        foreach (var resource in FlattenResources(propertyElement))
        {
            var key = resource.Attribute(XName.Get("Key", SyntaxHelpers.XamlNamespace))?.Value;

            if (resource.Name.LocalName == "Style")
            {
                EmitStyleResource(resource, key, ownerExpression, deferredStyleLinks);
                continue;
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                Report(Diagnostics.UnsupportedResource, resource, "Every resource needs an x:Key.");
                continue;
            }

            if (resource.Name.LocalName == "DataTemplate")
            {
                if (EmitDataTemplate(resource, key!))
                {
                    var methodName = "CreateTemplate_" + SyntaxHelpers.SanitizeIdentifier(key!);
                    _current.Line($"{ownerExpression}.Resources[{SyntaxHelpers.Quote(key!)}] = new {SyntaxHelpers.Fqn("DataTemplate")}(this.{methodName});");
                }

                continue;
            }

            if (resource.Name.LocalName == "ControlTemplate")
            {
                EmitControlTemplate(resource, key!, ownerExpression);
                continue;
            }

            if (!TryCreateResourceExpression(resource, out var expression))
            {
                continue;
            }

            _current.Line($"{ownerExpression}.Resources[{SyntaxHelpers.Quote(key!)}] = {expression};");
        }

        // 同一资源字典里的样式可能先引用后声明；等全部资源对象建好再连 BasedOn / Template。
        foreach (var line in deferredStyleLinks)
        {
            _current.Line(line);
        }
    }

    // 视觉资源字典与 HWND 版同形，但资源类型是 VisualStyle / VisualControlTemplate；键仍走 x:Key。
    private void EmitVisualResources(XElement propertyElement, string ownerExpression, ITypeSymbol ownerType)
    {
        var deferred = new List<string>();
        foreach (var resource in FlattenResources(propertyElement))
        {
            var key = resource.Attribute(XName.Get("Key", SyntaxHelpers.XamlNamespace))?.Value;
            var localName = resource.Name.LocalName;

            if (localName is "Style" or "VisualStyle")
            {
                EmitVisualStyleResource(resource, key, ownerExpression, deferred);
                continue;
            }

            if (localName is "ControlTemplate" or "VisualControlTemplate")
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    Report(
                        Diagnostics.UnsupportedResource,
                        resource,
                        "Every visual ControlTemplate resource needs an x:Key.");
                    continue;
                }

                var template = EmitVisualControlTemplate(resource, key);
                if (template is not null)
                {
                    _current.Line($"{ownerExpression}.Resources[{SyntaxHelpers.Quote(key!)}] = {template};");
                }

                continue;
            }

            if (localName == "DataTemplate")
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    Report(Diagnostics.UnsupportedResource, resource, "Every visual DataTemplate resource needs an x:Key.");
                    continue;
                }

                var template = EmitVisualDataTemplateResource(resource, key!);
                if (template is not null)
                {
                    _current.Line($"{ownerExpression}.Resources[{SyntaxHelpers.Quote(key!)}] = {template};");
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                Report(Diagnostics.UnsupportedResource, resource, "Every visual resource needs an x:Key.");
                continue;
            }

            if (!TryCreateResourceExpression(resource, out var expression))
            {
                continue;
            }

            _current.Line($"{ownerExpression}.Resources[{SyntaxHelpers.Quote(key!)}] = {expression};");
        }

        foreach (var line in deferred)
        {
            _current.Line(line);
        }
    }

    private void EmitVisualStyleResource(
        XElement styleElement,
        string? key,
        string ownerExpression,
        List<string> deferred)
    {
        ITypeSymbol? targetType = null;
        var targetTypeAttribute = styleElement.Attribute("TargetType");
        if (targetTypeAttribute is not null)
        {
            targetType = ResolveVisualTypeReference(targetTypeAttribute.Value, styleElement);
            if (targetType is null)
            {
                return;
            }
        }
        else if (string.IsNullOrWhiteSpace(key))
        {
            Report(
                Diagnostics.InvalidStyleTargetType,
                styleElement,
                "An implicit visual style needs a TargetType.");
            return;
        }

        var styleVariable = NextVariable();
        _current.Line($"var {styleVariable} = new {VisualFqn("VisualStyle")}();");
        if (targetType is not null)
        {
            _current.Line($"{styleVariable}.TargetType = typeof({SyntaxHelpers.TypeName(targetType)});");
        }

        var basedOn = styleElement.Attribute("BasedOn");
        if (basedOn is not null)
        {
            if (TryReadStaticResourceKey(basedOn.Value, out var baseKey))
            {
                deferred.Add(
                    $"{styleVariable}.BasedOn = {VisualFqn("VisualResourceLookup")}"
                    + $".Get<{VisualFqn("VisualStyle")}>({ownerExpression}, {SyntaxHelpers.Quote(baseKey)});");
            }
            else
            {
                Report(Diagnostics.UnsupportedConstruct, basedOn, "BasedOn needs a {StaticResource Key}.");
            }
        }

        foreach (var child in styleElement.Elements())
        {
            switch (ChildMemberName(child))
            {
                case "Setters":
                    EmitVisualSetters(child, targetType, styleVariable);
                    break;
                case "Triggers":
                    EmitVisualTriggers(child, targetType, styleVariable);
                    break;
                case "Template":
                    EmitVisualStyleTemplate(child, targetType, styleVariable);
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            _current.Line($"{ownerExpression}.Styles.Add({styleVariable});");
        }
        else
        {
            _current.Line($"{ownerExpression}.Resources[{SyntaxHelpers.Quote(key!)}] = {styleVariable};");
        }
    }

    private ITypeSymbol? ResolveVisualTypeReference(string rawValue, XElement context)
    {
        var text = rawValue.Trim();
        if (TryParseMarkup(text, out var extension, out var arguments, out var positional))
        {
            if (extension is not ("x:Type" or "Type"))
            {
                Report(Diagnostics.UnsupportedValue, context, text, "Type");
                return null;
            }

            text = arguments.TryGetValue("TypeName", out var explicitType) ? explicitType : positional ?? string.Empty;
        }

        var resolved = ResolveTypeReference(text, context);
        if (resolved is not null && IsVisualElement(resolved))
        {
            return resolved;
        }

        // 视觉类型来自 .Visuals.Controls（控件）或 .Visuals（样式/模板/辅助类型）；不带前缀时先按这两处找。
        var name = text.Contains(':') ? text.Substring(text.IndexOf(':') + 1) : text;
        var direct = _compilation.GetTypeByMetadataName("VeloxDev.WinForms.Xaml.Visuals.Controls." + name)
            ?? _compilation.GetTypeByMetadataName("VeloxDev.WinForms.Xaml.Visuals." + name);
        if (direct is not null && IsVisualElement(direct))
        {
            return direct;
        }

        Report(Diagnostics.InvalidStyleTargetType, context, $"'{text}' is not a visual element type.");
        return null;
    }

    private void EmitVisualSetters(XElement settersElement, ITypeSymbol? targetType, string ownerVariable)
    {
        foreach (var setterElement in settersElement.Elements())
        {
            if (ChildMemberName(setterElement) is not ("Setter" or "VisualSetter"))
            {
                continue;
            }

            if (!TryResolveVisualProperty(setterElement, targetType, out var property))
            {
                continue;
            }

            if (!TryGetDependencyPropertyReference(property, out var dependencyProperty))
            {
                Report(
                    Diagnostics.UnsupportedConstruct,
                    setterElement,
                    $"'{property.Name}' is not a dependency property and cannot be styled on the visual backend.");
                continue;
            }

            var valueAttribute = setterElement.Attribute("Value");
            var valueExpression = valueAttribute is null
                ? "null"
                : CreateVisualSetterValue(valueAttribute, property);
            if (valueExpression is null)
            {
                continue;
            }

            _current.Line(
                $"{ownerVariable}.Setters.Add(new {VisualFqn("VisualSetter")}({dependencyProperty}, {valueExpression}));");
        }
    }

    private string? CreateVisualSetterValue(XAttribute valueAttribute, IPropertySymbol property)
    {
        if (TryParseMarkup(valueAttribute.Value, out var extension, out var arguments, out var positional))
        {
            switch (extension)
            {
                case "x:Null":
                case "Null":
                    return "null";

                case "x:Static":
                    return CreateStaticExpression(
                        valueAttribute,
                        positional ?? (arguments.TryGetValue("Member", out var member) ? member : null));

                case "StaticResource":
                    Report(
                        Diagnostics.UnsupportedResource,
                        valueAttribute,
                        "A StaticResource value in a visual setter is not implemented; declare the value inline.");
                    return null;

                default:
                    Report(
                        Diagnostics.UnsupportedConstruct,
                        valueAttribute,
                        $"Markup extension '{extension}' is not supported in a visual setter value.");
                    return null;
            }
        }

        return CreateLiteralExpression(property.Type, valueAttribute.Value, valueAttribute);
    }

    private bool TryResolveVisualProperty(
        XElement element,
        ITypeSymbol? targetType,
        out IPropertySymbol property)
    {
        property = null!;
        var raw = element.Attribute("Property")?.Value;
        if (raw is null || string.IsNullOrWhiteSpace(raw))
        {
            Report(Diagnostics.UnsupportedConstruct, element, "A visual setter or trigger needs a Property.");
            return false;
        }

        var text = raw.Trim();
        var ownerType = targetType;
        var memberName = text;
        var dot = text.LastIndexOf('.');
        if (dot > 0)
        {
            ownerType = ResolveVisualTypeReference(text.Substring(0, dot), element);
            if (ownerType is null)
            {
                return false;
            }

            memberName = text.Substring(dot + 1);
        }

        if (ownerType is null)
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                element,
                "A property name needs a style TargetType, or write it as 'Owner.Property'.");
            return false;
        }

        var resolved = FindProperty(ownerType, memberName);
        if (resolved is null)
        {
            Report(Diagnostics.UnknownMember, element, ownerType.Name, memberName);
            return false;
        }

        property = resolved;
        return true;
    }

    private void EmitVisualTriggers(XElement triggersElement, ITypeSymbol? targetType, string ownerVariable)
    {
        foreach (var triggerElement in triggersElement.Elements())
        {
            if (ChildMemberName(triggerElement) is not ("Trigger" or "VisualTrigger"))
            {
                continue;
            }

            if (!TryResolveVisualProperty(triggerElement, targetType, out var property))
            {
                continue;
            }

            if (!TryGetDependencyPropertyReference(property, out var dependencyProperty))
            {
                Report(
                    Diagnostics.UnsupportedConstruct,
                    triggerElement,
                    $"'{property.Name}' is not a dependency property and cannot drive a visual trigger.");
                continue;
            }

            var valueExpression = "null";
            var valueAttribute = triggerElement.Attribute("Value");
            if (valueAttribute is not null)
            {
                valueExpression = CreateVisualSetterValue(valueAttribute, property);
                if (valueExpression is null)
                {
                    continue;
                }
            }

            var targetName = triggerElement.Attribute("TargetName")?.Value;
            var initializer = string.IsNullOrWhiteSpace(targetName)
                ? string.Empty
                : $" {{ TargetName = {SyntaxHelpers.Quote(targetName!)} }}";
            var triggerVariable = NextVariable();
            _current.Line(
                $"var {triggerVariable} = new {VisualFqn("VisualTrigger")}({dependencyProperty}, {valueExpression}){initializer};");

            foreach (var child in triggerElement.Elements())
            {
                switch (ChildMemberName(child))
                {
                    case "Setters":
                        EmitVisualSetters(child, targetType, triggerVariable);
                        break;

                    case "EnterAction":
                        EmitVisualTriggerAction(child, targetType, triggerVariable, isEnter: true);
                        break;

                    case "ExitAction":
                        EmitVisualTriggerAction(child, targetType, triggerVariable, isEnter: false);
                        break;

                    default:
                        Report(
                            Diagnostics.UnsupportedConstruct,
                            child,
                            $"'{child.Name.LocalName}' is not a supported visual trigger member.");
                        break;
                }
            }

            _current.Line($"{ownerVariable}.Triggers.Add({triggerVariable});");
        }
    }

    private void EmitVisualTriggerAction(
        XElement actionElement,
        ITypeSymbol? targetType,
        string triggerVariable,
        bool isEnter)
    {
        var actions = actionElement.Elements().ToArray();
        if (actions.Length == 0)
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                actionElement,
                "An EnterAction or ExitAction needs at least one action element.");
            return;
        }

        var variables = new List<string>();
        foreach (var action in actions)
        {
            var variable = EmitVisualActionElement(action, targetType);
            if (variable is not null)
            {
                variables.Add(variable);
            }
        }

        if (variables.Count == 0)
        {
            return;
        }

        var member = isEnter ? "EnterAction" : "ExitAction";
        if (variables.Count == 1)
        {
            _current.Line($"{triggerVariable}.{member} = {variables[0]};");
            return;
        }

        var groupVariable = NextVariable();
        _current.Line($"var {groupVariable} = new {VisualFqn("TriggerActionGroup")}();");
        foreach (var variable in variables)
        {
            _current.Line($"{groupVariable}.Actions.Add({variable});");
        }

        _current.Line($"{triggerVariable}.{member} = {groupVariable};");
    }

    private string? EmitVisualActionElement(XElement action, ITypeSymbol? targetType)
    {
        switch (action.Name.LocalName)
        {
            case "TransitionAction":
                return EmitVisualTransitionAction(action, targetType);

            case "KeyFrameAnimation":
                return EmitVisualKeyFrameAnimation(action, targetType);

            case "TriggerActionGroup":
                return EmitVisualActionGroup(action, targetType);

            default:
                return EmitCustomVisualTriggerAction(action);
        }
    }

    private string? EmitVisualActionGroup(XElement groupElement, ITypeSymbol? targetType)
    {
        if (!groupElement.HasElements)
        {
            Report(Diagnostics.UnsupportedConstruct, groupElement, "A TriggerActionGroup needs at least one action.");
            return null;
        }

        var groupVariable = NextVariable();
        _current.Line($"var {groupVariable} = new {VisualFqn("TriggerActionGroup")}();");
        foreach (var child in groupElement.Elements())
        {
            var variable = EmitVisualActionElement(child, targetType);
            if (variable is not null)
            {
                _current.Line($"{groupVariable}.Actions.Add({variable});");
            }
        }

        return groupVariable;
    }

    private string? EmitVisualTransitionAction(XElement action, ITypeSymbol? targetType)
    {
        if (!TryResolveVisualProperty(action, targetType, out var property))
        {
            return null;
        }

        if (!TryGetDependencyPropertyReference(property, out var dependencyProperty))
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                action,
                $"'{property.Name}' is not a dependency property and cannot drive a visual action.");
            return null;
        }

        var toAttribute = action.Attribute("To");
        var toExpression = "null";
        if (toAttribute is not null)
        {
            toExpression = CreateVisualSetterValue(toAttribute, property);
            if (toExpression is null)
            {
                return null;
            }
        }

        var effectParts = CollectVisualEffectParts(action);
        if (effectParts is null)
        {
            return null;
        }

        var effect = effectParts.Count > 0
            ? $", Effect = new global::VeloxDev.TransitionSystem.TransitionEffect {{ {string.Join(", ", effectParts)} }}"
            : string.Empty;

        var actionVariable = NextVariable();
        _current.Line(
            $"var {actionVariable} = new {VisualFqn("TransitionAction")} "
            + $"{{ Property = {dependencyProperty}, To = {toExpression}{effect} }};");
        return actionVariable;
    }

    private string? EmitVisualKeyFrameAnimation(XElement action, ITypeSymbol? targetType)
    {
        if (!TryResolveVisualProperty(action, targetType, out var property))
        {
            return null;
        }

        if (!TryGetDependencyPropertyReference(property, out var dependencyProperty))
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                action,
                $"'{property.Name}' is not a dependency property and cannot drive a visual action.");
            return null;
        }

        var effectParts = CollectVisualEffectParts(action);
        if (effectParts is null)
        {
            return null;
        }

        var frames = action.Elements().Where(child => child.Name.LocalName == "KeyFrame").ToArray();
        if (frames.Length == 0)
        {
            Report(Diagnostics.UnsupportedConstruct, action, "A KeyFrameAnimation needs at least one KeyFrame element.");
            return null;
        }

        var actionVariable = NextVariable();
        var initializer = $"Property = {dependencyProperty}";
        if (effectParts.Count > 0)
        {
            initializer += ", " + string.Join(", ", effectParts);
        }

        _current.Line($"var {actionVariable} = new {VisualFqn("KeyFrameAnimation")} {{ {initializer} }};");

        foreach (var frame in action.Elements())
        {
            if (frame.Name.LocalName != "KeyFrame")
            {
                Report(Diagnostics.UnsupportedConstruct, frame, "A KeyFrameAnimation accepts only KeyFrame children.");
                continue;
            }

            var offsetAttribute = frame.Attribute("Offset");
            if (offsetAttribute is null)
            {
                Report(Diagnostics.UnsupportedConstruct, frame, "A KeyFrame needs an Offset attribute.");
                continue;
            }

            if (!double.TryParse(offsetAttribute.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var offset))
            {
                Report(Diagnostics.UnsupportedValue, frame, offsetAttribute.Value, "System.Double");
                continue;
            }

            var valueAttribute = frame.Attribute("Value");
            if (valueAttribute is null)
            {
                Report(Diagnostics.UnsupportedConstruct, frame, "A KeyFrame needs a Value attribute.");
                continue;
            }

            var valueExpression = CreateVisualSetterValue(valueAttribute, property);
            if (valueExpression is null)
            {
                continue;
            }

            _current.Line(
                $"{actionVariable}.KeyFrames.Add(new {VisualFqn("KeyFrame")} "
                + $"{{ Offset = {offset.ToString("R", CultureInfo.InvariantCulture)}D, Value = {valueExpression} }});");
        }

        return actionVariable;
    }

    private List<string>? CollectVisualEffectParts(XElement action)
    {
        var parts = new List<string>();

        var durationAttribute = action.Attribute("Duration");
        if (durationAttribute is not null)
        {
            if (!TimeSpan.TryParse(durationAttribute.Value, CultureInfo.InvariantCulture, out _))
            {
                Report(Diagnostics.UnsupportedValue, durationAttribute, durationAttribute.Value, "System.TimeSpan");
                return null;
            }

            parts.Add(
                $"Duration = global::System.TimeSpan.Parse({SyntaxHelpers.Quote(durationAttribute.Value)}, global::System.Globalization.CultureInfo.InvariantCulture)");
        }

        var easeAttribute = action.Attribute("Ease");
        if (easeAttribute is not null)
        {
            if (TryMapEase(easeAttribute.Value, out var ease))
            {
                parts.Add($"Ease = {ease}");
            }
            else
            {
                Report(Diagnostics.UnsupportedValue, easeAttribute, easeAttribute.Value, "Ease");
            }
        }

        var loopAttribute = action.Attribute("LoopTime");
        if (loopAttribute is not null)
        {
            if (!int.TryParse(loopAttribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var loop))
            {
                Report(Diagnostics.UnsupportedValue, loopAttribute, loopAttribute.Value, "System.Int32");
                return null;
            }

            parts.Add($"LoopTime = {loop}");
        }

        var reverseAttribute = action.Attribute("IsAutoReverse");
        if (reverseAttribute is not null)
        {
            if (!bool.TryParse(reverseAttribute.Value, out var reverse))
            {
                Report(Diagnostics.UnsupportedValue, reverseAttribute, reverseAttribute.Value, "System.Boolean");
                return null;
            }

            parts.Add($"IsAutoReverse = {(reverse ? "true" : "false")}");
        }

        return parts;
    }

    private string? EmitCustomVisualTriggerAction(XElement action)
    {
        var type = ResolveElementType(action);
        if (type is null)
        {
            if (!IsReported(action))
            {
                Report(Diagnostics.UnknownType, action, action.Name.LocalName);
            }

            return null;
        }

        if (!SyntaxHelpers.DerivesFrom(type, "VeloxDev.WinForms.Xaml.Visuals.VisualTriggerAction"))
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                action,
                $"'{type.Name}' does not derive from VeloxDev.WinForms.Xaml.Visuals.VisualTriggerAction.");
            return null;
        }

        if (type is not INamedTypeSymbol namedType || namedType.IsGenericType)
        {
            Report(Diagnostics.UnsupportedConstruct, action, $"'{type.Name}' is not a usable trigger action type.");
            return null;
        }

        if (type.IsAbstract || !HasPublicParameterlessConstructor(namedType))
        {
            Report(Diagnostics.UnsupportedConstruct, action, $"'{type.Name}' needs a public parameterless constructor.");
            return null;
        }

        if (action.HasElements)
        {
            Report(Diagnostics.UnsupportedConstruct, action, "Nested content is not supported in a trigger action.");
            return null;
        }

        var actionVariable = NextVariable();
        _current.Line($"var {actionVariable} = new {SyntaxHelpers.TypeName(type)}();");

        foreach (var attribute in action.Attributes())
        {
            if (attribute.IsNamespaceDeclaration || attribute.Name.NamespaceName == SyntaxHelpers.XamlNamespace)
            {
                continue;
            }

            var name = attribute.Name.LocalName;
            if (name.IndexOf('.') >= 0)
            {
                Report(Diagnostics.UnsupportedConstruct, attribute, "Attached properties are not supported on a trigger action.");
                continue;
            }

            var property = FindProperty(type, name);
            if (property is null)
            {
                Report(Diagnostics.UnknownMember, attribute, type.Name, name);
                continue;
            }

            if (property.IsStatic
                || property.SetMethod is null
                || property.SetMethod.DeclaredAccessibility != Accessibility.Public)
            {
                Report(Diagnostics.UnsupportedConstruct, attribute, $"'{name}' is not a public writable instance property.");
                continue;
            }

            EmitCustomActionProperty(actionVariable, property, attribute);
        }

        return actionVariable;
    }

    private void EmitVisualStyleTemplate(XElement propertyElement, ITypeSymbol? targetType, string styleVariable)
    {
        var templateElement = propertyElement.Elements()
            .FirstOrDefault(child => ChildMemberName(child) is "ControlTemplate" or "VisualControlTemplate");
        if (templateElement is null)
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                propertyElement,
                "Style.Template needs one ControlTemplate element.");
            return;
        }

        var template = EmitVisualControlTemplate(templateElement, key: null, inheritedTargetType: targetType);
        if (template is not null)
        {
            _current.Line($"{styleVariable}.Template = {template};");
        }
    }

    private string? EmitVisualControlTemplate(
        XElement templateElement,
        string? key,
        ITypeSymbol? inheritedTargetType = null)
    {
        if (_templateDepth > 0)
        {
            Report(Diagnostics.UnsupportedConstruct, templateElement, "Nested visual ControlTemplates are not supported.");
            return null;
        }

        var content = templateElement.Elements().FirstOrDefault(child => !IsPropertyElement(child));
        if (content is null)
        {
            Report(Diagnostics.UnsupportedConstruct, templateElement, "A visual ControlTemplate needs one root element.");
            return null;
        }

        var type = ResolveElementType(content);
        if (type is null || !IsVisualElement(type))
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                content,
                "A visual ControlTemplate root must be a visual element.");
            return null;
        }

        ITypeSymbol? targetType = null;
        var targetTypeAttribute = templateElement.Attribute("TargetType");
        if (targetTypeAttribute is not null)
        {
            targetType = ResolveVisualTypeReference(targetTypeAttribute.Value, templateElement);
            if (targetType is null)
            {
                return null;
            }
        }
        else
        {
            targetType = inheritedTargetType;
        }

        var methodName = "CreateVisualControlTemplate_"
            + SyntaxHelpers.SanitizeIdentifier(string.IsNullOrWhiteSpace(key) ? "Inline" + _templateNames.Count : key!);
        if (!_templateNames.Add(methodName))
        {
            Report(Diagnostics.DuplicateName, templateElement, key ?? methodName, _owner.Name);
            return null;
        }

        _helpers.Open(
            $"private {VisualFqn("VisualElement")} {methodName}({VisualFqn("VisualNameScope")} __xamlScope)");
        var previous = _current;
        var previousScope = _scopeExpression;
        _current = _helpers;
        _templateDepth++;
        _scopeExpression = "__xamlScope";
        string expression;
        try
        {
            expression = EmitControl(content, type, null);
        }
        finally
        {
            _templateDepth--;
            _scopeExpression = previousScope;
            _current = previous;
        }

        _helpers.Line($"return {expression};");
        _helpers.Close();

        var templateVariable = NextVariable();
        _current.Line(
            $"var {templateVariable} = new {VisualFqn("VisualControlTemplate")}(__scope => this.{methodName}(__scope));");
        if (targetType is not null)
        {
            _current.Line($"{templateVariable}.TargetType = typeof({SyntaxHelpers.TypeName(targetType)});");
        }

        foreach (var child in templateElement.Elements().Where(IsPropertyElement))
        {
            if (ChildMemberName(child) == "Triggers")
            {
                EmitVisualTriggers(child, targetType, templateVariable);
            }
        }

        return templateVariable;
    }

    // 视觉 DataTemplate 资源：与 item 模板同形，但作为命名资源存进 Resources，可被
    // {StaticResource} 或模板选择器引用。
    private string? EmitVisualDataTemplateResource(XElement templateElement, string key)
    {
        var content = templateElement.Elements().FirstOrDefault(child => !IsPropertyElement(child));
        if (content is null)
        {
            Report(Diagnostics.UnsupportedResource, templateElement, "A visual DataTemplate needs one root element.");
            return null;
        }

        var type = ResolveElementType(content);
        if (type is null || !IsVisualElement(type))
        {
            Report(
                Diagnostics.UnsupportedResource,
                content,
                "A visual DataTemplate root must be a visual element.");
            return null;
        }

        var methodName = "CreateVisualDataTemplate_" + SyntaxHelpers.SanitizeIdentifier(key);
        if (!_templateNames.Add(methodName))
        {
            Report(Diagnostics.DuplicateName, templateElement, key, _owner.Name);
            return null;
        }

        _helpers.Open($"private {VisualFqn("VisualElement")} {methodName}()");
        var previous = _current;
        _current = _helpers;
        _templateDepth++;
        string expression;
        try
        {
            expression = EmitControl(content, type, null);
        }
        finally
        {
            _templateDepth--;
            _current = previous;
        }

        _helpers.Line($"return {expression};");
        _helpers.Close();

        return $"new {VisualFqn("DataTemplate")}(this.{methodName})";
    }

    // 视觉模板属性（ItemTemplate / ContentTemplate）：模板体编成一个返回 VisualElement 的私有工厂，交给
    // 视觉 DataTemplate。模板内的 x:Name 是每实例部件、不生成宿主字段。
    private void EmitVisualTemplateProperty(XElement templateElement, string ownerExpression, string propertyName)
    {
        var content = templateElement.Elements().FirstOrDefault(child => !IsPropertyElement(child));
        if (content is null)
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                templateElement,
                $"A visual {propertyName} needs one root element.");
            return;
        }

        var type = ResolveElementType(content);
        if (type is null || !IsVisualElement(type))
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                content,
                $"A visual {propertyName} root must be a visual element.");
            return;
        }

        var methodName = "CreateVisualItemTemplate_" + _templateNames.Count;
        if (!_templateNames.Add(methodName))
        {
            Report(Diagnostics.DuplicateName, templateElement, methodName, _owner.Name);
            return;
        }

        _helpers.Open($"private {VisualFqn("VisualElement")} {methodName}()");
        var previous = _current;
        _current = _helpers;
        _templateDepth++;
        string expression;
        try
        {
            expression = EmitControl(content, type, null);
        }
        finally
        {
            _templateDepth--;
            _current = previous;
        }

        _helpers.Line($"return {expression};");
        _helpers.Close();

        _current.Line(
            $"{ownerExpression}.{propertyName} = new "
            + $"{VisualFqn("DataTemplate")}(this.{methodName});");
    }

    private void EmitVisualStyleAttribute(XAttribute attribute, string targetExpression)
    {
        if (!TryReadStaticResourceKey(attribute.Value, out var key))
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                attribute,
                "A visual Style supports only {StaticResource Key}.");
            return;
        }

        // Generated initializers assign attributes before the element is parented under the view root, so the
        // ancestor resource chain is reachable only from the root.
        _current.Line(
            $"{targetExpression}.Style = {VisualFqn("VisualResourceLookup")}"
            + $".Get<{VisualFqn("VisualStyle")}>(this, {SyntaxHelpers.Quote(key)});");
    }

    private void EmitVisualPropertyValue(XAttribute attribute, string targetExpression, IPropertySymbol property)
    {
        if (property.SetMethod is null)
        {
            Report(Diagnostics.UnsupportedConstruct, attribute, $"'{property.Name}' is read-only in XAML.");
            return;
        }

        if (!TryParseMarkup(attribute.Value, out var extension, out var arguments, out var positional))
        {
            var literal = CreateLiteralExpression(property.Type, attribute.Value, attribute);
            if (literal is not null)
            {
                _current.Line($"{targetExpression}.{property.Name} = {literal};");
            }

            return;
        }

        switch (extension)
        {
            case "x:Null":
            case "Null":
                _current.Line($"{targetExpression}.{property.Name} = null;");
                return;

            case "Binding":
            case "TemplateBinding":
                if (!TryGetDependencyPropertyReference(property, out var dependencyProperty))
                {
                    Report(
                        Diagnostics.UnsupportedConstruct,
                        attribute,
                        $"A binding needs a dependency property; '{property.ContainingType.Name}.{property.Name}' is a CLR property.");
                    return;
                }

                var bindingExpression = BuildBindingExpression(
                    attribute,
                    property.Type,
                    arguments,
                    positional,
                    templateBinding: extension == "TemplateBinding",
                    allowTwoWay: true);
                if (bindingExpression is not null)
                {
                    _current.Line(
                        $"{SyntaxHelpers.Fqn("VisualBindingOperations")}.Bind({targetExpression}, {dependencyProperty}, {bindingExpression});");
                }

                return;

            case "StaticResource":
                var key = arguments.TryGetValue("Key", out var explicitKey) ? explicitKey : positional;
                if (string.IsNullOrEmpty(key))
                {
                    Report(Diagnostics.UnsupportedResource, attribute, "A StaticResource reference needs a key.");
                    return;
                }

                _current.Line(
                    $"{targetExpression}.{property.Name} = {VisualFqn("VisualResourceLookup")}"
                    + $".Get<{SyntaxHelpers.TypeName(property.Type)}>(this, {SyntaxHelpers.Quote(key!)});");
                return;

            case "DynamicResource":
            case "x:DynamicResource":
                Report(
                    Diagnostics.UnsupportedConstruct,
                    attribute,
                    "DynamicResource is not implemented on the visual backend; use StaticResource.");
                return;

            case "x:Static":
                var staticExpression = CreateStaticExpression(
                    attribute,
                    positional ?? (arguments.TryGetValue("Member", out var member) ? member : null));
                if (staticExpression is not null)
                {
                    _current.Line($"{targetExpression}.{property.Name} = {staticExpression};");
                }

                return;

            default:
                Report(
                    Diagnostics.UnsupportedConstruct,
                    attribute,
                    $"Markup extension '{extension}' is not supported on the visual backend.");
                return;
        }
    }

    private void EmitStyleResource(
        XElement styleElement,
        string? key,
        string ownerExpression,
        List<string> deferredStyleLinks)
    {
        var targetType = ResolveStyleTargetType(styleElement);
        if (targetType is null)
        {
            return;
        }

        var isNative = !IsXamlControl(targetType);
        if (isNative && !SyntaxHelpers.DerivesFrom(targetType, SyntaxHelpers.ControlMetadataName))
        {
            Report(
                Diagnostics.InvalidStyleTargetType,
                styleElement,
                $"Style TargetType '{targetType.Name}' must derive from System.Windows.Forms.Control.");
            return;
        }

        if (isNative && string.IsNullOrWhiteSpace(key))
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                styleElement,
                "An implicit style needs a VeloxDev.WinForms.Xaml.Controls.Control-derived TargetType; give a native-control style an x:Key.");
            return;
        }

        var styleVariable = NextVariable();
        _current.Line($"var {styleVariable} = new {SyntaxHelpers.Fqn("Style")}();");
        _current.Line($"{styleVariable}.TargetType = typeof({SyntaxHelpers.TypeName(targetType)});");

        var basedOn = styleElement.Attribute("BasedOn");
        if (basedOn is not null)
        {
            if (TryReadStaticResourceKey(basedOn.Value, out var baseKey))
            {
                deferredStyleLinks.Add(
                    $"{styleVariable}.BasedOn = {SyntaxHelpers.Fqn("XamlRuntime")}.GetResource<{SyntaxHelpers.Fqn("Style")}>({ownerExpression}, {SyntaxHelpers.Quote(baseKey)});");
            }
            else
            {
                Report(
                    Diagnostics.UnsupportedConstruct,
                    basedOn,
                    "Style BasedOn supports only {StaticResource Key}.");
            }
        }

        var templateAttribute = styleElement.Attribute("Template");
        if (templateAttribute is not null)
        {
            if (isNative)
            {
                Report(
                    Diagnostics.UnsupportedConstruct,
                    templateAttribute,
                    "ControlTemplate is not supported on a native-control style.");
            }
            else if (TryReadStaticResourceKey(templateAttribute.Value, out var templateKey))
            {
                deferredStyleLinks.Add(
                    $"{styleVariable}.Template = {SyntaxHelpers.Fqn("XamlRuntime")}.GetResource<{SyntaxHelpers.Fqn("ControlTemplate")}>({ownerExpression}, {SyntaxHelpers.Quote(templateKey)});");
            }
            else
            {
                Report(
                    Diagnostics.UnsupportedConstruct,
                    templateAttribute,
                    "Style.Template supports only {StaticResource Key}.");
            }
        }

        foreach (var child in styleElement.Elements())
        {
            switch (ChildMemberName(child))
            {
                case "Setters":
                    if (isNative)
                    {
                        EmitNativeSetters(child, targetType, $"{styleVariable}.Setters");
                    }
                    else
                    {
                        EmitSetters(child, targetType, $"{styleVariable}.Setters");
                    }

                    break;

                case "Triggers":
                    if (isNative)
                    {
                        Report(
                            Diagnostics.UnsupportedConstruct,
                            child,
                            "Triggers are not supported on a native-control style.");
                    }
                    else
                    {
                        EmitStyleTriggers(child, targetType, styleVariable);
                    }

                    break;

                case "Template":
                    if (isNative)
                    {
                        Report(
                            Diagnostics.UnsupportedConstruct,
                            child,
                            "ControlTemplate is not supported on a native-control style.");
                    }
                    else
                    {
                        EmitStyleTemplateElement(child, styleVariable, ownerExpression, deferredStyleLinks);
                    }

                    break;

                default:
                    Report(
                        Diagnostics.UnsupportedConstruct,
                        child,
                        $"'{child.Name.LocalName}' is not a supported Style member.");
                    break;
            }
        }

        var keyExpression = string.IsNullOrWhiteSpace(key)
            ? $"typeof({SyntaxHelpers.TypeName(targetType)})"
            : SyntaxHelpers.Quote(key!);

        _current.Line($"{ownerExpression}.Resources[{keyExpression}] = {styleVariable};");
    }

    private void EmitStyleTemplateElement(
        XElement templateElement,
        string styleVariable,
        string ownerExpression,
        List<string> deferredStyleLinks)
    {
        var children = templateElement.Elements().ToArray();
        if (children.Length != 1 || children[0].Name.LocalName != "StaticResource")
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                templateElement,
                "Style.Template needs exactly one <StaticResource Key=\"...\"/> element.");
            return;
        }

        var key = children[0].Attribute("Key")?.Value;
        if (string.IsNullOrWhiteSpace(key))
        {
            Report(Diagnostics.UnsupportedResource, children[0], "A StaticResource reference needs a key.");
            return;
        }

        deferredStyleLinks.Add(
            $"{styleVariable}.Template = {SyntaxHelpers.Fqn("XamlRuntime")}.GetResource<{SyntaxHelpers.Fqn("ControlTemplate")}>({ownerExpression}, {SyntaxHelpers.Quote(key!)});");
    }

    private void EmitNativeSetters(XElement settersElement, ITypeSymbol targetType, string setterCollectionExpression)
    {
        foreach (var child in settersElement.Elements())
        {
            if (child.Name.LocalName != "Setter")
            {
                Report(Diagnostics.UnsupportedConstruct, child, "The Setters collection only accepts Setter elements.");
                continue;
            }

            EmitNativeSetter(child, targetType, setterCollectionExpression);
        }
    }

    private void EmitNativeSetter(XElement setterElement, ITypeSymbol targetType, string setterCollectionExpression)
    {
        var propertyName = setterElement.Attribute("Property")?.Value;
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            Report(Diagnostics.UnsupportedConstruct, setterElement, "A Setter needs a Property attribute.");
            return;
        }

        propertyName = propertyName!.Trim();
        if (propertyName.IndexOf('.') >= 0)
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                setterElement,
                "A native-control style setter needs a plain CLR property name, not 'Owner.Property'.");
            return;
        }

        var property = FindProperty(targetType, propertyName);
        if (property is null)
        {
            Report(Diagnostics.UnknownMember, setterElement, targetType.Name, propertyName);
            return;
        }

        if (property.SetMethod is null)
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                setterElement,
                $"The native property '{propertyName}' is read-only.");
            return;
        }

        var valueAttribute = setterElement.Attribute("Value");
        string valueExpression;
        if (valueAttribute is null)
        {
            valueExpression = "null";
        }
        else if (TryParseMarkup(valueAttribute.Value, out var extension, out var arguments, out var positional))
        {
            switch (extension)
            {
                case "x:Null":
                case "Null":
                    valueExpression = "null";
                    break;

                case "StaticResource":
                    var key = arguments.TryGetValue("Key", out var explicitKey) ? explicitKey : positional;
                    if (string.IsNullOrEmpty(key) || _rootScope is null)
                    {
                        Report(Diagnostics.UnsupportedResource, valueAttribute, "A StaticResource reference needs a key and a Control scope.");
                        return;
                    }

                    valueExpression = $"{SyntaxHelpers.Fqn("XamlRuntime")}.GetResource<object>({_rootScope}, {SyntaxHelpers.Quote(key!)})";
                    break;

                case "DynamicResource":
                case "x:DynamicResource":
                    Report(
                        Diagnostics.UnsupportedConstruct,
                        valueAttribute,
                        $"A DynamicResource reference cannot be applied to the native CLR property '{propertyName}'; use a dependency property.");
                    return;

                case "x:Static":
                    var staticExpression = CreateStaticExpression(
                        valueAttribute,
                        positional ?? (arguments.TryGetValue("Member", out var member) ? member : null));
                    if (staticExpression is null)
                    {
                        return;
                    }

                    valueExpression = staticExpression;
                    break;

                default:
                    Report(
                        Diagnostics.UnsupportedConstruct,
                        valueAttribute,
                        "A native-control style setter supports literal, {StaticResource} and {x:Static} values only.");
                    return;
            }
        }
        else if (!LiteralFactory.TryCreate(property.Type, valueAttribute.Value, out valueExpression, out _))
        {
            valueExpression =
                $"{SyntaxHelpers.Fqn("XamlRuntime")}.ConvertValue({SyntaxHelpers.Quote(valueAttribute.Value)}, typeof({RuntimeTypeName(property.Type)}))";
        }

        _current.Line(
            $"{setterCollectionExpression}.Add(new {SyntaxHelpers.Fqn("Setter")}({SyntaxHelpers.Quote(propertyName)}, {valueExpression}));");
    }

    private static bool TryReadStaticResourceKey(string text, out string key)
    {
        key = string.Empty;
        if (!TryParseMarkup(text, out var extension, out var arguments, out var positional)
            || extension != "StaticResource")
        {
            return false;
        }

        key = arguments.TryGetValue("Key", out var explicitKey) ? explicitKey : positional ?? string.Empty;
        return !string.IsNullOrWhiteSpace(key);
    }

    private void EmitStyleTriggers(XElement triggersElement, ITypeSymbol targetType, string styleVariable)
    {
        foreach (var child in triggersElement.Elements())
        {
            if (!IsTriggerElement(child))
            {
                Report(
                    Diagnostics.UnsupportedConstruct,
                    child,
                    "The Style Triggers collection only accepts Trigger, DataTrigger or MultiTrigger elements.");
                continue;
            }

            EmitTrigger(child, targetType, styleVariable);
        }
    }

    private ITypeSymbol? ResolveStyleTargetType(XElement styleElement)
    {
        var attribute = styleElement.Attribute("TargetType");
        if (attribute is null || string.IsNullOrWhiteSpace(attribute.Value))
        {
            Report(Diagnostics.InvalidStyleTargetType, styleElement, "A Style needs a TargetType.");
            return null;
        }

        var value = attribute.Value.Trim();
        if (TryParseMarkup(value, out var extension, out var arguments, out var positional))
        {
            if (extension is not ("x:Type" or "Type"))
            {
                Report(
                    Diagnostics.InvalidStyleTargetType,
                    attribute,
                    "Style TargetType supports only a type name or {x:Type Type}.");
                return null;
            }

            value = arguments.TryGetValue("TypeName", out var explicitType)
                ? explicitType
                : positional ?? string.Empty;
        }

        var resolved = ResolveTypeReference(value, styleElement);
        if (resolved is null)
        {
            Report(
                Diagnostics.InvalidStyleTargetType,
                attribute,
                $"The Style TargetType '{value}' could not be resolved.");
        }

        return resolved;
    }

    private ITypeSymbol? ResolveTypeReference(string typeName, XElement contextElement)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        var colon = typeName.IndexOf(':');
        if (colon >= 0)
        {
            var prefix = typeName.Substring(0, colon);
            var localName = typeName.Substring(colon + 1);

            var uri = GetNamespaceUriForPrefix(contextElement, prefix);
            var clrNamespace = uri is null ? null : GetClrNamespace(uri);
            if (clrNamespace is not null)
            {
                if (string.Equals(clrNamespace, SyntaxHelpers.RuntimeNamespace, StringComparison.Ordinal))
                {
                    return ResolveVisualAdapterType(localName, contextElement);
                }

                return _compilation.GetTypeByMetadataName(clrNamespace + "." + localName);
            }

            if (prefix == "v")
            {
                return ResolveVisualAdapterType(localName, contextElement);
            }

            return null;
        }

        // A fully qualified name needs no prefix resolution.
        var qualified = _compilation.GetTypeByMetadataName(typeName);
        if (qualified is not null)
        {
            return qualified;
        }

        // No prefix in a type reference means VeloxDev too. Unknown names stay null here so the caller can report
        // its own diagnostic (InvalidStyleTargetType / UnknownType) instead of a duplicate VELOX_XAML019.
        return ResolveDefaultType(typeName, contextElement);
    }

    private static string? GetNamespaceUriForPrefix(XElement element, string prefix)
    {
        for (var current = element; current is not null; current = current.Parent)
        {
            var declaration = current.Attributes().FirstOrDefault(attribute =>
                attribute.IsNamespaceDeclaration
                && attribute.Name.LocalName == (prefix.Length == 0 ? "xmlns" : prefix));

            if (declaration is not null)
            {
                return declaration.Value;
            }
        }

        return null;
    }

    private static IEnumerable<XElement> FlattenResources(XElement propertyElement)
    {
        foreach (var child in propertyElement.Elements())
        {
            foreach (var resource in FlattenResourceEntry(child))
            {
                yield return resource;
            }
        }
    }

    private static IEnumerable<XElement> FlattenResourceEntry(XElement element)
    {
        if (element.Name.LocalName == "ResourceDictionary")
        {
            foreach (var child in element.Elements())
            {
                foreach (var inner in FlattenResourceEntry(child))
                {
                    yield return inner;
                }
            }

            yield break;
        }

        yield return element;
    }

    private bool TryCreateResourceExpression(XElement resource, out string expression)
    {
        expression = "null";

        if (resource.Name.NamespaceName == SyntaxHelpers.XamlNamespace)
        {
            ITypeSymbol? type = resource.Name.LocalName switch
            {
                "String" => _compilation.GetSpecialType(SpecialType.System_String),
                "Int32" => _compilation.GetSpecialType(SpecialType.System_Int32),
                "Int64" => _compilation.GetSpecialType(SpecialType.System_Int64),
                "Double" => _compilation.GetSpecialType(SpecialType.System_Double),
                "Single" => _compilation.GetSpecialType(SpecialType.System_Single),
                "Decimal" => _compilation.GetSpecialType(SpecialType.System_Decimal),
                "Boolean" => _compilation.GetSpecialType(SpecialType.System_Boolean),
                "Color" => _compilation.GetTypeByMetadataName("System.Drawing.Color"),
                _ => null,
            };

            if (type is null)
            {
                Report(Diagnostics.UnsupportedResource, resource, $"x:{resource.Name.LocalName} is not a supported resource type.");
                return false;
            }

            if (!LiteralFactory.TryCreate(type, resource.Value, out expression, out _))
            {
                Report(Diagnostics.UnsupportedValue, resource, resource.Value, type.ToDisplayString());
                return false;
            }

            return true;
        }

        var resourceType = ResolveElementType(resource);
        if (resourceType is null)
        {
            if (!IsReported(resource))
            {
                Report(Diagnostics.UnknownType, resource, resource.Name.LocalName);
            }

            return false;
        }

        if (resource.HasElements)
        {
            if (SyntaxHelpers.DerivesFrom(resourceType, SyntaxHelpers.ControlMetadataName))
            {
                Report(
                    Diagnostics.UnsupportedResource,
                    resource,
                    "A Control resource must be empty; declare its content where the resource is used.");
                return false;
            }

            var objectExpression = EmitXamlObject(resource, resourceType, null);
            if (objectExpression is null)
            {
                return false;
            }

            expression = objectExpression;
            return true;
        }

        if (LiteralFactory.TryCreate(resourceType, resource.Value, out expression, out _))
        {
            return true;
        }

        // 空元素 = 对象资源：new 一个实例并把属性写上去。选择器这类无字面量的对象因此能像样式一样声明。
        if (string.IsNullOrWhiteSpace(resource.Value))
        {
            var variable = NextVariable();
            _current.Line($"var {variable} = new {SyntaxHelpers.TypeName(resourceType)}();");
            ApplyAttributes(resource, resourceType, variable, null);
            expression = variable;
            return true;
        }

        Report(Diagnostics.UnsupportedValue, resource, resource.Value, resourceType.ToDisplayString());
        return false;
    }

    private bool EmitDataTemplate(XElement template, string key)
    {
        if (_templateDepth > 0)
        {
            Report(Diagnostics.UnsupportedResource, template, "Nested DataTemplates are not supported.");
            return false;
        }

        var content = template.Elements().FirstOrDefault(child => !IsPropertyElement(child));
        if (content is null)
        {
            Report(Diagnostics.UnsupportedResource, template, "A DataTemplate needs one root control.");
            return false;
        }

        var type = ResolveElementType(content);
        if (type is null)
        {
            if (!IsReported(content))
            {
                Report(Diagnostics.UnknownType, content, content.Name.LocalName);
            }

            return false;
        }

        if (!IsXamlControl(type))
        {
            Report(
                Diagnostics.UnsupportedResource,
                content,
                "A DataTemplate root must be a VeloxDev.WinForms.Xaml.Controls.Control so that its bindings can resolve a data context.");
            return false;
        }

        var methodName = "CreateTemplate_" + SyntaxHelpers.SanitizeIdentifier(key);
        if (!_templateNames.Add(methodName))
        {
            Report(Diagnostics.DuplicateName, template, key, _owner.Name);
            return false;
        }

        _helpers.Open($"private global::System.Windows.Forms.Control {methodName}()");
        var previous = _current;
        var previousScope = _scopeExpression;
        const string scopeVariable = "__xamlScope";
        _current = _helpers;
        _templateDepth++;
        _helpers.Line($"var {scopeVariable} = new global::VeloxDev.WinForms.Xaml.NameScope();");
        _scopeExpression = scopeVariable;
        string expression;
        try
        {
            expression = EmitControl(content, type, null);
            _helpers.Line($"{expression}.NameScope = {scopeVariable};");
        }
        finally
        {
            _templateDepth--;
            _scopeExpression = previousScope;
            _current = previous;
        }

        _helpers.Line($"return {expression};");
        _helpers.Close();
        return true;
    }

    private bool EmitControlTemplate(XElement template, string key, string ownerExpression)
    {
        if (_templateDepth > 0)
        {
            Report(Diagnostics.UnsupportedConstruct, template, "Nested ControlTemplates are not supported.");
            return false;
        }

        var children = template.Elements().Where(child => !IsPropertyElement(child)).ToArray();
        if (children.Length != 1)
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                template,
                "A ControlTemplate needs exactly one root Control element.");
            return false;
        }

        var content = children[0];
        var type = ResolveElementType(content);
        if (type is null)
        {
            if (!IsReported(content))
            {
                Report(Diagnostics.UnknownType, content, content.Name.LocalName);
            }

            return false;
        }

        if (!SyntaxHelpers.DerivesFrom(type, SyntaxHelpers.ControlMetadataName))
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                content,
                "A ControlTemplate root must be a System.Windows.Forms.Control.");
            return false;
        }

        var targetTypeAttribute = template.Attribute("TargetType");
        ITypeSymbol? targetType = _owner;
        if (targetTypeAttribute is not null)
        {
            targetType = ResolveTypeFromMarkup(targetTypeAttribute, targetTypeAttribute.Value);
            if (targetType is null)
            {
                return false;
            }
        }

        var methodName = "CreateControlTemplate_" + SyntaxHelpers.SanitizeIdentifier(key);
        if (!_templateNames.Add(methodName))
        {
            Report(Diagnostics.DuplicateName, template, key, _owner.Name);
            return false;
        }

        _helpers.Open($"private global::System.Windows.Forms.Control {methodName}()");
        var previous = _current;
        var previousScope = _scopeExpression;
        const string scopeVariable = "__xamlScope";
        _current = _helpers;
        _templateDepth++;
        _helpers.Line($"var {scopeVariable} = new global::VeloxDev.WinForms.Xaml.NameScope();");
        _scopeExpression = scopeVariable;
        string expression;
        try
        {
            expression = EmitControl(content, type, null);
            _helpers.Line($"{expression}.NameScope = {scopeVariable};");
        }
        finally
        {
            _templateDepth--;
            _scopeExpression = previousScope;
            _current = previous;
        }

        _helpers.Line($"return {expression};");
        _helpers.Close();

        var templateVariable = NextVariable();
        _current.Line(
            $"var {templateVariable} = new {SyntaxHelpers.Fqn("ControlTemplate")}(this.{methodName});");
        if (targetTypeAttribute is not null && targetType is not null)
        {
            _current.Line($"{templateVariable}.TargetType = typeof({SyntaxHelpers.TypeName(targetType)});");
        }

        foreach (var child in template.Elements().Where(IsPropertyElement))
        {
            if (ChildMemberName(child) == "Triggers")
            {
                if (targetType is null)
                {
                    Report(
                        Diagnostics.UnsupportedConstruct,
                        child,
                        "A ControlTemplate with triggers needs a TargetType.");
                    continue;
                }

                foreach (var trigger in child.Elements())
                {
                    if (!IsTriggerElement(trigger))
                    {
                        Report(
                            Diagnostics.UnsupportedConstruct,
                            trigger,
                            "The template Triggers collection only accepts Trigger, DataTrigger or MultiTrigger elements.");
                        continue;
                    }

                    EmitTrigger(trigger, targetType, templateVariable);
                }

                continue;
            }

            Report(
                Diagnostics.UnsupportedConstruct,
                child,
                $"'{child.Name.LocalName}' is not a supported ControlTemplate member.");
        }

        _current.Line($"{ownerExpression}.Resources[{SyntaxHelpers.Quote(key)}] = {templateVariable};");
        return true;
    }

    private ITypeSymbol? ResolveElementType(XElement element)
    {
        var uri = element.Name.NamespaceName;
        var localName = element.Name.LocalName;

        if (uri == SyntaxHelpers.XamlNamespace)
        {
            return null;
        }

        // Unprefixed elements (and the empty namespace) mean VeloxDev types.
        if (uri.Length == 0 || uri == SyntaxHelpers.PresentationNamespace)
        {
            return ResolveUnprefixedType(localName, element);
        }

        // 显式 clr-namespace 优先于历史 `v:` 约定：视觉后端的 .Visuals / .Visuals.Controls 才能按原 URI 解析，
        // 而 .Xaml 仍回退到适配器命名空间（Controls / Shapes / Runtime）。
        var clrNamespace = GetClrNamespace(uri);
        if (clrNamespace is not null)
        {
            if (string.Equals(clrNamespace, SyntaxHelpers.RuntimeNamespace, StringComparison.Ordinal))
            {
                return ResolveVisualAdapterType(localName, element);
            }

            if (clrNamespace.StartsWith("System.Windows.Forms", StringComparison.Ordinal))
            {
                Report(
                    Diagnostics.UnsupportedConstruct,
                    element,
                    $"'{localName}' is a native WinForms type; the visual XAML runtime hosts only VisualElement controls.");
                return null;
            }

            return _compilation.GetTypeByMetadataName(clrNamespace + "." + localName);
        }

        // A legacy `v:` prefix still means the adapter namespaces: controls live in `.Xaml.Controls`, every other
        // runtime type lives in `.Xaml`. This holds when the prefix maps to a non-clr URI.
        if (GetPrefixForNamespace(element, uri) == "v")
        {
            return ResolveVisualAdapterType(localName, element);
        }

        return null;
    }

    // 视觉运行时是唯一的 XAML 后端：控件在 .Visuals.Controls，形状在 .Visuals.Controls.Shapes，
    // 其余辅助类型（如 DataTemplate）在 .Visuals。Thickness / GridLength / RowDefinition 这类共享原语
    // 仍在 .Xaml.Controls（现只保留原语），作为最后回落。
    private ITypeSymbol? ResolveVisualAdapterType(string localName, XObject? source)
    {
        var controlType = _compilation.GetTypeByMetadataName(SyntaxHelpers.VisualControlsNamespace + "." + localName);
        if (controlType is not null)
        {
            return controlType;
        }

        var shapeType = _compilation.GetTypeByMetadataName(SyntaxHelpers.VisualShapesNamespace + "." + localName);
        if (shapeType is not null)
        {
            return shapeType;
        }

        return _compilation.GetTypeByMetadataName(SyntaxHelpers.VisualsNamespace + "." + localName)
            ?? _compilation.GetTypeByMetadataName(SyntaxHelpers.ControlsNamespace + "." + localName)
            ?? _compilation.GetTypeByMetadataName(SyntaxHelpers.ShapesNamespace + "." + localName)
            ?? _compilation.GetTypeByMetadataName(SyntaxHelpers.RuntimeNamespace + "." + localName);
    }

    private ITypeSymbol? ResolveDefaultType(string localName, XObject? source)
        => ResolveVisualAdapterType(localName, source);

    /// <summary>
    /// Resolves an unprefixed type name against the VeloxDev namespaces selected by the root type.
    /// </summary>
    /// <remarks>
    /// No prefix means the VeloxDev visual backend. A template/Demo type needs
    /// <c>local:</c>; an unprefixed name that resolves to no VeloxDev namespace is reported instead of
    /// silently falling back, so the same markup cannot mean different things in different assemblies.
    /// </remarks>
    private ITypeSymbol? ResolveUnprefixedType(string localName, XObject? source)
    {
        var type = ResolveDefaultType(localName, source);
        if (type is not null)
        {
            return type;
        }

        if (source is not null)
        {
            // 同一处不再补一条泛化的 UnknownType：两条诊断说的是同一件事。
            _reportedSources.Add(source);
        }

        Report(Diagnostics.UnprefixedTypeNotVeloxDev, source, localName);
        return null;
    }

    private bool IsReported(XObject? source) => source is not null && _reportedSources.Contains(source);

    private static string? GetPrefixForNamespace(XElement element, string uri)
    {
        try
        {
            return element.GetPrefixOfNamespace(XNamespace.Get(uri));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool IsVNamespace(XObject source, string uri)
    {
        var element = source switch
        {
            XAttribute attribute => attribute.Parent,
            XElement node => node,
            _ => null,
        };

        return element is not null && GetPrefixForNamespace(element, uri) == "v";
    }

    private static string? GetClrNamespace(string uri)
    {
        const string prefix = "clr-namespace:";
        if (!uri.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var body = uri.Substring(prefix.Length);
        var semicolon = body.IndexOf(';');
        return semicolon >= 0 ? body.Substring(0, semicolon) : body;
    }

    private static IPropertySymbol? FindProperty(ITypeSymbol type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(name))
            {
                if (member is IPropertySymbol property)
                {
                    return property;
                }
            }
        }

        return null;
    }

    private static IEventSymbol? FindEvent(ITypeSymbol type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(name))
            {
                if (member is IEventSymbol eventSymbol)
                {
                    return eventSymbol;
                }
            }
        }

        return null;
    }

    private static bool HasMember(ITypeSymbol type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.GetMembers(name).Length > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetDependencyPropertyReference(IPropertySymbol property, out string reference)
    {
        foreach (var attribute in property.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != "VeloxDev.WinForms.Xaml.XamlPropertyAttribute")
            {
                continue;
            }

            SyntaxHelpers.TryGetAttributeName(attribute, property.Name, out var name);
            reference = $"{SyntaxHelpers.TypeName(property.ContainingType)}.{name}Property";
            return true;
        }

        for (var type = property.ContainingType; type is not null; type = type.BaseType)
        {
            var field = type.GetMembers(property.Name + "Property")
                .OfType<IFieldSymbol>()
                .FirstOrDefault(candidate => candidate.Type.ToDisplayString() == SyntaxHelpers.DependencyPropertyMetadataName);

            if (field is not null)
            {
                reference = $"{SyntaxHelpers.TypeName(type)}.{field.Name}";
                return true;
            }
        }

        reference = string.Empty;
        return false;
    }

    private static bool IsCollection(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named && (named.Name == "IList" || named.Name == "ICollection"))
        {
            return true;
        }

        return type.AllInterfaces.Any(candidate => candidate.ToDisplayString() == "System.Collections.IList");
    }

    private static bool IsXamlControl(ITypeSymbol type) => SyntaxHelpers.DerivesFrom(type, SyntaxHelpers.XamlControlMetadataName);

    private static bool IsVisualElement(ITypeSymbol type)
        => SyntaxHelpers.DerivesFrom(type, SyntaxHelpers.VisualElementMetadataName);

    private static string VisualFqn(string simpleName) => $"global::VeloxDev.WinForms.Xaml.Visuals.{simpleName}";

    private static bool CanAssign(ITypeSymbol from, ITypeSymbol to)
    {
        for (var current = from; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, to))
            {
                return true;
            }
        }

        return to.SpecialType == SpecialType.System_Object;
    }

    private static bool IsPropertyElement(XElement element) => element.Name.LocalName.IndexOf('.') >= 0;

    private static bool IsResourcesProperty(XElement element) => IsPropertyElement(element)
        && element.Name.LocalName.EndsWith(".Resources", StringComparison.Ordinal);

    private static string? GetXName(XElement element)
        => element.Attribute(XName.Get("Name", SyntaxHelpers.XamlNamespace))?.Value;

    private string NextVariable() => "__xaml" + (++_variableCounter).ToString(CultureInfo.InvariantCulture);

    private static bool TryParseMarkup(
        string text,
        out string extension,
        out Dictionary<string, string> arguments,
        out string? positional)
    {
        extension = string.Empty;
        arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        positional = null;

        text = text.Trim();
        if (text.Length < 3 || text[0] != '{' || text[text.Length - 1] != '}')
        {
            return false;
        }

        var body = text.Substring(1, text.Length - 2).Trim();
        if (body.Length == 0)
        {
            return false;
        }

        var split = IndexOfTopLevel(body, static character => character is ' ' or ',' or '\t');
        if (split < 0)
        {
            extension = body;
            return true;
        }

        extension = body.Substring(0, split).Trim();
        var rest = body.Substring(split + 1);

        foreach (var part in SplitTopLevel(rest, ','))
        {
            var piece = part.Trim();
            if (piece.Length == 0)
            {
                continue;
            }

            var equals = IndexOfTopLevel(piece, static character => character == '=');
            if (equals < 0)
            {
                positional = TrimQuotes(piece);
                continue;
            }

            var key = piece.Substring(0, equals).Trim();
            var value = TrimQuotes(piece.Substring(equals + 1).Trim());
            arguments[key] = value;
        }

        return true;
    }

    // 标记扩展可以嵌套（Converter={StaticResource X}、AncestorType={x:Type local:Foo}），
    // 所以分隔只认顶层；引号与方括号里的逗号和等号属于值本身。
    private static int IndexOfTopLevel(string text, Func<char, bool> predicate)
    {
        var depth = 0;
        var quote = '\0';
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (quote != '\0')
            {
                if (character == quote && (index == 0 || text[index - 1] != '\\'))
                {
                    quote = '\0';
                }

                continue;
            }

            switch (character)
            {
                case '\'':
                case '"':
                    quote = character;
                    continue;

                case '{':
                case '[':
                case '(':
                    depth++;
                    continue;

                case '}':
                case ']':
                case ')':
                    depth = Math.Max(0, depth - 1);
                    continue;
            }

            if (depth == 0 && predicate(character))
            {
                return index;
            }
        }

        return -1;
    }

    private static IEnumerable<string> SplitTopLevel(string text, char separator)
    {
        var start = 0;
        var depth = 0;
        var quote = '\0';
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (quote != '\0')
            {
                if (character == quote && (index == 0 || text[index - 1] != '\\'))
                {
                    quote = '\0';
                }

                continue;
            }

            switch (character)
            {
                case '\'':
                case '"':
                    quote = character;
                    continue;

                case '{':
                case '[':
                case '(':
                    depth++;
                    continue;

                case '}':
                case ']':
                case ')':
                    depth = Math.Max(0, depth - 1);
                    continue;
            }

            if (depth == 0 && character == separator)
            {
                yield return text.Substring(start, index - start);
                start = index + 1;
            }
        }

        yield return text.Substring(start);
    }

    private static string TrimQuotes(string value)
    {
        if (value.Length >= 2
            && ((value[0] == '\'' && value[value.Length - 1] == '\'')
                || (value[0] == '"' && value[value.Length - 1] == '"')))
        {
            return value.Substring(1, value.Length - 2);
        }

        return value;
    }

    private Location GetLocation(XObject node)
    {
        if (node is IXmlLineInfo info && info.HasLineInfo())
        {
            var line = Math.Max(0, info.LineNumber - 1);
            var column = Math.Max(0, info.LinePosition - 1);
            return Location.Create(
                _filePath,
                default(TextSpan),
                new LinePositionSpan(
                    new LinePosition(line, column),
                    new LinePosition(line, column + 1)));
        }

        return Location.None;
    }

    private void Report(DiagnosticDescriptor descriptor, XObject? node, params object?[] arguments)
    {
        _production.ReportDiagnostic(Diagnostic.Create(
            descriptor,
            node is null ? Location.None : GetLocation(node),
            arguments));
    }

    private void ReportAt(DiagnosticDescriptor descriptor, Location location, params object?[] arguments)
    {
        _production.ReportDiagnostic(Diagnostic.Create(descriptor, location, arguments));
    }
}
