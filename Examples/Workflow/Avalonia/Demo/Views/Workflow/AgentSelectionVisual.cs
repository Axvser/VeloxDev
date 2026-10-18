using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Demo.Workflow;
using VeloxDev.WorkflowSystem;

namespace Demo;

/// <summary>
/// Paints the Agent working set on a node card by toggling the <c>agent-selected</c> class. The card style
/// in <c>CardTheme.axaml</c> owns the actual colours, so all five node views share one visual.
/// </summary>
public static class AgentSelectionVisual
{
    private static readonly ConditionalWeakTable<Control, Attachment> Attachments = new();

    /// <summary>Wires one node view to its node's Agent selection. Call from the view's constructor.</summary>
    public static void Attach(Control view)
    {
        var attachment = Attachments.GetValue(view, static v => new Attachment(v));
        view.DataContextChanged += (_, _) => attachment.Bind();
        view.AttachedToVisualTree += (_, _) => attachment.Bind();
        view.DetachedFromVisualTree += (_, _) => attachment.Detach();
        attachment.Bind();
    }

    private sealed class Attachment(Control view)
    {
        private IWorkflowNodeViewModel? _node;

        internal void Bind()
        {
            if (view.DataContext is not IWorkflowNodeViewModel node)
            {
                Detach();
                SetClass(false);
                return;
            }

            if (!ReferenceEquals(_node, node))
            {
                DetachNode();
                _node = node;
                AgentSelection.Changed += OnSelectionChanged;
            }

            SetClass(AgentSelection.IsSelected(node));
        }

        internal void Detach()
        {
            DetachNode();
            SetClass(false);
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
                SetClass(AgentSelection.IsSelected(node));
        }

        private void SetClass(bool selected)
        {
            if (selected) view.Classes.Add("agent-selected");
            else view.Classes.Remove("agent-selected");
        }
    }
}
