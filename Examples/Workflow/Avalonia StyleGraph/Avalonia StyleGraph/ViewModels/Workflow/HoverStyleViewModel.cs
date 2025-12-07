using Avalonia.Media;
using Avalonia_StyleGraph.ViewModels.Workflow.Helper;
using Avalonia_StyleGraph.ViewModels.Workflow.Helper.StyleItems;
using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace Avalonia_StyleGraph.ViewModels.Workflow;

[WorkflowBuilder.ViewModel.Node
    <StyleHelper>
    (workSemaphore: 1)]
public partial class HoverStyleViewModel
{
    public HoverStyleViewModel() => InitializeWorkflow();

    // …… 自由扩展您的节点视图模型

    [VeloxProperty] private byte _alpha = 255;
    [VeloxProperty] private byte _red = 255;
    [VeloxProperty] private byte _green = 255;
    [VeloxProperty] private byte _blue = 255;

    partial void OnAlphaChanged(byte oldValue, byte newValue)
    {
        Update();
    }

    partial void OnRedChanged(byte oldValue, byte newValue)
    {
        Update();
    }

    partial void OnGreenChanged(byte oldValue, byte newValue)
    {
        Update();
    }

    partial void OnBlueChanged(byte oldValue, byte newValue)
    {
        Update();
    }

    public async void Update()
    {
        await BroadcastCommand.ClearAsync();

        BorderStyle style = new();
        style.Transition.Property(
            control => control.Background,
            new SolidColorBrush(Color.FromArgb(Alpha, Red, Green, Blue
            )));

        await BroadcastCommand.ExecuteAsync(style);
    }
}
