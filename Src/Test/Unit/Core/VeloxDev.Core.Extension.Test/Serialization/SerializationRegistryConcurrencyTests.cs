using System.Threading.Tasks;
using VeloxDev.Serialization;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Serialization;

/// <summary>
/// The registry serves many threads at once without a lock, and returns whole answers while it does.
/// </summary>
/// <remarks>
/// <para>
/// The lookups sit on the per-node path, so they are deliberately lock-free: the tables are published as one
/// immutable snapshot and replaced, never mutated. That is only correct if a reader always observes a
/// <i>complete</i> table — so this asserts on results, not merely on "it did not throw". A torn or half-built
/// table would surface here as a writer that is suddenly <see langword="null"/> or a name that changed.
/// </para>
/// <para>
/// Registration itself already happened: it runs from the generated <c>[ModuleInitializer]</c>s when the
/// assembly loaded, which is before any test can run. So what is exercised here is the read path under load,
/// which is the path the snapshot exists for.
/// </para>
/// </remarks>
[TestClass]
public class SerializationRegistryConcurrencyTests
{
    private static TreeDefaultViewModel BuildTree()
    {
        var tree = new TreeDefaultViewModel();
        tree.Layout.Scale = new Scale(2d, 2d);

        foreach (var (anchor, size) in new[] { (1400d, 300d), (200d, 150d) })
        {
            var node = new NodeDefaultViewModel();
            tree.GetHelper().CreateNode(node);
            node.Anchor = new Anchor(anchor, 900d, 0);
            node.Size = new Size(size, 200d);
            node.CreateSlotCommand.Execute(new SlotDefaultViewModel { Channel = SlotChannel.OneTarget });
        }

        return tree;
    }

    [TestMethod]
    public void ManyThreads_ReadTheRegistryAtOnceAndAlwaysSeeAWholeTable()
    {
        var expectedName = VeloxJsonRegistry.NameOf(typeof(TreeDefaultViewModel));

        Assert.IsNotNull(expectedName, "the generated registration must have run before this test");

        Parallel.For(0, 4000, _ =>
        {
            Assert.IsNotNull(VeloxJsonRegistry.WriterFor(typeof(TreeDefaultViewModel)));
            Assert.IsNotNull(VeloxJsonRegistry.ReaderFor(typeof(TreeDefaultViewModel)));
            Assert.AreEqual(expectedName, VeloxJsonRegistry.NameOf(typeof(TreeDefaultViewModel)));
            Assert.AreEqual(typeof(TreeDefaultViewModel), VeloxJsonRegistry.TypeOf(expectedName));
        });
    }

    [TestMethod]
    public void ManyThreads_SerializeAndDeserializeDifferentGraphsAtOnce()
    {
        // 每份文档写一次留作期望值：引用 id 是每次写各自分配的，所以并发写之间不该互相影响。
        var documents = new TreeDefaultViewModel[8];
        var expected = new string[documents.Length];

        for (var i = 0; i < documents.Length; i++)
        {
            documents[i] = BuildTree();
            expected[i] = VeloxJsonSerializer.Serialize(documents[i]);
        }

        Parallel.For(0, 64, i =>
        {
            var index = i % documents.Length;

            Assert.AreEqual(expected[index], VeloxJsonSerializer.Serialize(documents[index]),
                "a concurrent write must reproduce the same document");

            var restored = VeloxJsonSerializer.Deserialize<TreeDefaultViewModel>(expected[index]);

            Assert.IsNotNull(restored);
            Assert.HasCount(2, restored.Nodes, "a concurrent read must restore the whole graph");
        });
    }
}
