using Microsoft.Agents.AI;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Threading;
using System.Linq;
using VeloxDev.AI;
using VeloxDev.AI.MCP;
using VeloxDev.AI.Skills;
using VeloxDev.AI.Workflow;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow;

/// <summary>
/// Coverage for <see cref="WorkflowAgentContextProvider"/>: that it re-renders when the scope changes,
/// reuses the previous render when nothing did, never returns null, and keeps its session-state key
/// unique per scope.
/// </summary>
[TestClass]
public class WorkflowAgentContextProviderTests
{
    private static WorkflowAgentScope ScopeWithSkills()
    {
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel()).WithSkills(new SkillScope());
        scope.Skills!.WithSource(new EmbeddedSkillSource("Workflow"));
        scope.Skills.Refresh();
        return scope;
    }

    [TestMethod]
    public void BuildContext_ContributesTheLiveCapabilityEnvelope()
    {
        // This slice used to contribute nothing, which meant the host froze the whole skeleton into
        // ChatOptions.Instructions at construction and every later scope change reached the tools but never
        // the prompt. The envelope closes that gap — a null here would put the model back on a stale
        // self-description of what it is allowed to do right now.
        var context = new WorkflowAgentContextProvider(ScopeWithSkills()).BuildContext();

        StringAssert.Contains(context.Instructions!, "Gates in force");
        StringAssert.Contains(context.Instructions!, "Tools switched off by the host");
        StringAssert.Contains(context.Instructions!, "Call budgets");
        Assert.IsNotEmpty(context.Tools!);
    }

    [TestMethod]
    public void BuildContext_NeverReturnsNull()
    {
        // A scope with no skills and no MCP servers still has to produce an AIContext: the framework
        // dereferences the result, and a null there fails the whole invocation rather than contributing
        // nothing.
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel());
        var context = new WorkflowAgentContextProvider(scope).BuildContext();

        Assert.IsNotNull(context);
        Assert.IsNotNull(context.Tools);
    }

    [TestMethod]
    public void BuildContext_AlwaysCarriesTheToolSet()
    {
        var scope = ScopeWithSkills();
        var provider = new WorkflowAgentContextProvider(scope);

        var first = provider.BuildContext();
        var second = provider.BuildContext();

        Assert.IsNotEmpty(first.Tools!, "the built-in workflow tools must be offered");
        CollectionAssert.Contains(first.Tools!.Select(t => t.Name).ToArray(), "ListNodes");
        Assert.AreEqual(first.Instructions, second.Instructions, "an unchanged scope must render the same text");
    }

    [TestMethod]
    public void BuildContext_ReusesTheRenderWhileNothingChanges()
    {
        var scope = ScopeWithSkills();
        var provider = new WorkflowAgentContextProvider(scope);

        var first = provider.BuildContext();
        var second = provider.BuildContext();

        Assert.AreSame(first.Tools, second.Tools, "an unchanged scope must not re-wrap the tool set");
        Assert.AreSame(first, second,
            "and must not re-wrap the context either — an idle turn is meant to allocate nothing at all");
    }

    [TestMethod]
    public void BuildContext_DoesNotReRenderOnASkillToggle()
    {
        // The inverse of the skill provider's own rebuild test, and the point of splitting the caches: a
        // skill switch must not churn the workflow slice, which cannot have changed.
        var scope = ScopeWithSkills();
        var provider = new WorkflowAgentContextProvider(scope);

        var before = provider.BuildContext();
        scope.Skills!.Disable("smart-layout");
        var after = provider.BuildContext();

        Assert.AreSame(before.Tools, after.Tools, "a skill toggle must not rebuild the workflow slice");
    }

    [TestMethod]
    public void ProviderInstructions_OmitTheStaticCorpus()
    {
        var scope = ScopeWithSkills();
        var staticPrompt = scope.ProvideProgressiveContextPrompt();

        // When skills are under dynamic management the provider owns them; leaving them in the static
        // prompt as well would send the whole corpus twice.
        Assert.IsFalse(staticPrompt.Contains("Skill: SlotEnumerator"),
            "the static prompt must not carry the skill corpus once the provider renders it");
    }

    [TestMethod]
    public void StateKeys_AreUniquePerScope()
    {
        // The framework throws when two providers attached to one agent share a state key, and the base
        // default is the concrete type name — so two trees would collide without an override.
        var first = new WorkflowAgentContextProvider(new WorkflowAgentScope(new TreeDefaultViewModel()));
        var second = new WorkflowAgentContextProvider(new WorkflowAgentScope(new TreeDefaultViewModel()));

        Assert.HasCount(1, first.StateKeys);
        Assert.AreNotEqual(first.StateKeys[0], second.StateKeys[0]);
    }

    [TestMethod]
    public void CreateContextProviders_ComposesWorkflowThenSkillThenMcp()
    {
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel())
            .WithMcps(new McpScope())          // deliberately attached before skills
            .WithSkills(new SkillScope());

        var providers = scope.CreateContextProviders();

        // Fixed order, not attachment order: the framework concatenates what providers contribute, so the
        // sequence the host happened to call the With* methods in must not decide how the prompt reads.
        Assert.HasCount(3, providers);
        Assert.IsInstanceOfType<WorkflowAgentContextProvider>(providers[0]);
        Assert.IsInstanceOfType<SkillAgentContextProvider>(providers[1]);
        Assert.IsInstanceOfType<McpAgentContextProvider>(providers[2]);
    }

    [TestMethod]
    public void CreateContextProviders_AppendsHostFactoriesAfterTheSubsystems()
    {
        var scope = ScopeWithSkills().WithContextProvider(s => new ExtraProvider());

        var providers = scope.CreateContextProviders();

        Assert.HasCount(3, providers, "the scope, its skill subsystem, and the host's own");
        Assert.IsInstanceOfType<ExtraProvider>(providers[^1], "a host's provider goes last");
    }

    /// <summary>A stand-in for a host-authored provider — deliberately <i>not</i> a second
    /// <see cref="WorkflowAgentContextProvider"/>, since two of those over one scope share a state key
    /// and the framework rejects that at agent construction.</summary>
    private sealed class ExtraProvider : AIContextProvider
    {
    }

    [TestMethod]
    public void CreateToolkit_ReturnsOneInstancePerScope()
    {
        // The toolkit owns the call counters and the snapshot history, so a second instance would give
        // its holder a separate budget and a separate diff baseline.
        var scope = ScopeWithSkills();

        Assert.AreSame(scope.CreateToolkit(), scope.CreateToolkit());
        Assert.AreSame(scope.CreateToolkit(), scope.CreateToolkit());
    }

    [TestMethod]
    public void CreateToolkit_FromProviderAndCaller_IsTheSameInstance()
    {
        var scope = ScopeWithSkills();

        var fromCaller = scope.CreateToolkit();
        scope.BuildDynamicTools();

        Assert.AreSame(fromCaller, scope.CreateToolkit());
    }

    // ── Attachment wiring ────────────────────────────────────────────────────

    /// <summary>Records marshalled work so the tests can prove a list really is bound to the host thread.</summary>
    private sealed class CountingContext : SynchronizationContext
    {
        public int Marshalled { get; private set; }

        public override void Send(SendOrPostCallback d, object? state)
        {
            Marshalled++;
            base.Send(d, state);
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            Marshalled++;
            base.Post(d, state);
        }
    }

    [TestMethod]
    public void WithSynchronizationContext_ReachesAnAlreadyAttachedSkillScope()
    {
        // Status.Skills is an ObservableCollection meant to be bound; if the context never reaches the
        // skill scope, a refresh mutates it from whatever thread happened to call.
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel()).WithSkills(new SkillScope());
        var context = new CountingContext();

        scope.WithSynchronizationContext(context);
        scope.Skills!.WithSource(new EmbeddedSkillSource("Workflow"));
        scope.Skills.Refresh();

        Assert.IsTrue(context.Marshalled > 0, "a refresh must run through the scope's UI context");
    }

    [TestMethod]
    public void WithSkills_AfterTheContext_IsBoundToo()
    {
        // The fluent chain is the host's to order; attaching in either order must end up bound.
        var context = new CountingContext();
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel()).WithSynchronizationContext(context);

        scope.WithSkills(new SkillScope());
        scope.Skills!.WithSource(new EmbeddedSkillSource("Workflow"));
        scope.Skills.Refresh();

        Assert.IsTrue(context.Marshalled > 0, "attaching after the context must still bind the skill list");
    }

    [TestMethod]
    public void CreateSkillToolkit_FollowsThePromptLanguage()
    {
        // The tools read skill text while the prompt advertises it; if the two disagree, load_skill
        // answers in a language the conversation is not being held in.
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel())
            .WithPromptLanguage(AgentLanguages.Chinese)
            .WithSkills(new SkillScope());

        Assert.AreEqual(AgentLanguages.Chinese, scope.CreateSkillToolkit().Language);
    }

    [TestMethod]
    public void CreateSkillToolkit_WithoutSkills_SaysWhatIsMissing()
    {
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel());

        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => scope.CreateSkillToolkit());
        Assert.Contains("WithSkills", ex.Message);
    }

    [TestMethod]
    public void WithSkills_MissingRoot_StillExposesTheEmbeddedCorpus()
    {
        // A host may point at a directory that does not exist yet (a skill folder it creates at runtime);
        // the library's own documents must still be discoverable.
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel())
            .WithSkills(Path.Combine(Path.GetTempPath(), "veloxdev-nonexistent-" + Guid.NewGuid().ToString("N")));

        Assert.HasCount(7, scope.Skills!.Status.Skills);
        Assert.AreEqual(7, scope.Skills.Status.ActiveCount);
    }
}
