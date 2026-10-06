using Demo.ViewModels.Workflow;
using Demo.ViewModels.Workflow.Enums;
using Jalium.UI.Controls;
using VeloxDev.WorkflowSystem;
using Size = VeloxDev.WorkflowSystem.Size;

namespace Demo.Views;

/// <summary>
/// The demo's composition: the workflow tree view, and the sample model behind it.
/// </summary>
/// <remarks>
/// Nothing here wires the surface up — the tree view's markup names its own parts and
/// <c>WorkflowSurfaceBehavior</c> drives them, so this file only builds the sample data and hands the tree
/// over as the data context (the same shape the WPF demo uses).
/// </remarks>
public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();

        var tree = new TreeViewModel();
        LoadTree(tree);
        DataContext = tree;
        VetoFrameworkGestures(tree);
    }

    /// <summary>
    /// The gestures this host has claimed: Shift-drag on the empty canvas, Ctrl-drag anywhere, and Shift+wheel —
    /// the framework's own hand stands down and the host's takes over.
    /// </summary>
    /// <remarks>
    /// Subscribe, test the condition, set <c>PreventDefault</c> — the whole starting point of an interaction of
    /// one's own. The press subscription covers the blank canvas and the cards alike, because the refusal is read
    /// wherever the framework's hand would have started: pan, node drag and slot connection. The wheel is a second
    /// subscription (<c>PointerWheelChanged</c>), because zoom is a wheel gesture rather than a press. Subscribed
    /// to the tree this view builds and hands over; this demo never swaps it, so one subscription lasts the
    /// window's life.
    /// </remarks>
    private static void VetoFrameworkGestures(TreeViewModel tree)
    {
        var input = ((IInputEvents)tree.GetHelper()).Input;

        input.PointerPressed += (_, e) =>
        {
            if (e.Modifiers.HasFlag(InputModifiers.Control)
                || (e.Target is null && e.Modifiers.HasFlag(InputModifiers.Shift)))
            {
                e.Handle.PreventDefault = true;
            }
        };

        // Shift+wheel is this host's own horizontal scroll, so the framework does not zoom on it.
        input.PointerWheelChanged += (_, e) =>
        {
            if (e.Modifiers.HasFlag(InputModifiers.Shift))
            {
                e.Handle.PreventDefault = true;
            }
        };
    }

    private static void LoadTree(TreeViewModel tree)
    {
        var size = new Size(260, 180);
        var nodes = new[]
        {
            new NodeViewModel { Name = "Boolean routes", Size = size, Anchor = new Anchor { Horizontal = 80, Vertical = 80 } },
            new NodeViewModel { Name = "Voltage routes", Size = size, Anchor = new Anchor { Horizontal = 400, Vertical = 220 } },
            new NodeViewModel { Name = "Model routes", Size = size, Anchor = new Anchor { Horizontal = 720, Vertical = 80 } },
        };
        foreach (var node in nodes)
        {
            tree.CreateNodeCommand.Execute(node);
        }

        nodes[0].OutputSlots.SetSelector(typeof(bool));
        nodes[1].OutputSlots.SetSelector(typeof(VoltageRange));
        nodes[2].OutputSlots.SetSelector(typeof(ModelProtocol));
        nodes[0].InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        nodes[1].InputSlot.SetChannelCommand.Execute(SlotChannel.MultipleSources);
        nodes[2].InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
    }
}
