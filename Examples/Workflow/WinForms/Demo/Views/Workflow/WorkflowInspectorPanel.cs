using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Demo.ViewModels;
using Demo.Views.Workbench;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;
using DrawingSize = System.Drawing.Size;

namespace Demo.Views.Workflow;

/// <summary>
/// The inspector's 属性 page: the selected element, then the compiled structure.
///
/// Run status, canvas totals, zoom and the minimap toggle are deliberately absent — the status bar and the
/// element page already report them, and repeating them here made the page read like a second copy of the
/// shell. Every value is read from the tree, so the panel follows a tree swap and a live run alike.
/// </summary>
internal sealed class WorkflowInspectorPanel : UserControl
{
    private readonly FramePanel _selectedCard;
    private readonly TableLayoutPanel _selectedContent;
    private readonly Label _selectedHint;
    private readonly Label _name;
    private readonly Label _kind;
    private readonly Label _detail;
    private readonly GhostButton _locate;
    private readonly GhostButton _delete;

    private readonly Label _compiledCount;
    private readonly ListBox _compiledList;
    private readonly Label _compiledEmpty;

    private TreeViewModel? _tree;
    private WorkflowElementEntry? _selected;

    public WorkflowInspectorPanel()
    {
        DoubleBuffered = true;
        BackColor = WorkbenchPalette.Panel;
        ForeColor = WorkbenchPalette.TextSecondary;
        Padding = Padding.Empty;

        _name = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Height = 20,
            ForeColor = WorkbenchPalette.TextPrimary,
            BackColor = Color.Transparent,
            Font = CardTheme.Font(9.5f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = Padding.Empty,
        };

        _kind = new Label
        {
            AutoSize = true,
            ForeColor = WorkbenchPalette.TextTertiary,
            BackColor = Color.Transparent,
            Font = CardTheme.Font(8f),
            Margin = new Padding(8, 0, 0, 0),
        };

        var titleRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        titleRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        titleRow.Controls.Add(_name, 0, 0);
        titleRow.Controls.Add(_kind, 1, 0);

        var separator = new Panel
        {
            Dock = DockStyle.Top,
            Height = 1,
            BackColor = WorkbenchPalette.BorderSubtle,
            Margin = new Padding(0, 7, 0, 7),
        };

        _detail = new Label
        {
            AutoSize = true,
            MaximumSize = new DrawingSize(288, 0),
            ForeColor = WorkbenchPalette.TextSecondary,
            BackColor = Color.Transparent,
            Font = CardTheme.Mono(8f),
            Margin = new Padding(0, 0, 0, 8),
        };

        _locate = CreateAction("定位", CardTheme.ActionCompile);
        _locate.Click += (_, _) => LocateSelected();
        _delete = CreateAction("删除", CardTheme.ActionStop);
        _delete.Click += (_, _) => DeleteSelected();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 2, 0, 0),
            Padding = Padding.Empty,
        };
        buttons.Controls.Add(_locate);
        buttons.Controls.Add(_delete);

        _selectedContent = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 4,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        _selectedContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        for (var i = 0; i < 4; i++) _selectedContent.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _selectedContent.Controls.Add(titleRow, 0, 0);
        _selectedContent.Controls.Add(separator, 0, 1);
        _selectedContent.Controls.Add(_detail, 0, 2);
        _selectedContent.Controls.Add(buttons, 0, 3);

        _selectedHint = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            MaximumSize = new DrawingSize(288, 0),
            Text = "在左侧「元素」列表里选一行，这里显示它的类型、位置、编译顺序与连接。",
            ForeColor = WorkbenchPalette.TextTertiary,
            BackColor = Color.Transparent,
            Font = CardTheme.Font(8f),
            Margin = Padding.Empty,
        };

        var selectedLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        selectedLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        selectedLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        selectedLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        selectedLayout.Controls.Add(_selectedContent, 0, 0);
        selectedLayout.Controls.Add(_selectedHint, 0, 1);

        _selectedCard = new FramePanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Fill = WorkbenchPalette.Surface,
            Frame = WorkbenchPalette.Border,
            Ground = WorkbenchPalette.Panel,
            Radius = 6,
            FrameWidth = 1,
            Padding = new Padding(10),
            Margin = new Padding(12, 0, 12, 0),
        };
        _selectedCard.Controls.Add(selectedLayout);

        _compiledCount = new Label
        {
            AutoSize = true,
            ForeColor = WorkbenchPalette.TextTertiary,
            BackColor = Color.Transparent,
            Font = CardTheme.Font(8f),
            Margin = new Padding(0, 0, 0, 0),
        };

        var compiledHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 6),
            Padding = Padding.Empty,
        };
        compiledHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        compiledHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        compiledHeader.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        compiledHeader.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "分段",
            ForeColor = WorkbenchPalette.TextTertiary,
            BackColor = Color.Transparent,
            Font = CardTheme.Font(8f),
            Margin = Padding.Empty,
        }, 0, 0);
        compiledHeader.Controls.Add(_compiledCount, 1, 0);

        _compiledList = new ListBox
        {
            Dock = DockStyle.Top,
            Height = 0,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 18,
            IntegralHeight = false,
            BorderStyle = BorderStyle.None,
            BackColor = WorkbenchPalette.PanelRaised,
            ForeColor = WorkbenchPalette.TextPrimary,
            SelectionMode = SelectionMode.None,
            Font = CardTheme.Font(8f),
            Margin = Padding.Empty,
        };
        _compiledList.DrawItem += OnDrawCompiledRow;

        _compiledEmpty = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            MaximumSize = new DrawingSize(288, 0),
            Text = "编译一次后这里列出实际执行段。",
            ForeColor = WorkbenchPalette.TextTertiary,
            BackColor = Color.Transparent,
            Font = CardTheme.Font(8f),
            Margin = new Padding(0, 0, 0, 0),
        };

        var compiledLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        compiledLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        compiledLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        compiledLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        compiledLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        compiledLayout.Controls.Add(compiledHeader, 0, 0);
        compiledLayout.Controls.Add(_compiledEmpty, 0, 1);
        compiledLayout.Controls.Add(_compiledList, 0, 2);

        var compiledCard = new FramePanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Fill = WorkbenchPalette.Surface,
            Frame = WorkbenchPalette.Border,
            Ground = WorkbenchPalette.Panel,
            Radius = 6,
            FrameWidth = 1,
            Padding = new Padding(10),
            Margin = new Padding(12, 0, 12, 12),
        };
        compiledCard.Controls.Add(compiledLayout);

        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 4,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        for (var i = 0; i < 4; i++) stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.Controls.Add(CreateSectionLabel("选中元素"), 0, 0);
        stack.Controls.Add(_selectedCard, 0, 1);
        stack.Controls.Add(CreateSectionLabel("编译结构"), 0, 2);
        stack.Controls.Add(compiledCard, 0, 3);

        var page = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = WorkbenchPalette.Panel,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        page.Controls.Add(stack);

        Controls.Add(page);

        RefreshSelection();
        RefreshCompiled();
    }

    /// <summary>The tree the inspector describes; swapping demos rebinds every subscription.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TreeViewModel? Tree
    {
        get => _tree;
        set
        {
            if (ReferenceEquals(_tree, value)) return;

            Detach();
            _tree = value;
            Attach();
            RefreshSelection();
            RefreshCompiled();
        }
    }

    private void Attach()
    {
        if (_tree is null) return;
        _tree.PropertyChanged += OnTreePropertyChanged;
        _tree.CompiledStructure.CollectionChanged += OnCompiledStructureChanged;
    }

    private void Detach()
    {
        if (_tree is not null)
        {
            _tree.PropertyChanged -= OnTreePropertyChanged;
            _tree.CompiledStructure.CollectionChanged -= OnCompiledStructureChanged;
        }

        if (_selected is not null) _selected.PropertyChanged -= OnSelectedEntryChanged;
        _selected = null;
        _tree = null;
    }

    private void OnTreePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TreeViewModel.SelectedElement) or nameof(TreeViewModel.HasSelectedElement))
            RefreshSelection();
    }

    private void OnSelectedEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkflowElementEntry.DetailText)) _detail.Text = _selected?.DetailText ?? string.Empty;
    }

    private void OnCompiledStructureChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshCompiled();

    private void RefreshSelection()
    {
        if (InvokeRequired)
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            try { BeginInvoke(RefreshSelection); } catch (InvalidOperationException) { }
            return;
        }

        if (_selected is not null) _selected.PropertyChanged -= OnSelectedEntryChanged;
        _selected = _tree?.SelectedElement;
        if (_selected is not null) _selected.PropertyChanged += OnSelectedEntryChanged;

        var hasSelection = _selected is not null;
        _selectedContent.Visible = hasSelection;
        _selectedHint.Visible = !hasSelection;

        if (_selected is { } entry)
        {
            _name.Text = entry.Label;
            _kind.Text = entry.KindText;
            _detail.Text = entry.DetailText;
        }
        else
        {
            _name.Text = string.Empty;
            _kind.Text = string.Empty;
            _detail.Text = string.Empty;
        }
    }

    private void RefreshCompiled()
    {
        if (InvokeRequired)
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            try { BeginInvoke(RefreshCompiled); } catch (InvalidOperationException) { }
            return;
        }

        var rows = _tree?.CompiledStructure;
        var count = rows?.Count ?? 0;

        _compiledList.BeginUpdate();
        _compiledList.Items.Clear();
        if (rows is not null)
        {
            foreach (var row in rows) _compiledList.Items.Add(row);
        }
        _compiledList.EndUpdate();

        _compiledCount.Text = $"{count} 段";
        _compiledList.Height = count > 0 ? Math.Min(220, count * 18 + 4) : 0;
        _compiledList.Visible = count > 0;
        _compiledEmpty.Visible = count == 0;
    }

    private void OnDrawCompiledRow(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _compiledList.Items.Count) return;
        if (_compiledList.Items[e.Index] is not CompiledOutlineRow row) return;

        var g = e.Graphics;
        using (var back = new SolidBrush(WorkbenchPalette.PanelRaised))
        {
            g.FillRectangle(back, e.Bounds);
        }

        var kindBounds = new Rectangle(e.Bounds.X + 4 + (row.Depth * 12), e.Bounds.Y, 58, e.Bounds.Height);
        TextRenderer.DrawText(
            g,
            row.Kind,
            CardTheme.Font(8f),
            kindBounds,
            WorkbenchPalette.Warning,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        var labelBounds = new Rectangle(kindBounds.Right, e.Bounds.Y, Math.Max(0, e.Bounds.Right - kindBounds.Right - 4), e.Bounds.Height);
        TextRenderer.DrawText(
            g,
            row.Label,
            CardTheme.Font(8f),
            labelBounds,
            WorkbenchPalette.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }

    private void LocateSelected()
    {
        if (_selected is not { } entry) return;

        switch (entry.Element)
        {
            case IWorkflowNodeViewModel node:
                node.Locate();
                break;
            case IWorkflowSlotViewModel slot:
                slot.Locate();
                break;
            case IWorkflowLinkViewModel link:
                link.Locate();
                break;
        }
    }

    private void DeleteSelected()
    {
        if (_selected is not { } entry) return;

        switch (entry.Element)
        {
            case IWorkflowNodeViewModel node:
                node.GetHelper().Delete();
                break;
            case IWorkflowSlotViewModel slot:
                slot.GetHelper().Delete();
                break;
            case IWorkflowLinkViewModel link:
                link.GetHelper().Delete();
                break;
        }

        if (_tree is not null && ReferenceEquals(_tree.SelectedElement, entry))
            _tree.SelectedElement = null;
    }

    private static Label CreateSectionLabel(string text) => new()
    {
        AutoSize = true,
        Text = text,
        ForeColor = WorkbenchPalette.TextSecondary,
        BackColor = Color.Transparent,
        Font = CardTheme.Font(8.5f, FontStyle.Bold),
        Margin = new Padding(12, 14, 12, 6),
    };

    private static GhostButton CreateAction(string text, Color color) => new()
    {
        Text = text,
        TextColor = color,
        AutoSize = false,
        Size = new DrawingSize(72, 24),
        Margin = new Padding(0, 0, 6, 0),
    };
}
