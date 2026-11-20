extern alias wf;

using System.ComponentModel;
using VeloxDev.WorkflowSystem;
using WfAttached = wf::VeloxDev.WorkflowSystem.AttachedBehaviors;
using WfXaml = wf::VeloxDev.WinForms.Xaml;

namespace VeloxDev.Adapters.Benchmarks.Headed;

// 刻意做小：一张边框 + 一段文字。WinForms 没有 DataContext，池按约定写 ViewModel 属性。
internal sealed class WinFormsBenchmarkNodeView : Panel
{
    private IWorkflowNodeViewModel? _node;

    public WinFormsBenchmarkNodeView()
    {
        Width = 160;
        Height = 90;
        BackColor = Color.FromArgb(0x31, 0x3A, 0x4A);
        BorderStyle = BorderStyle.FixedSingle;
        Controls.Add(new Label
        {
            Text = "node",
            ForeColor = Color.White,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0),
        });
    }

    // 池的 ViewContainer 按 ViewModel/DataContext/BindingContext 三个约定名接线。
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public IWorkflowViewModel? ViewModel
    {
        get => _node;
        set => Bind(value as IWorkflowNodeViewModel);
    }

    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);

        // 复用的控件在已经入树时换绑，父级变化不会再触发；池的 Arrange 也只会问附加属性。
        Apply();
    }

    private void Bind(IWorkflowNodeViewModel? node)
    {
        if (ReferenceEquals(_node, node))
        {
            return;
        }

        if (_node is INotifyPropertyChanged old)
        {
            old.PropertyChanged -= OnNodePropertyChanged;
        }

        _node = node;
        if (node is INotifyPropertyChanged current)
        {
            current.PropertyChanged += OnNodePropertyChanged;
        }

        Apply();
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor) or nameof(IWorkflowNodeViewModel.Size))
        {
            Apply();
        }
    }

    private void Apply()
    {
        if (_node is null)
        {
            Location = System.Drawing.Point.Empty;
            Size = new System.Drawing.Size(1, 1);
            return;
        }

        Size = new System.Drawing.Size(
            Math.Max(1, (int)Math.Round(_node.Size.Width)),
            Math.Max(1, (int)Math.Round(_node.Size.Height)));
        Location = new System.Drawing.Point(
            (int)Math.Round(_node.Anchor.Horizontal),
            (int)Math.Round(_node.Anchor.Vertical));
    }
}

// 连线只做 1×1 占位：基准量的是管线记账，不是曲线绘制。
internal sealed class WinFormsBenchmarkLinkView : Panel
{
    public WinFormsBenchmarkLinkView()
    {
        Size = new System.Drawing.Size(1, 1);
        BackColor = Color.FromArgb(0x4A, 0x6A, 0x9A);
    }

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public IWorkflowViewModel? ViewModel
    {
        get => null;
        set { }
    }
}

internal sealed class WinFormsBenchmarkTemplateSelector : WfXaml.TemplateSelector
{
    private static readonly WfXaml.DataTemplate NodeTemplate = new(() => new WinFormsBenchmarkNodeView());
    private static readonly WfXaml.DataTemplate LinkTemplate = new(() => new WinFormsBenchmarkLinkView());

    public override WfXaml.DataTemplate? SelectTemplate(object? item)
        => item is IWorkflowNodeViewModel ? NodeTemplate : LinkTemplate;
}
