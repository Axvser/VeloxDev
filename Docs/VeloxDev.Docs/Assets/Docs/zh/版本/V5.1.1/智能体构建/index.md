智能体可以发起权限确认框与单选框，这是一个重要的里程碑，标志着您将以更加可控的方式进行智能体任务编排；同时，工作空间现已新增一组实用配置方法，它们包括：全局语言配置、从程序集自动扫描工作流上下文。

```csharp
    public static async Task<ChatClientAgent> ProvideAgent(IWorkflowTreeViewModel tree, AgentHelper helper)
    {
        // 创建独立的工作空间
        var scope = tree.AsAgentScope()
            .WithPromptLanguage(AgentLanguages.English)   // 默认提示词语言
            .WithOutputLanguage(AgentLanguages.Chinese)   // 默认输出语言
            // 从程序集自动发现组件
            .WithAutoDiscovery(assemblyName: "VeloxDev.Core")
            .WithAutoDiscovery(assemblyName: "Lib") 
            .WithAutoMarkDirty(false)               // 视图是否自动标记为脏
            .WithMaxToolCalls(200)                  // 最大工具调用数
            .WithToolCallCallback(args =>           // 工具调用回调
            {
                helper.ToolCalled?.Invoke();
                return Task.CompletedTask;
            })
            .WithSelectionHandler(async args => // Agent询问用户执行哪一项操作
            {
                if (helper.SelectionHandler is not null)
                    await helper.SelectionHandler(args);
            })
            .WithConfirmationHandler(async args => // Agent向用户确认操作权限
            {
                if (helper.ConfirmationHandler is not null)
                    await helper.ConfirmationHandler(args);
            });

        // 交互工具激进程度 0~3
        scope.WithInteractionSafety(helper.InteractionSafety); 
        // 注册自定义安全等级提示词覆盖（仅对 1~3 档生效）
        foreach (var kvp in helper.InteractionSafetyPrompts)
            scope.WithInteractionSafetyPrompt(kvp.Key, kvp.Value);

        // 渐进式上下文
        var contextPrompt = scope.ProvideProgressiveContextPrompt();

        // 创建MAF工具集
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