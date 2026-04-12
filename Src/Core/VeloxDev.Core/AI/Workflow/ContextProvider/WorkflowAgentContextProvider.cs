using System.Reflection;
using System.Text;
using System.Windows.Input;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.AI.Workflow;

public static class WorkflowAgentContextProvider
{
    private static readonly Type[] FrameworkInterfaceTypes =
    [
        typeof(IWorkflowViewModel),
        typeof(IWorkflowTreeViewModel),
        typeof(IWorkflowNodeViewModel),
        typeof(IWorkflowSlotViewModel),
        typeof(IWorkflowLinkViewModel)
    ];

    private static readonly Type[] FrameworkTemplateTypes =
    [
        typeof(TreeViewModelBase),
        typeof(NodeViewModelBase),
        typeof(SlotViewModelBase),
        typeof(LinkViewModelBase)
    ];

    public static string ProvideWorkflowAgentContextDocument(AgentLanguages language)
    {
        var builder = new StringBuilder();
        AppendOverview(builder);
        builder.AppendLine();
        builder.AppendLine(ProvideWorkflowFrameworkContext(language));
        builder.AppendLine();
        builder.AppendLine(ProvideWorkflowEnumContext(language));
        builder.AppendLine();
        builder.AppendLine(ProvideWorkflowValueTypeContext(language));
        builder.AppendLine();
        builder.AppendLine(ProvideRegisteredWorkflowComponentContext(language));
        builder.AppendLine();
        builder.Append(ProvideWorkflowOtherAnnotatedMemberContext(language));
        return builder.ToString().TrimEnd();
    }

    public static string ProvideWorkflowFrameworkContext(AgentLanguages language)
    {
        var builder = new StringBuilder();
        builder.AppendLine("## 2. Framework Interfaces and Runtime Flow");
        builder.AppendLine();
        builder.AppendLine("This section describes the built-in workflow interfaces, template component classes, and the runtime relationships that an agent should understand before operating on a workflow graph.");
        builder.AppendLine();
        AppendInterfaceDesignPlantUml(builder);
        builder.AppendLine();
        AppendRuntimeFlowPlantUml(builder);
        builder.AppendLine();
        builder.AppendLine("### 2.1 Workflow Interfaces");
        builder.AppendLine();
        AppendTypeSummaryTable(builder, FrameworkInterfaceTypes, language);
        builder.AppendLine();
        AppendAnnotatedMemberTable(builder, "#### Interface Members", FrameworkInterfaceTypes, language);
        builder.AppendLine();
        AppendCommandTable(builder, "#### Interface Commands", FrameworkInterfaceTypes, language);
        builder.AppendLine();
        AppendOtherAnnotatedMemberTable(builder, "#### Other Interface Members", FrameworkInterfaceTypes, language);
        builder.AppendLine();
        builder.AppendLine("### 2.2 Template Component Classes");
        builder.AppendLine();
        AppendTypeSummaryTable(builder, FrameworkTemplateTypes, language);
        builder.AppendLine();
        AppendAnnotatedMemberTable(builder, "#### Template Members", FrameworkTemplateTypes, language);
        builder.AppendLine();
        AppendCommandTable(builder, "#### Template Commands", FrameworkTemplateTypes, language);
        builder.AppendLine();
        AppendOtherAnnotatedMemberTable(builder, "#### Other Template Members", FrameworkTemplateTypes, language);
        return builder.ToString().TrimEnd();
    }

    public static string ProvideWorkflowEnumContext(AgentLanguages language)
    {
        var enumTypes = GetFrameworkEnumTypes();
        var builder = new StringBuilder();
        builder.AppendLine("## 3. Enum Context");
        builder.AppendLine();
        builder.AppendLine("This section collects the built-in workflow enums that carry agent-facing semantic meanings.");
        builder.AppendLine();
        builder.AppendLine("### 3.1 Enum Summary");
        builder.AppendLine();
        AppendEnumSummaryTable(builder, enumTypes, language);
        builder.AppendLine();
        builder.AppendLine("### 3.2 Enum Members");
        builder.AppendLine();
        AppendEnumMemberTable(builder, enumTypes, language);
        return builder.ToString().TrimEnd();
    }

    public static string ProvideWorkflowValueTypeContext(AgentLanguages language)
    {
        var valueTypes = GetFrameworkValueTypes();
        var builder = new StringBuilder();
        builder.AppendLine("## 4. Value Type Context");
        builder.AppendLine();
        builder.AppendLine("This section collects the built-in workflow value types that are commonly used in geometry, positioning, and sizing operations.");
        builder.AppendLine();
        builder.AppendLine("### 4.1 Value Type Summary");
        builder.AppendLine();
        AppendTypeSummaryTable(builder, valueTypes, language);
        builder.AppendLine();
        AppendAnnotatedMemberTable(builder, "### 4.2 Value Type Members", valueTypes, language);
        builder.AppendLine();
        AppendOtherAnnotatedMemberTable(builder, "### 4.3 Other Value Type Members", valueTypes, language);
        return builder.ToString().TrimEnd();
    }

    public static string ProvideRegisteredWorkflowComponentContext(AgentLanguages language)
    {
        var registeredTypes = WorkflowAgentContextRegistry.GetRegisteredWorkflowAgentContextTypes();
        var treeTypes = GetRegisteredComponentTypes(registeredTypes, WorkflowComponentRole.Tree);
        var nodeTypes = GetRegisteredComponentTypes(registeredTypes, WorkflowComponentRole.Node);
        var slotTypes = GetRegisteredComponentTypes(registeredTypes, WorkflowComponentRole.Slot);
        var linkTypes = GetRegisteredComponentTypes(registeredTypes, WorkflowComponentRole.Link);

        var builder = new StringBuilder();
        builder.AppendLine("## 5. User Component Context");
        builder.AppendLine();
        builder.AppendLine("This section collects registered user-defined workflow component classes. Register custom types before generating this section so the agent can distinguish project-specific workflow contracts from framework defaults.");
        builder.AppendLine();
        AppendComponentSection(builder, "### 5.1 Tree Components", treeTypes, language);
        builder.AppendLine();
        AppendComponentSection(builder, "### 5.2 Node Components", nodeTypes, language);
        builder.AppendLine();
        AppendComponentSection(builder, "### 5.3 Slot Components", slotTypes, language);
        builder.AppendLine();
        AppendComponentSection(builder, "### 5.4 Link Components", linkTypes, language);
        return builder.ToString().TrimEnd();
    }

    public static string ProvideWorkflowOtherAnnotatedMemberContext(AgentLanguages language)
    {
        var relevantTypes = GetDocumentRelevantTypes();
        var builder = new StringBuilder();
        builder.AppendLine("## 6. Other Annotated Members");
        builder.AppendLine();
        builder.AppendLine("This section collects remaining agent-annotated members that are not already covered by the main workflow component, enum, or value type tables.");
        builder.AppendLine();
        AppendOtherAnnotatedMemberTable(builder, null, relevantTypes, language);
        return builder.ToString().TrimEnd();
    }

    public static string ProvideTypeAgentContext(this Type type, AgentLanguages language)
    {
        if (type is null)
        {
            throw new ArgumentNullException(nameof(type));
        }

        if (type.IsEnum)
        {
            return type.ProvideEnumAgentContext(language);
        }

        if (type.IsClass)
        {
            return type.ProvideClassAgentContext(language);
        }

        return CreateStructuredTypeContext(type, language, "Type");
    }

    public static string ProvideEnumAgentContext(this Type type, AgentLanguages language)
    {
        if (type is null)
        {
            throw new ArgumentNullException(nameof(type));
        }

        if (!type.IsEnum)
        {
            throw new ArgumentException("The provided type must be an enum.", nameof(type));
        }

        var builder = new StringBuilder();
        builder.AppendLine($"# Enum Agent Context: {FormatCode(type.FullName ?? type.Name)}");
        builder.AppendLine();
        builder.AppendLine("This document describes the enum-level and enum-member-level agent context for the specified type.");
        builder.AppendLine();
        builder.AppendLine("## Enum Summary");
        builder.AppendLine();
        AppendEnumSummaryTable(builder, [type], language);
        builder.AppendLine();
        builder.AppendLine("## Enum Members");
        builder.AppendLine();
        AppendEnumMemberTable(builder, [type], language);
        return builder.ToString().TrimEnd();
    }

    public static string ProvideClassAgentContext(this Type type, AgentLanguages language)
    {
        if (type is null)
        {
            throw new ArgumentNullException(nameof(type));
        }

        if (!type.IsClass)
        {
            throw new ArgumentException("The provided type must be a class.", nameof(type));
        }

        return CreateStructuredTypeContext(type, language, "Class");
    }

    internal static string ProvideTypeAgentContext(string typeName, AgentLanguages language)
    {
        var type = ResolveType(typeName);
        return type.ProvideTypeAgentContext(language);
    }

    internal static string ProvideRegisteredWorkflowComponentTypeList()
    {
        var registeredTypes = WorkflowAgentContextRegistry.GetRegisteredWorkflowAgentContextTypes();
        var rows = registeredTypes
            .Select(type => new[]
            {
                EscapeMarkdownCell(GetComponentRole(type)?.ToString() ?? "Other"),
                FormatCode(type.FullName ?? type.Name)
            })
            .ToList();

        var builder = new StringBuilder();
        builder.AppendLine("## Registered Workflow Component Types");
        builder.AppendLine();
        builder.AppendLine("| Role | TypeFullName |");
        builder.AppendLine("|---|---|");
        if (rows.Count == 0)
        {
            builder.AppendLine("| N/A | N/A |");
        }
        else
        {
            foreach (var row in rows)
            {
                builder.AppendLine($"| {row[0]} | {row[1]} |");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendOverview(StringBuilder builder)
    {
        builder.AppendLine("# Workflow Agent Context Document");
        builder.AppendLine();
        builder.AppendLine("## 1. Overview");
        builder.AppendLine();
        builder.AppendLine("This document describes the workflow interfaces, template components, built-in enums, value types, registered user components, and other annotated members that an agent may rely on when reasoning about the VeloxDev workflow framework.");
        builder.AppendLine();
        builder.AppendLine("Treat the names and contexts shown below as the authoritative source for workflow semantics. Reuse the exact type names and member names shown in the tables whenever you describe, inspect, or operate on workflow components.");
    }

    private static void AppendInterfaceDesignPlantUml(StringBuilder builder)
    {
        builder.AppendLine("### 2.1 Interface Design");
        builder.AppendLine();
        builder.AppendLine("```plantuml");
        builder.AppendLine("@startuml");
        builder.AppendLine("skinparam monochrome true");
        builder.AppendLine("interface IWorkflowViewModel");
        builder.AppendLine("interface IWorkflowTreeViewModel");
        builder.AppendLine("interface IWorkflowNodeViewModel");
        builder.AppendLine("interface IWorkflowSlotViewModel");
        builder.AppendLine("interface IWorkflowLinkViewModel");
        builder.AppendLine("interface IWorkflowTreeViewModelHelper");
        builder.AppendLine("interface IWorkflowNodeViewModelHelper");
        builder.AppendLine("interface IWorkflowSlotViewModelHelper");
        builder.AppendLine("interface IWorkflowLinkViewModelHelper");
        builder.AppendLine("class TreeViewModelBase");
        builder.AppendLine("class NodeViewModelBase");
        builder.AppendLine("class SlotViewModelBase");
        builder.AppendLine("class LinkViewModelBase");
        builder.AppendLine("IWorkflowViewModel <|-- IWorkflowTreeViewModel");
        builder.AppendLine("IWorkflowViewModel <|-- IWorkflowNodeViewModel");
        builder.AppendLine("IWorkflowViewModel <|-- IWorkflowSlotViewModel");
        builder.AppendLine("IWorkflowViewModel <|-- IWorkflowLinkViewModel");
        builder.AppendLine("TreeViewModelBase ..|> IWorkflowTreeViewModel");
        builder.AppendLine("NodeViewModelBase ..|> IWorkflowNodeViewModel");
        builder.AppendLine("SlotViewModelBase ..|> IWorkflowSlotViewModel");
        builder.AppendLine("LinkViewModelBase ..|> IWorkflowLinkViewModel");
        builder.AppendLine("IWorkflowTreeViewModel --> IWorkflowTreeViewModelHelper : GetHelper / SetHelper");
        builder.AppendLine("IWorkflowNodeViewModel --> IWorkflowNodeViewModelHelper : GetHelper / SetHelper");
        builder.AppendLine("IWorkflowSlotViewModel --> IWorkflowSlotViewModelHelper : GetHelper / SetHelper");
        builder.AppendLine("IWorkflowLinkViewModel --> IWorkflowLinkViewModelHelper : GetHelper / SetHelper");
        builder.AppendLine("@enduml");
        builder.AppendLine("```");
    }

    private static void AppendRuntimeFlowPlantUml(StringBuilder builder)
    {
        builder.AppendLine("### 2.2 Runtime Flow");
        builder.AppendLine();
        builder.AppendLine("```plantuml");
        builder.AppendLine("@startuml");
        builder.AppendLine("skinparam monochrome true");
        builder.AppendLine("class Tree");
        builder.AppendLine("class Node");
        builder.AppendLine("class Slot");
        builder.AppendLine("class Link");
        builder.AppendLine("Tree \"1\" *-- \"many\" Node : Nodes");
        builder.AppendLine("Tree \"1\" *-- \"many\" Link : Links");
        builder.AppendLine("Tree \"1\" o-- \"1\" Link : VirtualLink");
        builder.AppendLine("Node \"1\" *-- \"many\" Slot : Slots");
        builder.AppendLine("Slot \"1\" --> \"many\" Slot : Targets");
        builder.AppendLine("Slot \"1\" <-- \"many\" Slot : Sources");
        builder.AppendLine("Link --> Slot : Sender");
        builder.AppendLine("Link --> Slot : Receiver");
        builder.AppendLine("Node ..> Slot : Create / Broadcast / Work");
        builder.AppendLine("Tree ..> Slot : Validate / Connect");
        builder.AppendLine("Tree ..> Link : Reset / Preview / Undo / Redo");
        builder.AppendLine("@enduml");
        builder.AppendLine("```");
    }

    private static void AppendComponentSection(StringBuilder builder, string title, IReadOnlyList<Type> types, AgentLanguages language)
    {
        builder.AppendLine(title);
        builder.AppendLine();
        AppendTypeSummaryTable(builder, types, language);
        builder.AppendLine();
        AppendAnnotatedMemberTable(builder, "#### Members", types, language);
        builder.AppendLine();
        AppendCommandTable(builder, "#### Commands", types, language);
        builder.AppendLine();
        AppendOtherAnnotatedMemberTable(builder, "#### Other Annotated Members", types, language);
    }

    private static void AppendTypeSummaryTable(StringBuilder builder, IReadOnlyList<Type> types, AgentLanguages language)
    {
        builder.AppendLine("| TypeFullName | Kind | Implements | Context |");
        builder.AppendLine("|---|---|---|---|");

        if (types.Count == 0)
        {
            builder.AppendLine("| N/A | N/A | N/A | N/A |");
            return;
        }

        foreach (var type in types.OrderBy(static item => item.FullName, StringComparer.Ordinal))
        {
            var kind = GetTypeKind(type);
            var implements = GetImplementedContractText(type);
            var context = GetAgentContext(type, language);
            builder.AppendLine($"| {FormatCode(type.FullName ?? type.Name)} | {EscapeMarkdownCell(kind)} | {EscapeMarkdownCell(implements)} | {EscapeMarkdownCell(context)} |");
        }
    }

    private static void AppendAnnotatedMemberTable(StringBuilder builder, string? title, IReadOnlyList<Type> types, AgentLanguages language)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            builder.AppendLine(title);
            builder.AppendLine();
        }

        builder.AppendLine("| OwnerTypeFullName | Name | SourceKind | ValueTypeFullName | Context |");
        builder.AppendLine("|---|---|---|---|---|");

        var rows = types
            .SelectMany(type => GetAnnotatedValueMembers(type, language))
            .OrderBy(static row => row.OwnerTypeFullName, StringComparer.Ordinal)
            .ThenBy(static row => row.Name, StringComparer.Ordinal)
            .ToList();

        if (rows.Count == 0)
        {
            builder.AppendLine("| N/A | N/A | N/A | N/A | N/A |");
            return;
        }

        foreach (var row in rows)
        {
            builder.AppendLine($"| {FormatCode(row.OwnerTypeFullName)} | {FormatCode(row.Name)} | {EscapeMarkdownCell(row.SourceKind)} | {FormatCode(row.ValueTypeFullName)} | {EscapeMarkdownCell(row.Context)} |");
        }
    }

    private static void AppendCommandTable(StringBuilder builder, string title, IReadOnlyList<Type> types, AgentLanguages language)
    {
        builder.AppendLine(title);
        builder.AppendLine();
        builder.AppendLine("| OwnerTypeFullName | Name | ValueTypeFullName | Context |");
        builder.AppendLine("|---|---|---|---|");

        var rows = types
            .SelectMany(type => GetAnnotatedCommandMembers(type, language))
            .OrderBy(static row => row.OwnerTypeFullName, StringComparer.Ordinal)
            .ThenBy(static row => row.Name, StringComparer.Ordinal)
            .ToList();

        if (rows.Count == 0)
        {
            builder.AppendLine("| N/A | N/A | N/A | N/A |");
            return;
        }

        foreach (var row in rows)
        {
            builder.AppendLine($"| {FormatCode(row.OwnerTypeFullName)} | {FormatCode(row.Name)} | {FormatCode(row.ValueTypeFullName)} | {EscapeMarkdownCell(row.Context)} |");
        }
    }

    private static void AppendOtherAnnotatedMemberTable(StringBuilder builder, string? title, IReadOnlyList<Type> types, AgentLanguages language)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            builder.AppendLine(title);
            builder.AppendLine();
        }

        builder.AppendLine("| OwnerTypeFullName | Name | MemberKind | Signature | Context |");
        builder.AppendLine("|---|---|---|---|---|");

        var rows = types
            .SelectMany(type => GetOtherAnnotatedMembers(type, language))
            .OrderBy(static row => row.OwnerTypeFullName, StringComparer.Ordinal)
            .ThenBy(static row => row.Name, StringComparer.Ordinal)
            .ToList();

        if (rows.Count == 0)
        {
            builder.AppendLine("| N/A | N/A | N/A | N/A | N/A |");
            return;
        }

        foreach (var row in rows)
        {
            builder.AppendLine($"| {FormatCode(row.OwnerTypeFullName)} | {FormatCode(row.Name)} | {EscapeMarkdownCell(row.MemberKind)} | {EscapeMarkdownCell(row.Signature)} | {EscapeMarkdownCell(row.Context)} |");
        }
    }

    private static void AppendEnumSummaryTable(StringBuilder builder, IReadOnlyList<Type> enumTypes, AgentLanguages language)
    {
        builder.AppendLine("| TypeFullName | UnderlyingType | Context |");
        builder.AppendLine("|---|---|---|");
        if (enumTypes.Count == 0)
        {
            builder.AppendLine("| N/A | N/A | N/A |");
            return;
        }

        foreach (var type in enumTypes.OrderBy(static item => item.FullName, StringComparer.Ordinal))
        {
            builder.AppendLine($"| {FormatCode(type.FullName ?? type.Name)} | {FormatCode(GetFriendlyTypeName(Enum.GetUnderlyingType(type)))} | {EscapeMarkdownCell(GetAgentContext(type, language))} |");
        }
    }

    private static void AppendEnumMemberTable(StringBuilder builder, IReadOnlyList<Type> enumTypes, AgentLanguages language)
    {
        builder.AppendLine("| EnumTypeFullName | Name | Value | Context |");
        builder.AppendLine("|---|---|---:|---|");

        var rows = enumTypes
            .SelectMany(type => type
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(field => new EnumMemberRow(
                    type.FullName ?? type.Name,
                    field.Name,
                    Convert.ToInt64(field.GetValue(null)),
                    GetAgentContext(field, language))))
            .OrderBy(static row => row.EnumTypeFullName, StringComparer.Ordinal)
            .ThenBy(static row => row.Value)
            .ToList();

        if (rows.Count == 0)
        {
            builder.AppendLine("| N/A | N/A | N/A | N/A |");
            return;
        }

        foreach (var row in rows)
        {
            builder.AppendLine($"| {FormatCode(row.EnumTypeFullName)} | {FormatCode(row.Name)} | {row.Value} | {EscapeMarkdownCell(row.Context)} |");
        }
    }

    private static string CreateStructuredTypeContext(Type type, AgentLanguages language, string expectedKind)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {EscapeMarkdownCell(expectedKind)} Agent Context: {FormatCode(type.FullName ?? type.Name)}");
        builder.AppendLine();
        builder.AppendLine("This document describes the type-level context, annotated value members, command members, and other annotated members for the specified type.");
        builder.AppendLine();
        builder.AppendLine("## Type Summary");
        builder.AppendLine();
        AppendTypeSummaryTable(builder, [type], language);
        builder.AppendLine();
        AppendAnnotatedMemberTable(builder, "## Annotated Members", [type], language);
        builder.AppendLine();
        AppendCommandTable(builder, "## Command Members", [type], language);
        builder.AppendLine();
        AppendOtherAnnotatedMemberTable(builder, "## Other Annotated Members", [type], language);
        return builder.ToString().TrimEnd();
    }

    private static IReadOnlyList<Type> GetDocumentRelevantTypes()
        =>
        [
            .. FrameworkInterfaceTypes,
            .. FrameworkTemplateTypes,
            .. GetFrameworkEnumTypes(),
            .. GetFrameworkValueTypes(),
            .. WorkflowAgentContextRegistry.GetRegisteredWorkflowAgentContextTypes()
                .Where(static type => !IsFrameworkBuiltInType(type))
        ];

    private static IReadOnlyList<Type> GetFrameworkEnumTypes()
        => [.. typeof(Anchor).Assembly
            .GetLoadableTypes()
            .Where(static type => type.IsEnum && type.Namespace == typeof(Anchor).Namespace && HasAgentContext(type))
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)];

    private static IReadOnlyList<Type> GetFrameworkValueTypes()
        => [.. typeof(Anchor).Assembly
            .GetLoadableTypes()
            .Where(static type => !type.IsEnum
                                 && !type.IsInterface
                                 && !IsFrameworkTemplateType(type)
                                 && !IsWorkflowComponentType(type)
                                 && type.Namespace == typeof(Anchor).Namespace
                                 && HasAgentContext(type))
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)];

    private static IReadOnlyList<Type> GetRegisteredComponentTypes(IReadOnlyList<Type> registeredTypes, WorkflowComponentRole role)
        => [.. registeredTypes
            .Where(type => !IsFrameworkBuiltInType(type) && GetComponentRole(type) == role)
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)];

    private static WorkflowComponentRole? GetComponentRole(Type type)
    {
        if (typeof(IWorkflowTreeViewModel).IsAssignableFrom(type))
        {
            return WorkflowComponentRole.Tree;
        }

        if (typeof(IWorkflowNodeViewModel).IsAssignableFrom(type))
        {
            return WorkflowComponentRole.Node;
        }

        if (typeof(IWorkflowSlotViewModel).IsAssignableFrom(type))
        {
            return WorkflowComponentRole.Slot;
        }

        if (typeof(IWorkflowLinkViewModel).IsAssignableFrom(type))
        {
            return WorkflowComponentRole.Link;
        }

        return null;
    }

    private static bool IsWorkflowComponentType(Type type)
        => GetComponentRole(type) is not null;

    private static bool IsFrameworkTemplateType(Type type)
        => Array.IndexOf(FrameworkTemplateTypes, type) >= 0;

    private static bool IsFrameworkBuiltInType(Type type)
        => Array.IndexOf(FrameworkInterfaceTypes, type) >= 0
           || Array.IndexOf(FrameworkTemplateTypes, type) >= 0
           || GetFrameworkEnumTypes().Contains(type)
           || GetFrameworkValueTypes().Contains(type);

    private static Type ResolveType(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            throw new ArgumentException("Type name cannot be null or empty.", nameof(typeName));
        }

        var trimmedTypeName = typeName.Trim();
        var directType = Type.GetType(trimmedTypeName, throwOnError: false, ignoreCase: false);
        if (directType is not null)
        {
            return directType;
        }

        var candidates = GetDocumentRelevantTypes()
            .Distinct()
            .ToArray();

        var exactMatch = candidates.FirstOrDefault(type =>
            string.Equals(type.FullName, trimmedTypeName, StringComparison.Ordinal)
            || string.Equals(type.AssemblyQualifiedName, trimmedTypeName, StringComparison.Ordinal));
        if (exactMatch is not null)
        {
            return exactMatch;
        }

        var simpleMatches = candidates
            .Where(type => string.Equals(type.Name, trimmedTypeName, StringComparison.Ordinal))
            .ToArray();

        return simpleMatches.Length switch
        {
            1 => simpleMatches[0],
            > 1 => throw new InvalidOperationException($"Multiple types named '{trimmedTypeName}' were found: {string.Join(", ", simpleMatches.Select(static item => item.FullName))}. Please use the full type name."),
            _ => throw new KeyNotFoundException($"The type '{trimmedTypeName}' was not found in the workflow framework context or registered workflow component types.")
        };
    }

    private static IEnumerable<ValueMemberRow> GetAnnotatedValueMembers(Type type, AgentLanguages language)
    {
        var rows = new Dictionary<string, ValueMemberRow>(StringComparer.Ordinal);

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (!HasAgentContext(property) || IsCommandProperty(property))
            {
                continue;
            }

            var row = new ValueMemberRow(
                type.FullName ?? type.Name,
                property.Name,
                "Property",
                GetFriendlyTypeName(property.PropertyType),
                GetAgentContext(property, language));
            var propertyKey = $"P:{row.Name}";
            if (!rows.ContainsKey(propertyKey))
            {
                rows[propertyKey] = row;
            }
        }

        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (!HasAgentContext(field))
            {
                continue;
            }

            var row = new ValueMemberRow(
                type.FullName ?? type.Name,
                NormalizeFieldName(field.Name),
                "Field",
                GetFriendlyTypeName(field.FieldType),
                GetAgentContext(field, language));
            var fieldKey = $"F:{row.Name}:{row.ValueTypeFullName}";
            if (!rows.ContainsKey(fieldKey))
            {
                rows[fieldKey] = row;
            }
        }

        return rows.Values;
    }

    private static IEnumerable<CommandMemberRow> GetAnnotatedCommandMembers(Type type, AgentLanguages language)
        => type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(static property => HasAgentContext(property) && IsCommandProperty(property))
            .Select(property => new CommandMemberRow(
                type.FullName ?? type.Name,
                property.Name,
                GetFriendlyTypeName(property.PropertyType),
                GetAgentContext(property, language)));

    private static IEnumerable<OtherMemberRow> GetOtherAnnotatedMembers(Type type, AgentLanguages language)
        => type.GetMembers(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(static member => HasAgentContext(member))
            .Where(static member => !IsCoveredByStructuredTables(member))
            .Select(member => new OtherMemberRow(
                type.FullName ?? type.Name,
                member.Name,
                member.MemberType.ToString(),
                GetMemberSignature(member),
                GetAgentContext(member, language)));

    private static bool IsCoveredByStructuredTables(MemberInfo member)
        => member switch
        {
            PropertyInfo => true,
            FieldInfo => true,
            _ => false
        };

    private static bool IsCommandProperty(PropertyInfo property)
        => typeof(ICommand).IsAssignableFrom(property.PropertyType);

    private static bool HasAgentContext(MemberInfo member)
        => member.GetCustomAttributes(typeof(AgentContextAttribute), false).Length > 0;

    private static bool HasAgentContext(Type type)
        => type.GetCustomAttributes(typeof(AgentContextAttribute), false).Length > 0;

    private static string GetAgentContext(MemberInfo member, AgentLanguages language)
        => member.GetCustomAttributes(typeof(AgentContextAttribute), false)
            .OfType<AgentContextAttribute>()
            .FirstOrDefault(attribute => attribute.Language == language)?.Context
            ?? "N/A";

    private static string GetAgentContext(Type type, AgentLanguages language)
        => type.GetCustomAttributes(typeof(AgentContextAttribute), false)
            .OfType<AgentContextAttribute>()
            .FirstOrDefault(attribute => attribute.Language == language)?.Context
            ?? "N/A";

    private static string GetTypeKind(Type type)
    {
        if (type.IsInterface)
        {
            return "Interface";
        }

        if (type.IsEnum)
        {
            return "Enum";
        }

        if (type.IsValueType)
        {
            return "Struct";
        }

        return type.IsAbstract ? "AbstractClass" : "Class";
    }

    private static string GetImplementedContractText(Type type)
    {
        if (type.IsInterface)
        {
            var interfaces = type.GetInterfaces();
            return interfaces.Length == 0
                ? "N/A"
                : string.Join(", ", interfaces.Select(GetFriendlyTypeName));
        }

        var workflowContracts = type.GetInterfaces()
            .Where(static contract => contract == typeof(IWorkflowViewModel)
                                   || contract == typeof(IWorkflowTreeViewModel)
                                   || contract == typeof(IWorkflowNodeViewModel)
                                   || contract == typeof(IWorkflowSlotViewModel)
                                   || contract == typeof(IWorkflowLinkViewModel))
            .Distinct()
            .ToArray();

        return workflowContracts.Length == 0
            ? "N/A"
            : string.Join(", ", workflowContracts.Select(GetFriendlyTypeName));
    }

    private static string GetMemberSignature(MemberInfo member)
        => member switch
        {
            MethodInfo method => $"{GetFriendlyTypeName(method.ReturnType)} {method.Name}({string.Join(", ", method.GetParameters().Select(static parameter => $"{GetFriendlyTypeName(parameter.ParameterType)} {parameter.Name}"))})",
            EventInfo @event => $"{GetFriendlyTypeName(@event.EventHandlerType ?? typeof(void))} {@event.Name}",
            PropertyInfo property => $"{GetFriendlyTypeName(property.PropertyType)} {property.Name}",
            FieldInfo field => $"{GetFriendlyTypeName(field.FieldType)} {field.Name}",
            _ => member.Name
        };

    private static string NormalizeFieldName(string fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return fieldName;
        }

        var normalized = fieldName;
        if (normalized.StartsWith("m_", StringComparison.Ordinal))
        {
            normalized = normalized.Substring(2);
        }

        normalized = normalized.TrimStart('_');
        if (normalized.Length == 0)
        {
            return fieldName;
        }

        return char.ToUpperInvariant(normalized[0]) + normalized.Substring(1);
    }

    private static string GetFriendlyTypeName(Type type)
    {
        var nullableType = Nullable.GetUnderlyingType(type);
        if (nullableType is not null)
        {
            return $"{GetFriendlyTypeName(nullableType)}?";
        }

        if (type.IsArray)
        {
            return $"{GetFriendlyTypeName(type.GetElementType() ?? typeof(object))}[]";
        }

        if (type.IsGenericType)
        {
            var genericTypeName = type.GetGenericTypeDefinition().FullName ?? type.Name;
            var genericMarkerIndex = genericTypeName.IndexOf('`');
            if (genericMarkerIndex >= 0)
            {
                genericTypeName = genericTypeName.Substring(0, genericMarkerIndex);
            }

            return $"{genericTypeName}<{string.Join(", ", type.GetGenericArguments().Select(GetFriendlyTypeName))}>";
        }

        return type.FullName ?? type.Name;
    }

    private static string FormatCode(string text)
        => $"`{EscapeMarkdownCode(text)}`";

    private static string EscapeMarkdownCode(string text)
        => text.Replace("`", "\\`");

    private static string EscapeMarkdownCell(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? "N/A"
            : text!.Replace("|", "\\|").Replace(Environment.NewLine, "<br/>").Replace("\n", "<br/>").Replace("\r", string.Empty);

    private enum WorkflowComponentRole : byte
    {
        Tree = 0,
        Node = 1,
        Slot = 2,
        Link = 3,
    }

    private sealed class ValueMemberRow(string ownerTypeFullName, string name, string sourceKind, string valueTypeFullName, string context)
    {
        public string OwnerTypeFullName { get; } = ownerTypeFullName;
        public string Name { get; } = name;
        public string SourceKind { get; } = sourceKind;
        public string ValueTypeFullName { get; } = valueTypeFullName;
        public string Context { get; } = context;
    }

    private sealed class CommandMemberRow(string ownerTypeFullName, string name, string valueTypeFullName, string context)
    {
        public string OwnerTypeFullName { get; } = ownerTypeFullName;
        public string Name { get; } = name;
        public string ValueTypeFullName { get; } = valueTypeFullName;
        public string Context { get; } = context;
    }

    private sealed class OtherMemberRow(string ownerTypeFullName, string name, string memberKind, string signature, string context)
    {
        public string OwnerTypeFullName { get; } = ownerTypeFullName;
        public string Name { get; } = name;
        public string MemberKind { get; } = memberKind;
        public string Signature { get; } = signature;
        public string Context { get; } = context;
    }

    private sealed class EnumMemberRow(string enumTypeFullName, string name, long value, string context)
    {
        public string EnumTypeFullName { get; } = enumTypeFullName;
        public string Name { get; } = name;
        public long Value { get; } = value;
        public string Context { get; } = context;
    }
}
