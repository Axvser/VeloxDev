using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Demo.ViewModels;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

// 侧栏的「元素」页：节点 / 插槽 / 连线三张表，每行自带 Locate / Delete（与 Avalonia、WinUI、MAUI、Blazor 同形）。
// 列表状态全部来自共享的 WorkflowElementCatalog，这里只做渲染与转发。
internal sealed class WorkflowElementPanel : UserControl
{
    private readonly WorkflowElementCatalog _catalog = new();
    private readonly ElementRowList _nodeList;
    private readonly ElementRowList _slotList;
    private readonly ElementRowList _linkList;
    private readonly Label _status;
    private TreeViewModel? _tree;

    /// <summary>Raised when a row is picked, or when the host clears the selection. The host feeds it to the tree.</summary>
    public event EventHandler<WorkflowElementEntry?>? SelectedElementChanged;

    public WorkflowElementPanel()
    {
        BackColor = CardTheme.Ground;
        ForeColor = CardTheme.Value;
        Padding = new Padding(10);

        _nodeList = CreateList();
        _slotList = CreateList();
        _linkList = CreateList();
        foreach (var list in new[] { _nodeList, _slotList, _linkList })
        {
            list.SelectedEntryChanged += OnListSelected;
            list.LocateRequested += OnLocateRequested;
            list.DeleteRequested += OnDeleteRequested;
        }

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            BackColor = CardTheme.Ground,
            ForeColor = CardTheme.Value,
            Margin = new Padding(0),
        };
        tabs.TabPages.Add(CreatePage("节点", _nodeList));
        tabs.TabPages.Add(CreatePage("插槽", _slotList));
        tabs.TabPages.Add(CreatePage("连线", _linkList));

        _status = new Label
        {
            AutoSize = false,
            Height = 32,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = CardTheme.Label,
            Font = CardTheme.Font(CardTheme.LabelSize),
            Margin = new Padding(0, 6, 0, 0),
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = CardTheme.Ground,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(tabs, 0, 0);
        layout.Controls.Add(_status, 0, 1);

        Controls.Add(layout);

        _catalog.Changed += (_, _) => RefreshLists();
        RefreshLists();
    }

    /// <summary>Selects <paramref name="entry"/> in the list that owns it, without raising an event back.</summary>
    public void SetSelectedElement(WorkflowElementEntry? entry)
    {
        _nodeList.Select(entry is { Kind: WorkflowElementKind.Node } ? entry : null);
        _slotList.Select(entry is { Kind: WorkflowElementKind.Slot } ? entry : null);
        _linkList.Select(entry is { Kind: WorkflowElementKind.Link } ? entry : null);
    }

    private void OnListSelected(object? sender, WorkflowElementEntry? entry)
    {
        if (entry is null) return;

        // One list owns the selection at a time, as in the XAML demos.
        if (!ReferenceEquals(sender, _nodeList)) _nodeList.Select(null);
        if (!ReferenceEquals(sender, _slotList)) _slotList.Select(null);
        if (!ReferenceEquals(sender, _linkList)) _linkList.Select(null);

        SelectedElementChanged?.Invoke(this, entry);
    }

    private void OnLocateRequested(object? sender, WorkflowElementEntry entry)
    {
        ShowStatus(_catalog.Locate(entry)
            ? $"已定位 {entry.Label}"
            : $"未能定位 {entry.Label}：没有可用的表面，或该元素还没有有限坐标");
    }

    private void OnDeleteRequested(object? sender, WorkflowElementEntry entry)
    {
        _catalog.Delete(entry);
        // Delete rebuilds the lists and resets the status; write the line after it.
        ShowStatus($"已删除 {entry.Label}");
    }

    // 侧栏要列的树；换树时目录会退订旧树、重建三张表。不是可设计属性，序列化器不必知道它。
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IWorkflowTreeViewModel? Tree
    {
        set
        {
            _tree = value as TreeViewModel;
            _catalog.SetTree(value);
        }
    }

    private void RefreshLists()
    {
        // 目录自己会把重建排回创建它的线程，但这条防线很便宜：代理线程上的 Changed 也不会碰控件。
        if (InvokeRequired)
        {
            BeginInvoke(RefreshLists);
            return;
        }

        _nodeList.SetEntries(_catalog.Nodes);
        _slotList.SetEntries(_catalog.Slots);
        _linkList.SetEntries(_catalog.Links);
        _status.Text = _catalog.Summary;

        // The catalog keeps the rows alive until the next rebuild; when the tree still points at one of them,
        // put the highlight back rather than leave the inspector describing a row nothing is selected on.
        if (_tree?.SelectedElement is { } selected)
        {
            var stillListed = _catalog.Nodes.Contains(selected)
                || _catalog.Slots.Contains(selected)
                || _catalog.Links.Contains(selected);
            SetSelectedElement(stillListed ? selected : null);
        }
    }

    private void ShowStatus(string message) => _status.Text = $"{_catalog.Summary} · {message}";

    private ElementRowList CreateList() => new()
    {
        Dock = DockStyle.Fill,
    };

    private static TabPage CreatePage(string title, ElementRowList list) => new()
    {
        BackColor = CardTheme.Ground,
        ForeColor = CardTheme.Value,
        UseVisualStyleBackColor = false,
        Padding = new Padding(4),
        Controls = { list },
        Text = title,
    };
}
