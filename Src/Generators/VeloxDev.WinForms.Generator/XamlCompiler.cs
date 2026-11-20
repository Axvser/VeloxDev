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
/// <item>Native WinForms controls and types resolved through a <c>clr-namespace:</c> prefix.</item>
/// <item>Direct child controls added to <c>Controls</c>.</item>
/// <item>Property elements for scalar values, <c>Resources</c>, <c>Controls</c> and list properties.</item>
/// <item>String, numeric, boolean, enum, color, padding, thickness, size, point and time-span literals.</item>
/// <item><c>{Binding Path, Mode}</c> and <c>{StaticResource Key}</c>.</item>
/// <item><c>DataTemplate</c> resources whose root is a <see cref="Control"/>.</item>
/// </list>
/// Anything else is reported as a diagnostic and skipped; there is no runtime XAML parser to fall back to.
/// </remarks>
internal sealed class XamlCompiler
{
    private static readonly Dictionary<string, string> PresentationTypes = new(StringComparer.Ordinal)
    {
        ["Control"] = "System.Windows.Forms.Control",
        ["UserControl"] = "System.Windows.Forms.UserControl",
        ["Form"] = "System.Windows.Forms.Form",
        ["Panel"] = "System.Windows.Forms.Panel",
        ["ScrollableControl"] = "System.Windows.Forms.ScrollableControl",
        ["FlowLayoutPanel"] = "System.Windows.Forms.FlowLayoutPanel",
        ["TableLayoutPanel"] = "System.Windows.Forms.TableLayoutPanel",
        ["GroupBox"] = "System.Windows.Forms.GroupBox",
        ["SplitContainer"] = "System.Windows.Forms.SplitContainer",
        ["TabControl"] = "System.Windows.Forms.TabControl",
        ["Button"] = "System.Windows.Forms.Button",
        ["Label"] = "System.Windows.Forms.Label",
        ["LinkLabel"] = "System.Windows.Forms.LinkLabel",
        ["TextBox"] = "System.Windows.Forms.TextBox",
        ["RichTextBox"] = "System.Windows.Forms.RichTextBox",
        ["CheckBox"] = "System.Windows.Forms.CheckBox",
        ["RadioButton"] = "System.Windows.Forms.RadioButton",
        ["PictureBox"] = "System.Windows.Forms.PictureBox",
        ["ProgressBar"] = "System.Windows.Forms.ProgressBar",
        ["ListBox"] = "System.Windows.Forms.ListBox",
        ["ComboBox"] = "System.Windows.Forms.ComboBox",
        ["NumericUpDown"] = "System.Windows.Forms.NumericUpDown",
        ["TrackBar"] = "System.Windows.Forms.TrackBar",
    };

    private readonly SourceProductionContext _production;
    private readonly string _filePath;
    private readonly string? _text;
    private readonly Compilation _compilation;

    private readonly CodeBuilder _initialize = new();
    private readonly CodeBuilder _helpers = new();
    private readonly CodeBuilder _fields = new();
    private readonly HashSet<string> _declaredNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _templateNames = new(StringComparer.Ordinal);
    private readonly HashSet<XObject> _ambiguousSources = [];
    private readonly List<string> _deferredEvents = [];

    private CodeBuilder _current;
    private INamedTypeSymbol _owner = null!;
    private string? _rootScope;
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

        if (!SyntaxHelpers.DerivesFrom(owner, SyntaxHelpers.ControlMetadataName))
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
        var host = IsXamlControl(_owner) ? "this" : null;
        _rootScope = host;

        _current.Line("this.SuspendLayout();");

        foreach (var element in root.Elements())
        {
            if (IsResourcesProperty(element))
            {
                EmitResources(element, "this", _owner);
            }
        }

        ApplyAttributes(root, _owner, "this", host);
        ProcessChildren(root, _owner, "this", host);

        foreach (var line in _deferredEvents)
        {
            _current.Line(line);
        }

        _deferredEvents.Clear();
        _current.Line("this.ResumeLayout(false);");
    }

    private void ProcessChildren(XElement element, ITypeSymbol elementType, string elementExpression, string? hostExpression)
    {
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
                if (!IsAmbiguous(child))
                {
                    Report(Diagnostics.UnknownType, child, child.Name.LocalName);
                }

                continue;
            }

            if (!SyntaxHelpers.DerivesFrom(type, SyntaxHelpers.ControlMetadataName))
            {
                Report(Diagnostics.UnknownType, child, child.Name.LocalName);
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

        foreach (var child in element.Elements())
        {
            if (IsResourcesProperty(child))
            {
                EmitResources(child, expression, type);
            }
        }

        var childHost = IsXamlControl(type) ? expression : hostExpression;
        ApplyAttributes(element, type, expression, childHost);
        ProcessChildren(element, type, expression, childHost);
        EmitTextContent(element, type, expression);
        return expression;
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
            if (memberName == "Style"
                && !IsXamlControl(type)
                && SyntaxHelpers.DerivesFrom(type, SyntaxHelpers.ControlMetadataName))
            {
                EmitNativeStyleAttribute(attribute, targetExpression);
                continue;
            }

            if (memberName.IndexOf('.') >= 0)
            {
                EmitAttachedAttribute(attribute, targetExpression, hostExpression);
                continue;
            }

            var property = FindProperty(type, memberName);
            if (property is not null)
            {
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

                case "StaticResource":
                    EmitStaticResource(source, targetExpression, property, arguments, positional, hostExpression);
                    return;

                default:
                    Report(Diagnostics.UnsupportedConstruct, source, $"Markup extension '{extension}' is not supported.");
                    return;
            }
        }

        if (!LiteralFactory.TryCreate(property.Type, rawValue, out var expression, out _))
        {
            Report(Diagnostics.UnsupportedValue, source, rawValue, property.Type.ToDisplayString());
            return;
        }

        _current.Line($"{targetExpression}.{property.Name} = {expression};");
    }

    private void EmitBinding(
        XObject source,
        string targetExpression,
        IPropertySymbol property,
        Dictionary<string, string> arguments,
        string? positional,
        string? hostExpression)
    {
        var path = arguments.TryGetValue("Path", out var explicitPath) ? explicitPath : positional ?? string.Empty;
        var mode = arguments.TryGetValue("Mode", out var explicitMode) ? explicitMode : "OneWay";

        if (mode is not ("OneWay" or "TwoWay" or "OneTime"))
        {
            Report(Diagnostics.UnsupportedValue, source, mode, property.Type.ToDisplayString());
            return;
        }

        var bindingExpression = $"new {SyntaxHelpers.Fqn("Binding")}({SyntaxHelpers.Quote(path)})";
        if (mode != "OneWay")
        {
            bindingExpression += $" {{ Mode = {SyntaxHelpers.Fqn("BindingMode")}.{mode} }}";
        }

        if (TryGetDependencyPropertyReference(property, out var dependencyProperty))
        {
            _current.Line($"{targetExpression}.SetBinding({dependencyProperty}, {bindingExpression});");
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

    private void EmitAttachedAttribute(XAttribute attribute, string targetExpression, string? hostExpression)
    {
        var localName = attribute.Name.LocalName;
        var dot = localName.IndexOf('.');
        if (dot <= 0 || dot == localName.Length - 1 || localName.IndexOf('.', dot + 1) >= 0)
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                attribute,
                $"Attached XAML attribute '{localName}' must be written as 'prefix:Owner.Property'.");
            return;
        }

        var clrNamespace = GetClrNamespace(attribute.Name.NamespaceName);
        if (clrNamespace is null
            && attribute.Parent is XElement owner
            && GetPrefixForNamespace(owner, attribute.Name.NamespaceName) == "v")
        {
            clrNamespace = SyntaxHelpers.RuntimeNamespace;
        }

        if (clrNamespace is null)
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                attribute,
                "An attached XAML attribute needs a clr-namespace prefix (for example xmlns:local=\"clr-namespace:My.Namespace\").");
            return;
        }

        var ownerName = localName.Substring(0, dot);
        var propertyName = localName.Substring(dot + 1);
        var ownerType = string.Equals(clrNamespace, SyntaxHelpers.RuntimeNamespace, StringComparison.Ordinal)
            ? ResolveAdapterType(ownerName, attribute)
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

    private void EmitAttachedBinding(
        XObject source,
        string targetExpression,
        string dependencyProperty,
        Dictionary<string, string> arguments,
        string? positional,
        string? hostExpression)
    {
        var path = arguments.TryGetValue("Path", out var explicitPath) ? explicitPath : positional ?? string.Empty;
        var mode = arguments.TryGetValue("Mode", out var explicitMode) ? explicitMode : "OneWay";

        if (mode is not ("OneWay" or "TwoWay" or "OneTime"))
        {
            Report(Diagnostics.UnsupportedValue, source, mode, "BindingMode");
            return;
        }

        if (mode == "TwoWay")
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                source,
                "TwoWay is not supported for attached values on native controls; the binding behaves as OneWay.");
            mode = "OneWay";
        }

        if (hostExpression is null)
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                source,
                "An attached binding needs a Control in the tree to host its data context.");
            return;
        }

        var bindingExpression = $"new {SyntaxHelpers.Fqn("Binding")}({SyntaxHelpers.Quote(path)})";
        if (mode != "OneWay")
        {
            bindingExpression += $" {{ Mode = {SyntaxHelpers.Fqn("BindingMode")}.{mode} }}";
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

        if (!TryResolveControlProperty(ownerType, propertyPath!.Trim(), setterElement, out var dependencyProperty, out var valueType))
        {
            return;
        }

        var valueAttribute = setterElement.Attribute("Value");
        var valueExpression = valueAttribute is null
            ? "null"
            : CreatePropertyValueExpression(valueAttribute, dependencyProperty, valueType, valueAttribute.Value);

        if (valueExpression is null)
        {
            return;
        }

        _current.Line(
            $"{setterCollectionExpression}.Add(new {SyntaxHelpers.Fqn("Setter")}({dependencyProperty}, {valueExpression}));");
    }

    private void EmitTriggerAction(XElement actionElement, ITypeSymbol ownerType, string triggerVariable, bool isEnter)
    {
        var actions = actionElement.Elements().ToArray();
        if (actions.Length != 1)
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                actionElement,
                "An EnterAction or ExitAction needs exactly one action element.");
            return;
        }

        var action = actions[0];
        if (action.Name.LocalName == "TransitionAction")
        {
            EmitTransitionAction(action, ownerType, triggerVariable, isEnter);
            return;
        }

        if (action.Name.LocalName == "KeyFrameAnimation")
        {
            EmitKeyFrameAnimation(action, ownerType, triggerVariable, isEnter);
            return;
        }

        EmitCustomTriggerAction(action, triggerVariable, isEnter);
    }

    private void EmitTransitionAction(XElement action, ITypeSymbol ownerType, string triggerVariable, bool isEnter)
    {
        var propertyPath = action.Attribute("Property")?.Value;
        if (string.IsNullOrWhiteSpace(propertyPath))
        {
            Report(Diagnostics.UnsupportedConstruct, action, "A TransitionAction needs a Property attribute.");
            return;
        }

        if (!TryResolveControlProperty(ownerType, propertyPath!.Trim(), action, out var dependencyProperty, out var valueType))
        {
            return;
        }

        var toAttribute = action.Attribute("To");
        var toExpression = toAttribute is null
            ? "null"
            : CreatePropertyValueExpression(toAttribute, dependencyProperty, valueType, toAttribute.Value);

        if (toExpression is null)
        {
            return;
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
                return;
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

        var effect = effectParts.Count > 0
            ? $" Effect = new global::VeloxDev.TransitionSystem.TransitionEffect {{ {string.Join(", ", effectParts)} }},"
            : string.Empty;

        var member = isEnter ? "EnterAction" : "ExitAction";
        _current.Line(
            $"{triggerVariable}.{member} = new {SyntaxHelpers.Fqn("TransitionAction")} "
            + $"{{ Property = {dependencyProperty}, To = {toExpression},{effect} }};");
    }

    private void EmitKeyFrameAnimation(XElement action, ITypeSymbol ownerType, string triggerVariable, bool isEnter)
    {
        var propertyPath = action.Attribute("Property")?.Value;
        if (string.IsNullOrWhiteSpace(propertyPath))
        {
            Report(Diagnostics.UnsupportedConstruct, action, "A KeyFrameAnimation needs a Property attribute.");
            return;
        }

        if (!TryResolveControlProperty(ownerType, propertyPath!.Trim(), action, out var dependencyProperty, out var valueType))
        {
            return;
        }

        var effectParts = new List<string>();
        var durationAttribute = action.Attribute("Duration");
        if (durationAttribute is not null)
        {
            if (!TimeSpan.TryParse(durationAttribute.Value, CultureInfo.InvariantCulture, out _))
            {
                Report(Diagnostics.UnsupportedValue, durationAttribute, durationAttribute.Value, "System.TimeSpan");
                return;
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

        var frames = action.Elements().Where(child => child.Name.LocalName == "KeyFrame").ToArray();
        if (frames.Length == 0)
        {
            Report(Diagnostics.UnsupportedConstruct, action, "A KeyFrameAnimation needs at least one KeyFrame element.");
            return;
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

        var member = isEnter ? "EnterAction" : "ExitAction";
        _current.Line($"{triggerVariable}.{member} = {actionVariable};");
    }


    private void EmitCustomTriggerAction(XElement action, string triggerVariable, bool isEnter)
    {
        var type = ResolveElementType(action);
        if (type is null)
        {
            if (!IsAmbiguous(action))
            {
                Report(Diagnostics.UnknownType, action, action.Name.LocalName);
            }

            return;
        }

        if (!SyntaxHelpers.DerivesFrom(type, "VeloxDev.WinForms.Xaml.TriggerAction"))
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                action,
                $"'{type.Name}' does not derive from VeloxDev.WinForms.Xaml.TriggerAction.");
            return;
        }

        if (type is not INamedTypeSymbol namedType || namedType.IsGenericType)
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                action,
                $"'{type.Name}' is not a usable trigger action type.");
            return;
        }

        if (type.IsAbstract || !HasPublicParameterlessConstructor(namedType))
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                action,
                $"'{type.Name}' needs a public parameterless constructor.");
            return;
        }

        if (action.HasElements)
        {
            Report(
                Diagnostics.UnsupportedConstruct,
                action,
                "Nested content is not supported in a trigger action.");
            return;
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

        var member = isEnter ? "EnterAction" : "ExitAction";
        _current.Line($"{triggerVariable}.{member} = {actionVariable};");
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
                $"({SyntaxHelpers.TypeName(property.Type)}){SyntaxHelpers.Fqn("XamlRuntime")}.ConvertValue({SyntaxHelpers.Quote(raw)}, typeof({SyntaxHelpers.TypeName(property.Type)}))!";
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

        if (valueType is not null && LiteralFactory.TryCreate(valueType, rawValue, out var typed, out _))
        {
            return typed;
        }

        if (valueType is not null)
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
            // （形状在 .Controls.Shapes 里），所以最后按适配器命名空间找一次。
            foreach (var adapterNamespace in new[]
                     {
                         SyntaxHelpers.ControlsNamespace,
                         SyntaxHelpers.ShapesNamespace,
                         SyntaxHelpers.RuntimeNamespace,
                     })
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
            return ResolveAdapterType(ownerName, source);
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

        if (memberName == "Resources")
        {
            EmitResources(propertyElement, ownerExpression, ownerType);
            return;
        }

        if (memberName == "Controls")
        {
            ProcessChildren(propertyElement, ownerType, ownerExpression, hostExpression);
            return;
        }

        if (memberName == "Triggers")
        {
            EmitTriggers(propertyElement, ownerType, ownerExpression);
            return;
        }

        var property = FindProperty(ownerType, memberName);
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
                    if (!IsAmbiguous(child))
                    {
                        Report(Diagnostics.UnknownType, child, child.Name.LocalName);
                    }

                    continue;
                }

                var childExpression = EmitControl(child, childType, hostExpression);
                _current.Line($"{ownerExpression}.{property.Name}.Add({childExpression});");
            }

            return;
        }

        var content = propertyElement.Elements().FirstOrDefault(child => !IsPropertyElement(child));
        if (content is not null)
        {
            var contentType = ResolveElementType(content);
            if (contentType is null)
            {
                if (!IsAmbiguous(content))
                {
                    Report(Diagnostics.UnknownType, content, content.Name.LocalName);
                }

                return;
            }

            if (!SyntaxHelpers.DerivesFrom(contentType, SyntaxHelpers.ControlMetadataName))
            {
                Report(Diagnostics.UnknownType, content, content.Name.LocalName);
                return;
            }

            var contentExpression = EmitControl(content, contentType, hostExpression);
            _current.Line($"{ownerExpression}.{property.Name} = {contentExpression};");
            return;
        }

        if (!LiteralFactory.TryCreate(property.Type, propertyElement.Value, out var literal, out _))
        {
            Report(Diagnostics.UnsupportedValue, propertyElement, propertyElement.Value, property.Type.ToDisplayString());
            return;
        }

        _current.Line($"{ownerExpression}.{property.Name} = {literal};");
    }

    private void EmitResources(XElement propertyElement, string ownerExpression, ITypeSymbol ownerType)
    {
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
        else if (!LiteralFactory.TryCreate(property.Type, valueAttribute.Value, out valueExpression, out _))
        {
            valueExpression =
                $"{SyntaxHelpers.Fqn("XamlRuntime")}.ConvertValue({SyntaxHelpers.Quote(valueAttribute.Value)}, typeof({SyntaxHelpers.TypeName(property.Type)}))";
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

            if (prefix == "v")
            {
                return ResolveAdapterType(localName, contextElement);
            }

            var uri = GetNamespaceUriForPrefix(contextElement, prefix);
            var clrNamespace = uri is null ? null : GetClrNamespace(uri);
            if (clrNamespace is not null
                && string.Equals(clrNamespace, SyntaxHelpers.RuntimeNamespace, StringComparison.Ordinal))
            {
                return ResolveAdapterType(localName, contextElement);
            }

            return clrNamespace is null
                ? null
                : _compilation.GetTypeByMetadataName(clrNamespace + "." + localName);
        }

        // A fully qualified name needs no prefix resolution.
        var qualified = _compilation.GetTypeByMetadataName(typeName);
        if (qualified is not null)
        {
            return qualified;
        }

        return ResolveUnprefixedType(typeName, contextElement);
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
            if (!IsAmbiguous(resource))
            {
                Report(Diagnostics.UnknownType, resource, resource.Name.LocalName);
            }

            return false;
        }

        if (resource.HasElements)
        {
            Report(Diagnostics.UnsupportedResource, resource, "Resource values must be scalar text in this subset.");
            return false;
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
            if (!IsAmbiguous(content))
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
            if (!IsAmbiguous(content))
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

        var methodName = "CreateControlTemplate_" + SyntaxHelpers.SanitizeIdentifier(key);
        if (!_templateNames.Add(methodName))
        {
            Report(Diagnostics.DuplicateName, template, key, _owner.Name);
            return false;
        }

        _helpers.Open($"private global::System.Windows.Forms.Control {methodName}()");
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
            $"{ownerExpression}.Resources[{SyntaxHelpers.Quote(key)}] = new {SyntaxHelpers.Fqn("ControlTemplate")}(this.{methodName});");
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

        if (uri.Length == 0 || uri == SyntaxHelpers.PresentationNamespace)
        {
            return ResolveUnprefixedType(localName, element);
        }

        // `v:` always means the adapter namespaces: controls live in `.Xaml.Controls`, every other runtime type
        // lives in `.Xaml`. This holds whether the prefix maps to `.Xaml`, `.Xaml.Controls` or a non-clr URI.
        if (GetPrefixForNamespace(element, uri) == "v")
        {
            return ResolveAdapterType(localName, element);
        }

        var clrNamespace = GetClrNamespace(uri);
        if (clrNamespace is null)
        {
            return null;
        }

        if (string.Equals(clrNamespace, SyntaxHelpers.RuntimeNamespace, StringComparison.Ordinal))
        {
            return ResolveAdapterType(localName, element);
        }

        return _compilation.GetTypeByMetadataName(clrNamespace + "." + localName);
    }

    /// <summary>
    /// Resolves a type declared by the adapter: controls in <c>.Xaml.Controls</c> first, then every other runtime
    /// type in <c>.Xaml</c>. A name declared in both is ambiguous and gets a diagnostic.
    /// </summary>
    private ITypeSymbol? ResolveAdapterType(string localName, XObject? source)
    {
        var controlType = _compilation.GetTypeByMetadataName(SyntaxHelpers.ControlsNamespace + "." + localName);
        if (controlType is not null)
        {
            return controlType;
        }

        var shapeType = _compilation.GetTypeByMetadataName(SyntaxHelpers.ShapesNamespace + "." + localName);
        if (shapeType is not null)
        {
            return shapeType;
        }

        return _compilation.GetTypeByMetadataName(SyntaxHelpers.RuntimeNamespace + "." + localName);
    }

    /// <summary>
    /// Resolves an unprefixed type name against the native WinForms presentation table only.
    /// </summary>
    /// <remarks>
    /// No prefix means native WinForms. A VeloxDev control needs <c>v:</c> and a template/Demo type needs
    /// <c>local:</c>; letting an unprefixed name fall back to either would make the same markup mean different
    /// things depending on which assemblies happen to be referenced.
    /// </remarks>
    private ITypeSymbol? ResolveUnprefixedType(string localName, XObject? source)
    {
        if (PresentationTypes.TryGetValue(localName, out var metadataName))
        {
            return _compilation.GetTypeByMetadataName(metadataName);
        }

        if (ResolveAdapterType(localName, null) is not null || ResolveLocalType(localName) is not null)
        {
            if (source is not null)
            {
                // 同一处不再补一条泛化的 UnknownType：两条诊断说的是同一件事。
                _ambiguousSources.Add(source);
            }

            Report(Diagnostics.UnprefixedTypeNotNative, source, localName);
        }

        return null;
    }

    private bool IsAmbiguous(XObject? source) => source is not null && _ambiguousSources.Contains(source);

    private ITypeSymbol? ResolveLocalType(string localName)
    {
        if (_owner.ContainingNamespace is { IsGlobalNamespace: false })
        {
            return _compilation.GetTypeByMetadataName(
                _owner.ContainingNamespace.ToDisplayString() + "." + localName);
        }

        return _compilation.GetTypeByMetadataName(localName);
    }

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

        var split = body.IndexOfAny(new[] { ' ', ',', '\t' });
        if (split < 0)
        {
            extension = body;
            return true;
        }

        extension = body.Substring(0, split).Trim();
        var rest = body.Substring(split + 1);

        foreach (var part in rest.Split(','))
        {
            var piece = part.Trim();
            if (piece.Length == 0)
            {
                continue;
            }

            var equals = piece.IndexOf('=');
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
