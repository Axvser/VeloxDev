using VeloxDev.Core.Interfaces.WorkflowSystem;

#pragma warning disable

namespace VeloxDev.Core.WorkflowSystem
{
    public static class WorkflowHelper
    {
        public static class ViewModel
        {
            #region Link Helper
            public class Link : IWorkflowLinkViewModelHelper
            {

            }
            #endregion

            #region LinkGroup Helper
            public class LinkGroup : IWorkflowLinkGroupViewModelHelper
            {

            }
            #endregion

            #region Slot Helper
            public class Slot : IWorkflowSlotViewModelHelper
            {

            }
            #endregion

            #region Node Helper
            public class Node : IWorkflowNodeViewModelHelper
            {

            }
            #endregion

            #region Tree Helper
            public class Tree : IWorkflowTreeViewModelHelper
            {

            }
            #endregion
        }
    }
}