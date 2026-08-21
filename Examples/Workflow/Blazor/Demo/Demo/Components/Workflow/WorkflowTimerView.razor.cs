using Demo.ViewModels;
using Microsoft.AspNetCore.Components;
using System.ComponentModel;
using System.Globalization;

namespace Demo.Components.Workflow;

public partial class WorkflowTimerView : ComponentBase, IDisposable
{
    [Parameter]
    public TimerNodeViewModel? Selector { get; set; }

    private string _interval = "";

    protected override void OnInitialized()
    {
        SyncFromViewModel();
        if (Selector is INotifyPropertyChanged n)
            n.PropertyChanged += OnSelectorChanged;
    }

    private void OnSelectorChanged(object? sender, PropertyChangedEventArgs e)
    {
        InvokeAsync(() => { SyncFromViewModel(); StateHasChanged(); });
    }

    private void SyncFromViewModel()
    {
        if (Selector is null) return;
        _interval = Selector.IntervalMilliseconds.ToString(CultureInfo.InvariantCulture);
    }

    private void OnIntervalChanged(ChangeEventArgs e)
    {
        if (Selector is null || e.Value is null) return;
        if (int.TryParse(e.Value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
        {
            _interval = v.ToString(CultureInfo.InvariantCulture);
            Selector.IntervalMilliseconds = v;
        }
    }

    public void Dispose()
    {
        if (Selector is INotifyPropertyChanged n)
            n.PropertyChanged -= OnSelectorChanged;
    }
}
