// VeloxDev customization: the surface owns pan, zoom, link drawing and the input route; set ViewModel from the host.
using System.ComponentModel;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.VisualHosting;

namespace Demo.Views.Workflow;

/// <summary>The trimmed demo's workflow surface: the visual host plus its info HUD and link menu.</summary>
public sealed partial class TreeView : VeloxDev.WinForms.Xaml.Controls.UserControl
{
    /// <summary>Creates the surface, applies the palette and wires the HUD.</summary>
    public TreeView()
    {
        InitializeComponent();

        var background = XamlRuntime.ParseColor("#1E1E1E");
        workflowHost.BackColor = background;
        workflowHost.GridDecorator.Background = background;
        workflowHost.GridDecorator.BorderBrush = XamlRuntime.ParseColor("#33FFFFFF");
        workflowHost.GridDecorator.BorderThickness = XamlRuntime.ParseThickness("1");
        workflowHost.GridDecorator.CornerRadius = 3f;
        workflowHost.LinkLayer.LineColor = XamlRuntime.ParseColor("#DDFFFFFF");
        workflowHost.LinkLayer.HoverLineColor = XamlRuntime.ParseColor("#FFFFFFFF");        workflowHost.Minimap.NodeBrush = XamlRuntime.ParseColor("#DC38BDF8");
        workflowHost.Minimap.ViewportStroke = XamlRuntime.ParseColor("#F0FFFFFF");

        // 连线菜单归宿主：条目就是这里这几行。
        workflowHost.LinkMenuBuilder = static (menu, link) => menu.AddItem("Delete", () =>
        {
            if (link.DeleteCommand.CanExecute(null))
            {
                link.DeleteCommand.Execute(null);
            }
        });

        // 卡片走模板里的选择器；选择器优先于 surface 的默认卡片。
        workflowHost.TemplateSelector = new CustomTemplateSelector
        {
            NodeTemplate = new VisualDataTemplate(static () => new NodeView()),
        };

        // WinForms 把子控件追加到 z 序末尾（最下）；把 HUD 提上来。
        _infoOverlay.BringToFront();

        // 视口由 surface 在平移/缩放后推给 helper，HUD 随画面每帧重算即可。
        workflowHost.Paint += (_, _) => _infoOverlay.UpdateText();
    }

    /// <summary>The workflow tree the surface renders, or <see langword="null"/>.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ITreeComponent? ViewModel
    {
        get => workflowHost.Tree;
        set
        {
            workflowHost.Tree = value;
            _infoOverlay.Bind(value);
        }
    }

    /// <summary>The visual surface this control hosts.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public VisualWorkflowSurface Surface => workflowHost;
}
