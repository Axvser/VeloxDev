using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Demo.ViewModels;
using Demo.Workflow;

namespace Demo.Views.Workbench;

/// <summary>
/// The main area's output tab: the selected run's root/state/progress and the execution log. The session owns
/// the log collection; this view binds to it and keeps the header in step with the run catalog.
/// </summary>
public sealed partial class OutputPanel : WorkbenchViewBase
{
    private WorkflowDemoSession? _demo;
    private WorkflowRunTaskViewModel? _outputTask;
    private ObservableCollection<string>? _log;

    /// <summary>Creates the panel from XAML.</summary>
    public OutputPanel()
    {
        InitializeComponent();
        RefreshHeader();
    }

    /// <summary>Binds the panel to a session, or unbinds it.</summary>
    public void SetSession(WorkflowDemoSession? demo)
    {
        if (_demo is not null)
        {
            _demo.Tree.Runs.PropertyChanged -= OnRunsPropertyChanged;
        }

        if (_log is not null)
        {
            _log.CollectionChanged -= OnLogChanged;
        }

        _demo = demo;
        _log = demo?.Tree.ExecutionLog;

        if (_demo is not null)
        {
            _demo.Tree.Runs.PropertyChanged += OnRunsPropertyChanged;
        }

        if (_log is not null)
        {
            _log.CollectionChanged += OnLogChanged;
        }

        RebuildLog();
        RefreshHeader();
    }

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => WorkbenchDispatcher.Post(RebuildLog);

    // The engine appends from run threads; the list is rebuilt on the UI thread from a snapshot rather than
    // letting the ItemsControl observe a collection that changes off-thread.
    private void RebuildLog()
    {
        LogList.ItemsSource = _log is null ? Array.Empty<string>() : _log.ToList();
        CountLabel.Text = $"已记录 {_log?.Count ?? 0} 行";
    }

    private void OnRunsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => WorkbenchDispatcher.Post(RefreshHeader);

    private void OnOutputTaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => WorkbenchDispatcher.Post(RefreshHeader);

    private void RefreshHeader()
    {
        var selected = _demo?.Tree.Runs.Selected;
        if (!ReferenceEquals(selected, _outputTask))
        {
            if (_outputTask is not null)
            {
                _outputTask.PropertyChanged -= OnOutputTaskPropertyChanged;
            }

            _outputTask = selected;
            if (_outputTask is not null)
            {
                _outputTask.PropertyChanged += OnOutputTaskPropertyChanged;
            }
        }

        var hasSelection = selected is not null;
        RootLabel.Visibility = hasSelection ? Easel.Controls.Visibility.Visible : Easel.Controls.Visibility.Collapsed;
        StateLabel.Visibility = hasSelection ? Easel.Controls.Visibility.Visible : Easel.Controls.Visibility.Collapsed;
        ProgressLabel.Visibility = hasSelection ? Easel.Controls.Visibility.Visible : Easel.Controls.Visibility.Collapsed;
        HintLabel.Visibility = hasSelection ? Easel.Controls.Visibility.Collapsed : Easel.Controls.Visibility.Visible;

        RootLabel.Text = selected?.RootLabel ?? string.Empty;
        StateLabel.Text = selected?.StateText ?? string.Empty;
        ProgressLabel.Text = selected?.ProgressText ?? string.Empty;
        CountLabel.Text = $"已记录 {_demo?.Tree.ExecutionLog.Count ?? 0} 行";
    }
}
