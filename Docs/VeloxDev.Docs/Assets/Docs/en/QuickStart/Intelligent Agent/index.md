# Intelligent Agent

Add an AI agent that understands and manipulates your workflow at runtime.

```csharp
var scope = tree.AsAgentScope()
    .WithPromptLanguage(AgentLanguages.English)
    .WithAutoDiscovery("VeloxDev.Core")
    .WithMaxToolCalls(200);

var tools = scope.ProvideTools();
var agent = chatClient.AsAIAgent("You are a workflow assistant.", tools);

await agent.RunAsync(userMessage);
```

Agents can create nodes, connect slots, modify properties, and request user confirmation — all through natural language.
