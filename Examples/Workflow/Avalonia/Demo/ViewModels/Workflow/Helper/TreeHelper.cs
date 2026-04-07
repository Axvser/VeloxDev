using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem.StandardEx;

namespace Demo.ViewModels.Workflow.Helper;

public class TreeHelper : WorkflowHelper.ViewModel.Tree
{
    private TreeViewModel? _viewModel;

    public override void Install(IWorkflowTreeViewModel tree)
    {
        base.Install(tree);
        _viewModel = tree as TreeViewModel;

        // 使能空间索引，-1 状态码意味着使能失败
        if (_viewModel is not null && tree.EnableMap(240, _viewModel.VisibleItems) > -1)
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

        _viewModel = null;
    }

    public void Virtualize(Viewport viewport) => _viewModel?.Virtualize(viewport); // 执行虚拟化
}