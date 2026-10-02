using VeloxDev.AI;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.AI;

/// <summary>
/// The generated context tree: that it registers itself, that its directories are walked without touching
/// reflection, and that an accessor can act on a real object.
/// </summary>
/// <remarks>
/// Everything here goes through the registry the way a consumer would. Nothing reflects over the types under
/// test — if it did, the test would be rooting the metadata it is trying to prove survives on its own.
/// </remarks>
[TestClass]
public class AIContextTreeTests
{
    private const string SlotComponent = "VeloxDev.WorkflowSystem.SlotDefaultViewModel";

    [TestMethod]
    public void TheFrameworkFragmentRegistersItselfWhenTheModuleLoads()
    {
        CollectionAssert.Contains(
            AIContextTreeRegistry.Fragments.Select(static f => f.AssemblyName).ToList(),
            "VeloxDev.Core",
            "a module initializer in the generated fragment must have run");
    }

    [TestMethod]
    public void TheFrameworkRootAndItsDirectoriesAreListed()
    {
        var roots = AIContextTreeRegistry.List(string.Empty).Select(static n => n.Name).ToList();
        CollectionAssert.Contains(roots, AIContextTreeRegistry.FrameworkRoot);

        var components = AIContextTreeRegistry.List("Framework/Components").Select(static n => n.Name).ToList();
        CollectionAssert.AreEqual(
            new[] { "Links", "Nodes", "Slots", "Trees" },
            components,
            "the four component kinds are directories, ordered by name");
    }

    [TestMethod]
    public void ATypeEntryIsReachedByItsFullNameAndCarriesItsMembers()
    {
        var path = AIContextTreeRegistry.PathFor(SlotComponent);
        Assert.IsNotNull(path, "the type index must resolve a registered type's full name");

        var entry = AIContextTreeRegistry.FindEntry(path);
        Assert.IsNotNull(entry);
        Assert.AreEqual(AIContextNodeKind.ComponentType, entry.Kind);
        Assert.AreEqual(SlotComponent, entry.TypeName);

        var properties = AIContextTreeRegistry.List(path + "/Properties").Select(static n => n.Name).ToList();
        CollectionAssert.Contains(properties, "Channel", "a [VeloxProperty] field is listed under the property it becomes");

        var commands = AIContextTreeRegistry.List(path + "/Commands").Select(static n => n.Name).ToList();
        CollectionAssert.Contains(commands, "SetChannelCommand", "a [VeloxCommand] method is listed under the property it becomes");
    }

    [TestMethod]
    public void AnUnannotatedTypeHasNoEntry()
    {
        // 反向对照：目录只收录被标注过的类型，随便一个框架类型不该在里面。
        Assert.IsNull(
            AIContextTreeRegistry.PathFor("VeloxDev.AI.AIContextTreeRegistry"),
            "a type with no agent annotations must not be in the tree");
    }

    [TestMethod]
    public void TheAccessorReadsAndWritesAPromotedProperty()
    {
        var accessor = AIContextTreeRegistry.FindAccessor(SlotComponent);
        Assert.IsNotNull(accessor);

        var slot = accessor.Create();
        Assert.IsInstanceOfType<SlotDefaultViewModel>(slot);

        Assert.IsTrue(accessor.TryGet(slot, "Channel", out var before));
        Assert.IsInstanceOfType<SlotChannel>(before);

        // 枚举按名字写进去 —— 走的是 Enum.Parse<TEnum>，不是运行期 Type。
        Assert.IsNull(accessor.Set(slot, "Channel", "OneSource"), "writing an enum by name must succeed");

        Assert.IsTrue(accessor.TryGet(slot, "Channel", out var after));
        Assert.AreEqual(SlotChannel.OneSource, after);
    }

    [TestMethod]
    public void TheAccessorRefusesAnUnknownMember()
    {
        var accessor = AIContextTreeRegistry.FindAccessor(SlotComponent);
        Assert.IsNotNull(accessor);

        var slot = accessor.Create();

        Assert.IsFalse(accessor.TryGet(slot, "NoSuchMember", out _));
        StringAssert.Contains(accessor.Set(slot, "NoSuchMember", 1) ?? string.Empty, "not agent-patchable");
    }

    [TestMethod]
    public void TheAccessorExecutesAPromotedCommand()
    {
        var accessor = AIContextTreeRegistry.FindAccessor(SlotComponent);
        Assert.IsNotNull(accessor);

        var slot = accessor.Create();

        Assert.IsTrue(
            accessor.TryExecuteCommand(slot, "SetChannelCommand", SlotChannel.OneBoth, out var error),
            $"the generated command path must run: {error}");
    }

    [TestMethod]
    public void AnUnknownTypeHasNoAccessor()
    {
        Assert.IsNull(AIContextTreeRegistry.FindAccessor("VeloxDev.AI.AIContextTreeRegistry"));
    }
}
