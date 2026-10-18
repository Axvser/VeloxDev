using System.Runtime.CompilerServices;
using Demo.Workflow;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using VeloxDev.WorkflowSystem;

namespace Demo.Controls;

/// <summary>
/// Paints the Agent working set on a node card by recolouring its Border and adding a matching shadow.
/// The original stroke and shadow are restored when the node leaves the set.
/// </summary>
public static class AgentSelectionVisual
{
    private static readonly ConditionalWeakTable<ContentView, Attachment> Attachments = new();

    /// <summary>Wires one node view to its node's Agent selection. Call from the view's constructor.</summary>
    public static void Attach(ContentView view)
    {
        var attachment = Attachments.GetValue(view, static v => new Attachment(v));
        view.BindingContextChanged += (_, _) => attachment.Bind();
        view.Loaded += (_, _) => attachment.Bind();
        view.Unloaded += (_, _) => attachment.Detach();
        attachment.Bind();
    }

    private sealed class Attachment(ContentView view)
    {
        private IWorkflowNodeViewModel? _node;
        private Border? _card;
        private Brush? _originalStroke;
        private double _originalThickness;
        private Shadow? _originalShadow;

        internal void Bind()
        {
            if (view.BindingContext is not IWorkflowNodeViewModel node)
            {
                DetachNode();
                Apply(false);
                return;
            }

            if (!ReferenceEquals(_node, node))
            {
                DetachNode();
                _node = node;
                AgentSelection.Changed += OnSelectionChanged;
            }

            Apply(AgentSelection.IsSelected(node));
        }

        internal void Detach()
        {
            DetachNode();
            Apply(false);
        }

        private void DetachNode()
        {
            if (_node is not null)
                AgentSelection.Changed -= OnSelectionChanged;

            _node = null;
        }

        private void OnSelectionChanged(IWorkflowNodeViewModel node)
        {
            if (ReferenceEquals(node, _node))
                Apply(AgentSelection.IsSelected(node));
        }

        private void Apply(bool selected)
        {
            var card = _card ??= FindCard(view);
            if (card is null) return;

            if (selected)
            {
                if (_originalStroke is null)
                {
                    _originalStroke = card.Stroke;
                    _originalThickness = card.StrokeThickness;
                    _originalShadow = card.Shadow;
                }

                var brush = new SolidColorBrush(Color.FromArgb("#A78BFA"));
                card.Stroke = brush;
                card.StrokeThickness = 2;
                card.Shadow = new Shadow { Brush = brush, Radius = 18, Opacity = 0.85f, Offset = Point.Zero };
            }
            else if (_originalStroke is not null)
            {
                card.Stroke = _originalStroke;
                card.StrokeThickness = _originalThickness;
                card.Shadow = _originalShadow!;
                _originalStroke = null;
            }
        }

        private static Border? FindCard(Element element)
        {
            if (element is Border border)
                return border;

            foreach (var child in ((IVisualTreeElement)element).GetVisualChildren())
                if (child is Element nested && FindCard(nested) is { } found)
                    return found;

            return null;
        }
    }
}
