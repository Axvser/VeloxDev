using VeloxDev.AI;

namespace VeloxDev.Core.Test.AI;

[TestClass]
public class AgentTypeResolverTests
{
    [TestMethod]
    public void ResolveType_KnownType_Succeeds()
    {
        var type = AgentTypeResolver.ResolveType("System.String");
        Assert.IsNotNull(type);
        Assert.AreEqual(typeof(string), type);
    }

    [TestMethod]
    public void ResolveType_Null_ReturnsNull()
    {
        Assert.IsNull(AgentTypeResolver.ResolveType(null!));
    }

    [TestMethod]
    public void ResolveType_Empty_ReturnsNull()
    {
        Assert.IsNull(AgentTypeResolver.ResolveType(""));
        Assert.IsNull(AgentTypeResolver.ResolveType("   "));
    }

    [TestMethod]
    public void ResolveType_NonExistent_ReturnsNull()
    {
        Assert.IsNull(AgentTypeResolver.ResolveType("This.Type.Does.Not.Exist"));
    }

    [TestMethod]
    public void ResolveType_FromLoadedAssembly_Succeeds()
    {
        // This type is in the current test assembly's dependencies
        var type = AgentTypeResolver.ResolveType("VeloxDev.AI.AgentTypeResolver");
        Assert.IsNotNull(type);
    }
}
