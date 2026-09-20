using Demo.ViewModels;
using Demo.ViewModels.Workflow.Helper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace VeloxDev.Core.Extension.Test.Examples;

/// <summary>
/// Coverage for the structured agent transcript the demo chat panels bind to.
/// <para>
/// It lives in this test project because that is the only one referencing <c>Lib</c>, and it is worth having:
/// the shape of a tool-call row — its collapsed line and its parsed status — and the role a log line maps to
/// are the contract between the shared layer and whatever renders it, and a silent change there breaks a
/// panel without breaking a build.
/// </para>
/// <para>
/// What the panels bind today is the plain-text log, built from the transcript's own lines; the Avalonia
/// demo binds the transcript's markdown instead. The structured list here is the third shape, kept so a host
/// that wants per-role rendering has something to bind — which is why its mapping is checked against what
/// the log actually carries rather than against itself.
/// </para>
/// </summary>
[TestClass]
public class AgentTranscriptTests
{
    [TestMethod]
    public void ToolCall_ParsesStatusOutOfTheResult()
    {
        var message = AgentMessageViewModel.ToolCall("LoadMcpServers", """{"status":"error","message":"boom"}""");

        Assert.AreEqual(AgentMessageRole.ToolCall, message.Role);
        Assert.AreEqual("LoadMcpServers", message.ToolName);
        Assert.AreEqual("error", message.ToolStatus);
        Assert.Contains("LoadMcpServers", message.Summary);
        Assert.Contains("error", message.Summary);
    }

    [TestMethod]
    public void ToolCall_WithoutAStatus_SummarisesAsTheToolNameAlone()
    {
        // Several tools answer with a plain payload and no `status` — the collapsed row must still read
        // sensibly rather than showing a dangling separator.
        var message = AgentMessageViewModel.ToolCall("GetWorkflowSummary", """{"treeId":"t1","nodeCount":3}""");

        Assert.IsNull(message.ToolStatus);
        Assert.AreEqual("GetWorkflowSummary", message.Summary);
    }

    [TestMethod]
    public void ToolCall_WithANonJsonResult_DoesNotThrow()
    {
        // A tool may return plain text, and a result can arrive truncated. Neither is an error worth
        // surfacing — the row simply has no status.
        var message = AgentMessageViewModel.ToolCall("Something", "not json at all");

        Assert.IsNull(message.ToolStatus);
        Assert.AreEqual("not json at all", message.Detail);
    }

    [TestMethod]
    public void ToolCall_IsCollapsedByDefaultAndKnowsWhetherItCanExpand()
    {
        var withDetail = AgentMessageViewModel.ToolCall("ListNodes", "[]");
        var withoutDetail = AgentMessageViewModel.ToolCall("ListNodes", "");

        Assert.IsFalse(withDetail.IsExpanded, "a payload must not be forced open");
        Assert.IsTrue(withDetail.HasDetail);
        Assert.IsFalse(withoutDetail.HasDetail, "nothing to expand into, so no toggle should be offered");
    }

    [TestMethod]
    public void Summary_TracksTheTextOfAStreamingReply()
    {
        // A streaming assistant message grows its text in place, so `Summary` — what the templates bind —
        // has to follow it or the panel would freeze on the first fragment.
        var message = new AgentMessageViewModel(AgentMessageRole.Assistant, "first");
        var notified = false;
        message.PropertyChanged += (_, e) => notified |= e.PropertyName == nameof(AgentMessageViewModel.Summary);

        message.Text += " second";

        Assert.AreEqual("first second", message.Summary);
        Assert.IsTrue(notified, "a template binding Summary must be told when the text grows");
    }

    [TestMethod]
    public void Transcript_KeepsChatAndToolCallsInOneOrderedSequence()
    {
        // The panels render one list: a tool call is an entry in the conversation, not a separate log.
        var tree = new TreeViewModel();

        tree.AppendAgentLog("[User] do the thing");
        tree.AppendToolCall("ListNodes", """{"status":"ok"}""");
        tree.AppendAgentLog("[Agent] done");

        var roles = tree.AgentMessages.Select(m => m.Role).ToArray();
        Assert.HasCount(3, roles);
        Assert.AreEqual(AgentMessageRole.User, roles[0]);
        Assert.AreEqual(AgentMessageRole.ToolCall, roles[1]);
        Assert.AreEqual(AgentMessageRole.Assistant, roles[2]);
    }

    [TestMethod]
    public void AppendToolCall_WithoutAName_AddsNothing()
    {
        var tree = new TreeViewModel();

        tree.AppendToolCall("  ", "{}");

        Assert.IsEmpty(tree.AgentMessages);
    }

    [TestMethod]
    public void FromLogLine_MapsThinkingToItsOwnRole()
    {
        // The transcript renders reasoning as "[Thinking] …". Without a branch here the line lands as an
        // anonymous Plain message, so a host that shows the thinking cannot tell it from anything else.
        var thinking = AgentMessageViewModel.FromLogLine("[Thinking] 先看一下拓扑。");

        Assert.AreEqual(AgentMessageRole.Reasoning, thinking.Role);
        Assert.AreEqual("先看一下拓扑。", thinking.Text);

        // The prefixes that were recognised before must keep mapping the way they did.
        Assert.AreEqual(AgentMessageRole.User, AgentMessageViewModel.FromLogLine("[User] do it").Role);
        Assert.AreEqual(AgentMessageRole.Assistant, AgentMessageViewModel.FromLogLine("[Agent] done").Role);
        Assert.AreEqual(AgentMessageRole.Error, AgentMessageViewModel.FromLogLine("[Error] boom").Role);
        Assert.AreEqual(AgentMessageRole.Plain, AgentMessageViewModel.FromLogLine("just a line").Role);
    }

    [TestMethod]
    public void ConversationMarkdown_WrapsReasoningInAFence()
    {
        // The Avalonia demo hands this string straight to a Markdown control, so the fence has to survive
        // the hop through the view model — a rebuild that dropped it would leave the panel looking exactly
        // as it did before the fence existed.
        var tree = new TreeViewModel();

        ((AgentHelper)tree.Helper).Transcript.AppendReasoning("先看一下拓扑。");

        StringAssert.Contains(tree.ConversationMarkdown, "```thinking");
    }
}
