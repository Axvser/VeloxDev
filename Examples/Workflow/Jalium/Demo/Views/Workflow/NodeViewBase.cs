using System.ComponentModel;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>
/// Base for the full-demo's per-node-type views. These are VISUAL ONLY: the NodeEditorSurface owns all
/// interaction (drag, connect, pan) and positions each view at the node's anchor + layout offset, so a
/// node view must NOT set its own Canvas.Left/Top and must NOT wire its own drag/connection handling.
/// The base draws the card chrome (via <see cref="NodeChrome.Card"/>) and, in <see cref="OnPostRender"/>,
/// the port circles at the exact <see cref="NodePorts"/> centers the surface hit-tests against.
/// </summary>
internal abstract class NodeViewBase : Canvas
{
    private SlotState[] _inputStates = [];
    private SlotState[] _outputStates = [];

    /// <summary>The node view-model bound to this card.</summary>
    protected IWorkflowNodeViewModel Node { get; private set; } = null!;

    /// <summary>The status badge in the card header (updated by subclasses on property changes).</summary>
    protected TextBlock? StatusText { get; private set; }

    /// <summary>Accent color of the card border (per node type).</summary>
    protected abstract Brush Accent { get; }

    /// <summary>Initial status-badge text for the given node.</summary>
    protected abstract string InitialStatus(IWorkflowNodeViewModel node);

    /// <summary>Builds the card body content into the content grid.</summary>
    protected abstract void Build(IWorkflowNodeViewModel node, Grid content);

    /// <summary>Called for node property changes so subclasses can update header/status text.</summary>
    protected virtual void OnNodePropertyChanged(string propertyName)
    {
    }

    /// <summary>Adds right-aligned port-name labels in the content area, one per output row, aligned
    /// to the port circles the base draws (both use NodePorts' row pitch).</summary>
    protected void AddOutputLabels(Grid content)
    {
        var outputs = NodePorts.Outputs(Node);
        var grid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 10, 0),
        };
        for (int i = 0; i < outputs.Count; i++)
        {
            if (outputs[i].Name.Length == 0)
            {
                continue;
            }

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.FromPixels(NodePorts.RowH) });
            var label = new TextBlock
            {
                Text = outputs[i].Name,
                Foreground = NodeChrome.SubFg,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetRow(label, grid.RowDefinitions.Count - 1);
            grid.Children.Add(label);
        }

        content.Children.Add(grid);
    }

    /// <summary>Binds the view to a node and builds its chrome + content.</summary>
    /// <summary>The node's DESIGN (scale-1) size, captured at Bind. The card and ports are laid out at this
    /// size and the whole view is scaled by <c>RenderTransform</c> = (Width/DesignWidth, Height/DesignHeight),
    /// so the content shrinks by 1/scale when the workspace zooms — mirroring the WPF node Viewbox.</summary>
    public double DesignWidth { get; private set; }
    public double DesignHeight { get; private set; }

    public void Bind(IWorkflowNodeViewModel node)
    {
        Node = node;
        Children.Clear();
        Width = node.Size.Width;
        Height = node.Size.Height;
        DesignWidth = node.Size.Width;
        DesignHeight = node.Size.Height;
        _inputStates = new SlotState[NodePorts.Inputs(node).Count];
        _outputStates = new SlotState[NodePorts.Outputs(node).Count];

        var card = NodeChrome.Card(node.Size.Width, node.Size.Height, Accent, NodePorts.TitleOf(node),
            InitialStatus(node), out _, out _, out var statusText, out var content);
        StatusText = statusText;
        Canvas.SetLeft(card, 0);
        Canvas.SetTop(card, 0);
        Children.Add(card);

        Build(node, content);
        if (node is INotifyPropertyChanged notify)
        {
            notify.PropertyChanged += OnNodePropertyChangedHandler;
        }
    }

    /// <summary>Writes port colors for the given input/output state arrays and repaints.</summary>
    public void SetPortStates(SlotState[] inputs, SlotState[] outputs)
    {
        if (inputs.Length == _inputStates.Length)
        {
            Array.Copy(inputs, _inputStates, inputs.Length);
        }

        if (outputs.Length == _outputStates.Length)
        {
            Array.Copy(outputs, _outputStates, outputs.Length);
        }

        InvalidateVisual();
    }

    private void OnNodePropertyChangedHandler(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not null)
        {
            OnNodePropertyChanged(e.PropertyName);
        }
    }

    /// <summary>Scales the card content to the current (collapsed) size so the content shrinks by 1/scale
    /// when the workspace zooms — mirroring the WPF node Viewbox. Call after the view is positioned/sized.</summary>
    public void ApplyScale()
    {
        RenderTransform = new ScaleTransform(
            DesignWidth == 0 ? 1 : Width / DesignWidth,
            DesignHeight == 0 ? 1 : Height / DesignHeight);
    }

    /// <summary>Draws the port circles on top of the card, at the DESIGN-size centers (the RenderTransform
    /// scales them to the collapsed size), matching the world centers the surface hit-tests.</summary>
    protected override void OnPostRender(DrawingContext dc)
    {
        base.OnPostRender(dc);

        var inputs = NodePorts.Inputs(Node);
        for (int i = 0; i < inputs.Count; i++)
        {
            double y = inputs.Count > 1 ? NodePorts.TitleBarH + NodePorts.RowH * i + NodePorts.RowH / 2.0 : DesignHeight / 2.0;
            dc.DrawEllipse(PortBrush(_inputStates[i]), null, new Point(NodePorts.InputPortX, y), 9, 9);
        }

        var outputs = NodePorts.Outputs(Node);
        for (int i = 0; i < outputs.Count; i++)
        {
            double y = NodePorts.TitleBarH + NodePorts.RowH * i + NodePorts.RowH / 2.0;
            dc.DrawEllipse(PortBrush(_outputStates[i]), null, new Point(DesignWidth - NodePorts.OutputInset, y), 7, 7);
        }
    }

    private static Brush PortBrush(SlotState s)
    {
        bool sender = s.HasFlag(SlotState.Sender);
        bool receiver = s.HasFlag(SlotState.Receiver);
        if (sender && receiver) return new SolidColorBrush(Colors.Violet);
        if (sender) return new SolidColorBrush(Colors.Tomato);
        if (receiver) return new SolidColorBrush(Colors.Lime);
        return new SolidColorBrush(Color.FromArgb(0xDD, 0x1E, 0x1E, 0x1E));
    }
}
