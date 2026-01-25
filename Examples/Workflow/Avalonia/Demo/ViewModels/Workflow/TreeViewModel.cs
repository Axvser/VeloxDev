using Demo.ViewModels.Workflow.Helper;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.Core.Extension;
using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.ViewModels;

[WorkflowBuilder.ViewModel.Tree<TreeHelper>]
public partial class TreeViewModel
{
    public TreeViewModel() => InitializeWorkflow();

    // …… 自由扩展您的工作流树视图模型

    /// <summary>
    /// 原始尺寸
    /// </summary>
    [VeloxProperty] private Size originSize = new(2560, 1600);

    /// <summary>
    /// 逻辑正偏移
    /// </summary>
    [VeloxProperty] private Offset positiveOffset = new(0, 0);

    /// <summary>
    /// 逻辑负偏移
    /// </summary>
    [VeloxProperty] private Offset negativeOffset = new(0, 0);

    /// <summary>
    /// 缩放比例
    /// </summary>
    [VeloxProperty] private double scale = 1;

    /// <summary>
    /// 实际尺寸
    /// </summary>
    [VeloxProperty] private Size actualSize = new(2560, 1600);

    partial void OnOriginSizeChanged(Size oldValue, Size newValue) => Layout();
    partial void OnScaleChanged(double oldValue, double newValue) => Layout();
    partial void OnPositiveOffsetChanged(Offset oldValue, Offset newValue) => Layout();
    partial void OnNegativeOffsetChanged(Offset oldValue, Offset newValue) => Layout();

    private void Layout()
    {
        var baseWidth = OriginSize.Width + PositiveOffset.Left + NegativeOffset.Left;
        var baseHeight = OriginSize.Height + PositiveOffset.Top + NegativeOffset.Top;
        ActualSize = new Size(baseWidth, baseHeight);
    }

    [VeloxCommand]
    private async Task Save(object? parameter, CancellationToken ct)
    {
        if (parameter is not string path || !File.Exists(path)) return;
        await Helper.CloseAsync();
        var json = this.Serialize();
        await File.WriteAllTextAsync(path, json, ct);
    }
}
