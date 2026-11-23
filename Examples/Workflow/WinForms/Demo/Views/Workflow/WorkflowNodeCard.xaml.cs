// VeloxDev customization: the card is composed in XAML; the workflow surface binds it to the node model.
using Demo.ViewModels;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>A workflow node card composed of visual XAML.</summary>
public sealed partial class WorkflowNodeCard : VeloxDev.WinForms.Xaml.Visuals.Controls.UserControl
{
    /// <summary>Creates the card and runs the generated initializer.</summary>
    public WorkflowNodeCard()
    {
        InitializeComponent();
        cardTitle.Font = CardTheme.Font(CardTheme.TitleSize, System.Drawing.FontStyle.Bold);
        UpdateTitle();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, DataContextProperty))
        {
            UpdateTitle();
        }
    }

    // 节点接口只承诺几何与槽；标题是视图概念，按 Demo 的类型优先级取名，最后退回类型名。
    private void UpdateTitle()
    {
        cardTitle.Text = DataContext switch
        {
            null => string.Empty,
            TimerNodeViewModel timer => timer.Title,
            EnumSelectorNodeViewModel selector => selector.Title,
            PythonScriptNodeViewModel python => python.Title,
            INodeComponent node => node.GetType().Name.Replace("ViewModel", string.Empty),
            _ => DataContext.GetType().Name,
        };
    }
}
