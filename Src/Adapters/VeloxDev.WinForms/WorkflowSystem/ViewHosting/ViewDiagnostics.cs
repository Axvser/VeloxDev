using System;
using System.Diagnostics;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Writes view-pipeline failures to the debug output without interrupting the batch.</summary>
public sealed class ViewDiagnostics : IViewDiagnostics<IWorkflowComponent>
{
    /// <inheritdoc />
    public void Failed(IWorkflowComponent? item, ViewStage stage, Exception? exception)
    {
        Debug.WriteLine(
            $"[ViewDiagnostics] {stage} failed for {item?.GetType().Name ?? "batch"}: {exception}");
    }
}
