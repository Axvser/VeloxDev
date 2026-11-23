using System;
using System.Collections.Generic;
using System.Windows.Forms;
using VeloxDev.Threading;
using VeloxDev.WorkflowSystem;
using VisualPanel = VeloxDev.WinForms.Xaml.Visuals.Controls.Panel;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Carries materialization batches to the WinForms message loop, or runs them inline when there is none.</summary>
/// <remarks>
/// A hosted <see cref="VeloxDev.WinForms.Xaml.Visuals.VisualHost"/> inside a running WinForms message loop takes the batch through
/// <c>BeginInvoke</c>, so a pan does not create every newly visible view on the input frame. A canvas that is not
/// parented to a surface, whose surface has no handle, or a headless test host with a handle but no message loop
/// runs the batch synchronously; that is what makes headless tests and the benchmark host deterministic.
/// </remarks>
public sealed class ViewScheduler : IViewScheduler<NonPriority>
{
    private readonly VisualPanel _host;
    private readonly Queue<Action> _inline = new();
    private bool _draining;

    /// <summary>Creates a scheduler for the canvas whose surface receives the batches.</summary>
    /// <param name="host">The canvas that owns the views.</param>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> is <see langword="null"/>.</exception>
    public ViewScheduler(VisualPanel host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <inheritdoc />
    public NonPriority BatchPriority => default;

    /// <inheritdoc />
    public bool Schedule(Action batch, NonPriority priority)
    {
        if (batch is null) throw new ArgumentNullException(nameof(batch));

        if (_host.Surface is { IsDisposed: false, IsHandleCreated: true } surface && Application.MessageLoop)
        {
            try
            {
                surface.BeginInvoke((Action)batch);
                return true;
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        // 无句柄（或无头宿主）：同步跑完，池状态在 Schedule 返回时就已确定。
        // 池在批次里再排下一批时入队而不递归，避免大可视集的同步路径压深调用栈。
        if (_draining)
        {
            _inline.Enqueue(batch);
            return true;
        }

        _draining = true;
        try
        {
            batch();
            while (_inline.Count > 0)
            {
                _inline.Dequeue()();
            }
        }
        finally
        {
            _draining = false;
        }

        return true;
    }
}
