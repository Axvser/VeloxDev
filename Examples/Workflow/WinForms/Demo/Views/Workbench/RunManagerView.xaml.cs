using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using Demo.ViewModels;
using Easel;
using Easel.Controls;
using Easel.Visuals;
using Easel.Visuals.Controls;
using Button = Easel.Visuals.Controls.Button;

namespace Demo.Views.Workbench;

/// <summary>
/// The sidebar's run manager: every compile and run task, manual and Agent alike. The catalog drives the rows'
/// lifetime; each row subscribes to its own task so the elapsed clock and the control gate update in place.
/// </summary>
public sealed partial class RunManagerView : WorkbenchViewBase
{
    private readonly List<RunTaskRow> _rows = [];
    private WorkflowRunCatalog? _catalog;
    private WorkflowRunTaskViewModel? _selectedTask;

    /// <summary>Creates the manager from XAML.</summary>
    public RunManagerView()
    {
        InitializeComponent();
        UpdateHeader();
        UpdateEmpty();
    }

    /// <summary>The catalog to render; swapping trees rebinds the whole page.</summary>
    public WorkflowRunCatalog? Catalog
    {
        get => _catalog;
        set
        {
            if (ReferenceEquals(_catalog, value))
            {
                return;
            }

            if (_catalog is not null)
            {
                _catalog.Tasks.CollectionChanged -= OnTasksChanged;
                _catalog.PropertyChanged -= OnCatalogPropertyChanged;
            }

            _catalog = value;
            if (_catalog is not null)
            {
                _catalog.Tasks.CollectionChanged += OnTasksChanged;
                _catalog.PropertyChanged += OnCatalogPropertyChanged;
            }

            RebuildRows();
        }
    }

    private void OnClear(object? sender, EventArgs e) => _catalog?.ClearFinished();

    private void OnTasksChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => WorkbenchDispatcher.Post(RebuildRows);

    private void OnCatalogPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => WorkbenchDispatcher.Post(RefreshFromCatalog);

    private void RefreshFromCatalog()
    {
        UpdateHeader();
        UpdateEmpty();

        var selected = _catalog?.Selected;
        if (ReferenceEquals(selected, _selectedTask))
        {
            return;
        }

        _selectedTask = selected;
        foreach (var row in _rows)
        {
            row.UpdateSelection();
        }
    }

    private void RebuildRows()
    {
        foreach (var row in _rows)
        {
            row.Dispose();
        }

        _rows.Clear();
        Rows.Children.Clear();

        if (_catalog is not null)
        {
            foreach (var task in _catalog.Tasks)
            {
                var row = new RunTaskRow(task, _catalog, this);
                _rows.Add(row);
                Rows.Add(row.Root);
            }
        }

        _selectedTask = _catalog?.Selected;
        UpdateHeader();
        UpdateEmpty();
    }

    private void UpdateHeader() => Summary.Text = _catalog?.Summary ?? "运行中 0 · 已结束 0 · 共 0";

    private void UpdateEmpty() => EmptyHint.Visibility = _catalog is null || _catalog.IsEmpty
        ? Easel.Controls.Visibility.Visible
        : Easel.Controls.Visibility.Collapsed;

    /// <summary>One row of the run manager: source badge, root, state, times, progress and the gated controls.</summary>
    private sealed class RunTaskRow : IDisposable
    {
        private readonly WorkflowRunTaskViewModel _task;
        private readonly WorkflowRunCatalog _catalog;
        private readonly Border _root;
        private readonly TextBlock _label;
        private readonly TextBlock _state;
        private readonly TextBlock _started;
        private readonly TextBlock _elapsed;
        private readonly TextBlock _progress;
        private readonly Button _pause;
        private readonly Button _resume;
        private readonly Button _stop;
        private readonly Button _start;
        private readonly Button _remove;

        public RunTaskRow(WorkflowRunTaskViewModel task, WorkflowRunCatalog catalog, RunManagerView owner)
        {
            _task = task;
            _catalog = catalog;

            _root = new Border
            {
                Height = 92,
                Background = new SolidColorBrush(WorkbenchPalette.Surface),
                BorderBrush = new SolidColorBrush(WorkbenchPalette.BorderSubtle),
                BorderThickness = new Thickness(1),
                CornerRadius = 6,
                Padding = new Thickness(9, 7, 9, 7),
                Cursor = System.Windows.Forms.Cursors.Hand,
            };

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });

            var info = new StackPanel { Spacing = 2 };

            var rootRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            rootRow.Add(new Border
            {
                Background = new SolidColorBrush(WorkbenchPalette.PanelRaised),
                Padding = new Thickness(5, 0, 5, 0),
                VerticalAlignment = Easel.Controls.VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = task.SourceText,
                    FontSize = 7.5,
                    Foreground = WorkbenchPalette.TextSecondary,
                },
            });
            _label = new TextBlock
            {
                Text = task.RootLabel,
                FontSize = 9.5,
                FontWeight = FontWeight.Bold,
                Foreground = WorkbenchPalette.TextPrimary,
                VerticalAlignment = Easel.Controls.VerticalAlignment.Center,
            };
            rootRow.Add(_label);
            info.Add(rootRow);

            var metaRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            _state = CreateMeta(task.StateText, WorkbenchPalette.TextSecondary);
            _started = CreateMeta(task.StartedText, WorkbenchPalette.TextTertiary);
            _elapsed = CreateMeta(task.ElapsedText, WorkbenchPalette.TextTertiary);
            metaRow.Add(_state);
            metaRow.Add(_started);
            metaRow.Add(_elapsed);
            info.Add(metaRow);

            _progress = new TextBlock
            {
                Text = task.ProgressText,
                FontSize = 8,
                Foreground = WorkbenchPalette.TextTertiary,
            };
            info.Add(_progress);

            Grid.SetColumn(info, 0);
            body.Add(info);

            var actions = new StackPanel { Spacing = 2, VerticalAlignment = Easel.Controls.VerticalAlignment.Top };
            _pause = CreateAction("暂停", WorkbenchPalette.AccentText, task.Pause);
            _resume = CreateAction("继续", WorkbenchPalette.AccentText, task.Resume);
            _stop = CreateAction("停止", WorkbenchPalette.Danger, task.Stop);
            _start = CreateAction("运行", WorkbenchPalette.Success, task.Start);
            _remove = CreateAction("移除", WorkbenchPalette.TextSecondary, () => _catalog.Remove(task));
            actions.Add(_pause);
            actions.Add(_resume);
            actions.Add(_stop);
            actions.Add(_start);
            actions.Add(_remove);
            Grid.SetColumn(actions, 1);
            body.Add(actions);

            _root.Child = body;
            _root.PointerPressed += (_, args) =>
            {
                if (args.Button != System.Windows.Forms.MouseButtons.Left)
                {
                    return;
                }

                _catalog.Selected = _task;
                args.Handled = true;
            };

            _task.PropertyChanged += OnTaskPropertyChanged;
            UpdateContent();
            UpdateSelection();
        }

        public Border Root => _root;

        public void UpdateSelection()
        {
            var selected = ReferenceEquals(_catalog.Selected, _task);
            _root.Background = new SolidColorBrush(selected ? WorkbenchPalette.SurfaceElevated : WorkbenchPalette.Surface);
            _root.BorderBrush = new SolidColorBrush(selected ? WorkbenchPalette.Accent : WorkbenchPalette.BorderSubtle);
        }

        public void Dispose() => _task.PropertyChanged -= OnTaskPropertyChanged;

        private void OnTaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
            => WorkbenchDispatcher.Post(UpdateContent);

        private void UpdateContent()
        {
            _label.Text = _task.RootLabel;
            _state.Text = _task.StateText;
            _state.Foreground = _task.State switch
            {
                WorkflowRunState.Running => WorkbenchPalette.Warning,
                WorkflowRunState.Completed => WorkbenchPalette.Success,
                WorkflowRunState.Failed => WorkbenchPalette.Danger,
                _ => WorkbenchPalette.TextSecondary,
            };
            _started.Text = _task.StartedText;
            _elapsed.Text = _task.ElapsedText;
            _progress.Text = _task.ProgressText;

            _pause.Visibility = _task.CanPause ? Easel.Controls.Visibility.Visible : Easel.Controls.Visibility.Collapsed;
            _resume.Visibility = _task.CanResume ? Easel.Controls.Visibility.Visible : Easel.Controls.Visibility.Collapsed;
            _stop.Visibility = _task.CanStop ? Easel.Controls.Visibility.Visible : Easel.Controls.Visibility.Collapsed;
            _start.Visibility = _task.CanStart ? Easel.Controls.Visibility.Visible : Easel.Controls.Visibility.Collapsed;
            _remove.Visibility = _task.CanRemove ? Easel.Controls.Visibility.Visible : Easel.Controls.Visibility.Collapsed;
        }

        private static TextBlock CreateMeta(string text, Color color) => new()
        {
            Text = text,
            FontSize = 8,
            Foreground = color,
        };

        private static Button CreateAction(string text, Color color, Action action)
        {
            var button = new Button
            {
                Text = text,
                Width = 50,
                Height = 19,
                FontSize = 7.5,
                Foreground = color,
                Background = null,
                BorderThickness = default,
                Padding = new Thickness(2, 0, 2, 0),
                MinHeight = 18,
                CornerRadius = 3,
            };
            button.Click += (_, _) => action();
            return button;
        }
    }
}
