using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Writes node geometry into MAUI <see cref="AbsoluteLayout"/> attached properties and keeps it current.</summary>
/// <remarks>
/// <para>
/// MAUI templates do not bind <see cref="AbsoluteLayout.LayoutBounds"/>, so this layer writes the bounds
/// itself. The pool arranges a view only when it materializes it, so the arranger also listens to the
/// item's geometry properties and rewrites the bounds once per dispatcher turn; without that, moving or
/// resizing a node would never reach its view.
/// </para>
/// <para>
/// Link items are excluded: a link layer owns their box, and overwriting it would clip the geometry a
/// per-link view draws.
/// </para>
/// </remarks>
public sealed class ViewArranger : IViewArranger<IWorkflowViewModel, View>, IDisposable
{
    private readonly Layout _host;
    private readonly Dictionary<View, Arrangement> _arrangements = new(ViewReferenceComparer.Instance);

    /// <summary>Creates an arranger that writes bounds on <paramref name="host"/>.</summary>
    /// <param name="host">The layout whose views are arranged; nothing is written unless it is an <see cref="AbsoluteLayout"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> is <see langword="null"/>.</exception>
    public ViewArranger(Layout host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <inheritdoc />
    public void Arrange(View view, IWorkflowViewModel item)
    {
        if (!_arrangements.TryGetValue(view, out var arrangement))
        {
            arrangement = new Arrangement(this, view);
            _arrangements[view] = arrangement;
        }

        arrangement.Bind(item);
        Apply(view, item);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var arrangement in _arrangements.Values)
        {
            arrangement.Release();
        }

        _arrangements.Clear();
    }

    // 视图被隐藏时解绑：隐藏期间不必继续跟随模型几何，重新物化时 Arrange 会再订阅。
    internal void Release(View view)
    {
        if (_arrangements.TryGetValue(view, out var arrangement))
        {
            arrangement.Release();
        }
    }

    // 视图被真正移除时连同登记一起丢掉，避免字典继续持有它。
    internal void Forget(View view)
    {
        if (_arrangements.TryGetValue(view, out var arrangement))
        {
            arrangement.Release();
            _arrangements.Remove(view);
        }
    }

    private void Apply(View view, IWorkflowViewModel item)
    {
        if (_host is not AbsoluteLayout)
        {
            return;
        }

        switch (item)
        {
            case IWorkflowNodeViewModel node:
                AbsoluteLayout.SetLayoutFlags(view, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.None);
                AbsoluteLayout.SetLayoutBounds(view, new Rect(
                    node.Anchor.Horizontal,
                    node.Anchor.Vertical,
                    Math.Max(1, node.Size.Width),
                    Math.Max(1, node.Size.Height)));
                break;

            case IWorkflowLinkViewModel:
                // 连线盒子归链接层/每线视图；这里写回会覆盖它，画布局部坐标为负时几何会被整段裁掉。
                break;

            default:
                AbsoluteLayout.SetLayoutFlags(view, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.None);
                AbsoluteLayout.SetLayoutBounds(view, new Rect(
                    0,
                    0,
                    view.WidthRequest > 0 ? view.WidthRequest : -1,
                    view.HeightRequest > 0 ? view.HeightRequest : -1));
                break;
        }
    }

    private static bool AffectsLayout(IWorkflowViewModel item, string? propertyName)
        => item switch
        {
            IWorkflowNodeViewModel => propertyName is nameof(IWorkflowNodeViewModel.Anchor)
                or nameof(IWorkflowNodeViewModel.Size),
            IWorkflowSlotViewModel => propertyName is nameof(IWorkflowSlotViewModel.Anchor),
            _ => false,
        };

    private sealed class ViewReferenceComparer : IEqualityComparer<View>
    {
        public static readonly ViewReferenceComparer Instance = new();

        public bool Equals(View? x, View? y) => ReferenceEquals(x, y);

        public int GetHashCode(View obj) => RuntimeHelpers.GetHashCode(obj);
    }

    private sealed class Arrangement
    {
        private readonly ViewArranger _owner;
        private readonly View _view;
        private PropertyChangedEventHandler? _handler;

        public Arrangement(ViewArranger owner, View view)
        {
            _owner = owner;
            _view = view;
        }

        public IWorkflowViewModel? Item { get; private set; }

        public bool LayoutQueued { get; set; }

        public void Bind(IWorkflowViewModel item)
        {
            if (ReferenceEquals(Item, item))
            {
                return;
            }

            Release();
            Item = item;
            _handler = OnPropertyChanged;
            item.PropertyChanged += _handler;
        }

        public void Release()
        {
            if (Item is not null && _handler is not null)
            {
                Item.PropertyChanged -= _handler;
            }

            Item = null;
            _handler = null;
        }

        private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (Item is not { } item
                || !ReferenceEquals(item, sender)
                || !AffectsLayout(item, e.PropertyName)
                || LayoutQueued)
            {
                return;
            }

            // 合并：一帧内 Anchor/Size 的多次变化只写一次，避免布局变化反过来再催发属性变化。
            LayoutQueued = true;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                LayoutQueued = false;
                if (ReferenceEquals(Item, item))
                {
                    _owner.Apply(_view, item);
                }
            });
        }
    }
}
