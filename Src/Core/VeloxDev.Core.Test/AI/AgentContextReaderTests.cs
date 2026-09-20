using VeloxDev.AI;

namespace VeloxDev.Core.Test.AI;

[TestClass]
public class AgentContextReaderTests
{
    [AgentContext(AgentLanguages.English, "Test class for AI")]
    [AgentContext(AgentLanguages.Chinese, "AI测试类")]
    [AgentContext(AgentLanguages.English, "Second English context")]
    private sealed class DecoratedType
    {
        [AgentContext(AgentLanguages.English, "A test property")]
        public int TestProp { get; set; }

        public int NoProp { get; set; }
    }

    /// <summary>A type documented in one language only, so the fallback has somewhere to run out.</summary>
    [AgentContext(AgentLanguages.Chinese, "只有中文的说明")]
    private sealed class ChineseOnlyType
    {
        [AgentContext(AgentLanguages.Chinese, "只有中文的属性说明")]
        public int TestProp { get; set; }
    }

    [TestMethod]
    public void GetContexts_Type_English_ReturnsAll()
    {
        var contexts = AgentContextReader.GetContexts(typeof(DecoratedType), AgentLanguages.English);
        Assert.AreEqual(2, contexts.Length);
        CollectionAssert.Contains(contexts, "Test class for AI");
        CollectionAssert.Contains(contexts, "Second English context");
    }

    [TestMethod]
    public void GetContexts_Type_Chinese_ReturnsFiltered()
    {
        var contexts = AgentContextReader.GetContexts(typeof(DecoratedType), AgentLanguages.Chinese);
        Assert.AreEqual(1, contexts.Length);
        Assert.AreEqual("AI测试类", contexts[0]);
    }

    [TestMethod]
    public void GetContexts_Type_UntranslatedLanguage_FallsBackToEnglish()
    {
        // The language picks which descriptions to collect; a type that was never annotated in it still
        // has to reach the model described, and English is the corpus that is always there.
        var contexts = AgentContextReader.GetContexts(typeof(DecoratedType), AgentLanguages.Japanese);

        Assert.AreEqual(2, contexts.Length);
        CollectionAssert.Contains(contexts, "Test class for AI");
        CollectionAssert.Contains(contexts, "Second English context");
    }

    [TestMethod]
    public void GetContexts_Type_FallbackIsAllOrNothing()
    {
        // One translated description must not drag its untranslated siblings along: Chinese has its own
        // annotation here, so the English ones stay out even though both exist.
        var contexts = AgentContextReader.GetContexts(typeof(DecoratedType), AgentLanguages.Chinese);

        CollectionAssert.DoesNotContain(contexts, "Test class for AI");
        CollectionAssert.DoesNotContain(contexts, "Second English context");
    }

    [TestMethod]
    public void GetContexts_Type_NoEnglishEither_ReturnsEmpty()
    {
        // Nothing to fall back to: a Chinese-only type is simply undocumented for a Japanese reader.
        var contexts = AgentContextReader.GetContexts(typeof(ChineseOnlyType), AgentLanguages.Japanese);

        Assert.AreEqual(0, contexts.Length);
    }

    [TestMethod]
    public void GetContexts_Type_EnglishRequestNeverFallsBack()
    {
        // The fallback is a floor for non-English readers, not a merge — English has nowhere to go, so a
        // Chinese-only type reads as undocumented rather than as described in a language nobody asked for.
        var contexts = AgentContextReader.GetContexts(typeof(ChineseOnlyType), AgentLanguages.English);

        Assert.AreEqual(0, contexts.Length);
    }

    [TestMethod]
    public void GetContexts_Member_ReturnsDescriptions()
    {
        var member = typeof(DecoratedType).GetProperty(nameof(DecoratedType.TestProp))!;
        var contexts = AgentContextReader.GetContexts(member, AgentLanguages.English);
        Assert.AreEqual(1, contexts.Length);
        Assert.AreEqual("A test property", contexts[0]);
    }

    [TestMethod]
    public void GetContexts_Member_UntranslatedLanguage_FallsBackToEnglish()
    {
        // Members take the same route as types: a property is described, or it is not — never half.
        var member = typeof(DecoratedType).GetProperty(nameof(DecoratedType.TestProp))!;
        var contexts = AgentContextReader.GetContexts(member, AgentLanguages.Japanese);

        Assert.AreEqual(1, contexts.Length);
        Assert.AreEqual("A test property", contexts[0]);
    }

    [TestMethod]
    public void HasAgentContext_Decorated_ReturnsTrue()
    {
        var member = typeof(DecoratedType).GetProperty(nameof(DecoratedType.TestProp))!;
        Assert.IsTrue(AgentContextReader.HasAgentContext(member));
    }

    [TestMethod]
    public void HasAgentContext_NotDecorated_ReturnsFalse()
    {
        var member = typeof(DecoratedType).GetProperty(nameof(DecoratedType.NoProp))!;
        Assert.IsFalse(AgentContextReader.HasAgentContext(member));
    }
}
