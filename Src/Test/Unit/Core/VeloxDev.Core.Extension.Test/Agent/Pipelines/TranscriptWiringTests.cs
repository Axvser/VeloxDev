using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using VeloxDev.AI.MCP;
using VeloxDev.AI.Pipelines;
using VeloxDev.AI.Workflow;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Pipelines;

/// <summary>
/// The scope composes its stage chain on first use, so anything that reads the pipeline before the
/// transcript is attached decides what the chain is made of. Attaching a subsystem reads it — which is
/// what made an attached conversation silently receive nothing.
/// </summary>
[TestClass]
public class TranscriptWiringTests
{
    [TestMethod]
    public async Task AttachingTheTranscriptAfterASubsystem_StillFeedsIt()
    {
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel());
        var transcript = new AgentTranscript();

        // A subsystem is attached first, and wiring it reads the pipeline.
        _ = scope.WithMcps(new McpScope()).Pipeline;

        scope.WithTranscript(transcript);

        await scope.Pipeline.PublishAsync(new AgentTextDelta("写进去了"));

        Assert.HasCount(1, transcript.Entries, "the conversation must be fed regardless of attach order");
        Assert.AreEqual("写进去了", transcript.Entries[0].Text);
    }

    [TestMethod]
    public async Task AttachingTheTranscriptFirst_FeedsItToo()
    {
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel());
        var transcript = new AgentTranscript();

        scope.WithTranscript(transcript);
        _ = scope.WithSkills(new AI.Skills.SkillScope());

        await scope.Pipeline.PublishAsync(new AgentTextDelta("也写进去了"));

        Assert.HasCount(1, transcript.Entries);
    }

    [TestMethod]
    public async Task ToolEvents_ReachTheTranscript_RegardlessOfAttachOrder()
    {
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel());
        var transcript = new AgentTranscript();

        _ = scope.Pipeline;                       // compose before the transcript exists
        scope.WithTranscript(transcript);

        await scope.Pipeline.PublishAsync(
            new AgentToolCallCompleted("ListNodes", "{}", AgentToolOutcome.Succeeded, System.TimeSpan.Zero));

        Assert.HasCount(1, transcript.Entries);
        Assert.AreEqual(System.Enum.Parse<AgentTranscriptRole>("ToolCall"), transcript.Entries[0].Role);
    }
}
