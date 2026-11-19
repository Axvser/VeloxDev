using System.Collections;
using System.Collections.Specialized;
using System.Linq;
using Microsoft.AspNetCore.Components;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Blazor analogue of the XAML adapters' <c>ViewPool.ItemsSource</c>/<c>ViewPool.TemplateSelector</c>
/// attached properties. Renders an observable collection through a per-item template and re-renders
/// when the collection changes. Blazor's renderer handles diffing/pooling of elements, so the
/// component is a thin, API-consistent wrapper.
/// </summary>
/// <remarks>
/// This is the Blazor-native view pool and deliberately does not run the Core
/// <see cref="VeloxDev.WorkflowSystem.ViewSourcePipelineCore{THost,TView,TItem,TPriorityCore,TFactory,TContainer,TArranger,TScheduler,TDiagnostics}"/>
/// pipeline: the renderer owns component instantiation, tree membership
/// and diffing, so there is no view instance for a factory to create or a container to attach.
/// Keyed rendering through <see cref="KeySelector"/> is what preserves per-item component state.
/// </remarks>
public partial class ViewPool : ComponentBase, IDisposable
{
    /// <summary>Gets or sets the pooled items source. When it implements <see cref="INotifyCollectionChanged"/> the component re-renders on change.</summary>
    [Parameter]
    public IEnumerable? ItemsSource { get; set; }

    /// <summary>Gets or sets the per-item template. The item is passed as the fragment's context.</summary>
    [Parameter]
    public RenderFragment<object>? ItemTemplate { get; set; }

    /// <summary>Gets or sets the content rendered when <see cref="ItemsSource"/> is empty.</summary>
    [Parameter]
    public RenderFragment? EmptyContent { get; set; }

    /// <summary>Gets or sets a key selector used to stabilize re-renders (analogue of <c>@key</c>).</summary>
    [Parameter]
    public Func<object, object>? KeySelector { get; set; }

    private IReadOnlyList<object>? _items;
    private INotifyCollectionChanged? _notifier;
    private object? _lastSource;
    private bool _refreshQueued;

    private IReadOnlyList<object>? Items => _items;

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        base.OnParametersSet();

        var notifier = ItemsSource as INotifyCollectionChanged;
        if (!ReferenceEquals(notifier, _notifier))
        {
            if (_notifier is not null)
            {
                _notifier.CollectionChanged -= OnCollectionChanged;
            }

            _notifier = notifier;
            if (_notifier is not null)
            {
                _notifier.CollectionChanged += OnCollectionChanged;
            }
        }

        // 只在集合引用变化时快照源；每次父级重渲染都快照会每滚动帧分配一个 O(N) 数组。内容增删由下面的 OnCollectionChanged 显式重快照。
        if (!ReferenceEquals(ItemsSource, _lastSource))
        {
            _lastSource = ItemsSource;
            _items = ItemsSource is null ? null : ItemsSource.Cast<object>().ToArray();
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // 一次虚拟化会连续发出很多 Add/Remove；每个事件都重快照 + StateHasChanged 会把 20 步扫视口
        // 放大成上千次渲染。合并到 renderer 的下一回合：只快照一次、只请求一次渲染。
        if (_refreshQueued)
        {
            return;
        }

        _refreshQueued = true;
        _ = InvokeAsync(async () =>
        {
            await Task.Yield();
            _refreshQueued = false;
            _items = ItemsSource is null ? null : ItemsSource.Cast<object>().ToArray();
            StateHasChanged();
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _refreshQueued = false;
        if (_notifier is not null)
        {
            _notifier.CollectionChanged -= OnCollectionChanged;
        }
    }
}
