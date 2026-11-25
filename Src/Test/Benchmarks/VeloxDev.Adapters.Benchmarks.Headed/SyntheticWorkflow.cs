using System.Collections.ObjectModel;
using System.ComponentModel;
using VeloxDev.MVVM;
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
            var node = new SyntheticNode
            {
                Parent = tree,
                Anchor = new Anchor(origin + column * stepX, origin + row * stepY, 0),
                Size = new WfSize(options.NodeWidth, options.NodeHeight),
                Name = $"Node {index}",
            };

            var input = new SlotDefaultViewModel { Parent = node, Channel = SlotChannel.MultipleBoth };
            var output = new SlotDefaultViewModel { Parent = node, Channel = SlotChannel.MultipleBoth };
            var extra = new SlotDefaultViewModel { Parent = node, Channel = SlotChannel.MultipleBoth };
            node.Slots.Add(input);
            node.Slots.Add(output);
            node.Slots.Add(extra);
            node.InputSlot = input;
            node.OutputSlots.Items.Add(new SyntheticSlotItem { Name = "Out", Slot = output });
            node.OutputSlots.Items.Add(new SyntheticSlotItem { Name = "Extra", Slot = extra });
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

/// <summary>
/// A synthetic node whose shape matches the generated item templates (a display name, one input slot and an
/// output-slot list) while delegating the workflow contract to a default node model.
/// </summary>
/// <remarks>
/// <see cref="NodeDefaultViewModel"/> is sealed, so a headed benchmark that wants the published node template's
/// bindings to resolve composes it instead. Property notifications raised by the inner model are forwarded, so the
/// spatial index, arranger and slot layout all observe this instance.
/// </remarks>
public sealed class SyntheticNode : INodeComponent
{
    private readonly NodeDefaultViewModel inner = new();

    /// <summary>Creates the node and wires its notifications to this instance.</summary>
    public SyntheticNode()
    {
        inner.PropertyChanging += (_, e) => PropertyChanging?.Invoke(this, e);
        inner.PropertyChanged += (_, e) => PropertyChanged?.Invoke(this, e);
    }

    /// <summary>Gets or sets the display name bound by the published node template.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the input slot bound by the published node template.</summary>
    public SlotDefaultViewModel InputSlot { get; set; } = null!;

    /// <summary>Gets the output slots bound by the published node template.</summary>
    public SyntheticSlotList OutputSlots { get; } = new();

    /// <inheritdoc />
    public ITreeComponent? Parent { get => inner.Parent; set => inner.Parent = value; }

    /// <inheritdoc />
    public Anchor Anchor { get => inner.Anchor; set => inner.Anchor = value; }

    /// <inheritdoc />
    public Size Size { get => inner.Size; set => inner.Size = value; }

    /// <inheritdoc />
    public ObservableCollection<ISlotComponent> Slots { get => inner.Slots; set => inner.Slots = value; }

    /// <inheritdoc />
    public IVeloxCommand MoveCommand => inner.MoveCommand;

    /// <inheritdoc />
    public IVeloxCommand SetAnchorCommand => inner.SetAnchorCommand;

    /// <inheritdoc />
    public IVeloxCommand SetSizeCommand => inner.SetSizeCommand;

    /// <inheritdoc />
    public IVeloxCommand CreateSlotCommand => inner.CreateSlotCommand;

    /// <inheritdoc />
    public IVeloxCommand DeleteCommand => inner.DeleteCommand;

    /// <inheritdoc />
    public IVeloxCommand ReceiveCommand => inner.ReceiveCommand;

    /// <inheritdoc />
    public IVeloxCommand BroadcastCommand => inner.BroadcastCommand;

    /// <inheritdoc />
    public IVeloxCommand ReverseBroadcastCommand => inner.ReverseBroadcastCommand;

    /// <inheritdoc />
    public INodeHelper GetHelper() => inner.GetHelper();

    /// <inheritdoc />
    public void SetHelper(INodeHelper helper) => inner.SetHelper(helper);

    /// <inheritdoc />
    public void InitializeWorkflow()
    {
        // The inner default model initializes itself in its constructor.
    }

    /// <inheritdoc />
    public void OnPropertyChanging(string propertyName)
        => PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(propertyName));

    /// <inheritdoc />
    public void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <inheritdoc />
    public IVeloxCommand CloseCommand => inner.CloseCommand;

    /// <inheritdoc />
    public event PropertyChangingEventHandler? PropertyChanging;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>One row of <see cref="SyntheticNode.OutputSlots"/>.</summary>
public sealed class SyntheticSlotItem
{
    /// <summary>Gets or sets the row label.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the slot the row renders.</summary>
    public SlotDefaultViewModel Slot { get; set; } = null!;
}

/// <summary>The output-slot collection shape the published node template binds to.</summary>
public sealed class SyntheticSlotList
{
    /// <summary>Gets the rows materialized by the template's items control.</summary>
    public ObservableCollection<SyntheticSlotItem> Items { get; } = [];
}
