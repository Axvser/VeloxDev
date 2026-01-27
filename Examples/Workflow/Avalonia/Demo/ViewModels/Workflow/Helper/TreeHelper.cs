using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.ViewModels.Workflow.Helper;

public class TreeHelper : WorkflowHelper.ViewModel.Tree
{
    private TreeViewModel? _viewModel;
    public readonly SpatialHashMap _spatialHashMap = new(cellSize: 250);

    public override void Install(IWorkflowTreeViewModel tree)
    {
        base.Install(tree);
        _viewModel = tree as TreeViewModel;

        // 初始化时重建索引（如果已有节点）
        if (_viewModel?.Nodes != null)
        {
            foreach (var node in _viewModel.Nodes)
            {
                _spatialHashMap.Insert(node);
            }
        }
    }

    protected override void OnNodeAdded(IWorkflowNodeViewModel node)
    {
        base.OnNodeAdded(node);
        _spatialHashMap.Insert(node);

        // 如果当前视口可见，可选：触发一次可见性刷新
        // _viewModel?.UpdateVisibleNodes();
    }

    protected override void OnNodeRemoved(IWorkflowNodeViewModel node)
    {
        base.OnNodeRemoved(node);
        _spatialHashMap.Remove(node);
    }

    // 新增：供外部（如 ScrollViewer）调用的视口更新入口
    public void UpdateVisibleNodes(double viewportLeft, double viewportTop, double viewportWidth, double viewportHeight)
    {
        if (_viewModel == null) return;

        var visible = _spatialHashMap.Query(viewportLeft, viewportTop, viewportWidth, viewportHeight);
        _viewModel.VisibleNodes.Clear();
        foreach (var node in visible)
        {
            _viewModel.VisibleNodes.Add(node);
        }
    }
}