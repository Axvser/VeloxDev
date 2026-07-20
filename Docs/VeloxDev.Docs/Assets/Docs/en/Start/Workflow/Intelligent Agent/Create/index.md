# Create Agent

> **💡V5.1.1 has API changes. You can check the [Version] chapter.**

```csharp
public static async Task<ChatClientAgent> ProvideAgent(IWorkflowTreeViewModel tree, AgentHelper helper)
{
    // Using a single Tree component as the Agent's takeover scope (domain), you can use the Fluent API to add various definitions needed for the workflow.
    var scope = tree.AsAgentScope()
        .WithComponents(AgentLanguages.English,
            typeof(NodeViewModel),
            typeof(ControllerViewModel),
            typeof(SlotViewModel),
            typeof(LinkViewModel),
            typeof(TreeViewModel))
        .WithEnums(AgentLanguages.English, [])
        .WithInterfaces(AgentLanguages.English, [])
        .WithToolCallCallback((_, _) => helper.ToolCalled?.Invoke())
        .WithTools(
            "Your Prompt",
            AIFunctionFactory.Create(Your Agent Tools)
                  ));

    // Progressive context
    var contextPrompt = scope.ProvideProgressiveContextPrompt(AgentLanguages.English);

    // Create MAF tool set
    var tools = scope.ProvideTools();

    var apiKey = Environment.GetEnvironmentVariable(EnvironmentVariableName);

    var chatClient = new OpenAIClient(
        new ApiKeyCredential(apiKey),
        new OpenAIClientOptions
        {
            Endpoint = new Uri(Endpoint)
        }).GetChatClient(string.IsNullOrWhiteSpace(Model) ? "qwen-plus" : Model)
          .AsIChatClient();

    var agent = chatClient.AsAIAgent(
        instructions: contextPrompt,
        tools: tools);

    return agent;
}
```