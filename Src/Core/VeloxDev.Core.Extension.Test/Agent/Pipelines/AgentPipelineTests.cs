using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.AI;
using VeloxDev.AI.Pipelines;

namespace VeloxDev.Core.Extension.Test.Agent.Pipelines;

/// <summary>
/// Coverage for the pipeline: that a run's output reaches a host as a conversation — including the
/// reasoning, which used to be discarded before anything downstream could see it.
/// <para>
/// Everything here runs against a scripted <see cref="IChatClient"/>, so it proves what the pipeline does
/// with a model's output, not that any particular model produces reasoning. Whether reasoning is emitted
/// at all is the endpoint's business; the OpenAI adapter has <c>TryGetReasoningDelta</c> for endpoints that
/// do.
/// </para>
/// </summary>
[TestClass]
public class AgentPipelineTests
{
    /// <summary>An <see cref="IChatClient"/> that replays a fixed stream without a network.</summary>
    private sealed class ScriptedChatClient(IReadOnlyList<ChatResponseUpdate> stream, ChatMessage? whole = null) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(whole ?? new ChatMessage(ChatRole.Assistant, "ok")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var update in stream)
            {
                // A real suspension, so anything that buffers the stream rather than passing it through
                // would still have to work here.
                await Task.Yield();
                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    private static ChatResponseUpdate Update(params AIContent[] contents)
        => new(ChatRole.Assistant, contents);

    private static (AIAgent Agent, AgentTranscript Transcript) Build(params ChatResponseUpdate[] stream)
    {
        var transcript = new AgentTranscript();
        var pipeline = new AgentPipeline().Use(new TextPipeline(() => transcript));
        var agent = new ScriptedChatClient(stream).AsAIAgent([]).WithPipeline(pipeline);
        return (agent, transcript);
    }

    private static async Task DrainAsync(AIAgent agent, string message)
    {
        await foreach (var _ in agent.RunStreamingAsync(message)) { }
    }

    // ── The headline: reasoning is its own entry ────────────────────────────

    [TestMethod]
    public async Task Reasoning_ReachesTheTranscript_AsItsOwnEntry()
    {
        // Reading AgentResponseUpdate.Text — which is what every demo did — yields the answer only; the
        // thinking sits beside it as TextReasoningContent and was silently dropped.
        var (agent, transcript) = Build(
            Update(new TextReasoningContent("先看一下拓扑。")),
            Update(new TextContent("我把 Ticker 右移了。")));

        await DrainAsync(agent, "把 Ticker 右移 120 像素");

        Assert.HasCount(3, transcript.Entries);
        Assert.AreEqual(AgentTranscriptRole.User, transcript.Entries[0].Role);
        Assert.AreEqual("把 Ticker 右移 120 像素", transcript.Entries[0].Text);

        Assert.AreEqual(AgentTranscriptRole.Reasoning, transcript.Entries[1].Role);
        Assert.AreEqual("先看一下拓扑。", transcript.Entries[1].Text);

        Assert.AreEqual(AgentTranscriptRole.Assistant, transcript.Entries[2].Role);
        Assert.AreEqual("我把 Ticker 右移了。", transcript.Entries[2].Text,
            "the answer must carry only the answer, not the reasoning");
    }

    [TestMethod]
    public async Task Reasoning_AccumulatesInOneEntry_AcrossUpdates()
    {
        var (agent, transcript) = Build(
            Update(new TextReasoningContent("第一步，")),
            Update(new TextReasoningContent("第二步。")),
            Update(new TextContent("做完了。")));

        await DrainAsync(agent, "做点事");

        var reasoning = transcript.Entries[1];
        Assert.AreEqual(AgentTranscriptRole.Reasoning, reasoning.Role);
        Assert.AreEqual("第一步，第二步。", reasoning.Text);
        Assert.HasCount(3, transcript.Entries);
    }

    [TestMethod]
    public async Task AModelThatDoesNotReason_ProducesNoReasoningEntry()
    {
        // The pipeline does not invent thinking. An endpoint that emits no reasoning yields none.
        var (agent, transcript) = Build(Update(new TextContent("好的。")));

        await DrainAsync(agent, "你好");

        Assert.HasCount(2, transcript.Entries);
        Assert.AreEqual(AgentTranscriptRole.User, transcript.Entries[0].Role);
        Assert.AreEqual(AgentTranscriptRole.Assistant, transcript.Entries[1].Role);
    }

    [TestMethod]
    public async Task TheStreamIsPassedThroughUntouched()
    {
        // A middleware stage observes; it must not consume or re-time what the caller reads.
        var stream = new[]
        {
            Update(new TextContent("一")),
            Update(new TextContent("二")),
        };
        var (agent, _) = Build(stream);

        var seen = new List<string>();
        await foreach (var update in agent.RunStreamingAsync("数数"))
            seen.Add(update.Text ?? string.Empty);

        Assert.HasCount(2, seen, "every update must still reach the caller");
        Assert.AreEqual("一", seen[0]);
        Assert.AreEqual("二", seen[1]);
    }

    [TestMethod]
    public async Task ReasoningAndAnswer_StaySeparateAcrossTheWholeRun()
    {
        var (agent, transcript) = Build(
            Update(new TextReasoningContent("想想。")),
            Update(new TextContent("答案是 42。")),
            Update(new TextReasoningContent("再想想。")),
            Update(new TextContent("补充一句。")));

        await DrainAsync(agent, "问题");

        // user, reasoning, answer, reasoning, answer — the second reasoning must NOT be appended to the
        // first, and the second answer must not be appended to the first.
        Assert.HasCount(5, transcript.Entries);
        Assert.AreEqual(AgentTranscriptRole.Reasoning, transcript.Entries[1].Role);
        Assert.AreEqual("想想。", transcript.Entries[1].Text);
        Assert.AreEqual(AgentTranscriptRole.Assistant, transcript.Entries[2].Role);
        Assert.AreEqual("答案是 42。", transcript.Entries[2].Text);
        Assert.AreEqual(AgentTranscriptRole.Reasoning, transcript.Entries[3].Role);
        Assert.AreEqual("再想想。", transcript.Entries[3].Text);
        Assert.AreEqual(AgentTranscriptRole.Assistant, transcript.Entries[4].Role);
        Assert.AreEqual("补充一句。", transcript.Entries[4].Text);
    }

    // ── The two rendering rules the demos got wrong ─────────────────────────

    [TestMethod]
    public void TextAfterAToolCall_StartsANewEntry_InsteadOfBeingDropped()
    {
        // Regression for the reported bug: the demos appended a fragment only when the last message was
        // still an assistant message, so as soon as a tool call landed mid-run every later fragment was
        // written to a side log and never rendered.
        var transcript = new AgentTranscript();

        transcript.AppendAnswer("我来调用工具。");
        transcript.AddToolCall("ListNodes", """{"status":"ok"}""", AgentToolOutcome.Succeeded);
        transcript.AppendAnswer("上面是结果。");

        Assert.HasCount(3, transcript.Entries);
        Assert.AreEqual("我来调用工具。", transcript.Entries[0].Text);
        Assert.AreEqual(AgentTranscriptRole.ToolCall, transcript.Entries[1].Role);
        Assert.AreEqual("上面是结果。", transcript.Entries[2].Text,
            "the answer after a tool call must reach the conversation");
    }

    [TestMethod]
    public void ConsecutiveToolCalls_RenderAsOneBlock_NotAsBlankRules()
    {
        // Regression for the other reported bug: a tool-call row has no text, and the demos' markdown path
        // appended its (empty) text with a separator before it — one blank rule per call.
        var transcript = new AgentTranscript();

        transcript.AddUser("做点事");
        transcript.AppendAnswer("好的。");
        transcript.AddToolCall("ListNodes", "{}", AgentToolOutcome.Succeeded);
        transcript.AddToolCall("GetWorkflowSummary", "{}", AgentToolOutcome.Succeeded);
        transcript.AppendAnswer("完成了。");

        var markdown = transcript.ToMarkdown();

        StringAssert.Contains(markdown, "ListNodes");
        StringAssert.Contains(markdown, "GetWorkflowSummary");
        StringAssert.Contains(markdown, "完成了。");
        Assert.AreEqual(1, CountOccurrences(markdown, "**工具调用：**"), "one heading for the run");
        Assert.AreEqual(2, CountOccurrences(markdown, "\n\n---\n\n"),
            "only the real turns are separated; the tool run is one block");
    }

    [TestMethod]
    public void PlainTextLines_IncludeToolCalls()
    {
        // The non-markdown hosts used to show no trace of the tools at all.
        var transcript = new AgentTranscript();

        transcript.AddUser("做点事");
        transcript.AppendAnswer("好的。");
        transcript.AddToolCall("ListNodes", """{"status":"ok"}""", AgentToolOutcome.Succeeded);

        var lines = transcript.ToPlainTextLines();

        Assert.HasCount(3, lines);
        StringAssert.Contains(lines[0], "[User]");
        StringAssert.Contains(lines[1], "[Agent]");
        StringAssert.Contains(lines[2], "ListNodes");
        StringAssert.Contains(lines[2], "Succeeded");
    }

    [TestMethod]
    public void AddUser_ClosesAnOpenAnswer()
    {
        var transcript = new AgentTranscript();

        transcript.AppendAnswer("上一轮的话。");
        transcript.AddUser("新一轮。");
        transcript.AppendAnswer("新一轮的回答。");

        // The new answer must be its own entry, not appended to the previous turn's.
        Assert.HasCount(3, transcript.Entries);
        Assert.AreEqual(AgentTranscriptRole.Assistant, transcript.Entries[0].Role);
        Assert.AreEqual("上一轮的话。", transcript.Entries[0].Text);
        Assert.AreEqual(AgentTranscriptRole.User, transcript.Entries[1].Role);
        Assert.AreEqual("新一轮。", transcript.Entries[1].Text);
        Assert.AreEqual(AgentTranscriptRole.Assistant, transcript.Entries[2].Role);
        Assert.AreEqual("新一轮的回答。", transcript.Entries[2].Text);
    }

    // ── Stage chain ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task AStageMayDropAnEvent_ForTheStagesAfterIt_ButNotForTheOnesBeforeIt()
    {
        var transcript = new AgentTranscript();

        // The filter is placed ahead of the transcript: a stage only controls what the stages *after* it
        // see, which is why order in the chain is meaningful rather than decorative.
        var pipeline = new AgentPipeline()
            .Use((e, next, ct) => e is AgentReasoningDelta ? default : next(e))
            .Use(new TextPipeline(() => transcript));

        await pipeline.PublishAsync(new AgentReasoningDelta("不该出现"));
        await pipeline.PublishAsync(new AgentTextDelta("该出现"));

        Assert.HasCount(1, transcript.Entries);
        Assert.AreEqual("该出现", transcript.Entries[0].Text);
    }

    [TestMethod]
    public async Task AStageAfterTheTranscript_CannotUnwriteIt()
    {
        // The same chain composed the other way round: the transcript has already been written by the time
        // the filter runs, so the reasoning stays. Documenting the ordering contract by its failure mode.
        var transcript = new AgentTranscript();
        var pipeline = new AgentPipeline()
            .Use(new TextPipeline(() => transcript))
            .Use((e, next, ct) => e is AgentReasoningDelta ? default : next(e));

        await pipeline.PublishAsync(new AgentReasoningDelta("已经写进去了"));

        Assert.HasCount(1, transcript.Entries);
        Assert.AreEqual("已经写进去了", transcript.Entries[0].Text);
    }

    [TestMethod]
    public async Task StagesRunInOrder()
    {
        var order = new List<string>();
        var pipeline = new AgentPipeline()
            .Use((e, next, ct) => { order.Add("first"); return next(e); })
            .Use((e, next, ct) => { order.Add("second"); return next(e); })
            .Use((e, next, ct) => { order.Add("third"); return next(e); });

        await pipeline.PublishAsync(new AgentTurnStarted(AgentRunKind.Streaming, null));

        CollectionAssert.AreEqual(new[] { "first", "second", "third" }, order);
    }

    [TestMethod]
    public async Task AStageThatThrows_DoesNotBreakTheRun_ButIsReported()
    {
        // Publishing happens on the run's own path — for a tool call, inside the wrapper's try, whose catch
        // turns anything thrown into the tool's error result and throws the real one away. A stage that is
        // only drawing something must not be able to tell the model its tool failed.
        var pipeline = new AgentPipeline().Use((e, next, ct) => throw new InvalidOperationException("stage broke"));
        AgentStageFailedEventArgs? failure = null;
        pipeline.StageFailed += (_, args) => failure = args;

        await pipeline.PublishAsync(new AgentTurnStarted(AgentRunKind.Streaming, null));

        Assert.IsNotNull(failure, "a broken stage must be reported, not swallowed");
        Assert.AreEqual("stage broke", failure!.Error.Message);
        Assert.IsInstanceOfType<AgentTurnStarted>(failure.Event);
    }

    [TestMethod]
    public async Task AStageThatThrows_StillLetsTheEarlierStagesFinish()
    {
        var transcript = new AgentTranscript();
        var pipeline = new AgentPipeline()
            .Use(new TextPipeline(() => transcript))
            .Use((e, next, ct) => throw new InvalidOperationException("second stage broke"));

        await pipeline.PublishAsync(new AgentTextDelta("写进去了"));

        Assert.HasCount(1, transcript.Entries, "the stage before the broken one still did its job");
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal);
             i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
