using Jalium.UI.Controls;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>
/// 兜底节点卡：节点类型自带视图时不会用到。
/// <para>
/// 故意留空：<c>IWorkflowNodeViewModel</c> 只有几何与插槽、没有显示名 —— 标题是具体 ViewModel 的属性，
/// 所以这张卡没法诚实地说出它不认识的那个节点叫什么。这个 demo 里每种节点都有自己的视图
/// （ControllerView / TimerNodeView / PythonNodeView / EnumSelectorNodeView），它只会画到外来类型上。
/// </para>
/// <para>
/// 共用的仍是同一套外壳：卡面、发丝边、类型色条都来自 <see cref="CardPalette"/>，外来节点因此落在与已知
/// 节点同一族的观感上，而这张卡不用假装知道任何多余的事。色条取中性灰蓝 —— 这里没有类型可给它上色。
/// </para>
/// </summary>
internal sealed class NodeView : NodeViewBase
{
    protected override Color Accent => CardPalette.AccentFallback;

    protected override bool IsBareCard => true;

    protected override void Build(IWorkflowNodeViewModel node, Grid content)
    {
        // 兜底卡没有主体：只有卡面与那条贯穿全高的色条
    }

    protected override void OnNodePropertyChanged(string propertyName)
    {
        // 没有别的可读
    }
}
