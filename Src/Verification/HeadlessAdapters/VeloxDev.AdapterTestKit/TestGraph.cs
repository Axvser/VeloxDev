using VeloxDev.WorkflowSystem;

namespace VeloxDev.AdapterTestKit;

/// <summary>
/// The graph every headless adapter suite drives. It is deliberately small and deterministic:
/// two nodes, one connection, and an optional far-away node that only a scrolled viewport can see.
/// </summary>
/// <remarks>
/// Built through the component helpers rather than the generated commands, because the helpers are
/// synchronous. A test that drives the command pipeline would have to await it before asserting, and
/// then a failure would report the command's state instead of the adapter's.
/// </remarks>
public sealed class TestGraph
{
    /// <summary>The tree the adapter is asked to render.</summary>
    public TreeDefaultViewModel Tree { get; }

    /// <summary>The left node; its output slot is the connection's sender.</summary>
    public NodeDefaultViewModel Left { get; }

    /// <summary>The right node; its input slot is the connection's receiver.</summary>
    public NodeDefaultViewModel Right { get; }

    /// <summary>The sender port.</summary>
    public SlotDefaultViewModel LeftOutput { get; }

    /// <summary>The receiver port.</summary>
    public SlotDefaultViewModel RightInput { get; }

    /// <summary>The single link, or <see langword="null"/> when the graph was built unconnected.</summary>
    public IWorkflowLinkViewModel? Link { get; }

    /// <summary>A node far enough away that the default viewport cannot see it.</summary>
    public NodeDefaultViewModel Far { get; }

    private TestGraph(
        TreeDefaultViewModel tree,
        NodeDefaultViewModel left,
        NodeDefaultViewModel right,
        SlotDefaultViewModel leftOutput,
        SlotDefaultViewModel rightInput,
        IWorkflowLinkViewModel? link,
        NodeDefaultViewModel far)
    {
        Tree = tree;
        Left = left;
        Right = right;
        LeftOutput = leftOutput;
        RightInput = rightInput;
        Link = link;
        Far = far;
    }

    /// <summary>Builds the graph. <paramref name="connect"/> adds the single left → right link.</summary>
    public static TestGraph Create(bool connect = true)
    {
        var tree = new TreeDefaultViewModel();

        // The default TreeHelper() has virtualization off; the surface writes Viewport unconditionally,
        // so a tree driven through an adapter must use the cell-size constructor.
        tree.SetHelper(new TreeHelper(200d));

        var left = NewNode(40, 40);
        var right = NewNode(320, 40);
        var far = NewNode(6000, 4000);

        var output = new SlotDefaultViewModel { Anchor = new Anchor(200, 85, 0) };
        var input = new SlotDefaultViewModel { Anchor = new Anchor(320, 85, 0) };

        left.GetHelper().CreateSlot(output);
        right.GetHelper().CreateSlot(input);

        tree.GetHelper().CreateNode(left);
        tree.GetHelper().CreateNode(right);
        tree.GetHelper().CreateNode(far);

        IWorkflowLinkViewModel? link = null;
        if (connect)
        {
            tree.GetHelper().SendConnection(output);
            tree.GetHelper().ReceiveConnection(input);
            link = tree.Links.Count == 1 ? tree.Links[0] : null;
        }

        return new TestGraph(tree, left, right, output, input, link, far);
    }

    private static NodeDefaultViewModel NewNode(double x, double y)
        => new()
        {
            Anchor = new Anchor(x, y, 0),
            Size = new Size(160, 90),
        };
}
