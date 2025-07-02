using VeloxDev.Core.Generators;

namespace VeloxDev.Core.WorkflowSystem;

// 假设可能存在 CalculateNode 🔗 ValidateNode 之间的连接

[WorkflowBuilder.Context]
public partial class CalculateNode();
[WorkflowBuilder.Context]
public partial class ValidateNode();

[WorkflowBuilder.View(typeof(CalculateNode))]
public partial class CalculateGrid();
[WorkflowBuilder.View(typeof(ValidateNode))]
public partial class ValidateGrid();

//-------------------------------------------------------

// 源生成器动态生成下述内容
public partial class CalculateNode
{

}
public partial class ValidateNode
{
    
}
public partial class CalculateGrid
{

}
public partial class ValidateGrid
{

}
