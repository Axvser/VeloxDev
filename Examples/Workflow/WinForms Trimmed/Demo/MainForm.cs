using Demo.ViewModels.Workflow;
using Demo.ViewModels.Workflow.Enums;
using Demo.Views.Workflow;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;
// `TreeView` collides with System.Windows.Forms.TreeView; alias the visual surface product.
using WorkflowSurface = Demo.Views.Workflow.TreeView;
// `Size` collides between System.Drawing and VeloxDev.WorkflowSystem; the drawing alias keeps `new Size(...)` unambiguous.
using Size = System.Drawing.Size;

namespace Demo;

public sealed class MainForm : Form
{
    // 窗口按 1085×683 逻辑 DIP 设计；无论当前监视器 DPI 是多少，客户区都表示这个逻辑尺寸。
    private static readonly Size s_logicalClientSize = new(1085, 683);

    private readonly VisualHost _host;
    private readonly System.Windows.Forms.Timer _copyResetTimer = new() { Interval = 1500 };
    private ITreeComponent? _tree;
    private IInputEvents? _events;
    private Button? _copyButton;

    public MainForm()
    {
        Text = "VeloxDev WinForms Trimmed";

        // 尺寸只由 ApplyLogicalClientSize 按当前 DPI 换算；关掉 WinForms 自己的 autoscale，避免两套缩放相乘。
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = s_logicalClientSize; // 句柄创建前按 96 DPI 的临时尺寸
        BackColor = Color.FromArgb(30, 30, 30);

        _host = new VisualHost { Dock = DockStyle.Fill };
        _host.Root = new WorkflowSurface();

        var shell = new Panel { Dock = DockStyle.Fill };
        shell.Controls.Add(_host);
        Controls.Add(shell);
        AddHudCopyButton(shell);
        Resize += (_, _) => PositionHudCopyButton();
        _host.LayoutUpdated += (_, _) => PositionHudCopyButton();

        WorkflowSurfaceBehavior.SetCanvasName(_host, "PART_Canvas");
        WorkflowSurfaceBehavior.SetScrollViewerName(_host, "PART_ScrollViewer");
        WorkflowSurfaceBehavior.SetGridDecoratorName(_host, "PART_GridDecorator");
        WorkflowSurfaceBehavior.SetMinimapOverlayName(_host, "PART_MinimapOverlay");
        WorkflowSurfaceBehavior.SetPointerPressSourceName(_host, "PART_SurfaceBorder");
        WorkflowSurfaceBehavior.SetLinkMenuBuilder(_host, BuildLinkMenu);
        WorkflowSurfaceBehavior.SetZoomEnabled(_host, true);
        WorkflowSurfaceBehavior.SetIsEnabled(_host, true);

        var tree = new TreeViewModel();
        LoadTree(tree);
        BindTree(tree);
    }

    // 连线条目归宿主：模板只声明空的 LinkContextMenu 资源，条目由这里加。
    private static void BuildLinkMenu(ContextMenuStrip menu, ILinkComponent link)
    {
        menu.Items.Add("Delete", null, (_, _) =>
        {
            if (link.DeleteCommand.CanExecute(null))
            {
                link.DeleteCommand.Execute(null);
            }
        });
    }

    // 演示层自己的 HUD 复制按钮：适配器只负责画 HUD 并提供 Lines，剪贴板留给宿主。
    private void AddHudCopyButton(Control shell)
    {
        _copyButton = new Button
        {
            Text = "复制",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0x1E, 0x3A, 0x5F),
            ForeColor = Color.FromArgb(0x7E, 0xC8, 0xFF),
            TabStop = false,
            Visible = false,
        };
        _copyButton.FlatAppearance.BorderColor = Color.FromArgb(0x7E, 0xC8, 0xFF);
        _copyButton.FlatAppearance.BorderSize = 1;
        _copyButton.Click += OnCopyHudClicked;
        _copyResetTimer.Tick += OnCopyResetTick;

        shell.Controls.Add(_copyButton);
        _copyButton.BringToFront();
    }

    // 放在 HUD 内部右下角（与 Avalonia/MAUI 的 InfoOverlay 同构）；位置从 HUD 实际边界来。
    private void PositionHudCopyButton()
    {
        if (_copyButton is null)
        {
            return;
        }

        if (FindInfoOverlay(_host.Root) is not { } hud || hud.AbsoluteBounds.Width <= 0f || hud.AbsoluteBounds.Height <= 0f)
        {
            _copyButton.Visible = false;
            return;
        }

        var scale = _host.DpiScale <= 0f ? 1f : _host.DpiScale;
        var bounds = hud.AbsoluteBounds;
        var width = Math.Max(1, (int)Math.Round(64 * scale));
        var height = Math.Max(1, (int)Math.Round(24 * scale));
        var gap = (int)Math.Round(8 * scale);
        var left = _host.Left + (int)Math.Round(bounds.Right * scale) - width - gap;
        var top = _host.Top + (int)Math.Round(bounds.Bottom * scale) - height - gap;

        _copyButton.Bounds = new Rectangle(left, Math.Max(0, top), width, height);
        _copyButton.Visible = true;
        _copyButton.BringToFront();
    }

    private void OnCopyHudClicked(object? sender, EventArgs e)
    {
        if (_copyButton is null || FindInfoOverlay(_host.Root) is not { } hud)
        {
            return;
        }

        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, hud.Lines));
            _copyButton.Text = "已复制";
            _copyResetTimer.Stop();
            _copyResetTimer.Start();
        }
        catch (ExternalException)
        {
            _copyButton.Text = "复制失败";
            _copyResetTimer.Stop();
            _copyResetTimer.Start();
        }
    }

    private void OnCopyResetTick(object? sender, EventArgs e)
    {
        _copyResetTimer.Stop();
        if (_copyButton is not null)
        {
            _copyButton.Text = "复制";
        }
    }

    private static WorkflowInfoOverlay? FindInfoOverlay(ContainerVisual? root)
    {
        if (root is null)
        {
            return null;
        }

        if (root is WorkflowInfoOverlay info)
        {
            return info;
        }

        foreach (var child in root.Children)
        {
            if (child is ContainerVisual container && FindInfoOverlay(container) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void BindTree(TreeViewModel tree)
    {
        _tree = tree;
        WorkflowSurfaceBehavior.SetWorkflowTree(_host, tree);

        Unwire();
        if (tree.GetHelper() is IInputEvents events)
        {
            _events = events;
            events.Input.KeyDown += OnKeyDown;
            events.Input.PointerWheelChanged += OnWheel;
        }
    }

    private void Unwire()
    {
        if (_events is not null)
        {
            _events.Input.KeyDown -= OnKeyDown;
            _events.Input.PointerWheelChanged -= OnWheel;
            _events = null;
        }
    }

    // VeloxDev customization: 撤销/重做与删除都是宿主的 —— 库只把按键路由过来，做什么由这里写。
    private void OnKeyDown(object? sender, KeyDownEventArgs e)
    {
        if (_tree is not { } tree) return;

        if (e.Key == InputKey.Delete)
        {
            if (WorkflowInput.For(tree).HoveredLink is { } hovered && hovered.DeleteCommand.CanExecute(null))
            {
                hovered.DeleteCommand.Execute(null);
                e.Handle.PreventDefault = true;
            }

            return;
        }

        if (!e.Modifiers.HasFlag(InputModifiers.Control)) return;

        var redo = e.Modifiers.HasFlag(InputModifiers.Shift);
        if (e.Key == InputKey.Z && !redo) tree.UndoCommand.Execute(null);
        else if (e.Key == InputKey.Y || (e.Key == InputKey.Z && redo)) tree.RedoCommand.Execute(null);
        else return;

        e.Handle.PreventDefault = true;
    }

    // VeloxDev customization: 画布滚轮由适配器整笔接管，所以这里是唯一能改写它去哪的机会。
    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!e.Modifiers.HasFlag(InputModifiers.Shift) || _tree is not { } tree) return;

        e.Handle.PreventDefault = true;
        // 竖轴正 = 远离用户，塞进横轴要取反 —— 横轴正 = 向右，而「远离用户」在画面上是向左。
        WorkflowInput.For(tree).Scroller?.ScrollBy(-e.DeltaY, 0d);
    }


    /// <inheritdoc />
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyLogicalClientSize();
    }

    /// <inheritdoc />
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyLogicalClientSize();

        // 框架会在本通知之后套用系统建议的物理矩形；排到消息队列末尾再校正一次，保证客户区精确等于逻辑尺寸。
        if (IsHandleCreated && !IsDisposed)
        {
            BeginInvoke(new Action(ApplyLogicalClientSize));
        }
    }

    /// <inheritdoc />
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyLogicalClientSize();
    }

    private void ApplyLogicalClientSize()
    {
        if (!IsHandleCreated || IsDisposed)
        {
            return;
        }

        var scale = DeviceDpi / 96f;
        var size = new Size(
            (int)Math.Round(s_logicalClientSize.Width * scale),
            (int)Math.Round(s_logicalClientSize.Height * scale));
        if (ClientSize != size)
        {
            ClientSize = size;
        }

        PositionHudCopyButton();
    }

    private static void LoadTree(TreeViewModel tree)
    {
        // Shift the world origin into the visible area so nodes in every quadrant are on screen when zoomed.
        tree.Layout.NegativeOffset = new Offset(320, 260);

        var size = new VeloxDev.WorkflowSystem.Size(260, 180);
        var nodes = new[]
        {
            new NodeViewModel { Name = "Lower-right (+x,+y)", Size = size, Anchor = new Anchor { Horizontal = 140, Vertical = 140 } },
            new NodeViewModel { Name = "Lower-left  (-x,+y)", Size = size, Anchor = new Anchor { Horizontal = -140, Vertical = 140 } },
            new NodeViewModel { Name = "Upper-left  (-x,-y)", Size = size, Anchor = new Anchor { Horizontal = -140, Vertical = -140 } },
            new NodeViewModel { Name = "Upper-right (+x,-y)", Size = size, Anchor = new Anchor { Horizontal = 140, Vertical = -140 } }
        };

        foreach (var node in nodes)
        {
            tree.CreateNodeCommand.Execute(node);
        }

        nodes[0].OutputSlots.SetSelector(typeof(bool));
        nodes[1].OutputSlots.SetSelector(typeof(VoltageRange));
        nodes[2].OutputSlots.SetSelector(typeof(ModelProtocol));
        nodes[0].InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        nodes[1].InputSlot.SetChannelCommand.Execute(SlotChannel.MultipleSources);
        nodes[2].InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        nodes[3].InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);

        // One visible link so the trimmed demo exercises the visual link policy, not only the unit graph.
        if (nodes[2].OutputSlots.Items.FirstOrDefault()?.Slot is { } sender)
        {
            tree.Links.Add(new LinkDefaultComponent
            {
                Sender = sender,
                Receiver = nodes[3].InputSlot,
                IsVisible = true,
            });
        }
    }
}
