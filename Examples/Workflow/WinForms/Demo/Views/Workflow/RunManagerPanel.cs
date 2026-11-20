using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Demo.ViewModels;
using Demo.Views.Workbench;

namespace Demo.Views.Workflow;

/// <summary>
/// The sidebar's run manager: every compile and run task, manual and Agent alike.
///
/// WinForms has no binding, so the panel plays the role Avalonia's <c>ItemsControl</c> does: the catalog's
/// collection drives the rows' lifetime, and each row subscribes to its own task so the elapsed clock and the
/// control gate update in place. Rebuilding on every property change would fight the catalog's 250ms tick.
/// </summary>
internal sealed class RunManagerPanel : UserControl
{
    private readonly Label _summary;
    private readonly FlowLayoutPanel _rows;
    private readonly Label _empty;
    private readonly GhostButton _clear;
    private readonly List<RunTaskRow> _rowControls = [];
    private WorkflowRunCatalog? _catalog;
    private WorkflowRunTaskViewModel? _selectedTask;

    public RunManagerPanel()
    {
        DoubleBuffered = true;
        BackColor = WorkbenchPalette.Panel;
        ForeColor = WorkbenchPalette.TextSecondary;
        Padding = Padding.Empty;

        _summary = new Label
        {
            AutoSize = true,
            Text = "运行中 0 · 已结束 0 · 共 0",
            ForeColor = WorkbenchPalette.TextTertiary,
            BackColor = Color.Transparent,
            Font = CardTheme.Font(8f),
            Margin = Padding.Empty,
        };

        var title = new Label
        {
            AutoSize = true,
            Text = "运行任务",
            ForeColor = WorkbenchPalette.TextPrimary,
            BackColor = Color.Transparent,
            Font = CardTheme.Font(9.5f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 2),
        };

        var titleStack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        titleStack.Controls.Add(title);
        titleStack.Controls.Add(_summary);

        _clear = new GhostButton
        {
            Text = "清除已完成",
            TextColor = WorkbenchPalette.AccentText,
            AutoSize = false,
            Size = new Size(76, 22),
            Margin = new Padding(0, 2, 0, 0),
        };
        _clear.Click += (_, _) => _catalog?.ClearFinished();

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = new Padding(12, 12, 12, 6),
            Padding = Padding.Empty,
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.Controls.Add(titleStack, 0, 0);
        header.Controls.Add(_clear, 1, 0);

        var hint = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(248, 0),
            Text = "手动编译、手动运行，以及 Agent 编译 / 运行的任务都会出现在这里。选中一行，输出页会跟随它。",
            ForeColor = WorkbenchPalette.TextTertiary,
            BackColor = Color.Transparent,
            Font = CardTheme.Font(8f),
            Margin = new Padding(12, 0, 12, 8),
        };

        _rows = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = WorkbenchPalette.Panel,
            Margin = Padding.Empty,
            Padding = new Padding(8, 0, 8, 8),
        };
        _rows.ClientSizeChanged += (_, _) => ResizeRows();

        _empty = new Label
        {
            Dock = DockStyle.Top,
            Height = 48,
            Text = "还没有任务。在控制器卡上编译一次，或让 Agent 编译 / 运行工作流。",
            ForeColor = WorkbenchPalette.TextTertiary,
            BackColor = WorkbenchPalette.Panel,
            Font = CardTheme.Font(8f),
            Padding = new Padding(12, 8, 12, 0),
            Margin = Padding.Empty,
        };

        var listHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = WorkbenchPalette.Panel,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        listHost.Controls.Add(_rows);
        listHost.Controls.Add(_empty);
        _empty.BringToFront();

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = WorkbenchPalette.Panel,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(hint, 0, 1);
        layout.Controls.Add(listHost, 0, 2);

        Controls.Add(layout);
        UpdateHeader();
        UpdateEmpty();
    }

    /// <summary>The catalog to render; swapping trees rebinds the whole panel.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public WorkflowRunCatalog? Catalog
    {
        get => _catalog;
        set
        {
            if (ReferenceEquals(_catalog, value)) return;

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

    private void OnTasksChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildRows();

    private void OnCatalogPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (InvokeRequired)
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            try { BeginInvoke(RefreshFromCatalog); } catch (InvalidOperationException) { }
            return;
        }

        RefreshFromCatalog();
    }

    private void RefreshFromCatalog()
    {
        UpdateHeader();
        UpdateEmpty();

        // The catalog raises this notification on every task property change — including the 250ms clock.
        // Repainting every row then would repaint its frame over the row's own labels, so only a real change
        // of the selected row asks the rows to repaint.
        var selected = _catalog?.Selected;
        if (ReferenceEquals(selected, _selectedTask)) return;

        _selectedTask = selected;
        foreach (var row in _rowControls) row.UpdateSelection();
    }

    private void RebuildRows()
    {
        if (InvokeRequired)
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            try { BeginInvoke(RebuildRows); } catch (InvalidOperationException) { }
            return;
        }

        _rows.SuspendLayout();

        foreach (var row in _rowControls) row.Dispose();
        _rowControls.Clear();
        _rows.Controls.Clear();

        if (_catalog is not null)
        {
            foreach (var task in _catalog.Tasks)
            {
                var row = new RunTaskRow(task, _catalog) { Width = RowWidth() };
                _rowControls.Add(row);
                _rows.Controls.Add(row);
            }
        }

        _rows.ResumeLayout(true);
        ResizeRows();
        _selectedTask = _catalog?.Selected;
        UpdateHeader();
        UpdateEmpty();
    }

    private void ResizeRows()
    {
        if (_rowControls.Count == 0) return;

        var width = RowWidth();
        foreach (var row in _rowControls) row.Width = width;
    }

    private int RowWidth()
    {
        // ClientSize already excludes the auto-scroll bars; the extra 2 is the row's own hairline.
        var width = _rows.ClientSize.Width - _rows.Padding.Horizontal - 2;
        return Math.Max(120, width);
    }

    private void UpdateHeader()
    {
        _summary.Text = _catalog?.Summary ?? "运行中 0 · 已结束 0 · 共 0";
    }

    private void UpdateEmpty()
    {
        _empty.Visible = _catalog is null || _catalog.IsEmpty;
    }

    /// <summary>
    /// One row of the run manager: source badge, root, state, times, progress and the five gated controls.
    /// </summary>
    private sealed class RunTaskRow : FramePanel
    {
        private readonly WorkflowRunTaskViewModel _task;
        private readonly WorkflowRunCatalog _catalog;
        private readonly Label _root;
        private readonly Label _state;
        private readonly Label _started;
        private readonly Label _elapsed;
        private readonly Label _progress;
        private readonly GhostButton _pause;
        private readonly GhostButton _resume;
        private readonly GhostButton _stop;
        private readonly GhostButton _start;
        private readonly GhostButton _remove;

        public RunTaskRow(WorkflowRunTaskViewModel task, WorkflowRunCatalog catalog)
        {
            _task = task;
            _catalog = catalog;

            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = WorkbenchPalette.Panel;
            Fill = WorkbenchPalette.Surface;
            Frame = WorkbenchPalette.BorderSubtle;
            Ground = WorkbenchPalette.Panel;
            Radius = 6;
            FrameWidth = 1;
            Height = 92;
            Margin = new Padding(0, 0, 0, 6);
            Padding = new Padding(9, 7, 9, 7);
            Cursor = Cursors.Hand;

            var badge = new Label
            {
                AutoSize = true,
                Text = task.SourceText,
                BackColor = WorkbenchPalette.PanelRaised,
                ForeColor = WorkbenchPalette.TextSecondary,
                Font = CardTheme.Font(7.5f),
                Padding = new Padding(5, 0, 5, 0),
                Margin = new Padding(0, 1, 6, 0),
            };

            _root = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Height = 18,
                Text = task.RootLabel,
                ForeColor = WorkbenchPalette.TextPrimary,
                BackColor = Color.Transparent,
                Font = CardTheme.Font(9.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Margin = Padding.Empty,
            };

            var rootRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            rootRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            rootRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            rootRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rootRow.Controls.Add(badge, 0, 0);
            rootRow.Controls.Add(_root, 1, 0);

            _state = CreateMeta(task.StateText, WorkbenchPalette.TextSecondary);
            _started = CreateMeta(task.StartedText, WorkbenchPalette.TextTertiary);
            _elapsed = CreateMeta(task.ElapsedText, WorkbenchPalette.TextTertiary);

            var metaRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 2, 0, 0),
                Padding = Padding.Empty,
            };
            metaRow.Controls.Add(_state);
            metaRow.Controls.Add(_started);
            metaRow.Controls.Add(_elapsed);

            _progress = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Height = 16,
                Text = task.ProgressText,
                ForeColor = WorkbenchPalette.TextTertiary,
                BackColor = Color.Transparent,
                Font = CardTheme.Font(8f),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Margin = new Padding(0, 1, 0, 0),
            };

            var info = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            info.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            info.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            info.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            info.Controls.Add(rootRow, 0, 0);
            info.Controls.Add(metaRow, 0, 1);
            info.Controls.Add(_progress, 0, 2);

            _pause = CreateAction("暂停", CardTheme.ActionCompile, () => _task.Pause());
            _resume = CreateAction("继续", CardTheme.ActionCompile, () => _task.Resume());
            _stop = CreateAction("停止", CardTheme.ActionStop, () => _task.Stop());
            _start = CreateAction("运行", CardTheme.ActionRun, () => _task.Start());
            _remove = CreateAction("移除", CardTheme.ActionClose, () => _catalog.Remove(_task));

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            actions.Controls.Add(_pause);
            actions.Controls.Add(_resume);
            actions.Controls.Add(_stop);
            actions.Controls.Add(_start);
            actions.Controls.Add(_remove);

            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54F));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            body.Controls.Add(info, 0, 0);
            body.Controls.Add(actions, 1, 0);

            Controls.Add(body);

            // A label does not bubble its clicks to the panel that owns it, so every non-button surface in the
            // row asks to select the task itself.
            WireSelect(this);
            WireSelect(info);
            WireSelect(rootRow);
            WireSelect(metaRow);
            WireSelect(badge);
            WireSelect(_root);
            WireSelect(_state);
            WireSelect(_started);
            WireSelect(_elapsed);
            WireSelect(_progress);

            _task.PropertyChanged += OnTaskPropertyChanged;
            UpdateContent();
            UpdateSelection();
        }

        public void UpdateSelection()
        {
            var selected = ReferenceEquals(_catalog.Selected, _task);
            Fill = selected ? WorkbenchPalette.SurfaceElevated : WorkbenchPalette.Surface;
            Frame = selected ? WorkbenchPalette.Accent : WorkbenchPalette.BorderSubtle;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _task.PropertyChanged -= OnTaskPropertyChanged;
            base.Dispose(disposing);
        }

        private void OnTaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                if (Disposing || !IsHandleCreated) return;
                try { BeginInvoke(UpdateContent); } catch (InvalidOperationException) { }
                return;
            }

            UpdateContent();
        }

        private void UpdateContent()
        {
            _root.Text = _task.RootLabel;
            _state.Text = _task.StateText;
            _state.ForeColor = _task.State switch
            {
                WorkflowRunState.Running => WorkbenchPalette.Warning,
                WorkflowRunState.Completed => WorkbenchPalette.Success,
                WorkflowRunState.Failed => WorkbenchPalette.Danger,
                _ => WorkbenchPalette.TextSecondary,
            };
            _started.Text = _task.StartedText;
            _elapsed.Text = _task.ElapsedText;
            _progress.Text = _task.ProgressText;

            _pause.Visible = _task.CanPause;
            _resume.Visible = _task.CanResume;
            _stop.Visible = _task.CanStop;
            _start.Visible = _task.CanStart;
            _remove.Visible = _task.CanRemove;
        }

        private void WireSelect(Control control)
        {
            control.Cursor = Cursors.Hand;
            control.Click += (_, _) => _catalog.Selected = _task;
        }

        private static Label CreateMeta(string text, Color color) => new()
        {
            AutoSize = true,
            Text = text,
            ForeColor = color,
            BackColor = Color.Transparent,
            Font = CardTheme.Font(8f),
            Margin = new Padding(0, 0, 8, 0),
        };

        private static GhostButton CreateAction(string text, Color color, Action action)
        {
            var button = new GhostButton
            {
                Text = text,
                TextColor = color,
                AutoSize = false,
                Size = new Size(50, 19),
                Font = CardTheme.Font(7.5f),
                Margin = new Padding(0, 0, 0, 2),
                Padding = Padding.Empty,
            };
            button.Click += (_, _) => action();
            return button;
        }
    }
}
