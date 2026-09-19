using Microsoft.Extensions.AI;
using VeloxDev.AI.Pipelines;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using VeloxDev.AI;
using VeloxDev.AI.Skills;

namespace VeloxDev.Core.Extension.Test.Agent.Skills;

/// <summary>
/// Coverage for <see cref="SkillAgentContextProvider"/> — the skill subsystem standing on its own.
/// <para>
/// This file deliberately references <b>no</b> Workflow type. That it compiles at all is part of what it
/// asserts: the provider is usable without the workflow layer, and stays that way.
/// </para>
/// </summary>
[TestClass]
public class SkillAgentContextProviderTests
{
    /// <summary>Owns one thread and installs itself as current there, the way a real dispatcher does.</summary>
    private sealed class SingleThreadContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];

        public int ThreadId { get; private set; }

        public SingleThreadContext()
        {
            var thread = new Thread(() =>
            {
                ThreadId = Environment.CurrentManagedThreadId;
                SetSynchronizationContext(this);
                foreach (var (callback, state) in _queue.GetConsumingEnumerable())
                    callback(state);
            })
            { IsBackground = true, Name = "skill-provider-context" };
            thread.Start();
            SpinWait.SpinUntil(() => ThreadId != 0, TimeSpan.FromSeconds(5));
        }

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public override void Send(SendOrPostCallback d, object? state)
        {
            if (Environment.CurrentManagedThreadId == ThreadId) { d(state); return; }

            using var done = new ManualResetEventSlim();
            Exception? failure = null;
            Post(_ =>
            {
                try { d(state); }
                catch (Exception ex) { failure = ex; }
                finally { done.Set(); }
            }, null);
            done.Wait(TimeSpan.FromSeconds(10));
            if (failure is not null) throw failure;
        }

        public void Dispose() => _queue.CompleteAdding();
    }

    private static SkillScope ScopeWithEmbeddedSkills()
    {
        var scope = new SkillScope().WithSource(new EmbeddedSkillSource("Workflow"));
        scope.Refresh();
        return scope;
    }

    private static string Invoke(AITool tool, params (string Name, object? Value)[] args)
    {
        var callArgs = new AIFunctionArguments();
        foreach (var (name, value) in args)
            if (value is not null)
                callArgs[name] = value;

        var result = ((AIFunction)tool)
            .InvokeAsync(callArgs, CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();
        return result?.ToString() ?? string.Empty;
    }

    [TestMethod]
    public void BuildContext_ContributesTheEnabledSkillCorpus()
    {
        var context = new SkillAgentContextProvider(ScopeWithEmbeddedSkills()).BuildContext();

        Assert.IsNotNull(context.Instructions);
        Assert.Contains("Skill:", context.Instructions!, "the embedded corpus is injected in full");
    }

    [TestMethod]
    public void BuildContext_ContributesTheFourSkillToolsWrapped()
    {
        var context = new SkillAgentContextProvider(ScopeWithEmbeddedSkills()).BuildContext();

        var names = context.Tools!.Select(t => t.Name).ToArray();
        CollectionAssert.AreEquivalent(SkillAgentToolkit.ToolNames, names);
        foreach (var tool in context.Tools!)
            Assert.AreEqual("TrackedAIFunction", tool.GetType().Name, $"'{tool.Name}' is not wrapped");
    }

    [TestMethod]
    public void BuildContext_ReusesTheRenderWhileNothingChanges()
    {
        var provider = new SkillAgentContextProvider(ScopeWithEmbeddedSkills());

        var first = provider.BuildContext();
        var second = provider.BuildContext();

        Assert.AreSame(first.Tools, second.Tools, "an unchanged scope must not re-wrap the tools");
        Assert.AreEqual(first.Instructions, second.Instructions);
    }

    [TestMethod]
    public void BuildContext_ReRendersAfterASkillIsSwitchedOff()
    {
        var scope = ScopeWithEmbeddedSkills();
        var provider = new SkillAgentContextProvider(scope);

        var before = provider.BuildContext();
        var body = scope.ReadSkillBody("smart-layout", scope.PromptLanguage);
        Assert.IsNotNull(body);
        Assert.Contains(body!.Trim(), before.Instructions!);

        scope.Disable("smart-layout");
        var after = provider.BuildContext();

        Assert.AreNotSame(before.Tools, after.Tools, "a changed scope must rebuild");
        Assert.IsFalse(after.Instructions!.Contains(body.Trim()), "a disabled skill must leave the prompt");
    }

    [TestMethod]
    public void BuildContext_ReRendersAfterALanguageChange()
    {
        // The prompt language is not part of the scope's Version, so it has to be part of the cache key —
        // otherwise a host that switches language keeps the corpus it first rendered.
        var scope = ScopeWithEmbeddedSkills();
        var provider = new SkillAgentContextProvider(scope);

        var english = provider.BuildContext().Instructions;
        scope.WithPromptLanguage(AgentLanguages.Chinese);
        var chinese = provider.BuildContext().Instructions;

        Assert.AreNotEqual(english, chinese, "changing the prompt language must re-render the corpus");
    }

    [TestMethod]
    public void BuildContext_ContributesNoInstructionsWhenNothingIsEnabled()
    {
        var scope = new SkillScope();          // no sources at all
        scope.Refresh();

        var context = new SkillAgentContextProvider(scope).BuildContext();

        Assert.IsNull(context.Instructions, "no enabled skill means nothing to contribute");
        Assert.IsNotNull(context.Tools, "but the tools are still offered so the model can discover that");
    }

    [TestMethod]
    public void BuildContext_NeverReturnsNull()
    {
        // The framework dereferences the result; a null there fails the whole invocation.
        Assert.IsNotNull(new SkillAgentContextProvider(new SkillScope()).BuildContext());
    }

    [TestMethod]
    public void Tools_ReadSkillTextInTheScopesLanguage()
    {
        // The tools answer in the same language the prompt advertises, which is what makes the language
        // part of the provider's cache key worth having.
        var scope = ScopeWithEmbeddedSkills().WithPromptLanguage(AgentLanguages.Chinese);
        var provider = new SkillAgentContextProvider(scope);

        var load = provider.BuildContext().Tools!.Single(t => t.Name == "load_skill");
        var content = JObject.Parse(Invoke(load, ("skillName", "smart-layout")))["content"]?.Value<string>();

        Assert.IsFalse(string.IsNullOrWhiteSpace(content), "load_skill must return the skill's text");
        Assert.IsTrue(content!.Any(c => c >= '一' && c <= '鿿'),
            "the scope's language is Chinese, so the text read back must be too");
    }

    [TestMethod]
    public void StateKeys_AreEqualForTwoProvidersOverOneScope()
    {
        // Keyed by the scope, not by a per-provider id: two providers over one scope then collide and the
        // framework fails loudly at agent construction, instead of both contributing everything twice.
        var scope = ScopeWithEmbeddedSkills();

        var first = new SkillAgentContextProvider(scope);
        var second = new SkillAgentContextProvider(scope);

        Assert.AreEqual(first.StateKeys[0], second.StateKeys[0]);
    }

    [TestMethod]
    public void StateKeys_DifferBetweenScopes()
    {
        // Two trees — or two skill roots — are an ordinary arrangement and must not collide.
        var first = new SkillAgentContextProvider(new SkillScope());
        var second = new SkillAgentContextProvider(new SkillScope());

        Assert.AreNotEqual(first.StateKeys[0], second.StateKeys[0]);
    }

    // ── The policy seam ──────────────────────────────────────────────────────

    [TestMethod]
    public void Tools_MarshalOntoThePolicyContext()
    {
        // The `AfterCall` hook runs on the marshalled thread, which is what makes it an honest place to
        // observe the affinity from.
        using var context = new SingleThreadContext();
        var scope = ScopeWithEmbeddedSkills();
        int? calledOn = null;
        var pipeline = new AgentPipeline().Use((e, next, ct) =>
        {
            if (e is AgentToolCallCompleted) calledOn = Environment.CurrentManagedThreadId;
            return next(e);
        });

        var list = new SkillAgentContextProvider(scope, new ToolPipeline { MarshalTo = () => context }, pipeline)
            .BuildContext().Tools!.Single(t => t.Name == "ListSkills");
        Invoke(list);

        Assert.AreEqual(context.ThreadId, calledOn, "a subsystem tool must run where the composing host says");
    }

    [TestMethod]
    public void WithAPolicy_ThePolicyGatesTheTool()
    {
        // The seam is how a composing host keeps its budgets applying to subsystem tools.
        var scope = ScopeWithEmbeddedSkills();
        var refusals = 0;
        var list = new SkillAgentContextProvider(
                scope,
                new ToolPipeline { Refuse = _ => { refusals++; return "refused by the composing host"; } })
            .BuildContext().Tools!.Single(t => t.Name == "ListSkills");
        var json = JObject.Parse(Invoke(list));

        Assert.AreEqual("error", json["status"]?.Value<string>());
        Assert.Contains("refused by the composing host", json["message"]?.Value<string>() ?? string.Empty);
        Assert.AreEqual(1, refusals);
    }

    [TestMethod]
    public void WithAPolicy_ThePolicyIsNotifiedAfterACall()
    {
        var scope = ScopeWithEmbeddedSkills();
        var calls = new ConcurrentQueue<string>();
        var pipeline = new AgentPipeline().Use((e, next, ct) =>
        {
            if (e is AgentToolCallCompleted done) calls.Enqueue(done.ToolName);
            return next(e);
        });

        var list = new SkillAgentContextProvider(scope, null, pipeline).BuildContext().Tools!.Single(t => t.Name == "ListSkills");
        Invoke(list);

        Assert.HasCount(1, calls);
        Assert.AreEqual("ListSkills", calls.Single());
    }
}
