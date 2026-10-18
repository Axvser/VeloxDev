using System.Runtime.CompilerServices;
using Demo.Workflow;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VeloxDev.WorkflowSystem;

namespace Demo.Views;

/// <summary>
/// Paints the Agent working set on a node card. WinUI has no style triggers, so the card's own Border is
/// recoloured in place; the visual tree is searched once after Loaded and the original values are restored
/// when the node leaves the set.
/// </summary>
public static class AgentSelectionVisual
{
    private static readonly ConditionalWeakTable<UserControl, Attachment> Attachments = new();

    /// <summary>Wires one node view to its node's Agent selection. Call from the view's constructor.</summary>
    public static void Attach(UserControl view)
    {
        var attachment = Attachments.GetValue(view, static v => new Attachment(v));
        view.DataContextChanged += (_, _) => attachment.Bind();
        view.Loaded += (_, _) => attachment.Bind();
        view.Unloaded += (_, _) => attachment.Detach();
        attachment.Bind();
    }

    private sealed class Attachment(UserControl view)
    {
        private IWorkflowNodeViewModel? _node;
        private Border? _card;
        private Brush? _originalBrush;
        private Thickness _originalThickness;

        internal void Bind()
        {
            if (view.DataContext is not IWorkflowNodeViewModel node)
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
                if (_originalBrush is null)
                {
                    _originalBrush = card.BorderBrush;
                    _originalThickness = card.BorderThickness;
                }

                card.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xA7, 0x8B, 0xFA));
                card.BorderThickness = new Thickness(2);
            }
            else if (_originalBrush is not null)
            {
                card.BorderBrush = _originalBrush;
                card.BorderThickness = _originalThickness;
                _originalBrush = null;
            }
        }

        private static Border? FindCard(DependencyObject root)
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is Border border) return border;
                if (FindCard(child) is { } nested) return nested;
            }

            return null;
        }
    }
}
