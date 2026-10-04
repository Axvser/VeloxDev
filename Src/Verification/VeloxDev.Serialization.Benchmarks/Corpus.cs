using VeloxDev.WorkflowSystem;

namespace VeloxDev.Serialization.Benchmarks;

/// <summary>Builds the object graphs the benchmarks read and write.</summary>
internal static class Corpus
{
    // 形状照黄金文件 tree.json 放大：节点挂两个槽位、相邻节点之间连一条链路。
    // 这样一来文档里该有的难处都在：$id/$ref 引用表、节点对树的 Parent 回指、
    // 以及 LinksMap —— 接口键套着接口键的嵌套字典，写读两侧最贵的一条路。
    // 缩放系数设成 2 是为了让 Anchor/Size 的 getter 真的走一次折叠，而不是原样返回。
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
