using Demo.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace VeloxDev.Core.Extension.Test.Examples;

/// <summary>
/// Coverage for the structured agent transcript the demo chat panels bind to.
/// <para>
/// It lives in this test project because that is the only one referencing <c>Lib</c>, and it is worth
/// having: seven platform panels render <see cref="AgentMessageViewModel"/> and the shape of a tool-call
/// row — its collapsed line and its parsed status — is the whole contract between them and the shared
/// layer. A silent change here breaks every panel at once without breaking a build.
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
}
