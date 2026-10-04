using System.ComponentModel;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

// 全量 demo 里每种节点视图的基类。这些视图是纯展示的：拖拽 / 连线 / 平移全归 NodeEditorSurface，由它把每个视图摆到
// node.Anchor + 布局偏移上，所以节点视图既不该自己设 Canvas.Left/Top，也不该自己接拖拽与连线。基类按设计尺寸搭出卡片，
// 再套一层 Viewbox 把整张卡（外壳、字，以及表面画在其上的端口）缩到被折叠过的节点盒上。
// 端口不在这棵树里：本平台渲染器按布局盒裁剪子元素，而端口有一半骑在卡边外，所以它由表面画在卡之上。于是这个基类
// 只剩下卡片本身。
internal abstract class NodeViewBase : Canvas
{
    private Viewbox? _viewbox;
    private Border? _pill;

    // 绑定到这张卡的节点视图模型。
    protected IWorkflowNodeViewModel Node { get; private set; } = null!;

    // 卡片标题行上的标题，给标题会变的卡用。
    protected TextBlock? TitleText { get; private set; }

    // 执行序号那块字（标题行右侧），空串即不显示。
    protected TextBlock? ExecOrderText { get; private set; }

    // 标题行状态胶囊里的字（由子类在属性变化时更新）。
    protected TextBlock? StatusText { get; private set; }

    // 卡片标题行左侧那条 2px 色条的颜色（每种节点类型一个）。
    protected abstract Color Accent { get; }

    // 标题行右侧状态胶囊的初始文字；空串即这张卡没有胶囊（Controller 卡就没有）。
    protected virtual string InitialStatus(IWorkflowNodeViewModel node) => string.Empty;

    // 标题行右侧执行序号的初始文字；空串即不显示。
    protected virtual string InitialExecOrder(IWorkflowNodeViewModel node) => string.Empty;

    // 胶囊文字是否加粗（Enum 卡的路由结果加粗，Python 卡的状态不加粗）。
    protected virtual bool StatusBold => false;

    protected virtual Color StatusTextColor => CardPalette.GhostCloseText;

    // 兜底卡：只有卡面与类型色条，没有标题行，也不填主体。
    protected virtual bool IsBareCard => false;

    // 卡片标题。接口只有几何与插槽、没有显示名，所以读类型自己发布的 Title；不发布的类型（控制器）重写此方法给出自己的名字。
    protected virtual string TitleFor(IWorkflowNodeViewModel node) => NodePorts.TitleOf(node);

    // 把主体内容放进 content（卡片的第 2 行）。
    protected abstract void Build(IWorkflowNodeViewModel node, Grid content);

    // 节点属性变化时调用，子类据此更新标题 / 状态文字。
    protected virtual void OnNodePropertyChanged(string propertyName)
    {
    }

    // 节点在设计尺寸（缩放 1）下的尺寸，在 Bind 时捕获。
    public double DesignWidth { get; private set; }
    public double DesignHeight { get; private set; }

    // 把视图绑到一个节点并搭出它的卡片。
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

    // 设置状态胶囊：文字为空就把整颗胶囊收起来（Auto 列会跟着塌掉）。
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

    // 设置标题行右侧的执行序号；空串即不显示。
    protected void SetExecOrder(string text)
    {
        string value = text ?? string.Empty;

        if (ExecOrderText is not null)
        {
            ExecOrderText.Text = value;
            ExecOrderText.Visibility = value.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // 类型的 [DefaultSize] 特性是节点设计画布的唯一来源；只有类型没声明 DefaultSize 时才退回活的 node.Size。
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

    // 把整张卡（外壳、字）缩到当前（折叠）尺寸：工作区缩放时把 Viewbox 调小 1/scale，与 WPF 那家的节点 Viewbox 一致。
    // 视图定尺寸后调用。
    public void ApplyScale()
    {
        if (_viewbox is not null)
        {
            _viewbox.Width = Width;
            _viewbox.Height = Height;
        }
    }
}
