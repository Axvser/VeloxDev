using Avalonia.Controls;
using Demo.ViewModels.Workflow;
using Demo.ViewModels.Workflow.Enums;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views
{
    public partial class MainView : UserControl
    {
        public MainView()
        {
            InitializeComponent();

            var tree = new TreeViewModel();
            LoadTree(tree);
            DataContext = tree;
        }

        private void LoadTree(TreeViewModel tree)
        {
            // Shift the world origin into the visible area so nodes in every quadrant are on screen when zoomed.
            tree.Layout.NegativeOffset = new Offset(320, 260);

            var size = new Size(260, 180);
            var nodes = new[]
            {
                new NodeViewModel { Name = "Lower-right (+x,+y)", Size = size, Anchor = new Anchor { Horizontal = 140, Vertical = 140 } },
                new NodeViewModel { Name = "Lower-left  (-x,+y)", Size = size, Anchor = new Anchor { Horizontal = -140, Vertical = 140 } },
                new NodeViewModel { Name = "Upper-left  (-x,-y)", Size = size, Anchor = new Anchor { Horizontal = -140, Vertical = -140 } },
                new NodeViewModel { Name = "Upper-right (+x,-y)", Size = size, Anchor = new Anchor { Horizontal = 140, Vertical = -140 } }
            };

            foreach (var node in nodes)
            {
                tree.CreateNodeCommand.Execute(node);
            }

            // Shift-drag on empty canvas: the framework stands down and the host takes over. This is the whole
            // starting point of "a press-and-drag interaction of my own on the blank canvas" — subscribe, test the
            // condition, set PreventDefault. What happens next (a rubber band, the coordinate conversion, whatever
            // the selection looks like) is the host's, and goes here.
            ((IInputEvents)tree.GetHelper()).Input.PointerPressed += (_, e) =>
            {
                if (e.Target is null && e.Modifiers.HasFlag(InputModifiers.Shift))
                {
                    e.Handle.PreventDefault = true;
                }
            };

            nodes[0].OutputSlots.SetSelector(typeof(bool));
            nodes[1].OutputSlots.SetSelector(typeof(VoltageRange));
            nodes[2].OutputSlots.SetSelector(typeof(ModelProtocol));
            nodes[0].InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
            nodes[1].InputSlot.SetChannelCommand.Execute(SlotChannel.MultipleSources);
            nodes[2].InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        }
    }
}