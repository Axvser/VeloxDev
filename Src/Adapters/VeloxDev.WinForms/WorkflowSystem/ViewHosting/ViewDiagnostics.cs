using System.Diagnostics;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Writes view-pipeline failures to the debug output without interrupting the batch.</summary>
public sealed class ViewDiagnostics : IViewDiagnostics<IWorkflowViewModel>
{
    /// <inheritdoc />
    public void Failed(IWorkflowViewModel? item, ViewStage stage, System.Exception? exception)
    {
        Debug.WriteLine(
            $"[ViewDiagnostics] {stage} failed for {item?.GetType().Name ?? "batch"}: {exception}");
    }
}
