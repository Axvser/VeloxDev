using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.WinForms.Headless.Tests.Infrastructure;

// WinForms 适配器的表面是一个可继承的 WorkflowTreeView；宿主只提供两个视图工厂。
// 这里用最轻的 Panel 顶替卡片/连线视图 —— 本套件验的是表面把模型推进视图池、把视口写回模型，
// 不是卡片画成什么样。
internal sealed class TestWorkflowTreeView : WorkflowTreeView
{
    protected override Control CreateNodeView(IWorkflowNodeViewModel node)
        => new Panel { Width = 160, Height = 90, BackColor = Color.Transparent };

    protected override Control CreateLinkView(IWorkflowLinkViewModel link)
        => new Panel { Width = 12, Height = 12, BackColor = Color.Transparent };
}
