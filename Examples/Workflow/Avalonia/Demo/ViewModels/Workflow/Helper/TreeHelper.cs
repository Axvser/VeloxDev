using System.Collections.ObjectModel;
using System.Linq;
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

        // 初始化时重建空间索引
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
    public void UpdateVisibleNodes(Viewport viewport)
    {
        if (_viewModel?.VisibleItems is not ObservableCollection<IWorkflowViewModel> visibleItems)
            return;

        var newVisibleSet = _spatialHashMap.Query(viewport).ToHashSet();
        var oldVisibleList = visibleItems.ToList(); // 快照（保持顺序和引用）
        var oldVisibleSet = oldVisibleList.ToHashSet();

        // 计算差集
        var toRemove = oldVisibleList.Where(item => !newVisibleSet.Contains(item)).ToList();
        var toAdd = newVisibleSet.Where(item => !oldVisibleSet.Contains(item)).ToList();

        // 先移除（从后往前，避免索引偏移问题）
        foreach (var item in toRemove.AsEnumerable().Reverse())
        {
            visibleItems.Remove(item);
        }

        // 再添加
        foreach (var item in toAdd)
        {
            visibleItems.Add(item);
        }
    }
}