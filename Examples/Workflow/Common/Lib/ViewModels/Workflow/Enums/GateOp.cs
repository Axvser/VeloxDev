using VeloxDev.AI;

namespace Demo.ViewModels;

[AgentContext(AgentLanguages.Chinese, "逻辑运算门运算类型：Identity 直接按输入真值路由；Not 取反后再路由")]
[AgentContext(AgentLanguages.English, "Logic gate operation: Identity routes by the input's truthiness; Not inverts it before routing.")]
public enum GateOp
{
    Identity,

    Not,
}
