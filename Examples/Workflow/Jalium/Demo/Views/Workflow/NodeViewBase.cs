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
/// The base builds the card chrome (via <see cref="NodeChrome.Card"/>) on a design-size canvas inside a
/// <see cref="Viewbox"/> that scales the whole card — chrome, text and the port circles drawn by
/// <see cref="CardLayer.OnPostRender"/> — to the collapsed node box, mirroring the WPF node Viewbox.
/// </summary>
internal abstract class NodeViewBase : Canvas
{
    private SlotState[] _inputStates = [];
    private SlotState[] _outputStates = [];
    private Viewbox? _viewbox;
    private CardLayer? _cardLayer;

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
    /// to the port circles the base draws (both use NodePorts' row pitch at the DESIGN size).</summary>
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

    /// <summary>The node's DESIGN (scale-1) size, captured at Bind. The card and ports are laid out at this
    /// size and the whole view is scaled by the <c>Viewbox</c> = (Width/DesignWidth, Height/DesignHeight),
    /// so the content shrinks by 1/scale when the workspace zooms — mirroring the WPF node Viewbox. The link
    /// endpoints (and surface hit-test) are computed as node.Anchor + designLocal·s by the surface.</summary>
    public double DesignWidth { get; private set; }
    public double DesignHeight { get; private set; }

    /// <summary>Binds the view to a node and builds its chrome + content.</summary>
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

        var card = NodeChrome.Card(DesignWidth, DesignHeight, Accent, NodePorts.TitleOf(node),
            InitialStatus(node), out _, out _, out var statusText, out var content);
        StatusText = statusText;

        // The card is laid out at the node's DESIGN size on an inner design canvas; a Viewbox that fills
        // the collapsed box scales the whole card (chrome, text, and the port circles drawn by
        // CardLayer.OnPostRender) by 1/scale when the workspace zooms — mirroring the WPF node Viewbox.
        _cardLayer = new CardLayer(this) { Width = DesignWidth, Height = DesignHeight };
        Canvas.SetLeft(card, 0);
        Canvas.SetTop(card, 0);
        _cardLayer.Children.Add(card);
        _viewbox = new Viewbox { Child = _cardLayer, Stretch = Stretch.Uniform };
        Canvas.SetLeft(_viewbox, 0);
        Canvas.SetTop(_viewbox, 0);
        Children.Add(_viewbox);

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

        _cardLayer?.InvalidateVisual();
    }

    private void OnNodePropertyChangedHandler(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not null)
        {
            OnNodePropertyChanged(e.PropertyName);
        }
    }

    /// <summary>Scales the whole card (chrome, text, ports) to the current (collapsed) size: resize the
    /// Viewbox so the design-size card shrinks by 1/scale when the workspace zooms — mirroring the WPF
    /// node Viewbox. Call after the view is sized.</summary>
    public void ApplyScale()
    {
        if (_viewbox is not null)
        {
            _viewbox.Width = Width;
            _viewbox.Height = Height;
        }
    }

    /// <summary>Draws the port circles at the DESIGN-size local centers; the Viewbox scales them to the
    /// collapsed size, matching the link endpoints (node.Anchor + designLocal·s).</summary>
    private void DrawPorts(DrawingContext dc)
    {
        var inputs = NodePorts.Inputs(Node);
        for (int i = 0; i < inputs.Count; i++)
        {
            dc.DrawEllipse(PortBrush(_inputStates[i]), null, NodePorts.InputCenterLocalDesign(Node, i, DesignHeight), 9, 9);
        }

        var outputs = NodePorts.Outputs(Node);
        for (int i = 0; i < outputs.Count; i++)
        {
            dc.DrawEllipse(PortBrush(_outputStates[i]), null, NodePorts.OutputCenterLocalDesign(Node, i, DesignWidth), 7, 7);
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

    /// <summary>The design-size canvas inside the Viewbox: holds the card border and draws the port
    /// circles in OnPostRender (which runs after children, so ports sit on top of the chrome). Both are
    /// scaled to the collapsed box by the Viewbox.</summary>
    private sealed class CardLayer : Canvas
    {
        private readonly NodeViewBase _owner;

        public CardLayer(NodeViewBase owner) => _owner = owner;

        protected override void OnPostRender(DrawingContext dc)
        {
            base.OnPostRender(dc);
            _owner.DrawPorts(dc);
        }
    }
}
