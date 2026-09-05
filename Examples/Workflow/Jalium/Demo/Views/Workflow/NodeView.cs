using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>Generic node card catch-all: the card chrome (title + ports) is provided by the base.
/// Used only for node types without a dedicated view in <see cref="NodeViewFactory"/>; it no longer
/// depends on the pruned plain-worker view-model.</summary>
internal sealed class NodeView : NodeViewBase
{
    protected override Brush Accent => NodeChrome.DefaultBorder;

    protected override string InitialStatus(IWorkflowNodeViewModel node) => string.Empty;

    protected override void Build(IWorkflowNodeViewModel node, Grid content)
    {
        // No worker-specific body: specific node types (Controller/Timer/Enum/Python) get
        // dedicated views, so a generic catch-all card just shows chrome + ports.
    }

    protected override void OnNodePropertyChanged(string propertyName)
    {
        // No worker-status reads to update.
    }
}
