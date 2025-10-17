using VeloxDev.Core.Interfaces.WorkflowSystem;

#pragma warning disable

namespace VeloxDev.Core.WorkflowSystem
{
    public static class WorkflowHelper
    {
        public static class ViewModel
        {
            public static bool TryFindTree(object viewModel, out IWorkflowTreeViewModel? value)
            {
                switch (viewModel)
                {
                    case IWorkflowLinkViewModel link:
                        if (link.Sender is not null)
                        {
                            value = link.Sender.Parent.Parent;
                            return value is null;
                        }
                        else if (link.Receiver is not null)
                        {
                            value = link.Receiver.Parent.Parent;
                            return value is null;
                        }
                        else
                        {
                            value = null;
                            return false;
                        }
                    case IWorkflowLinkGroupViewModel linkGroup:
                        {
                            value = linkGroup.Parent;
                            return value is null;
                        }
                    case IWorkflowSlotViewModel slot:
                        {
                            value = slot.Parent.Parent;
                            return value is null;
                        }
                    case IWorkflowNodeViewModel node:
                        {
                            value = node.Parent;
                            return value is null;
                        }
                    case IWorkflowTreeViewModel tree:
                        {
                            value = tree;
                            return value is null;
                        }
                    default:
                        value = null;
                        return false;
                }
            }
            public static bool TryFindTree(IWorkflowLinkViewModel link, out IWorkflowTreeViewModel? value)
            {
                if (link.Sender is not null)
                {
                    value = link.Sender.Parent.Parent;
                    return value is null;
                }
                else if (link.Receiver is not null)
                {
                    value = link.Receiver.Parent.Parent;
                    return value is null;
                }
                else
                {
                    value = null;
                    return false;
                }
            }
            public static bool TryFindTree(IWorkflowLinkGroupViewModel linkGroup, out IWorkflowTreeViewModel? value)
            {
                value = linkGroup.Parent;
                return value is null;
            }
            public static bool TryFindTree(IWorkflowSlotViewModel slot, out IWorkflowTreeViewModel? value)
            {
                value = slot.Parent.Parent;
                return value is null;
            }
            public static bool TryFindTree(IWorkflowNodeViewModel node, out IWorkflowTreeViewModel? value)
            {
                value = node.Parent;
                return value is null;
            }
            public static bool TryFindTree(IWorkflowTreeViewModel tree, out IWorkflowTreeViewModel? value)
            {
                value = tree;
                return true;
            }

            #region Link Helper
            public class Link : IWorkflowLinkViewModelHelper
            {
                private IWorkflowLinkViewModel? viewModel = null;

                public virtual Task CloseAsync()
                {
                    throw new NotImplementedException();
                }

                public virtual void Delete()
                {
                    throw new NotImplementedException();
                }

                public virtual void Dispose()
                {
                    GC.SuppressFinalize(this);
                }

                public virtual void Initialize(IWorkflowLinkViewModel link)
                {
                    viewModel = link;
                }
            }
            #endregion

            #region LinkGroup Helper
            public class LinkGroup : IWorkflowLinkGroupViewModelHelper
            {
                private IWorkflowLinkGroupViewModel? viewModel = null;

                public virtual Task CloseAsync()
                {
                    throw new NotImplementedException();
                }

                public virtual void Delete()
                {
                    throw new NotImplementedException();
                }

                public virtual void Dispose()
                {
                    GC.SuppressFinalize(this);
                }

                public virtual void Initialize(IWorkflowLinkGroupViewModel linkGroup)
                {
                    viewModel = linkGroup;
                }
            }
            #endregion

            #region Slot Helper
            public class Slot : IWorkflowSlotViewModelHelper
            {
                private IWorkflowSlotViewModel? viewModel = null;

                public virtual void ApplyConnection()
                {
                    throw new NotImplementedException();
                }

                public virtual Task CloseAsync()
                {
                    throw new NotImplementedException();
                }

                public virtual void Delete()
                {
                    throw new NotImplementedException();
                }

                public virtual void Dispose()
                {
                    GC.SuppressFinalize(this);
                }

                public virtual void Initialize(IWorkflowSlotViewModel slot)
                {
                    viewModel = slot;
                }

                public virtual void OnAnchorChanged(Anchor oldValue, Anchor newValue)
                {
                    throw new NotImplementedException();
                }

                public virtual void OnChannelChanged(SlotChannel oldValue, SlotChannel newValue)
                {
                    throw new NotImplementedException();
                }

                public virtual void OnOffsetChanged(Anchor oldValue, Anchor newValue)
                {
                    throw new NotImplementedException();
                }

                public virtual void OnParentChanged(IWorkflowNodeViewModel? oldValue, IWorkflowNodeViewModel? newValue)
                {
                    throw new NotImplementedException();
                }

                public virtual void OnSizeChanged(Size oldValue, Size newValue)
                {
                    throw new NotImplementedException();
                }

                public virtual void OnStateChanged(SlotState oldValue, SlotState newValue)
                {
                    throw new NotImplementedException();
                }

                public virtual void Press(Anchor anchor)
                {
                    throw new NotImplementedException();
                }

                public virtual void ReceiveConnection()
                {
                    throw new NotImplementedException();
                }

                public virtual void Release(Anchor anchor)
                {
                    throw new NotImplementedException();
                }

                public virtual void Scale(Size size)
                {
                    throw new NotImplementedException();
                }

                public virtual void Translate(Offset offset)
                {
                    throw new NotImplementedException();
                }
            }
            #endregion

            #region Node Helper
            public class Node : IWorkflowNodeViewModelHelper
            {
                private IWorkflowNodeViewModel? viewModel = null;

                public virtual Task BroadcastAsync(object? parameter)
                {
                    throw new NotImplementedException();
                }

                public virtual Task CloseAsync()
                {
                    throw new NotImplementedException();
                }

                public virtual void CreateSlot(IWorkflowSlotViewModel viewModel)
                {
                    throw new NotImplementedException();
                }

                public virtual void Delete()
                {
                    throw new NotImplementedException();
                }

                public virtual void Dispose()
                {
                    GC.SuppressFinalize(this);
                }

                public virtual void Initialize(IWorkflowNodeViewModel node)
                {
                    viewModel = node;
                }

                public virtual void Move(Anchor newValue)
                {
                    throw new NotImplementedException();
                }

                public virtual void OnAnchorChanged(Anchor oldValue, Anchor newValue)
                {
                    throw new NotImplementedException();
                }

                public virtual void OnSizeChanged(Size oldValue, Size newValue)
                {
                    throw new NotImplementedException();
                }

                public virtual void Press(Anchor newValue)
                {
                    throw new NotImplementedException();
                }

                public virtual void Release(Anchor newValue)
                {
                    throw new NotImplementedException();
                }

                public virtual void Scale(Size newValue)
                {
                    throw new NotImplementedException();
                }

                public virtual Task WorkAsync(object? parameter)
                {
                    throw new NotImplementedException();
                }
            }
            #endregion

            #region Tree Helper
            public class Tree : IWorkflowTreeViewModelHelper
            {
                private IWorkflowTreeViewModel? viewModel = null;

                public virtual void ApplyConnection(IWorkflowSlotViewModel slot)
                {
                    throw new NotImplementedException();
                }

                public virtual Task CloseAsync()
                {
                    throw new NotImplementedException();
                }

                public virtual void CreateNode(IWorkflowNodeViewModel node)
                {
                    throw new NotImplementedException();
                }

                public virtual void Dispose()
                {
                    GC.SuppressFinalize(this);
                }

                public virtual void Initialize(IWorkflowTreeViewModel tree)
                {
                    viewModel = tree;
                }

                public virtual void MovePointer(Anchor anchor)
                {
                    throw new NotImplementedException();
                }

                public virtual void ReceiveConnection(IWorkflowSlotViewModel slot)
                {
                    throw new NotImplementedException();
                }

                public virtual void Redo()
                {
                    throw new NotImplementedException();
                }

                public virtual void Submit(IWorkflowActionPair actionPair)
                {
                    throw new NotImplementedException();
                }

                public virtual void Undo()
                {
                    throw new NotImplementedException();
                }
            }
            #endregion
        }
    }
}