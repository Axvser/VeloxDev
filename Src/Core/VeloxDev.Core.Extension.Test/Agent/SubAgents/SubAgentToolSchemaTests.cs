using Microsoft.Extensions.AI;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace VeloxDev.Core.Extension.Test.Agent.SubAgents;

/// <summary>
/// The shape the model actually reads: the JSON schema of each management tool.
/// <para>
/// A pinned assumption rather than a behaviour of this library. Every capability a spawn takes is an optional
/// parameter with a default, and the ergonomics of the whole subsystem rest on how
/// <c>AIFunctionFactory</c> describes such a parameter — a defaulted parameter wrongly listed as required
/// would leave every offline test green while making the tools unusable to a model, which is exactly the
/// kind of failure a test has to raise rather than a reader has to notice.
/// </para>
/// </summary>
[TestClass]
public class SubAgentToolSchemaTests
{
    [TestMethod]
    public async Task TheTask_IsTheOnlyThingTheModelMustSupply()
    {
        await using var fx = new SubAgentFixture();

        var schema = SchemaOf(fx, "SpawnSubAgent");
        var required = RequiredOf(schema);

        CollectionAssert.AreEqual(new[] { "task" }, required,
            "every capability argument is optional — the model must be able to dispatch with the task alone");
    }

    [TestMethod]
    public async Task EveryCapability_IsDescribedToTheModel()
    {
        // Optional is not the same as absent: a parameter the schema omits entirely is one the model cannot
        // pass at all, and narrowing a child's abilities is the point of the tool.
        await using var fx = new SubAgentFixture();

        var schema = SchemaOf(fx, "SpawnSubAgent");
        var properties = schema["properties"] as JObject ?? throw new AssertFailedException("no properties");

        string[] capabilityArguments =
        [
            "task", "name", "allowedTools", "maxToolCalls", "maxReadToolCalls", "maxWriteToolCalls",
            "allowNodeExecution", "allowedGenericCommands", "autoMarkDirty", "notes",
            "allowedSkills", "allowedMcpServers",
        ];

        foreach (var name in capabilityArguments)
            Assert.IsNotNull(properties[name], $"'{name}' is not described to the model at all");
    }

    [TestMethod]
    public async Task TheWaitingTools_TakeNothingAtAll()
    {
        // So that "wait for everything" and "survey the roster" are expressible as the model calling a tool
        // with no arguments, which is the form it reaches for when it is unsure what to name.
        await using var fx = new SubAgentFixture();

        Assert.IsEmpty(RequiredOf(SchemaOf(fx, "ListSubAgents")));
        Assert.IsEmpty(RequiredOf(SchemaOf(fx, "WaitSubAgents")));
    }

    [TestMethod]
    public async Task TheHandlesTools_RequireTheirHandle()
    {
        await using var fx = new SubAgentFixture();

        CollectionAssert.AreEqual(new[] { "id" }, RequiredOf(SchemaOf(fx, "GetSubAgentResult")));
        CollectionAssert.AreEqual(new[] { "id" }, RequiredOf(SchemaOf(fx, "CancelSubAgent")));
    }

    [TestMethod]
    public async Task ASpawnThatNamesOnlyItsTask_IsAccepted()
    {
        // The schema is one claim; the runtime filling those defaults is another. A parameter optional in the
        // schema but unbound at invocation time would fail every spawn the model makes in the natural way.
        await using var fx = new SubAgentFixture(client: new InstantChatClient(), maxToolCalls: 20);

        var reply = JObject.Parse(fx.Invoke("SpawnSubAgent", ("task", "count the nodes")));

        Assert.AreEqual("ok", (string?)reply["status"], (string?)reply["message"]);
        var row = fx.RowOf((string)reply["id"]!);

        Assert.AreEqual(19, row.MaxToolCalls, "the omitted budget inherited the parent's remaining allowance");
        Assert.IsEmpty(row.DroppedRequests, "and omitting a capability is not being refused one");
        Assert.AreNotEqual(0, row.GrantedToolCount, "the omitted whitelist inherited the read-only surface");
    }

    private static JObject SchemaOf(SubAgentFixture fx, string toolName)
    {
        var tool = fx.Tools.OfType<AIFunction>()
            .FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase))
            ?? throw new AssertFailedException($"'{toolName}' was not contributed to the host.");

        return JObject.Parse(tool.JsonSchema.GetRawText());
    }

    private static string[] RequiredOf(JObject schema)
        => (schema["required"] as JArray)?.Select(token => (string)token!).ToArray() ?? [];
}
