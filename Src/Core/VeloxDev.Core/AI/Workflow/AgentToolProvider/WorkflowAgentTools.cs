using System.ComponentModel;

namespace VeloxDev.AI.Workflow;

public static class WorkflowAgentToolProvider
{
    public static IEnumerable<Delegate> ProvideWorkflowAgentTools()
    {
        yield return WorkflowAgentTools.GetWorkflowAgentContextDocument;
        yield return WorkflowAgentTools.GetWorkflowAgentContextDocumentInLanguage;
        yield return WorkflowAgentTools.GetWorkflowFrameworkContext;
        yield return WorkflowAgentTools.GetWorkflowFrameworkContextInLanguage;
        yield return WorkflowAgentTools.GetWorkflowEnumContext;
        yield return WorkflowAgentTools.GetWorkflowEnumContextInLanguage;
        yield return WorkflowAgentTools.GetWorkflowValueTypeContext;
        yield return WorkflowAgentTools.GetWorkflowValueTypeContextInLanguage;
        yield return WorkflowAgentTools.GetRegisteredWorkflowComponentContext;
        yield return WorkflowAgentTools.GetRegisteredWorkflowComponentContextInLanguage;
        yield return WorkflowAgentTools.GetWorkflowOtherAnnotatedMemberContext;
        yield return WorkflowAgentTools.GetWorkflowOtherAnnotatedMemberContextInLanguage;
        yield return WorkflowAgentTools.GetWorkflowTypeAgentContext;
        yield return WorkflowAgentTools.GetWorkflowTypeAgentContextInLanguage;
        yield return WorkflowAgentTools.ListRegisteredWorkflowComponentTypes;
    }
}

public static class WorkflowAgentTools
{
    [Description("Read this first. Returns the complete workflow agent context document in English, including framework interfaces, runtime flow, enums, value types, registered user components, and other annotated members.")]
    public static string GetWorkflowAgentContextDocument()
        => WorkflowAgentContextProvider.ProvideWorkflowAgentContextDocument(AgentLanguages.English);

    [Description("Returns the complete workflow agent context document in the requested language code, such as 'en', 'zh', or 'ja'.")]
    public static string GetWorkflowAgentContextDocumentInLanguage([Description("The target language code, such as 'en', 'zh', or 'ja'.")] string languageCode)
        => WorkflowAgentContextProvider.ProvideWorkflowAgentContextDocument(ParseLanguage(languageCode));

    [Description("Returns the framework section in English, including the interface design PlantUML, runtime flow PlantUML, built-in workflow interfaces, and template component classes.")]
    public static string GetWorkflowFrameworkContext()
        => WorkflowAgentContextProvider.ProvideWorkflowFrameworkContext(AgentLanguages.English);

    [Description("Returns the framework section in the requested language code.")]
    public static string GetWorkflowFrameworkContextInLanguage([Description("The target language code, such as 'en', 'zh', or 'ja'.")] string languageCode)
        => WorkflowAgentContextProvider.ProvideWorkflowFrameworkContext(ParseLanguage(languageCode));

    [Description("Returns the built-in workflow enum context in English, including enum summaries and enum member tables.")]
    public static string GetWorkflowEnumContext()
        => WorkflowAgentContextProvider.ProvideWorkflowEnumContext(AgentLanguages.English);

    [Description("Returns the built-in workflow enum context in the requested language code.")]
    public static string GetWorkflowEnumContextInLanguage([Description("The target language code, such as 'en', 'zh', or 'ja'.")] string languageCode)
        => WorkflowAgentContextProvider.ProvideWorkflowEnumContext(ParseLanguage(languageCode));

    [Description("Returns the built-in workflow value type context in English, including value type summaries and annotated member tables.")]
    public static string GetWorkflowValueTypeContext()
        => WorkflowAgentContextProvider.ProvideWorkflowValueTypeContext(AgentLanguages.English);

    [Description("Returns the built-in workflow value type context in the requested language code.")]
    public static string GetWorkflowValueTypeContextInLanguage([Description("The target language code, such as 'en', 'zh', or 'ja'.")] string languageCode)
        => WorkflowAgentContextProvider.ProvideWorkflowValueTypeContext(ParseLanguage(languageCode));

    [Description("Returns the registered user workflow component context in English, grouped into Tree, Node, Slot, and Link sections.")]
    public static string GetRegisteredWorkflowComponentContext()
        => WorkflowAgentContextProvider.ProvideRegisteredWorkflowComponentContext(AgentLanguages.English);

    [Description("Returns the registered user workflow component context in the requested language code.")]
    public static string GetRegisteredWorkflowComponentContextInLanguage([Description("The target language code, such as 'en', 'zh', or 'ja'.")] string languageCode)
        => WorkflowAgentContextProvider.ProvideRegisteredWorkflowComponentContext(ParseLanguage(languageCode));

    [Description("Returns the remaining workflow agent-annotated members in English that are not already covered by the main framework, enum, value type, or component tables.")]
    public static string GetWorkflowOtherAnnotatedMemberContext()
        => WorkflowAgentContextProvider.ProvideWorkflowOtherAnnotatedMemberContext(AgentLanguages.English);

    [Description("Returns the remaining workflow agent-annotated members in the requested language code.")]
    public static string GetWorkflowOtherAnnotatedMemberContextInLanguage([Description("The target language code, such as 'en', 'zh', or 'ja'.")] string languageCode)
        => WorkflowAgentContextProvider.ProvideWorkflowOtherAnnotatedMemberContext(ParseLanguage(languageCode));

    [Description("Returns the workflow agent context in English for a specific type by full type name or simple type name.")]
    public static string GetWorkflowTypeAgentContext([Description("The full type name or simple type name.")] string typeName)
        => WorkflowAgentContextProvider.ProvideTypeAgentContext(typeName, AgentLanguages.English);

    [Description("Returns the workflow agent context for a specific type in the requested language code.")]
    public static string GetWorkflowTypeAgentContextInLanguage(
        [Description("The full type name or simple type name.")] string typeName,
        [Description("The target language code, such as 'en', 'zh', or 'ja'.")] string languageCode)
        => WorkflowAgentContextProvider.ProvideTypeAgentContext(typeName, ParseLanguage(languageCode));

    [Description("Lists the currently registered user workflow component types grouped by workflow role.")]
    public static string ListRegisteredWorkflowComponentTypes()
        => WorkflowAgentContextProvider.ProvideRegisteredWorkflowComponentTypeList();

    private static AgentLanguages ParseLanguage(string languageCode)
        => AgentLanguagesExtensions.ParseLanguageCode(languageCode);
}
