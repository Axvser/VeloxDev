using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Threading.Tasks;
using VeloxDev.AI.SubAgents;
using VeloxDev.AI.Workflow;

namespace VeloxDev.Core.Extension.Test.Agent.SubAgents;

/// <summary>
/// Dispatch and poll: what the model holds while a child works, and how it gets the answer back.
/// <para>
/// The shape is forced by where a spawn runs. It is a tool body, on a thread the host's UI owns, and a
/// synchronous child would hold that thread for the child's whole conversation — so a spawn returns a handle
/// and the child runs behind it. Everything here is about that handle being trustworthy: it arrives before
/// the work is done, it can be waited on without blocking the host, it can be abandoned, and it never
/// becomes a window onto another agent's children.
/// </para>
/// </summary>
[TestClass]
public class SubAgentDispatchTests
{
    [TestMethod]
    public async Task ASpawnReturns_BeforeTheChildHasAnswered()
    {
        // The property the whole design rests on. If a spawn blocked until the child finished, a parent
        // whose turn is suspended would hold the UI thread for as long as its child took, and no amount of
        // parallel dispatch would be worth anything.
        var gate = new GateChatClient();
        await using var fx = new SubAgentFixture(client: gate);

        var id = fx.Spawn("look into it");
        gate.WaitForCalls(1);

        Assert.AreEqual(SubAgentState.Running, fx.RowOf(id).State,
            "the spawn returned while its child was still on the wire");
        Assert.IsFalse(fx.RowOf(id).IsFinished);
    }

    [TestMethod]
    public async Task SeveralChildren_AreInFlightAtOnce()
    {
        var gate = new GateChatClient();
        await using var fx = new SubAgentFixture(client: gate);

        var first = fx.Spawn("first job", ("name", "alpha"));
        var second = fx.Spawn("second job", ("name", "beta"));
        gate.WaitForCalls(2);

        Assert.AreEqual(2, fx.Rows.Count);
        Assert.AreEqual(2, fx.Rows.Count(r => r.IsRunning));
        Assert.AreEqual(2, gate.WaitingCount, "both children are genuinely stuck in the same place");

        CollectionAssert.AreEquivalent(
            new[] { "alpha", "beta" }, fx.Rows.Select(r => r.Name).ToArray());
    }

    [TestMethod]
    public async Task AChildWithNoTitle_FallsBackToANumberUnderItsOwnParent()
    {
        // The panel's label is the title the spawn gave, and a numbered stand-in when it gave none. The
        // number is per parent rather than the id's first eight characters, because its consumer is the
        // person watching the tree: the tree already says where the node sits, so an identifier there spends
        // the width a title would have used and tells them nothing. The grandchild is the point — numbering
        // that counted the whole tree instead of one parent's roster would make it 3.
        await using var fx = new SubAgentFixture(client: new InstantChatClient());

        var outer = fx.Spawn("the outer job");
        fx.Spawn("the second job");
        SubAgentFixture.InvokeTool(
            SubAgentFixture.SubAgentToolOf(fx.ChildScope(outer), "SpawnSubAgent"), ("task", "the inner job"));

        CollectionAssert.AreEqual(new[] { "子代理 1", "子代理 2" }, fx.Rows.Select(r => r.Name).ToArray());

        var grand = fx.ChildSubAgents(outer).Snapshot.Single();
        Assert.AreEqual("子代理 1", grand.Name, "a grandchild is the first child of its own parent, not the third of the tree");
    }

    [TestMethod]
    public async Task AWaitThatTimesOut_SaysSoAndLeavesThemRunning()
    {
        // A timeout is information, not a failure: the model is told which children are still going so it
        // can decide between waiting again and cancelling, rather than being left to guess.
        var gate = new GateChatClient();
        await using var fx = new SubAgentFixture(client: gate);
        var id = fx.Spawn("slow job");
        gate.WaitForCalls(1);

        var reply = JObject.Parse(fx.Invoke("WaitSubAgents", ("ids", new[] { id }), ("timeoutMs", 200)));

        Assert.IsTrue((bool?)reply["timedOut"]);
        Assert.AreEqual("Running", (string?)reply["agents"]![0]!["state"]);
        Assert.AreEqual(SubAgentState.Running, fx.RowOf(id).State, "a timeout waits for nothing to finish");
    }

    [TestMethod]
    public async Task ASecondWait_CollectsWhatTheFirstTimedOutOn()
    {
        var gate = new GateChatClient("the answer");
        await using var fx = new SubAgentFixture(client: gate);
        var id = fx.Spawn("slow job");
        gate.WaitForCalls(1);

        fx.Invoke("WaitSubAgents", ("ids", new[] { id }), ("timeoutMs", 100));
        gate.ReleaseAll();

        var reply = JObject.Parse(fx.Invoke("WaitSubAgents", ("ids", new[] { id }), ("timeoutMs", 5000)));

        Assert.IsFalse((bool?)reply["timedOut"]);
        Assert.AreEqual("Completed", (string?)reply["agents"]![0]!["state"]);
        Assert.AreEqual("the answer", (string?)reply["agents"]![0]!["result"]);
        Assert.AreEqual(SubAgentState.Completed, fx.RowOf(id).State);
    }

    [TestMethod]
    public async Task ANamedWaitIgnoresChildrenItWasNotAskedAbout()
    {
        // Naming ids has to mean something, or a model that finished with one child cannot collect it
        // without also being held up by an unrelated one.
        var gate = new GateChatClient();
        await using var fx = new SubAgentFixture(client: gate);

        var quick = fx.Spawn("the one I want");
        var slow = fx.Spawn("the one I do not");
        gate.WaitForCalls(2);

        var reply = JObject.Parse(fx.Invoke("WaitSubAgents", ("ids", new[] { quick }), ("timeoutMs", 100)));

        Assert.AreEqual(1, ((JArray)reply["agents"]!).Count, "only the named child is reported");
        Assert.AreEqual(quick, (string?)reply["agents"]![0]!["id"]);
        Assert.IsTrue(fx.RowOf(slow).IsRunning);
    }

    [TestMethod]
    public async Task CancellingAChild_ReadsAsCancelled_NotAsFailed()
    {
        // A cancelled child did not go wrong, and the panel and the model must both be able to tell the
        // difference — a UI that painted an intentional stop red teaches the user to distrust the control.
        var gate = new GateChatClient();
        await using var fx = new SubAgentFixture(client: gate);
        var id = fx.Spawn("no longer worth it");
        gate.WaitForCalls(1);

        fx.Invoke("CancelSubAgent", ("id", id));
        SubAgentFixture.WaitFor(() => fx.RowOf(id).State == SubAgentState.Cancelled);

        Assert.AreEqual(SubAgentState.Cancelled, fx.RowOf(id).State);
        Assert.IsFalse(fx.RowOf(id).HasError,
            "a cancellation is not an error to report — the row carries the empty string, not null, and the "
            + "summary answers the question a consumer would otherwise answer with a null check that passes");
        Assert.AreEqual("已取消", fx.RowOf(id).StateText);
    }

    [TestMethod]
    public async Task CancellingAChildThatAlreadyFinished_ChangesNothing()
    {
        await using var fx = new SubAgentFixture(client: new InstantChatClient("the answer"));
        var id = fx.Spawn("quick job");
        SubAgentFixture.WaitFor(() => fx.RowOf(id).State == SubAgentState.Completed);

        var reply = JObject.Parse(fx.Invoke("CancelSubAgent", ("id", id)));

        Assert.AreEqual("Completed", (string?)reply["state"], "there is nothing left to stop");
        Assert.AreEqual("the answer", fx.RowOf(id).Result);
    }

    [TestMethod]
    public async Task AnUnknownHandle_IsRefusedRatherThanInvented()
    {
        await using var fx = new SubAgentFixture();

        var reply = JObject.Parse(fx.Invoke("GetSubAgentResult", ("id", "nope")));

        Assert.AreEqual("refused", (string?)reply["status"]);
        StringAssert.Contains((string?)reply["message"] ?? string.Empty, "ListSubAgents",
            "a refusal has to point at the way to find the real handles");
    }

    [TestMethod]
    public async Task WaitingWithNothingRunning_SaysSoInsteadOfHanging()
    {
        await using var fx = new SubAgentFixture();
        var started = Environment.TickCount64;

        var reply = JObject.Parse(fx.Invoke("WaitSubAgents", ("timeoutMs", 30_000)));

        Assert.AreEqual(0, ((JArray)reply["agents"]!).Count);
        Assert.IsFalse((bool?)reply["timedOut"]);
        Assert.IsTrue(Environment.TickCount64 - started < 5_000,
            "with no children to wait for it must return at once, not serve out its timeout");
    }

    [TestMethod]
    public async Task AChildSeesItsOwnChildren_AndNobodyElses()
    {
        // Isolation is structural: the handle table lives on the scope, and a scope holds only what it
        // issued. A sibling's handle is not refused with a scolding — it simply does not exist here.
        await using var fx = new SubAgentFixture(client: new InstantChatClient());

        var mine = fx.Spawn("my job");
        var theirs = fx.Spawn("their job");

        var seen = JObject.Parse(SubAgentFixture.InvokeTool(
            SubAgentFixture.SubAgentToolOf(fx.ChildScope(mine), "ListSubAgents")));

        Assert.AreEqual(0, (int?)seen["count"], "a child has no children of its own yet");
        Assert.AreEqual(0, ((JArray)seen["agents"]!).Count);

        var peek = JObject.Parse(SubAgentFixture.InvokeTool(
            SubAgentFixture.SubAgentToolOf(fx.ChildScope(mine), "GetSubAgentResult"), ("id", theirs)));

        Assert.AreEqual("refused", (string?)peek["status"],
            "a sibling's handle must not resolve from here, or one branch could read another's work");
    }

    [TestMethod]
    public async Task DisposingTheSession_StopsAndCollectsEveryRunningChild()
    {
        // Awaited rather than fired off: a child's tool calls are marshalled onto the host's thread, so
        // disposal is a boundary — after it returns, nothing is still in flight to post into a dead pump.
        var gate = new GateChatClient();
        await using var fx = new SubAgentFixture(client: gate);

        var id = fx.Spawn("still going");
        gate.WaitForCalls(1);
        var row = fx.RowVm(id);

        await fx.SubAgents.DisposeAsync();

        Assert.AreEqual(SubAgentState.Cancelled, row.State, "disposal stops children; it does not fail them");
        Assert.IsNotNull(row.FinishedAt);
    }

    [TestMethod]
    public async Task AfterDisposal_NothingMoreCanBeSpawned()
    {
        await using var fx = new SubAgentFixture();
        await fx.SubAgents.DisposeAsync();

        var reply = JObject.Parse(fx.Invoke("SpawnSubAgent", ("task", "one more")));

        Assert.AreEqual("refused", (string?)reply["status"]);
        StringAssert.Contains((string?)reply["message"] ?? string.Empty, "disposed");
    }

    [TestMethod]
    public async Task DisposingTwice_IsHarmless()
    {
        await using var fx = new SubAgentFixture();
        await fx.SubAgents.DisposeAsync();
        await fx.SubAgents.DisposeAsync();
    }

    [TestMethod]
    public async Task SpawnAndWait_MoveTheReadCounters_NotTheWriteOnes()
    {
        // They change no part of the graph. A host reading its own usage has to see that, or delegation
        // looks like editing.
        var gate = new GateChatClient();
        await using var fx = new SubAgentFixture(client: gate, maxToolCalls: 50);

        var id = fx.Spawn("the job");
        gate.WaitForCalls(1);
        fx.Invoke("WaitSubAgents", ("ids", new[] { id }), ("timeoutMs", 50));
        fx.Invoke("ListSubAgents");

        var (total, read, write) = fx.Scope.CreateToolkit().CallUsage;
        Assert.AreEqual(3, total);
        Assert.AreEqual(3, read, "every one of them is a query");
        Assert.AreEqual(0, write, "and none of them touches the mutation budget");
    }

    [TestMethod]
    public async Task TheRoster_OnlyAppearsOnceThereAreChildren()
    {
        // The prompt is a budget too. An agent that has never delegated should not pay for the roster, and
        // one that has should not be shown a stale one.
        await using var fx = new SubAgentFixture(client: new InstantChatClient());
        var provider = fx.Scope.CreateContextProviders().OfType<SubAgentAgentContextProvider>().Single();

        var before = provider.BuildContext().Instructions ?? string.Empty;
        StringAssert.Contains(before, "sub-agents", "the standing instructions are there from the start");
        Assert.IsFalse(before.Contains("你的子代理"), "but the roster is not, because there is nothing in it");

        fx.Spawn("a named job", ("name", "scout"));

        var after = provider.BuildContext().Instructions ?? string.Empty;
        StringAssert.Contains(after, "你的子代理");
        StringAssert.Contains(after, "scout");
    }

    [TestMethod]
    public async Task TheStandingText_MakesDelegationARule_NotAPermission()
    {
        // The wording behind the mandate, pinned as a string so that a later round cannot soften it back. A
        // "you may dispatch" sentence answers a question the model is not asking — it already assumes nothing
        // forbids delegating — and the cost of not delegating is invisible to it, because doing the work
        // itself costs one turn and is never wrong. So what has to survive here is the kind of work *and* the
        // must. Whether a real model obeys it is the separate question SubAgentLiveTests exists for.
        await using var fx = new SubAgentFixture();

        var prompt = SubAgentFixture.PromptOf(fx.Scope);

        StringAssert.Contains(prompt, "must be dispatched to a sub-agent",
            "delegation is stated as required for a kind of work, not as something merely allowed");
        StringAssert.Contains(prompt, "web search",
            "and the kind of work is named, so the model does not have to infer which tasks qualify");
        StringAssert.Contains(prompt, "omit them to hand down the lot",
            "what a spawn hands down is stated where the model reads it, not only in the tool schema");

        // The escape clause is the part that has to stay narrow, and it used to be wide enough to swallow the
        // rule: "only when it is one call you already know how to make" is satisfied by every read there is,
        // because a model knows how to make all of them. Measured against a six-chapter corpus, that wording
        // produced six calls in the model's own context and no child at all. The threshold is the *work* now —
        // material gathered rather than a value read off — so this pins that it cannot drift back.
        Assert.IsFalse(prompt.Contains("one call you already know how to make"),
            "the exemption is about the work, not about how easy each step is");
    }

    [TestMethod]
    public async Task TheStandingText_AsksForATitle_BecauseThePanelShowsOne()
    {
        // The same reasoning as the pin above, applied to the one argument whose consumer is neither the
        // model nor the child but the person watching the panel. Nothing the model can observe would tell it
        // that the title matters, or that "node-counter" is the wrong register for something a human reads —
        // and an unfilled optional argument is silent rather than an error. So it is asked for in the standing
        // text and not only in the parameter schema, which is the half a model filling arguments from memory
        // may never read again. Whether a real model then fills it is the separate question
        // SubAgentLiveTests exists for.
        await using var fx = new SubAgentFixture();

        var prompt = SubAgentFixture.PromptOf(fx.Scope);

        StringAssert.Contains(prompt, "Title each one with `name`",
            "the title is asked for where the model reads it every turn, not only in the parameter description");
        StringAssert.Contains(prompt, "what the user reads on the panel",
            "and the reason given is the one the model will act on: this string has a reader who is not it");
    }
}
