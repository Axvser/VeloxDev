Agents can initiate permission confirmation dialogs and radio buttons, which is an important milestone, marking that you can orchestrate agent tasks in a more controllable manner; meanwhile, the workspace now has a set of new practical configuration methods, including: global language configuration, and automatically scanning workflow context from assemblies.

```csharp
    public static async Task<ChatClientAgent> ProvideAgent(IWorkflowTreeViewModel tree, AgentHelper helper)
    {
        // Create isolated workspace
        var scope = tree.AsAgentScope()
            .WithPromptLanguage(AgentLanguages.English)   // Default prompt language
            .WithOutputLanguage(AgentLanguages.Chinese)   // Default output language
            // Auto-discover components from assemblies
            .WithAutoDiscovery(assemblyName: "VeloxDev.Core")
            .WithAutoDiscovery(assemblyName: "Lib") 
            .WithAutoMarkDirty(false)               // Whether view is automatically marked as dirty
            .WithMaxToolCalls(200)                  // Maximum tool call count
            .WithToolCallCallback(args =>           // Tool call callback
            {
                helper.ToolCalled?.Invoke();
                return Task.CompletedTask;
            })
            .WithSelectionHandler(async args => // Agent asks user which operation to execute
            {
                if (helper.SelectionHandler is not null)
                    await helper.SelectionHandler(args);
            })
            .WithConfirmationHandler(async args => // Agent asks user for operation permission confirmation
            {
                if (helper.ConfirmationHandler is not null)
                    await helper.ConfirmationHandler(args);
            });

        // Interaction tool aggressiveness level 0~3
        scope.WithInteractionSafety(helper.InteractionSafety); 
        // Register custom safety level prompt overrides (only effective for levels 1~3)
        foreach (var kvp in helper.InteractionSafetyPrompts)
            scope.WithInteractionSafetyPrompt(kvp.Key, kvp.Value);

        // Progressive context
        var contextPrompt = scope.ProvideProgressiveContextPrompt();

        // Create MAF tool set
        var tools = scope.ProvideTools();

        var apiKey = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"Environment variable '{EnvironmentVariableName}' is not configured.");
        }

        var chatClient = new OpenAIClient(
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(Endpoint)
            }).GetChatClient(string.IsNullOrWhiteSpace(Model) ? "deepseek-v4-flash" : Model)
              .AsIChatClient();

        var agent = chatClient.AsAIAgent(
            instructions: contextPrompt,
            tools: tools);

        return agent;
    }
```