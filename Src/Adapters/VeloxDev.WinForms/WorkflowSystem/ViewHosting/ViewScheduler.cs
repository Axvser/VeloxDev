using System;
using System.Windows.Forms;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Posts materialization batches to the host control's message queue.</summary>
/// <remarks>
/// The batch is always queued, never run inline: the pool relies on that boundary to keep view creation out of the
/// collection-change call stack. <see cref="Control.BeginInvoke(Delegate)"/> needs a window handle, so a host that
/// has not been shown yet gets one created on demand from the UI thread.</remarks>
public sealed class ViewScheduler : IViewScheduler<NonPriority>
{
    private readonly Control host;

    /// <summary>Creates a scheduler that posts batches through <paramref name="host"/>.</summary>
    /// <param name="host">The control whose message queue carries the batches.</param>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> is <see langword="null"/>.</exception>
    public ViewScheduler(Control host)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <inheritdoc />
    public NonPriority BatchPriority => default;

    /// <inheritdoc />
    public bool Schedule(Action batch, NonPriority priority)
    {
        if (batch is null || host.IsDisposed)
        {
            return false;
        }

        try
        {
            // BeginInvoke 要求句柄已建：树还没挂上窗体时先建出来（这里本来就在 UI 线程）。
            if (!host.IsHandleCreated)
            {
                _ = host.Handle;
            }

            // 一批视图在同一个布局事务里入树；否则每个 Controls.Add 都会触发一次整面板布局。
            host.BeginInvoke(new Action(() =>
            {
                if (host.IsDisposed)
                {
                    return;
                }

                host.SuspendLayout();
                try
                {
                    batch();
                }
                finally
                {
                    if (!host.IsDisposed)
                    {
                        host.ResumeLayout(false);
                        host.PerformLayout();
                    }
                }
            }));
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            // 句柄正在重建或控件已销毁：这一批不跑，等下一次可见集变化再排。
            return false;
        }
    }
}
