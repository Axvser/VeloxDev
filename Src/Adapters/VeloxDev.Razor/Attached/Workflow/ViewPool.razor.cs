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

        _items = ItemsSource is null ? null : ItemsSource.Cast<object>().ToArray();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => InvokeAsync(StateHasChanged);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_notifier is not null)
        {
            _notifier.CollectionChanged -= OnCollectionChanged;
        }
    }
}
