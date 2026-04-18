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
    public void GetContexts_Type_NoMatch_ReturnsEmpty()
    {
        var contexts = AgentContextReader.GetContexts(typeof(DecoratedType), AgentLanguages.Japanese);
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
