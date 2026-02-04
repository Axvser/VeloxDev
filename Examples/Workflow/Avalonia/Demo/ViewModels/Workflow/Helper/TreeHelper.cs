using System.Collections.Generic;
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
        _viewModel?.VisibleItems.Add(tree.VirtualLink);

        // 初始化时重建空间索引
        if (_viewModel?.Nodes != null)
        {
            foreach (var node in _viewModel.Nodes)
            {
                _spatialHashMap.Insert(node);
            }
        }
    }

    public override void Uninstall(IWorkflowTreeViewModel tree)
    {
        base.Uninstall(tree);
        _viewModel?.VisibleItems.Remove(tree.VirtualLink);
        _viewModel = null;

        // 卸载时清理空间索引
        _spatialHashMap.Clear();
    }

    protected override void OnNodeAdded(IWorkflowNodeViewModel node)
    {
        base.OnNodeAdded(node);
        _spatialHashMap.Insert(node);
    }

    protected override void OnNodeRemoved(IWorkflowNodeViewModel node)
    {
        base.OnNodeRemoved(node);
        _spatialHashMap.Remove(node);
    }

    protected override void OnLinkAdded(IWorkflowLinkViewModel link)
    {
        base.OnLinkAdded(link);
        _viewModel?.VisibleItems.Add(link);
    }

    protected override void OnLinkRemoved(IWorkflowLinkViewModel link)
    {
        base.OnLinkRemoved(link);
        _viewModel?.VisibleItems.Remove(link);
    }

    /// <summary>
    /// 框选节点
    /// </summary>
    /// <param name="viewport">可见区域</param>
    /// <returns></returns>
    public IEnumerable<IWorkflowNodeViewModel> Select(Viewport viewport)
        => _spatialHashMap.Query(viewport);

    /// <summary>
    /// 虚拟化
    /// </summary>
    /// <param name="viewport">可见区域</param>
    public void Virtualize(Viewport viewport)
    {
        if (_viewModel?.VisibleItems is not ObservableCollection<IWorkflowViewModel> visibleItems)
            return;

        // 网格哈希查询
        var newVisibleNotes = _spatialHashMap.Query(viewport).ToHashSet();
        // 原始集合拷贝
        var oldVisibleItems = visibleItems.ToHashSet();

        // 待移除节点
        var toRemove = oldVisibleItems.OfType<IWorkflowNodeViewModel>().Where(item => !newVisibleNotes.Contains(item)).ToList();
        // 待添加节点
        var toAdd = newVisibleNotes.Where(item => !oldVisibleItems.Contains(item)).ToList();

        // 先移除
        foreach (IWorkflowNodeViewModel node in toRemove)
        {
            visibleItems.Remove(node);
            foreach (var slot in node.Slots)
            {
                foreach (var target in slot.Targets)
                {
                    if (target.Parent is not null &&
                        slot.Parent is not null &&
                        !newVisibleNotes.Contains(slot.Parent) &&
                        !newVisibleNotes.Contains(target.Parent) &&
                        _viewModel.LinksMap.TryGetValue(target, out var targets) &&
                        targets.TryGetValue(slot, out var link))
                    {
                        visibleItems.Remove(link);
                    }
                }
                foreach (var source in slot.Sources)
                {
                    if (source.Parent is not null &&
                        slot.Parent is not null &&
                        !newVisibleNotes.Contains(source.Parent) &&
                        !newVisibleNotes.Contains(slot.Parent) &&
                        _viewModel.LinksMap.TryGetValue(source, out var targets) &&
                        targets.TryGetValue(slot, out var link))
                    {
                        visibleItems.Remove(link);
                    }
                }
            }
        }

        // 再添加
        foreach (IWorkflowNodeViewModel node in toAdd)
        {
            visibleItems.Add(node);
            foreach (var slot in node.Slots)
            {
                foreach (var target in slot.Targets)
                {
                    if (target.Parent is not null &&
                        slot.Parent is not null &&
                        (newVisibleNotes.Contains(target.Parent) || newVisibleNotes.Contains(slot.Parent)) &&
                        _viewModel.LinksMap.TryGetValue(slot, out var targets) &&
                        targets.TryGetValue(target, out var link))
                    {
                        visibleItems.Add(link);
                    }
                }
                foreach (var source in slot.Sources)
                {
                    if (source.Parent is not null &&
                        slot.Parent is not null &&
                        (newVisibleNotes.Contains(slot.Parent) || newVisibleNotes.Contains(source.Parent)) &&
                        _viewModel.LinksMap.TryGetValue(source, out var targets) &&
                        targets.TryGetValue(slot, out var link))
                    {
                        visibleItems.Add(link);
                    }
                }
            }
        }
    }
}