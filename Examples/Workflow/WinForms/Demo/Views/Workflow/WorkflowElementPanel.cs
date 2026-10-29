using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Demo.ViewModels;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

// 侧栏的「元素」页：把树里的节点 / 插槽 / 连线列成三张表，每张表下各有 Locate / Delete ——
// 列表状态全部来自共享的 WorkflowElementCatalog，这里只做渲染与转发。
internal sealed class WorkflowElementPanel : UserControl
{
    private readonly WorkflowElementCatalog _catalog = new();
    private readonly ListBox _nodeList;
    private readonly ListBox _slotList;
    private readonly ListBox _linkList;
    private readonly Label _status;

    public WorkflowElementPanel()
    {
        BackColor = CardTheme.Ground;
        ForeColor = CardTheme.Value;
        Padding = new Padding(10);

        _nodeList = CreateList();
        _slotList = CreateList();
        _linkList = CreateList();

        var header = new Label
        {
            AutoSize = true,
            Text = "元素（从 Tree 加载）",
            ForeColor = CardTheme.AccentController,
            Font = CardTheme.Font(11.5f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 6),
        };

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
            RowCount = 3,
            BackColor = CardTheme.Ground,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(tabs, 0, 1);
        layout.Controls.Add(_status, 0, 2);

        Controls.Add(layout);

        _catalog.Changed += (_, _) => RefreshLists();
        RefreshLists();
    }

    // 侧栏要列的树；换树时目录会退订旧树、重建三张表。不是可设计属性，序列化器不必知道它。
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IWorkflowTreeViewModel? Tree
    {
        set => _catalog.SetTree(value);
    }

    // 元素被删掉后列表会整表重建；选中项不跨重建保留，所以每次重建后选中的是「无」而不是旧行的索引。
    private void RefreshLists()
    {
        // 目录自己会把重建排回创建它的线程，但这条防线很便宜：代理线程上的 Changed 也不会碰控件。
        if (InvokeRequired)
        {
            BeginInvoke(RefreshLists);
            return;
        }

        Reload(_nodeList, _catalog.Nodes);
        Reload(_slotList, _catalog.Slots);
        Reload(_linkList, _catalog.Links);
        _status.Text = _catalog.Summary;
    }

    private static void Reload(ListBox list, ObservableCollection<WorkflowElementEntry> entries)
    {
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var entry in entries) list.Items.Add(entry);
        list.EndUpdate();
    }

    private void LocateSelected(ListBox list)
    {
        if (list.SelectedItem is not WorkflowElementEntry entry)
        {
            ShowStatus("先选择一个元素");
            return;
        }

        ShowStatus(_catalog.Locate(entry)
            ? $"已定位 {entry.Label}"
            : $"未能定位 {entry.Label}：没有可用的表面，或该元素还没有有限坐标");
    }

    private void DeleteSelected(ListBox list)
    {
        if (list.SelectedItem is not WorkflowElementEntry entry)
        {
            ShowStatus("先选择一个元素");
            return;
        }

        _catalog.Delete(entry);
        // Delete 触发重建、把状态行刷成摘要；这一行覆盖回来，用户才看得到自己刚删了什么。
        ShowStatus($"已删除 {entry.Label}");
    }

    private void ShowStatus(string message) => _status.Text = $"{_catalog.Summary} · {message}";

    private static ListBox CreateList() => new()
    {
        Dock = DockStyle.Fill,
        BackColor = CardTheme.Field,
        ForeColor = CardTheme.Value,
        BorderStyle = BorderStyle.FixedSingle,
        DisplayMember = nameof(WorkflowElementEntry.Label),
        IntegralHeight = false,
        Font = CardTheme.Font(CardTheme.ValueSize),
    };

    private TabPage CreatePage(string title, ListBox list)
    {
        var page = new TabPage(title)
        {
            BackColor = CardTheme.Ground,
            ForeColor = CardTheme.Value,
            UseVisualStyleBackColor = false,
            Padding = new Padding(4),
        };

        var locate = CreateAction("Locate", CardTheme.ActionCompile);
        locate.Click += (_, _) => LocateSelected(list);
        var delete = CreateAction("Delete", CardTheme.ActionStop);
        delete.Click += (_, _) => DeleteSelected(list);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = CardTheme.Ground,
            Padding = new Padding(0, 6, 0, 0),
        };
        actions.Controls.Add(locate);
        actions.Controls.Add(delete);

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
        layout.Controls.Add(list, 0, 0);
        layout.Controls.Add(actions, 0, 1);
        page.Controls.Add(layout);

        return page;
    }

    private static GhostButton CreateAction(string text, Color color) => new()
    {
        Text = text,
        TextColor = color,
        AutoSize = false,
        Size = new System.Drawing.Size(64, 24),
        Margin = new Padding(0, 0, 6, 0),
    };
}
