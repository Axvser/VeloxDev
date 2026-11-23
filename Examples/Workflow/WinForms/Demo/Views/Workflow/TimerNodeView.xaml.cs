// VeloxDev customization: the Timer card's typography, accent and interval editor policy.
using System.ComponentModel;
using Demo.ViewModels;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;

namespace Demo.Views.Workflow;

/// <summary>The Timer node card: interval editor and the most recent tick.</summary>
public sealed partial class TimerNodeView : VeloxDev.WinForms.Xaml.Visuals.Controls.UserControl
{
    private bool _syncingInterval;
    private TimerNodeViewModel? _bound;

    /// <summary>Creates the card and runs the generated initializer.</summary>
    public TimerNodeView()
    {
        InitializeComponent();
        ApplyTheme();
        intervalBox.PropertyChanged += OnIntervalPropertyChanged;
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!ReferenceEquals(e.Property, DataContextProperty))
        {
            return;
        }

        if (_bound is not null)
        {
            _bound.PropertyChanged -= OnModelChanged;
        }

        _bound = DataContext as TimerNodeViewModel;
        if (_bound is not null)
        {
            _bound.PropertyChanged += OnModelChanged;
        }

        LoadInterval();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TimerNodeViewModel.IntervalMilliseconds))
        {
            LoadInterval();
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && _bound is not null)
        {
            _bound.PropertyChanged -= OnModelChanged;
            _bound = null;
        }

        base.Dispose(disposing);
    }

    private void ApplyTheme()
    {
        accentBar.Background = new SolidColorBrush(CardTheme.AccentTimer);
        CardStyle.Title(titleText);
        CardStyle.Value(orderText);
        CardStyle.Label(intervalLabel);
        CardStyle.Field(intervalBox);
        CardStyle.Label(tickLabel);
        CardStyle.Value(tickValue);
    }

    private void LoadInterval()
    {
        if (DataContext is not TimerNodeViewModel timer)
        {
            return;
        }

        _syncingInterval = true;
        try
        {
            var text = timer.IntervalMilliseconds.ToString();
            if (intervalBox.Text != text)
            {
                intervalBox.Text = text;
            }
        }
        finally
        {
            _syncingInterval = false;
        }
    }

    // Non-numeric input is left in the field but not written to the model, so a half-typed value does not
    // collapse to zero while the user is still typing.
    private void OnIntervalPropertyChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Property, VeloxDev.WinForms.Xaml.Visuals.Controls.TextBox.TextProperty)
            || _syncingInterval || DataContext is not TimerNodeViewModel timer)
        {
            return;
        }

        if (int.TryParse(intervalBox.Text, out var interval) && interval != timer.IntervalMilliseconds)
        {
            timer.IntervalMilliseconds = interval;
        }
    }
}
