﻿using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using VeloxDev.Core.Interfaces.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.Core.Interfaces.WorkflowSystem
{
    public interface IWorkflowTreeViewModel : IWorkflowViewModel
    {
        public IWorkflowLinkViewModel VirtualLink { get; set; }
        public ObservableCollection<IWorkflowNodeViewModel> Nodes { get; set; }
        public ConcurrentDictionary<IWorkflowSlotViewModel, ConcurrentDictionary<IWorkflowSlotViewModel, IWorkflowLinkGroupViewModel>> LinkGroupMap { get; set; }
        public ObservableCollection<IWorkflowLinkGroupViewModel> LinkGroups { get; set; }

        public IVeloxCommand CreateNodeCommand { get; }        // 创建节点           | parameter IWorkflowNodeViewModel
        public IVeloxCommand MovePointerCommand { get; }       // 触点跟踪           | parameter Anchor
        public IVeloxCommand ResetVirtualLinkCommand { get; }  // 重置虚拟连接       | parameter Null
        public IVeloxCommand ApplyConnectionCommand { get; }   // 处理连接构建发起方 | parameter Null
        public IVeloxCommand ReceiveConnectionCommand { get; } // 处理连接构建接收方 | parameter Null

        public IVeloxCommand SubmitCommand { get; }        // 提交 | parameter IWorkflowActionPair
        public IVeloxCommand RedoCommand { get; }          // 重做 | parameter Null
        public IVeloxCommand UndoCommand { get; }          // 撤销 | parameter Null

        public IWorkflowTreeViewModelHelper GetHelper();
        public void SetHelper(IWorkflowTreeViewModelHelper helper);
    }

    public interface IWorkflowTreeViewModelHelper : IWorkflowHelper
    {
        public void Initialize(IWorkflowTreeViewModel tree);

        public void CreateNode(IWorkflowNodeViewModel node);

        public void MovePointer(Anchor anchor);
        public void ApplyConnection(IWorkflowSlotViewModel slot);
        public void ReceiveConnection(IWorkflowSlotViewModel slot);
        public void ResetVirtualLink();

        public void Submit(IWorkflowActionPair actionPair);
        public void Redo();
        public void Undo();
        public void ClearHistory();
    }
}
