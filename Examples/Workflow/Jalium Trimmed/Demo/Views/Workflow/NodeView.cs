using System.Collections.Specialized;
using System.ComponentModel;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>A poolable node card for the node-editor surface: white rounded card, semi-bold title,
/// filled-circle ports at the exact centers the surface hit-tests. The node comes from DataContext;
/// position/size track the node's Anchor/Size + layout offset (world + ActualOffset). Port colors
/// are model-driven — read from each slot's State (maintained by Core), like the other GUI adapters.
/// The card is drawn at the DESIGN size on an inner canvas inside a Viewbox that scales it (chrome,
/// text and ports together) to the collapsed node box — mirroring the WPF node Viewbox.</summary>
public class NodeView : Canvas
{
    private const string FontFamilyName = "Segoe UI";

    private static readonly SolidColorBrush s_titleBrush = new(Color.FromArgb(0xDD, 0x1E, 0x1E, 0x1E));
    private static readonly SolidColorBrush s_standByBrush = new(Color.FromArgb(0xDD, 0x1E, 0x1E, 0x1E));
    private static readonly SolidColorBrush s_senderBrush = new(Color.FromRgb(0xFF, 0x63, 0x47));
    private static readonly SolidColorBrush s_receiverBrush = new(Color.FromRgb(0x32, 0xCD, 0x32));
    private static readonly SolidColorBrush s_bothBrush = new(Color.FromRgb(0xEE, 0x82, 0xEE));

    private readonly Viewbox _viewbox;
    private readonly NodeCardLayer _layer;

    private IWorkflowNodeViewModel? _node;
    private INotifyPropertyChanged? _layoutNotify;
    private PropertyChangedEventHandler? _layoutHandler;
    private INotifyCollectionChanged? _slotsNotify;

    public NodeView()
    {
        // The card draws at the DESIGN size on an inner canvas; the Viewbox fills the collapsed node box
        // and scales it (and the ports drawn with it) by 1/scale — mirroring the WPF node Viewbox.
        _layer = new NodeCardLayer(this) { Width = SlotView.DesignWidth, Height = SlotView.DesignHeight };
        _viewbox = new Viewbox { Child = _layer, Stretch = Stretch.Uniform };
        Canvas.SetLeft(_viewbox, 0);
        Canvas.SetTop(_viewbox, 0);
        Children.Add(_viewbox);

        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (_node is INotifyPropertyChanged old) old.PropertyChanged -= OnNodeChanged;
        UnsubscribeLayout();
        UnsubscribeSlots();
        _node = DataContext as IWorkflowNodeViewModel;
        if (_node is INotifyPropertyChanged notify) notify.PropertyChanged += OnNodeChanged;
        if (_node?.Parent?.Layout is INotifyPropertyChanged layout)
        {
            _layoutNotify = layout;
            _layoutHandler = (_, _) => ApplyPosition();
            layout.PropertyChanged += _layoutHandler;
        }

        // Repaint when slots are added/removed (selector switch) or a slot's State changes
        // (connection created/deleted — Core's UpdateState), so port colors stay current.
        if (_node?.Slots is INotifyCollectionChanged slots)
        {
            _slotsNotify = slots;
            slots.CollectionChanged += OnSlotsChanged;
        }
        if (_node is not null)
        {
            foreach (var slot in _node.Slots)
            {
                if (slot is INotifyPropertyChanged sp) sp.PropertyChanged += OnSlotChanged;
            }
        }

        ApplyPosition();
        _layer.InvalidateVisual();
    }

    private void UnsubscribeSlots()
    {
        if (_slotsNotify is not null)
        {
            _slotsNotify.CollectionChanged -= OnSlotsChanged;
            _slotsNotify = null;
        }
        if (_node is not null)
        {
            foreach (var slot in _node.Slots)
            {
                if (slot is INotifyPropertyChanged sp) sp.PropertyChanged -= OnSlotChanged;
            }
        }
    }

    private void OnSlotsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is INotifyPropertyChanged sp) sp.PropertyChanged += OnSlotChanged;
            }
        }
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is INotifyPropertyChanged sp) sp.PropertyChanged -= OnSlotChanged;
            }
        }

        _layer.InvalidateVisual();
    }

    private void OnSlotChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowSlotViewModel.State))
        {
            _layer.InvalidateVisual();
        }
    }

    private void UnsubscribeLayout()
    {
        if (_layoutNotify is not null)
        {
            _layoutNotify.PropertyChanged -= _layoutHandler;
            _layoutNotify = null;
            _layoutHandler = null;
        }
    }

    private static Brush SlotBrush(SlotState s)
    {
        bool sender = s.HasFlag(SlotState.Sender);
        bool receiver = s.HasFlag(SlotState.Receiver);
        if (sender && receiver) return s_bothBrush;
        if (sender) return s_senderBrush;
        if (receiver) return s_receiverBrush;
        return s_standByBrush;
    }

    private void OnNodeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor) or nameof(IWorkflowNodeViewModel.Size))
        {
            ApplyPosition();
        }
    }

    private void ApplyPosition()
    {
        if (_node is null) return;
        var origin = _node.Parent?.Layout.ActualOffset ?? new Offset();
        Canvas.SetLeft(this, _node.Anchor.Horizontal + origin.Horizontal);
        Canvas.SetTop(this, _node.Anchor.Vertical + origin.Vertical);
        Width = _node.Size.Width;
        Height = _node.Size.Height;
        _viewbox.Width = Width;
        _viewbox.Height = Height;
    }

    /// <summary>Draws the card at the DESIGN size; the Viewbox scales the whole output (chrome, text and
    /// ports) to the collapsed box by 1/scale when the workspace zooms.</summary>
    private void DrawCard(DrawingContext dc)
    {
        if (_node is null) return;
        dc.DrawRoundedRectangle(new SolidColorBrush(Colors.White),
            new Pen(new SolidColorBrush(Color.FromArgb(0x33, 0x1E, 0x1E, 0x1E)), 1),
            new Rect(0, 0, SlotView.DesignWidth, SlotView.DesignHeight), 6, 6);

        var title = new FormattedText(SlotView.TitleOf(_node), FontFamilyName, 14)
        {
            Foreground = s_titleBrush,
            FontWeight = 600,
        };
        dc.DrawText(title, new Point(12, 9));

        var inputs = SlotView.Inputs(_node);
        if (inputs.Count > 0)
        {
            dc.DrawEllipse(SlotBrush(inputs[0].Slot.State), null,
                new Point(SlotView.InputPortX, SlotView.DesignHeight / 2.0), 9, 9);
        }

        var outputs = SlotView.Outputs(_node);
        for (int i = 0; i < outputs.Count; i++)
        {
            double rowCenter = SlotView.TitleBarH + SlotView.RowH * i + SlotView.RowH / 2.0;
            if (outputs[i].Name.Length > 0)
            {
                var label = new FormattedText(outputs[i].Name, FontFamilyName, 12) { Foreground = s_titleBrush };
                TextMeasurement.MeasureText(label);
                dc.DrawText(label, new Point(SlotView.DesignWidth - 32 - label.Width, rowCenter - label.Height / 2.0));
            }

            dc.DrawEllipse(SlotBrush(outputs[i].Slot.State), null,
                new Point(SlotView.DesignWidth - SlotView.OutputInset, rowCenter), 7, 7);
        }
    }

    /// <summary>The design-size canvas inside the Viewbox that self-draws the card (chrome, text, ports)
    /// at DESIGN coordinates; the Viewbox scales it to the collapsed node box.</summary>
    private sealed class NodeCardLayer : Canvas
    {
        private readonly NodeView _owner;

        public NodeCardLayer(NodeView owner) => _owner = owner;

        protected override void OnRender(DrawingContext dc) => _owner.DrawCard(dc);
    }
}
