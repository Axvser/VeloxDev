using VeloxDev.WorkflowSystem;
using WfSize = VeloxDev.WorkflowSystem.Size;

namespace VeloxDev.Adapters.Benchmarks.Headed;

/// <summary>A deterministic workflow tree used by the headed view-pipeline benchmark.</summary>
/// <remarks>
/// Nodes form a near-square grid and links stay local to adjacent nodes, so the visible set stays a
/// realistic few hundred items during scrolling rather than covering the whole graph.
/// </remarks>
public sealed class SyntheticWorkflow
{
    private SyntheticWorkflow(TreeDefaultViewModel tree, double contentWidth, double contentHeight)
    {
        Tree = tree;
        ContentWidth = contentWidth;
        ContentHeight = contentHeight;
    }

    /// <summary>Gets the generated tree.</summary>
    public TreeDefaultViewModel Tree { get; }

    /// <summary>Gets the full content width including the grid margins.</summary>
    public double ContentWidth { get; }

    /// <summary>Gets the full content height including the grid margins.</summary>
    public double ContentHeight { get; }

    /// <summary>Builds the deterministic tree and its spatial helper.</summary>
    /// <param name="options">The scenario that defines counts, geometry and the random seed.</param>
    /// <returns>The created workflow.</returns>
    public static SyntheticWorkflow Create(HeadedOptions options)
    {
        var tree = new TreeDefaultViewModel();
        tree.SetHelper(new TreeHelper<TreeDefaultViewModel>(options.CellSize));

        var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(options.Nodes * 1.6d)));
        var rows = (int)Math.Ceiling(options.Nodes / (double)columns);
        const double stepX = 260d;
        const double stepY = 150d;
        const double origin = 40d;

        var inputs = new ISlotComponent[options.Nodes];
        var outputs = new ISlotComponent[options.Nodes];
        for (var index = 0; index < options.Nodes; index++)
        {
            var column = index % columns;
            var row = index / columns;
            var node = new NodeDefaultViewModel
            {
                Parent = tree,
                Anchor = new Anchor(origin + column * stepX, origin + row * stepY, 0),
                Size = new WfSize(options.NodeWidth, options.NodeHeight),
            };

            var input = new SlotDefaultViewModel { Parent = node, Channel = SlotChannel.MultipleBoth };
            var output = new SlotDefaultViewModel { Parent = node, Channel = SlotChannel.MultipleBoth };
            node.Slots.Add(input);
            node.Slots.Add(output);
            inputs[index] = input;
            outputs[index] = output;
            tree.Nodes.Add(node);
        }

        var random = new Random(options.Seed);
        for (var index = 0; index < options.Links && options.Nodes >= 2; index++)
        {
            // Keep links local: a random long link's endpoint union covers a huge area and would
            // make almost every link "visible" in every viewport, which is not a realistic graph.
            var sender = random.Next(options.Nodes);
            var span = 1 + random.Next(3);
            var receiver = sender + span < options.Nodes ? sender + span : sender - span;
            if (receiver < 0)
            {
                receiver = sender + 1 < options.Nodes ? sender + 1 : sender - 1;
            }

            if (receiver == sender)
            {
                receiver = (receiver + 1) % options.Nodes;
            }

            tree.Links.Add(new LinkDefaultViewModel
            {
                Sender = outputs[sender],
                Receiver = inputs[receiver],
                IsVisible = true,
            });
        }

        var contentWidth = origin * 2d + (columns - 1) * stepX + options.NodeWidth;
        var contentHeight = origin * 2d + (rows - 1) * stepY + options.NodeHeight;
        tree.Layout.OriginSize = new WfSize(contentWidth, contentHeight);
        return new SyntheticWorkflow(tree, contentWidth, contentHeight);
    }
}
