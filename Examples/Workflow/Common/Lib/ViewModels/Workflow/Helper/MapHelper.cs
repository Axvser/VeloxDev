using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;

namespace Demo.ViewModels.Workflow.Helper;

public class MapHelper : TreeHelper<TreeViewModel>
{
    public override void Install(IWorkflowTreeViewModel tree)
    {
        base.Install(tree);

        // 使能空间索引，-1 状态码意味着使能失败
        if (Component is not null && tree.EnableMap(240, Component.VisibleItems) > -1)
        {
            // 240 描述一个典型网格的大小，与节点的典型大小相匹配时能获得更好的性能
            // VisibleItems 是一个可通知集合，此处与Map绑定后，虚拟化的结果将同步给该集合
        }
    }

    public override void Uninstall(IWorkflowTreeViewModel tree)
    {
        base.Uninstall(tree);

        // 清理空间索引，5 状态码意味着合理的情况
        if (tree.ClearMap() == 5)
        {

        }
    }

    public void Virtualize(Viewport viewport) => Component?.Virtualize(viewport); // 执行虚拟化
}