using System.ComponentModel;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>
/// 全量 demo 里每种节点视图的基类。这些视图是<b>纯展示</b>的：拖拽 / 连线 / 平移全归
/// <see cref="NodeEditorSurface"/>，由它把每个视图摆到 node.Anchor + 布局偏移上，所以节点视图
/// 既不该自己设 Canvas.Left/Top，也不该自己接拖拽与连线。基类按设计尺寸搭出卡片，再套一层
/// <see cref="Viewbox"/> 把整张卡（外壳、字、以及表面画在其上的端口）缩到被折叠过的节点盒上，
/// 与 WPF 那家节点视图的 Viewbox 同一种做法。
/// <para>
/// 端口不在这棵树里：本平台渲染器按布局盒裁剪子元素，而端口有一半骑在卡边外，所以它由表面画在卡之上
/// （见 <see cref="NodeEditorSurface.DrawPorts"/>）。于是这个基类只剩下卡片本身。
/// </para>
/// </summary>
internal abstract class NodeViewBase : Canvas
{
    private Viewbox? _viewbox;
    private Border? _pill;

    /// <summary>The node view-model bound to this card.</summary>
    protected IWorkflowNodeViewModel Node { get; private set; } = null!;

    /// <summary>The card's header title, for cards whose title changes.</summary>
    protected TextBlock? TitleText { get; private set; }

    /// <summary>执行序号那块字（标题行右侧），空串即不显示。</summary>
    protected TextBlock? ExecOrderText { get; private set; }

    /// <summary>The text inside the header's status capsule (updated by subclasses on property changes).</summary>
    protected TextBlock? StatusText { get; private set; }

    /// <summary>卡片标题行左侧那条 2px 色条的颜色（每种节点类型一个）。</summary>
    protected abstract Color Accent { get; }

    /// <summary>标题行右侧状态胶囊的初始文字；空串即这张卡没有胶囊（Controller 卡就没有）。</summary>
    protected virtual string InitialStatus(IWorkflowNodeViewModel node) => string.Empty;

    /// <summary>标题行右侧执行序号的初始文字；空串即不显示。</summary>
    protected virtual string InitialExecOrder(IWorkflowNodeViewModel node) => string.Empty;

    /// <summary>胶囊文字是否加粗（Enum 卡的路由结果加粗，Python 卡的状态不加粗）。</summary>
    protected virtual bool StatusBold => false;

    /// <summary>胶囊文字色。</summary>
    protected virtual Color StatusTextColor => CardPalette.GhostCloseText;

    /// <summary>兜底卡：只有卡面与类型色条，没有标题行，也不填主体。</summary>
    protected virtual bool IsBareCard => false;

    /// <summary>卡片标题。接口只有几何与插槽、没有显示名，所以读类型自己发布的 Title；
    /// 不发布的类型（控制器）重写此方法给出自己的名字。</summary>
    protected virtual string TitleFor(IWorkflowNodeViewModel node) => NodePorts.TitleOf(node);

    /// <summary>把主体内容放进 <paramref name="content"/>（卡片的第 2 行）。</summary>
    protected abstract void Build(IWorkflowNodeViewModel node, Grid content);

    /// <summary>Called for node property changes so subclasses can update header/status text.</summary>
    protected virtual void OnNodePropertyChanged(string propertyName)
    {
    }

    /// <summary>The node's DESIGN (scale-1) size, captured at Bind.</summary>
    public double DesignWidth { get; private set; }
    public double DesignHeight { get; private set; }

    /// <summary>Binds the view to a node and builds its card.</summary>
    public void Bind(IWorkflowNodeViewModel node)
    {
        Node = node;
        Children.Clear();

        // 设计（缩放前）画布的尺寸永远是节点类型的 [DefaultSize]，不是活的 node.Size（那是 DefaultSize×折叠）。
        // 这样卡片的排版对着类型走（Controller 230×170、Timer 200×140、Python 280×260、Enum 280×380），
        // 与节点是在哪个缩放下被建出来的无关；下面的 Viewbox 再把这块固定设计画布缩到被折叠的盒子上
        (double designWidth, double designHeight) = ResolveDesignSize(node);
        DesignWidth = designWidth;
        DesignHeight = designHeight;
        Width = node.Size.Width;
        Height = node.Size.Height;

        FrameworkElement card;
        if (IsBareCard)
        {
            card = NodeChrome.BareCard(DesignWidth, DesignHeight, Accent);
        }
        else
        {
            card = NodeChrome.Card(DesignWidth, DesignHeight, Accent, TitleFor(node),
                InitialExecOrder(node), InitialStatus(node), StatusTextColor, StatusBold,
                out var content, out var titleText, out var execText, out var pill, out var pillText);
            TitleText = titleText;
            ExecOrderText = execText;
            StatusText = pillText;
            _pill = pill;
            Build(node, content);
        }

        // 卡片排在设计尺寸的内层画布上，Viewbox 铺满被折叠的盒子并整体缩放它
        // （外壳、字、以及表面画在它上面的端口都按同一比例），与 WPF 那家节点视图的 Viewbox 一致
        _viewbox = new Viewbox { Child = card, Stretch = Stretch.Uniform };
        Canvas.SetLeft(_viewbox, 0);
        Canvas.SetTop(_viewbox, 0);
        Children.Add(_viewbox);

        if (node is INotifyPropertyChanged notify)
        {
            notify.PropertyChanged += OnNodePropertyChangedHandler;
        }
    }

    /// <summary>设置状态胶囊：文字为空就把整颗胶囊收起来（Auto 列会跟着塌掉）。</summary>
    protected void SetStatus(string text)
    {
        string value = text ?? string.Empty;

        if (StatusText is not null)
        {
            StatusText.Text = value;
        }

        if (_pill is not null)
        {
            _pill.Visibility = value.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>设置标题行右侧的执行序号；空串即不显示。</summary>
    protected void SetExecOrder(string text)
    {
        string value = text ?? string.Empty;

        if (ExecOrderText is not null)
        {
            ExecOrderText.Text = value;
            ExecOrderText.Visibility = value.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>The type's [DefaultSize] attribute is the single source of the node's design
    /// canvas; falls back to the live node.Size only when a type declares no DefaultSize.</summary>
    private static (double Width, double Height) ResolveDesignSize(IWorkflowNodeViewModel node)
    {
        if (Attribute.GetCustomAttribute(node.GetType(), typeof(DefaultSizeAttribute)) is DefaultSizeAttribute d
            && d.Width > 0 && d.Height > 0)
        {
            return (d.Width, d.Height);
        }

        return (node.Size.Width, node.Size.Height);
    }

    private void OnNodePropertyChangedHandler(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not null)
        {
            OnNodePropertyChanged(e.PropertyName);
        }
    }

    /// <summary>Scales the whole card (chrome, text) to the current (collapsed) size: resize the
    /// Viewbox so the design-size card shrinks by 1/scale when the workspace zooms — mirroring the WPF
    /// node Viewbox. Call after the view is sized.</summary>
    public void ApplyScale()
    {
        if (_viewbox is not null)
        {
            _viewbox.Width = Width;
            _viewbox.Height = Height;
        }
    }
}
