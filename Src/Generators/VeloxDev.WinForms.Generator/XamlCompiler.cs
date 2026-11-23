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
/// <item>Direct child controls added to <c>Controls</c>; visual children are added with <c>Add</c> (or the single
/// content property when the parent has one).</item>
/// <item>Property elements for scalar values, <c>Resources</c>, <c>Controls</c>, list properties and attached
/// properties written as <c>&lt;Grid.Row&gt;1&lt;/Grid.Row&gt;</c>.</item>
/// <item>String, numeric, boolean, enum, color, padding, thickness, size, point and time-span literals.</item>
/// <item><c>{Binding Path, Mode}</c>, <c>{StaticResource Key}</c> and <c>{DynamicResource Key}</c> on visual
/// properties and attached properties.</item>
/// <item><c>DataTemplate</c>, <c>ControlTemplate</c> and <c>ItemsPanelTemplate</c> resources and property elements;
/// each instance receives its own <c>VisualNameScope</c>.</item>
/// <item>Visual styles with direct or wrapped <c>Setter</c> and <c>VisualTrigger</c> elements.</item>
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

    private CodeBuilder _current;
    private INamedTypeSymbol _owner = null!;
    private XElement? _rootElement;
    private string? _scopeExpression;
    private string _resourceAnchor = "this";
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
        _scopeExpression = null;
        _resourceAnchor = "this";

        foreach (var element in root.Elements())
        {
            if (IsResourcesProperty(element))
            {
                EmitResources(element, "this", _owner);
            }
        }

        ApplyAttributes(root, _owner, "this");
        ProcessChildren(root, _owner, "this");
    }

    private void ProcessChildren(XElement element, ITypeSymbol elementType, string elementExpression)
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
                EmitPropertyElement(child, elementType, elementExpression);
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

                var visualExpression = EmitControl(child, type);
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

            var collection = FindItemsCollection(elementType);
            if (collection is null)
            {
                Report(Diagnostics.UnknownType, child, child.Name.LocalName);
                continue;
            }

            var objectExpression = EmitXamlObject(child, type);
            if (objectExpression is not null)
            {
                _current.Line($"{elementExpression}.{collection.Name}.Add({objectExpression});");
            }
        }
    }

    private string EmitControl(XElement element, ITypeSymbol type)
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
            // 视觉模板的 x:Name 是模板部件，不是字段；每实例一个 VisualNameScope，GetTemplateChild 从这里取。
            _current.Line($"{_scopeExpression}.Register({SyntaxHelpers.Quote(name!)}, {expression});");
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

        ApplyAttributes(element, type, expression);
        ProcessChildren(element, type, expression);
        EmitTextContent(element, type, expression);
        return expression;
    }

    // 返回 null 表示这个对象无法生成（已报诊断）：调用方要跳过赋值，不能把一个未声明的变量写进代码。
    private string? EmitXamlObject(XElement element, ITypeSymbol type)
    {
        // 视觉元素必须走 EmitControl：只有它生成 x:Name 字段并递归用 Add 挂子项。
        if (IsVisualElement(type))
        {
            return EmitControl(element, type);
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
        ApplyAttributes(element, type, variable);

        foreach (var child in element.Elements())
        {
            if (IsPropertyElement(child))
            {
                EmitObjectPropertyElement(child, type, variable);
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

            var childExpression = EmitXamlObject(child, childType);
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
        string ownerExpression)
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

                var childExpression = EmitXamlObject(child, childType);
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

            var contentExpression = EmitXamlObject(content, contentType);
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

    private void ApplyAttributes(XElement element, ITypeSymbol type, string targetExpression)
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
                }
                else
                {
                    Report(
                        Diagnostics.UnsupportedConstruct,
                        attribute,
                        $"'{type.Name}' is not a visual element; a Style attribute applies only to the visual backend.");
                }

                continue;
            }

            if (memberName.IndexOf('.') >= 0)
            {
                if (isVisual)
                {
                    EmitVisualAttachedAttribute(attribute, targetExpression);
                }
                else
                {
                    Report(
                        Diagnostics.UnsupportedConstruct,
                        attribute,
                        $"'{type.Name}' is not a visual element and cannot receive attached properties.");
                }

                continue;
            }

            var property = FindProperty(type, memberName);
            if (property is not null)
            {
                if (isVisual)
                {
                    EmitVisualPropertyValue(attribute, targetExpression, property);
                }
                else
                {
                    EmitObjectPropertyValue(attribute, targetExpression, property);
                }

                continue;
            }

            var eventSymbol = FindEvent(type, memberName);
            if (eventSymbol is not null)
            {
                EmitEvent(attribute, targetExpression, eventSymbol, attribute.Value);
                continue;
            }

            Report(Diagnostics.UnknownMember, attribute, type.Name, memberName);
        }
    }

    // 非视觉对象（菜单项、选择器、自定义动作）仍支持字面量和资源值，但没有 DP 管线，不能附着绑定。
    private void EmitObjectPropertyValue(XObject source, string targetExpression, IPropertySymbol property)
    {
        if (property.SetMethod is null)
        {
            Report(Diagnostics.UnsupportedConstruct, source, $"'{property.Name}' is read-only in XAML.");
            return;
        }

        if (!TryParseMarkup(attributeValue(source), out var extension, out var arguments, out var positional))
        {
            var literal = CreateLiteralExpression(property.Type, attributeValue(source), source);
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

            case "StaticResource":
                var key = arguments.TryGetValue("Key", out var explicitKey) ? explicitKey : positional;
                if (string.IsNullOrEmpty(key))
                {
                    Report(Diagnostics.UnsupportedResource, source, "A StaticResource reference needs a key.");
                    return;
                }

                _current.Line(
                    $"{targetExpression}.{property.Name} = {VisualFqn("VisualResourceLookup")}"
                    + $".Get<{SyntaxHelpers.TypeName(property.Type)}>(this, {SyntaxHelpers.Quote(key!)});");
                return;

            case "x:Static":
                var staticExpression = CreateStaticExpression(
                    source,
                    positional ?? (arguments.TryGetValue("Member", out var member) ? member : null));
                if (staticExpression is not null)
                {
                    _current.Line($"{targetExpression}.{property.Name} = {staticExpression};");
                }

                return;

            default:
                Report(
                    Diagnostics.UnsupportedConstruct,
                    source,
                    $"Markup extension '{extension}' is not supported on '{property.ContainingType.Name}.{property.Name}'.");
                return;
        }
    }

    private static string attributeValue(XObject source)
        => source switch
        {
            XAttribute attribute => attribute.Value,
            XElement element => element.Value,
            _ => string.Empty,
        };

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

                    return $"{VisualFqn("VisualResourceLookup")}.Get<object>(this, {SyntaxHelpers.Quote(key!)})";

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


    // 视觉元素的附加属性由生成器直接写依赖属性：值类型在生成期已知，直接 SetValue(Owner.XxxProperty, value)。
    // 属性元素（<Grid.Row>1</Grid.Row>）复用同一条路径。
    private void EmitVisualAttachedAttribute(XAttribute attribute, string targetExpression)
    {
        if (!TryResolveVisualAttachedProperty(attribute.Name.LocalName, attribute, out var dependencyProperty, out var valueType))
        {
            return;
        }

        EmitVisualAttachedValue(attribute, targetExpression, dependencyProperty, valueType, attribute.Value);
    }

    // 视觉附加属性：owner 按视觉层优先解析，显式 clr-namespace 前缀仍优先。属性元素与属性共用这条路径。
    private bool TryResolveVisualAttachedProperty(
        string localName,
        XObject source,
        out string dependencyProperty,
        out ITypeSymbol? valueType)
    {
        dependencyProperty = string.Empty;
        valueType = null;
        var dot = localName.IndexOf('.');
        if (dot <= 0 || dot == localName.Length - 1 || localName.IndexOf('.', dot + 1) >= 0)
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                source,
                $"Attached XAML member '{localName}' must be written as 'Owner.Property' or 'prefix:Owner.Property'.");
            return false;
        }

        var ownerName = localName.Substring(0, dot);
        var propertyName = localName.Substring(dot + 1);
        var namespaceName = source switch
        {
            XAttribute attribute => attribute.Name.NamespaceName,
            XElement element => element.Name.NamespaceName,
            _ => string.Empty,
        };

        var clrNamespace = GetClrNamespace(namespaceName);
        var ownerType = clrNamespace is not null
            ? _compilation.GetTypeByMetadataName(clrNamespace + "." + ownerName)
            : ResolveVisualAdapterType(ownerName, source);
        if (ownerType is null)
        {
            Report(Diagnostics.UnknownType, source, ownerName);
            return false;
        }

        if (!TryGetAttachedPropertyReference(ownerType, propertyName, out dependencyProperty, out valueType))
        {
            Report(Diagnostics.UnknownAttachedProperty, source, ownerType.Name, propertyName);
            return false;
        }

        return true;
    }

    private void EmitVisualAttachedValue(
        XObject source,
        string targetExpression,
        string dependencyProperty,
        ITypeSymbol? valueType,
        string rawValue)
    {
        if (TryParseMarkup(rawValue, out var extension, out var arguments, out var positional))
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
                        source,
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
                    var staticKey = arguments.TryGetValue("Key", out var explicitStaticKey) ? explicitStaticKey : positional;
                    if (string.IsNullOrEmpty(staticKey))
                    {
                        Report(Diagnostics.UnsupportedResource, source, "A StaticResource reference needs a key.");
                        return;
                    }

                    var staticType = valueType is null ? "object" : SyntaxHelpers.TypeName(valueType);
                    _current.Line(
                        $"{VisualFqn("VisualResourceLookup")}.ApplyStatic<{staticType}>({targetExpression}, {SyntaxHelpers.Quote(staticKey!)}, "
                        + $"__v => {targetExpression}.SetValue({dependencyProperty}, __v));");
                    return;

                case "DynamicResource":
                case "x:DynamicResource":
                    var dynamicKey = arguments.TryGetValue("Key", out var explicitDynamicKey) ? explicitDynamicKey : positional;
                    if (string.IsNullOrEmpty(dynamicKey))
                    {
                        Report(Diagnostics.UnsupportedResource, source, "A DynamicResource reference needs a key.");
                        return;
                    }

                    var dynamicType = valueType is null ? "object" : SyntaxHelpers.TypeName(valueType);
                    _current.Line(
                        $"{VisualFqn("VisualResourceLookup")}.BindDynamic<{dynamicType}>({targetExpression}, {SyntaxHelpers.Quote(dynamicKey!)}, "
                        + $"__v => {targetExpression}.SetValue({dependencyProperty}, __v), "
                        + $"() => {targetExpression}.ClearValue({dependencyProperty}));");
                    return;

                case "x:Static":
                    var staticExpression = CreateStaticExpression(
                        source,
                        positional ?? (arguments.TryGetValue("Member", out var member) ? member : null));
                    if (staticExpression is not null)
                    {
                        _current.Line($"{targetExpression}.SetValue({dependencyProperty}, {staticExpression});");
                    }

                    return;

                default:
                    Report(
                        Diagnostics.UnsupportedConstruct,
                        source,
                        $"Markup extension '{extension}' is not supported on a visual attached property.");
                    return;
            }
        }

        var valueExpression = valueType is null
            ? $"{SyntaxHelpers.Fqn("XamlRuntime")}.ConvertValue({SyntaxHelpers.Quote(UnescapeMarkup(rawValue))}, {dependencyProperty}.PropertyType)"
            : CreateLiteralExpression(valueType, rawValue, source);
        if (valueExpression is null)
        {
            return;
        }

        _current.Line($"{targetExpression}.SetValue({dependencyProperty}, {valueExpression});");
    }

    private static string UnescapeMarkup(string value)
        => value.StartsWith("{}", StringComparison.Ordinal) ? value.Substring(2) : value;

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

                _current.Line(
                    $"{actionVariable}.{property.Name} = {VisualFqn("VisualResourceLookup")}"
                    + $".Get<{SyntaxHelpers.TypeName(property.Type)}>(this, {SyntaxHelpers.Quote(key!)});");
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

        _current.Line($"{targetExpression}.{eventSymbol.Name} += this.{handler};");
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
        string ownerExpression)
    {
        var fullName = propertyElement.Name.LocalName;
        var dot = fullName.LastIndexOf('.');
        var memberName = dot >= 0 ? fullName.Substring(dot + 1) : fullName;
        var prefixOwner = dot > 0 ? fullName.Substring(0, dot) : null;
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

        // ItemsPanelTemplate 同样没有公共无参构造：把内层的面板编成工厂再交给模板对象。
        var itemsPanelProperty = isVisual && memberName == "ItemsPanel"
            ? FindProperty(ownerType, memberName)
            : null;
        if (itemsPanelProperty?.Type.Name == "ItemsPanelTemplate")
        {
            EmitVisualItemsPanelTemplate(propertyElement, ownerExpression, memberName);
            return;
        }

        if (memberName == "Controls")
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                propertyElement,
                "A visual element adds children with 'Add'; a Controls property element is not supported.");
            return;
        }

        if (memberName == "Triggers")
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                propertyElement,
                "A visual element has no Triggers collection; declare triggers inside a Style or a ControlTemplate.");
            return;
        }

        var property = FindProperty(ownerType, memberName);
        if (property is null)
        {
            // S4：<Grid.Row>1</Grid.Row> 这类属性元素指向附加属性，不是元素自身的成员。
            if (isVisual
                && prefixOwner is not null
                && TryResolveVisualAttachedProperty(fullName, propertyElement, out var attachedProperty, out var attachedValueType))
            {
                EmitPropertyElementAttachedValue(propertyElement, ownerExpression, attachedProperty, attachedValueType);
                return;
            }

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

                var childExpression = EmitXamlObject(child, childType);
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

            var contentExpression = EmitXamlObject(content, contentType);
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

    // 附加属性属性元素的值：子元素按对象生成，纯文本按 DP 值类型或标记扩展处理。
    private void EmitPropertyElementAttachedValue(
        XElement propertyElement,
        string ownerExpression,
        string dependencyProperty,
        ITypeSymbol? valueType)
    {
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

            var contentExpression = EmitXamlObject(content, contentType);
            if (contentExpression is not null)
            {
                _current.Line($"{ownerExpression}.SetValue({dependencyProperty}, {contentExpression});");
            }

            return;
        }

        EmitVisualAttachedValue(propertyElement, ownerExpression, dependencyProperty, valueType, propertyElement.Value);
    }

    private void EmitResources(XElement propertyElement, string ownerExpression, ITypeSymbol ownerType)
    {
        if (IsVisualElement(ownerType))
        {
            EmitVisualResources(propertyElement, ownerExpression, ownerType);
            return;
        }

        // 视觉后端是唯一的 XAML 后端：非视觉对象没有资源字典。
        Report(
            Diagnostics.UnsupportedResource,
            propertyElement,
            $"'{ownerType.Name}' is not a visual element and cannot own resources.");
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

            if (localName == "ItemsPanelTemplate")
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    Report(Diagnostics.UnsupportedResource, resource, "Every ItemsPanelTemplate resource needs an x:Key.");
                    continue;
                }

                var panelTemplate = EmitVisualItemsPanelTemplateResource(resource, key!);
                if (panelTemplate is not null)
                {
                    _current.Line($"{ownerExpression}.Resources[{SyntaxHelpers.Quote(key!)}] = {panelTemplate};");
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

        var previousAnchor = _resourceAnchor;
        _resourceAnchor = ownerExpression;
        try
        {
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
                    case "Setter":
                    case "VisualSetter":
                        // WPF 允许 <Style><Setter/></Style>：Setters 是 Style 的默认内容属性。
                        EmitVisualSetter(child, targetType, styleVariable);
                        break;
                    case "Trigger":
                    case "VisualTrigger":
                        EmitVisualTrigger(child, targetType, styleVariable);
                        break;
                    default:
                        Report(
                            Diagnostics.UnsupportedConstruct,
                            child,
                            $"'{child.Name.LocalName}' is not a supported Style member.");
                        break;
                }
            }
        }
        finally
        {
            _resourceAnchor = previousAnchor;
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
                Report(
                    Diagnostics.UnsupportedConstruct,
                    setterElement,
                    "The Setters collection only accepts Setter elements.");
                continue;
            }

            EmitVisualSetter(setterElement, targetType, ownerVariable);
        }
    }

    private void EmitVisualSetter(XElement setterElement, ITypeSymbol? targetType, string ownerVariable)
    {
        if (!TryResolveVisualProperty(setterElement, targetType, out var property))
        {
            return;
        }

        if (!TryGetDependencyPropertyReference(property, out var dependencyProperty))
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                setterElement,
                $"'{property.Name}' is not a dependency property and cannot be styled on the visual backend.");
            return;
        }

        var initializer = string.Empty;
        var targetName = setterElement.Attribute("TargetName")?.Value;
        if (!string.IsNullOrWhiteSpace(targetName))
        {
            // WPF 形状：TargetName 在 Setter 上，指向模板部件。
            initializer = $"TargetName = {SyntaxHelpers.Quote(targetName!)}, ";
        }

        var valueAttribute = setterElement.Attribute("Value");
        string? assignment;
        if (valueAttribute is null)
        {
            assignment = "Value = null";
        }
        else if (TryParseMarkup(valueAttribute.Value, out var extension, out var arguments, out var positional))
        {
            switch (extension)
            {
                case "x:Null":
                case "Null":
                    assignment = "Value = null";
                    break;

                case "Binding":
                case "TemplateBinding":
                    var bindingExpression = BuildBindingExpression(
                        valueAttribute,
                        property.Type,
                        arguments,
                        positional,
                        templateBinding: extension == "TemplateBinding",
                        allowTwoWay: true);
                    if (bindingExpression is null)
                    {
                        return;
                    }

                    assignment = $"Binding = {bindingExpression}";
                    break;

                case "StaticResource":
                    var staticKey = arguments.TryGetValue("Key", out var explicitKey) ? explicitKey : positional;
                    if (string.IsNullOrEmpty(staticKey))
                    {
                        Report(Diagnostics.UnsupportedResource, valueAttribute, "A StaticResource reference needs a key.");
                        return;
                    }

                    assignment =
                        $"Value = {VisualFqn("VisualResourceLookup")}.Get<{SyntaxHelpers.TypeName(property.Type)}>"
                        + $"({_resourceAnchor}, {SyntaxHelpers.Quote(staticKey!)})";
                    break;

                case "x:Static":
                    var staticExpression = CreateStaticExpression(
                        valueAttribute,
                        positional ?? (arguments.TryGetValue("Member", out var member) ? member : null));
                    if (staticExpression is null)
                    {
                        return;
                    }

                    assignment = $"Value = {staticExpression}";
                    break;

                case "DynamicResource":
                case "x:DynamicResource":
                    Report(
                        Diagnostics.UnsupportedConstruct,
                        valueAttribute,
                        "A DynamicResource value in a visual setter is not supported; the setter value is resolved once when the style is declared.");
                    return;

                default:
                    Report(
                        Diagnostics.UnsupportedConstruct,
                        valueAttribute,
                        $"Markup extension '{extension}' is not supported in a visual setter value.");
                    return;
            }
        }
        else
        {
            var literal = CreateLiteralExpression(property.Type, valueAttribute.Value, valueAttribute);
            if (literal is null)
            {
                return;
            }

            assignment = $"Value = {literal}";
        }

        _current.Line(
            $"{ownerVariable}.Setters.Add(new {VisualFqn("VisualSetter")}({dependencyProperty}) {{ {initializer}{assignment} }});");
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
            switch (ChildMemberName(triggerElement))
            {
                case "Trigger":
                case "VisualTrigger":
                    EmitVisualTrigger(triggerElement, targetType, ownerVariable);
                    break;

                case "DataTrigger":
                case "MultiTrigger":
                    Report(
                        Diagnostics.UnsupportedConstruct,
                        triggerElement,
                        $"{triggerElement.Name.LocalName} is not supported on the visual backend; "
                        + "VisualTrigger compares one dependency property of the styled element and DataTrigger/MultiTrigger need a data-context trigger engine.");
                    break;

                default:
                    Report(
                        Diagnostics.UnsupportedConstruct,
                        triggerElement,
                        $"'{triggerElement.Name.LocalName}' is not a supported trigger element.");
                    break;
            }
        }
    }

    private void EmitVisualTrigger(XElement triggerElement, ITypeSymbol? targetType, string ownerVariable)
    {
        if (!TryResolveVisualProperty(triggerElement, targetType, out var property))
        {
            return;
        }

        if (!TryGetDependencyPropertyReference(property, out var dependencyProperty))
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                triggerElement,
                $"'{property.Name}' is not a dependency property and cannot drive a visual trigger.");
            return;
        }

        var valueExpression = "null";
        var valueAttribute = triggerElement.Attribute("Value");
        if (valueAttribute is not null)
        {
            valueExpression = CreateVisualSetterValue(valueAttribute, property);
            if (valueExpression is null)
            {
                return;
            }
        }

        // 触发器级 TargetName 保留为整个触发器的默认目标；每条 Setter 自己的 TargetName 优先。
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
            expression = EmitControl(content, type);
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
        if (_templateDepth > 0)
        {
            Report(Diagnostics.UnsupportedResource, templateElement, "Nested visual DataTemplates are not supported.");
            return null;
        }

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

        _helpers.Open($"private {VisualFqn("VisualElement")} {methodName}({VisualFqn("VisualNameScope")} __xamlScope)");
        var previous = _current;
        var previousScope = _scopeExpression;
        _current = _helpers;
        _templateDepth++;
        _scopeExpression = "__xamlScope";
        string expression;
        try
        {
            expression = EmitControl(content, type);
        }
        finally
        {
            _templateDepth--;
            _scopeExpression = previousScope;
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
        if (_templateDepth > 0)
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                templateElement,
                $"A nested visual {propertyName} is not supported.");
            return;
        }

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

        _helpers.Open($"private {VisualFqn("VisualElement")} {methodName}({VisualFqn("VisualNameScope")} __xamlScope)");
        var previous = _current;
        var previousScope = _scopeExpression;
        _current = _helpers;
        _templateDepth++;
        _scopeExpression = "__xamlScope";
        string expression;
        try
        {
            expression = EmitControl(content, type);
        }
        finally
        {
            _templateDepth--;
            _scopeExpression = previousScope;
            _current = previous;
        }

        _helpers.Line($"return {expression};");
        _helpers.Close();

        _current.Line(
            $"{ownerExpression}.{propertyName} = new "
            + $"{VisualFqn("DataTemplate")}(this.{methodName});");
    }

    // ItemsPanelTemplate 的模板体必须是一个 Panel；与 DataTemplate 一样编成私有工厂。
    private void EmitVisualItemsPanelTemplate(XElement propertyElement, string ownerExpression, string propertyName)
    {
        var wrapper = propertyElement.Elements().FirstOrDefault(child => ChildMemberName(child) == "ItemsPanelTemplate");
        var templateRoot = wrapper ?? propertyElement;
        var expression = EmitVisualItemsPanelTemplateCore(templateRoot, propertyName);
        if (expression is not null)
        {
            _current.Line($"{ownerExpression}.{propertyName} = {expression};");
        }
    }

    private string? EmitVisualItemsPanelTemplateResource(XElement templateElement, string key)
        => EmitVisualItemsPanelTemplateCore(templateElement, key);

    private string? EmitVisualItemsPanelTemplateCore(XElement templateElement, string nameHint)
    {
        if (_templateDepth > 0)
        {
            Report(Diagnostics.UnsupportedConstruct, templateElement, "Nested visual ItemsPanelTemplates are not supported.");
            return null;
        }

        var content = templateElement.Elements().FirstOrDefault(child => !IsPropertyElement(child));
        if (content is null)
        {
            Report(Diagnostics.UnsupportedConstruct, templateElement, "An ItemsPanelTemplate needs one root Panel.");
            return null;
        }

        var type = ResolveElementType(content);
        if (type is null || !SyntaxHelpers.DerivesFrom(type, "VeloxDev.WinForms.Xaml.Visuals.Controls.Panel"))
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                content,
                "An ItemsPanelTemplate root must be a VeloxDev.WinForms.Xaml.Visuals.Controls.Panel.");
            return null;
        }

        var methodName = "CreateVisualItemsPanel_"
            + SyntaxHelpers.SanitizeIdentifier(nameHint) + "_" + _templateNames.Count.ToString(CultureInfo.InvariantCulture);
        if (!_templateNames.Add(methodName))
        {
            Report(Diagnostics.DuplicateName, templateElement, nameHint, _owner.Name);
            return null;
        }

        _helpers.Open(
            $"private global::VeloxDev.WinForms.Xaml.Visuals.Controls.Panel {methodName}({VisualFqn("VisualNameScope")} __xamlScope)");
        var previous = _current;
        var previousScope = _scopeExpression;
        _current = _helpers;
        _templateDepth++;
        _scopeExpression = "__xamlScope";
        string expression;
        try
        {
            expression = EmitControl(content, type);
        }
        finally
        {
            _templateDepth--;
            _scopeExpression = previousScope;
            _current = previous;
        }

        _helpers.Line($"return {expression};");
        _helpers.Close();

        return $"new global::VeloxDev.WinForms.Xaml.Visuals.Controls.ItemsPanelTemplate(__scope => this.{methodName}(__scope))";
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

        // Generated initializers assign attributes before the element is parented; ApplyStatic retries once the chain
        // is complete, so a style declared on this element or any ancestor resolves, not only one on the view root.
        _current.Line(
            $"{VisualFqn("VisualResourceLookup")}.ApplyStatic<{VisualFqn("VisualStyle")}>({targetExpression}, "
            + $"{SyntaxHelpers.Quote(key)}, __v => {targetExpression}.Style = __v);");
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
                    $"{VisualFqn("VisualResourceLookup")}.ApplyStatic<{SyntaxHelpers.TypeName(property.Type)}>({targetExpression}, "
                    + $"{SyntaxHelpers.Quote(key!)}, __v => {targetExpression}.{property.Name} = __v);");
                return;

            case "DynamicResource":
            case "x:DynamicResource":
                var dynamicKey = arguments.TryGetValue("Key", out var explicitDynamicKey) ? explicitDynamicKey : positional;
                if (string.IsNullOrEmpty(dynamicKey))
                {
                    Report(Diagnostics.UnsupportedResource, attribute, "A DynamicResource reference needs a key.");
                    return;
                }

                // DP 目标在资源被移除时清回默认值；CLR 属性没有可清除的层，只停止更新。
                var missing = TryGetDependencyPropertyReference(property, out var dynamicProperty)
                    ? $", () => {targetExpression}.ClearValue({dynamicProperty})"
                    : string.Empty;
                _current.Line(
                    $"{VisualFqn("VisualResourceLookup")}.BindDynamic<{SyntaxHelpers.TypeName(property.Type)}>({targetExpression}, "
                    + $"{SyntaxHelpers.Quote(dynamicKey!)}, __v => {targetExpression}.{property.Name} = __v{missing});");
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

            var objectExpression = EmitXamlObject(resource, resourceType);
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
            ApplyAttributes(resource, resourceType, variable);
            expression = variable;
            return true;
        }

        Report(Diagnostics.UnsupportedValue, resource, resource.Value, resourceType.ToDisplayString());
        return false;
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
