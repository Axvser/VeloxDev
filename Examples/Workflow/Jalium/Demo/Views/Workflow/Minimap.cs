using Jalium.UI.Controls;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views.Workflow;

// 节点编辑器表面的小地图覆盖层：派生适配器自带的 WorkflowMinimapOverlay，与 Trimmed demo 的做法一致
// 配色即适配器默认值（原先写死的几个色）；组装窗口在滚动或平移时喂它 WorkflowTree、ScrollViewer 与三个偏移数
internal sealed class Minimap : WorkflowMinimapOverlay
{
    public Minimap(ScrollViewer viewer)
    {
        ScrollViewer = viewer;
    }
}
