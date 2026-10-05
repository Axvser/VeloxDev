using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Demo.ViewModels;
using Demo.Workflow;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;
using VeloxDev.AI.Workflow;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow;

/// <summary>
/// The one question no offline test can answer: will a real model, given the real tool surface, reshape a node's
/// ports — which is what the Agent was asked to do when it reported that it could not.
/// </summary>
/// <remarks>
/// <para>
/// The offline suite proves the tools work when they are called. It cannot prove a model reaches for the right
/// one, and it cannot prove the thing the user actually saw. What the user saw was a refusal: asked to extend the
/// Merge Report node's inputs, the model answered that the host had not registered a JSON reader for the port
/// provider — an engine exception, surfaced as the model's own explanation of why it could not proceed. Nothing
/// offline would have produced that sentence.
/// </para>
/// <para>
/// So this drives the demo's own graph with the demo's own instructions and asks for the same edit. It runs
/// against the real DeepSeek endpoint, gated the way <c>SubAgentLiveTests</c> is: without the key it is
/// <c>Inconclusive</c>, so a machine that holds none stays green rather than red.
/// </para>
/// </remarks>
[TestClass]
public class AgentWorkflowLiveTests
{
    private const string KeyVariable = "API_KEY_DEEPSEEK";
    private const string Endpoint = "https://api.deepseek.com";
    private const string Model = "deepseek-v4-flash";

    /// <summary>
    /// The user's request, with the payload spelled out.
    /// </summary>
    /// <remarks>
    /// The provider JSON is handed over on purpose. A live model composes that nested object badly often enough
    /// that leaving it to guess makes the test a probe of the model rather than of this repository — and the
    /// hand-run that found the defect showed exactly that: the tool answered <c>ok</c> and the ports still did
    /// not change, because the model had sent three of them. What is under test here is the loop the user could
    /// not get through — request → the right tool → the provider rebuilt from JSON → the node's ports actually
    /// replaced — so the payload is given and everything downstream of it is not.
    /// </remarks>
    private const string ExtendTheMergeNode =
        "工作流里有一个标题是 'Merge Report' 的节点。" +
        "请把它的输入口重建为这四个，顺序如下：" +
        "{\"Ports\":[{\"Name\":\"stats\"},{\"Name\":\"dist\"},{\"Name\":\"anomalies\"},{\"Name\":\"trend\"}]}" +
        "（这是 PythonPortProvider 的 JSON，属性名是 Ports，每个元素有一个 Name。）" +
        "做完之后告诉我它现在有哪几个输入口，每行一个。";

    private readonly List<string> _scratch = [];

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var directory in _scratch)
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        _scratch.Clear();
    }

    private string Scratch()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"veloxdev-agent-live-{Guid.NewGuid():N}");
        _scratch.Add(directory);
        return directory;
    }

    private static IChatClient? ClientOrNull()
    {
        var key = Environment.GetEnvironmentVariable(KeyVariable);
        if (string.IsNullOrWhiteSpace(key)) return null;

        return new OpenAIClient(
            new ApiKeyCredential(key),
            new OpenAIClientOptions { Endpoint = new Uri(Endpoint) })
            .GetChatClient(Model)
            .AsIChatClient();
    }

    [TestMethod]
    public async Task ARealModel_ExtendsTheMergeNodesPorts()
    {
        var client = ClientOrNull();
        if (client is null) Assert.Inconclusive($"Set {KeyVariable} to run this against a real model.");

        using var session = WorkflowDemoSession.Create(Scratch());

        var scope = session.Tree.AsAgentScope().WithMaxToolCalls(40);

        // Recorded so the assertion can tell "the tool did the work" from "the model found another way" — and so
        // a future change that makes the tool unreachable again fails here with the tool list rather than with a
        // bare port mismatch.
        var calls = new List<string>();
        scope.WithToolCallCallback(args =>
        {
            var result = args.Result.Length > 400 ? args.Result[..400] + "…" : args.Result;
            lock (calls) calls.Add($"{args.ToolName} → {result}");
            return Task.CompletedTask;
        });

        var agent = client!.AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new ChatOptions
            {
                Instructions = "You are an assistant working on a workflow graph. Use the tools you are given.",
            },
            AIContextProviders = scope.CreateContextProviders(),
        });

        var reply = await agent.RunAsync(ExtendTheMergeNode);
        var said = reply.ToString();

        var merge = session.Tree.Nodes.OfType<PythonScriptNodeViewModel>()
            .Single(n => string.Equals(n.Title, "Merge Report", StringComparison.Ordinal));

        string[] ports;
        string transcript;
        lock (calls)
        {
            transcript = string.Join("\n  ", calls);
            ports = [.. merge.InputSlots.Items.Select(i => i.Value?.ToString() ?? string.Empty)];

            Assert.IsTrue(calls.Any(c => c.StartsWith("SetEnumSlotCollection", StringComparison.Ordinal)),
                $"the only route that reshapes a node's ports was never called.\n  Tools the model called:\n  {transcript}\n  It said: {said}");
        }

        CollectionAssert.AreEquivalent(
            new[] { "stats", "dist", "anomalies", "trend" },
            ports,
            $"the model was asked to keep the three original ports and add trend.\n  Tools it called:\n  {transcript}\n  It said: {said}");
    }
}
