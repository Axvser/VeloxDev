using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

// Arranger 与 Container 共用这一份订阅表：Arranger 每次物化按新 item 重订，Container 在
// Deactivate/Destroy 时释放。回收复用后旧 item 的变更不会再写到新宿主上。
internal sealed class ViewLayoutTracker
{
    private readonly Dictionary<FrameworkElement, Entry> _entries = new(FrameworkElementReferenceComparer.Instance);

    internal void Track(
        FrameworkElement view,
        IWorkflowComponent item,
        Action<FrameworkElement, IWorkflowComponent> arrange)
    {
        Release(view);
        if (item is not INotifyPropertyChanged notify)
        {
            return;
        }

        PropertyChangedEventHandler handler = (_, e) =>
        {
            if (!IsLayoutProperty(e.PropertyName))
            {
                return;
            }

            var dispatcher = view.DispatcherQueue;
            if (dispatcher is null || dispatcher.HasThreadAccess)
            {
                arrange(view, item);
                return;
            }

            // 落地时确认这一对仍然是当前绑定：排队期间视图可能已被回收并复用到另一个 item。
            dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (_entries.TryGetValue(view, out var current) && ReferenceEquals(current.Source, item))
                {
                    arrange(view, item);
                }
            });
        };

        notify.PropertyChanged += handler;
        _entries[view] = new Entry(notify, handler);
    }

    internal void Release(FrameworkElement view)
    {
        if (_entries.Remove(view, out var entry))
        {
            entry.Source.PropertyChanged -= entry.Handler;
        }
    }

    private static bool IsLayoutProperty(string? propertyName)
        => propertyName is nameof(INodeComponent.Anchor)
            or nameof(INodeComponent.Size)
            or nameof(ILinkComponent.Sender)
            or nameof(ILinkComponent.Receiver)
            or nameof(ISlotComponent.Anchor);

    private sealed record Entry(INotifyPropertyChanged Source, PropertyChangedEventHandler Handler);

    private sealed class FrameworkElementReferenceComparer : IEqualityComparer<FrameworkElement>
    {
        public static readonly FrameworkElementReferenceComparer Instance = new();

        public bool Equals(FrameworkElement? x, FrameworkElement? y) => ReferenceEquals(x, y);

        public int GetHashCode(FrameworkElement obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
