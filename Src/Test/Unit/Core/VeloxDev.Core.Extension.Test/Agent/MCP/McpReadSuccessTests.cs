using System;
using System.IO.Pipelines;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using VeloxDev.AI.MCP;

namespace VeloxDev.Core.Extension.Test.Agent.MCP;

/// <summary>
/// The success half of the MCP read tools, against an in-process server over a pair of pipes. The existing
/// tests cover tool presence and the unknown-server envelope; these pin what the model actually receives when
/// a server answers — including the text cap and the prompt's message expansion.
/// </summary>
[TestClass]
public class McpReadSuccessTests
{
    private const string ServerName = "probe";

    private static async Task<McpScope> ConnectedAsync()
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        var serverTransport = new StreamServerTransport(
            clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream(), ServerName);
        var server = McpServer.Create(serverTransport, new McpServerOptions
        {
            ServerInfo = new Implementation { Name = ServerName, Version = "1.0" },
            Capabilities = new ServerCapabilities
            {
                Resources = new ResourcesCapability(),
                Prompts = new PromptsCapability(),
            },
            Handlers = new McpServerHandlers
            {
                ListResourcesHandler = (_, _) => ValueTask.FromResult(new ListResourcesResult
                {
                    Resources =
                    {
                        new Resource
                        {
                            Uri = "file:///doc.txt",
                            Name = "doc",
                            Description = "a document",
                            MimeType = "text/plain",
                        },
                    },
                }),
                ReadResourceHandler = (ctx, _) => ValueTask.FromResult(new ReadResourceResult
                {
                    Contents =
                    {
                        new TextResourceContents
                        {
                            Uri = ctx.Params.Uri,
                            Text = new string('x', 400),
                            MimeType = "text/plain",
                        },
                    },
                }),
                ListPromptsHandler = (_, _) => ValueTask.FromResult(new ListPromptsResult
                {
                    Prompts =
                    {
                        new Prompt
                        {
                            Name = "summarize",
                            Description = "summarizes a topic",
                            Arguments = [new PromptArgument { Name = "topic", Description = "what to summarize", Required = true }],
                        },
                    },
                }),
                GetPromptHandler = (ctx, _) => ValueTask.FromResult(new GetPromptResult
                {
                    Description = "summarizes a topic",
                    Messages =
                    {
                        new PromptMessage
                        {
                            Role = Role.User,
                            Content = new TextContentBlock { Text = "summarize " + ctx.Params.Name },
                        },
                    },
                }),
            },
        });

        // The server's session handler is what consumes the transport's messages; without RunAsync the
        // handshake has no listener and the client's initialize request times out.
        _ = server.RunAsync();
        await Task.Delay(50);

        var client = await McpClient.CreateAsync(new StreamClientTransport(
            clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()));

        var scope = new McpScope
        {
            ConnectOverride = (_, _, _) => Task.FromResult(new McpConnection(client, [])),
        };
        await scope.LoadAsync([new McpServerConfiguration
        {
            Name = ServerName,
            RunMode = McpServerRunMode.Http,
            Endpoint = "https://example.invalid/mcp",
        }]);
        return scope;
    }

    private static string Invoke(McpScope scope, string toolName, params (string Name, object? Value)[] args)
    {
        var tool = new McpAgentToolkit(scope, []).CreateTools()
            .OfType<AIFunction>()
            .Single(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase));
        var aiArgs = new AIFunctionArguments();
        foreach (var (name, value) in args)
            if (value is not null) aiArgs[name] = value;

        var result = tool.InvokeAsync(aiArgs, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        return result?.ToString() ?? string.Empty;
    }

    [TestMethod]
    public async Task ListResources_ReturnsTheServersCatalog()
    {
        await using var scope = await ConnectedAsync();

        var json = JObject.Parse(Invoke(scope, McpAgentToolkit.ListResourcesName, ("serverName", ServerName)));

        Assert.AreEqual("ok", json["status"]?.Value<string>(), json.ToString());
        Assert.AreEqual(1, json["resourceCount"]?.Value<int>());
        var resource = (json["resources"] as JArray)![0];
        Assert.AreEqual("file:///doc.txt", resource["uri"]?.Value<string>());
        Assert.AreEqual("doc", resource["name"]?.Value<string>());
        Assert.AreEqual("text/plain", resource["mimeType"]?.Value<string>());
    }

    [TestMethod]
    public async Task ReadResource_ReturnsTheText_AndCapsIt()
    {
        await using var scope = await ConnectedAsync();

        var full = JObject.Parse(Invoke(scope, McpAgentToolkit.ReadResourceName,
            ("serverName", ServerName), ("uri", "file:///doc.txt")));
        Assert.AreEqual("ok", full["status"]?.Value<string>(), full.ToString());
        Assert.AreEqual(400, ((full["contents"] as JArray)![0]["text"]?.Value<string>() ?? string.Empty).Length);
        Assert.AreEqual(false, full["truncated"]?.Value<bool>());

        var capped = JObject.Parse(Invoke(scope, McpAgentToolkit.ReadResourceName,
            ("serverName", ServerName), ("uri", "file:///doc.txt"), ("maxChars", 50)));
        var content = (capped["contents"] as JArray)![0];
        Assert.AreEqual(50, (content["text"]?.Value<string>() ?? string.Empty).Length);
        Assert.AreEqual(400, content["originalLength"]?.Value<int>());
        Assert.AreEqual(true, content["truncated"]?.Value<bool>());
        Assert.AreEqual(true, capped["truncated"]?.Value<bool>(), "the envelope summarises the per-content flag");
    }

    [TestMethod]
    public async Task ListPrompts_ReturnsTheDeclaredArguments()
    {
        await using var scope = await ConnectedAsync();

        var json = JObject.Parse(Invoke(scope, McpAgentToolkit.ListPromptsName, ("serverName", ServerName)));

        Assert.AreEqual("ok", json["status"]?.Value<string>(), json.ToString());
        Assert.AreEqual(1, json["promptCount"]?.Value<int>());
        var prompt = (json["prompts"] as JArray)![0];
        Assert.AreEqual("summarize", prompt["name"]?.Value<string>());
        var argument = (prompt["arguments"] as JArray)![0];
        Assert.AreEqual("topic", argument["name"]?.Value<string>());
        Assert.AreEqual(true, argument["required"]?.Value<bool>());
    }

    [TestMethod]
    public async Task GetPrompt_ExpandsTheMessage_AndCapsTheTotalText()
    {
        await using var scope = await ConnectedAsync();

        var full = JObject.Parse(Invoke(scope, McpAgentToolkit.GetPromptName,
            ("serverName", ServerName), ("promptName", "summarize")));
        Assert.AreEqual("ok", full["status"]?.Value<string>(), full.ToString());
        Assert.AreEqual(1, full["messageCount"]?.Value<int>());
        var message = (full["messages"] as JArray)![0];
        Assert.AreEqual("user", message["role"]?.Value<string>());
        Assert.AreEqual("summarize summarize", message["text"]?.Value<string>());
        Assert.AreEqual(false, full["truncated"]?.Value<bool>());

        var capped = JObject.Parse(Invoke(scope, McpAgentToolkit.GetPromptName,
            ("serverName", ServerName), ("promptName", "summarize"), ("maxChars", 9)));
        Assert.AreEqual(true, capped["truncated"]?.Value<bool>());
        Assert.AreEqual(9, ((capped["messages"] as JArray)![0]["text"]?.Value<string>() ?? string.Empty).Length);
    }
}
