using System.Collections.Concurrent;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Serialization.Benchmarks;

/// <summary>Builds the object graphs the benchmarks read and write.</summary>
internal static class Corpus
{
    private static readonly ConcurrentDictionary<int, TreeDefaultViewModel> Built = new();

    /// <summary>
    /// The corpus for one magnitude, assembled once per run rather than once per benchmark case.
    /// </summary>
    /// <remarks>
    /// BenchmarkDotNet calls <c>[GlobalSetup]</c> for every case, so an 8-case class built the same tree eight
    /// times. At the large magnitude that was minutes of setup for a minute of measurement — the number that made
    /// a full run feel like a build. Nothing mutates the tree: the serializer writes it and never keeps a change.
    /// </remarks>
    /// <param name="nodeCount">How many nodes.</param>
    /// <returns>The shared graph.</returns>
    internal static TreeDefaultViewModel Shared(int nodeCount) => Built.GetOrAdd(nodeCount, BuildTree);

    // 形状照黄金文件 tree.json 放大：节点挂两个槽位、相邻节点之间连一条链路。
    // 这样一来文档里该有的难处都在：$id/$ref 引用表、节点对树的 Parent 回指、多态成员（VirtualLink）。
    // 缩放系数设成 2 是为了让 Anchor/Size 的 getter 真的走一次折叠，而不是原样返回。
    //
    // ⚠ **LinksMap 在这张语料里是空的，这里是 2026-10-04 改正的一句话。** 原来的注释说语料覆盖了
    // 「LinksMap —— 接口键套着接口键的嵌套字典」，实测是假的：只有 SlotEnumerator 会往 LinksMap 里写
    // （SlotEnumerator.cs:429 起），而这棵树用的是 NodeDefaultViewModel / SlotDefaultViewModel。
    // 黄金文件 tree.json 同样写着 `"LinksMap": {}`，所以不是语料造错了，是那句话写错了。
    // 后果是**这套格式最贵的那个形状没有被这张语料覆盖**。
    // 要覆盖它得让树带上 SlotEnumerator，而那会让 System.Text.Json 那一行需要转换器（它要求字典键是
    // 属性名），「配置对配置」的比较就不成立了 —— 见 StjSerializationBridge.cs 的 remarks。
    internal static TreeDefaultViewModel BuildTree(int nodeCount)
    {
        var tree = new TreeDefaultViewModel();
        tree.Layout.Scale = new Scale(2d, 2d);

        IWorkflowSlotViewModel? previousTarget = null;

        for (var i = 0; i < nodeCount; i++)
        {
            var node = new NodeDefaultViewModel();
            tree.GetHelper().CreateNode(node);
            node.Anchor = new Anchor(1400d, 900d, 0);
            node.Size = new Size(300d, 200d);

            var source = new SlotDefaultViewModel { Channel = SlotChannel.OneSource };
            node.CreateSlotCommand.Execute(source);

            var target = new SlotDefaultViewModel { Channel = SlotChannel.OneTarget };
            node.CreateSlotCommand.Execute(target);

            // 第一个节点没有前驱可连，链路从第二个节点开始。
            if (previousTarget is not null) tree.GetHelper().CreateLink(source, previousTarget);

            previousTarget = target;
        }

        return tree;
    }
}
