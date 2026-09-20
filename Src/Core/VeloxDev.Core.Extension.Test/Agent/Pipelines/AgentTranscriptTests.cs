using VeloxDev.AI.Pipelines;

namespace VeloxDev.Core.Extension.Test.Agent.Pipelines;

/// <summary>
/// Coverage for how the transcript renders itself. The markdown is not an internal detail — a demo binds it
/// straight to a Markdown control — and the reasoning in it has to arrive as a block of its own rather than
/// as the answer's shape under a different label.
/// <para>
/// The fence is the part worth guarding. CommonMark closes a fenced block at the first line whose backtick
/// run is at least as long as the opener's, and a model thinking about code writes backtick runs constantly.
/// A fixed three-backtick fence would be closed from inside, and the rest of the conversation — answer, tool
/// calls, everything — would be swallowed into the code block.
/// </para>
/// </summary>
[TestClass]
public class AgentTranscriptTests
{
    [TestMethod]
    public void Reasoning_IsWrappedInAFence_NotRenderedAsTheAnswersShape()
    {
        var transcript = new AgentTranscript();

        transcript.AddUser("把 Ticker 右移 120 像素");
        transcript.AppendReasoning("先看一下拓扑。");
        transcript.AppendAnswer("我把 Ticker 右移了。");

        var markdown = transcript.ToMarkdown();

        StringAssert.Contains(markdown, "```thinking\n先看一下拓扑。\n```",
            "the thinking must arrive as a fenced block, not as a bold paragraph shaped like the answer");
        StringAssert.Contains(markdown, "**思考：**\n\n```thinking",
            "the label stays above the fence — inside it, it would be read as code");
    }

    [TestMethod]
    public void ReasoningFence_OutgrowsEveryBacktickRunInTheBody()
    {
        // The defect this guards: a fixed fence is closed by the first backtick run the model writes, and
        // everything after it is rendered as code.
        var transcript = new AgentTranscript();

        transcript.AppendReasoning("先写 ``` 这样的围栏。");
        transcript.AppendAnswer("结论在后面。");

        var markdown = transcript.ToMarkdown(new AgentMarkdownOptions { ReasoningHeading = null });

        StringAssert.Contains(markdown, "````thinking\n先写 ``` 这样的围栏。\n````",
            "the opener must outgrow the run inside the body, and the closer must match it");
        StringAssert.EndsWith(markdown, "结论在后面。",
            "nothing after the thinking may be swallowed into the code block");
    }

    [TestMethod]
    public void EmptyReasoning_StillProducesAWellFormedFence()
    {
        // AgentTranscriptEntry.Reasoning() is public, so a host can hand the transcript an empty one. The
        // rendering must not gain a blank line inside the block or leave the opener unclosed.
        var transcript = new AgentTranscript();

        transcript.AppendReasoning(string.Empty);

        Assert.AreEqual("```thinking\n```", transcript.ToMarkdown(new AgentMarkdownOptions { ReasoningHeading = null }));
    }

    [TestMethod]
    public void MarkdownOptions_Null_IsTheShippedShape()
    {
        var transcript = new AgentTranscript();

        transcript.AddUser("做点事");
        transcript.AppendReasoning("想一下。");
        transcript.AppendAnswer("好了。");
        transcript.AddToolCall("ListNodes", "{}", AgentToolOutcome.Succeeded);

        Assert.AreEqual(transcript.ToMarkdown(), transcript.ToMarkdown(null),
            "passing nothing must render exactly what the parameterless call renders");
    }

    [TestMethod]
    public void MarkdownOptions_ReasoningFence_IsWhatTheHostAsksFor()
    {
        // The escape hatch: a control that does not know the default's language can be handed one it does.
        var transcript = new AgentTranscript();
        transcript.AppendReasoning("想一下。");

        var markdown = transcript.ToMarkdown(new AgentMarkdownOptions { ReasoningFence = "reasoning" });

        StringAssert.Contains(markdown, "```reasoning");
        Assert.DoesNotContain("```thinking", markdown, "the default language must not survive an explicit one");
    }

    [TestMethod]
    public void MarkdownOptions_WithoutAHeading_EmitsTheBareFence()
    {
        var transcript = new AgentTranscript();
        transcript.AppendReasoning("想一下。");

        var markdown = transcript.ToMarkdown(new AgentMarkdownOptions { ReasoningHeading = null });

        StringAssert.Contains(markdown, "```thinking");
        Assert.DoesNotContain("**思考：**", markdown, "a null label means no label, not the default one");
    }

    [TestMethod]
    public void MarkdownOptions_EmptyInfoString_TurnsTheFenceOff()
    {
        var transcript = new AgentTranscript();
        transcript.AppendReasoning("想一下。");

        var markdown = transcript.ToMarkdown(new AgentMarkdownOptions { ReasoningFence = "" });

        StringAssert.Contains(markdown, "**思考：**\n\n想一下。", "an empty info string restores the shape the fence replaced");
        Assert.DoesNotContain("```", markdown);
    }

    [TestMethod]
    public void MarkdownOptions_ABacktickInTheInfoString_DoesNotBreakTheOpeningFence()
    {
        // A backtick in the info string would merge with the fence and lengthen the opening run past the
        // closer's, leaving the block open for the whole rest of the document. It is dropped instead.
        var transcript = new AgentTranscript();
        transcript.AppendReasoning("想一下。");

        var lines = transcript.ToMarkdown(new AgentMarkdownOptions { ReasoningFence = "thin`king", ReasoningHeading = null })
            .Split('\n');

        Assert.AreEqual("```thinking", lines[0]);
        Assert.AreEqual("```", lines[^1]);
    }
}
