using VeloxDev.AI;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.AI;

[TestClass]
public class AgentTypeResolverTests
{
    [TestMethod]
    public void ResolveType_ATypeInTheTree_Succeeds()
    {
        // 解析走的是目录登记过的访问器 —— 那个 typeof 字面量同时把类型给裁剪器 root 住了。
        var type = AgentTypeResolver.ResolveType("VeloxDev.WorkflowSystem.SlotDefaultViewModel");

        Assert.IsNotNull(type);
        Assert.AreEqual(typeof(SlotDefaultViewModel), type);
    }

    [TestMethod]
    public void ResolveType_ATypeTheTreeDoesNotCarry_ReturnsNull()
    {
        // 封闭世界：`System.String` 这种 BCL 类型没有分片，够不着就是够不着。
        // 旧的实现会去扫已加载程序集，那正是裁剪器跟不上的那一步。
        Assert.IsNull(AgentTypeResolver.ResolveType("System.String"));
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
    public void ResolveType_AnUnannotatedFrameworkType_ReturnsNull()
    {
        // 没有 Agent 标注的框架类型不进目录，因此也解析不到 —— 这是「不留反射回退」的直接后果。
        Assert.IsNull(AgentTypeResolver.ResolveType("VeloxDev.AI.AgentTypeResolver"));
    }
}
