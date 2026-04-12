using VeloxDev.AI;

namespace VeloxDev.WorkflowSystem;

[AgentContext(AgentLanguages.Chinese,"定义工作流广播模式，不支持组合运算。")]
[AgentContext(AgentLanguages.English,"Defines workflow broadcast modes, does not support combination operations.")]
public enum BroadcastMode : int
{
    [AgentContext(AgentLanguages.Chinese,"并行广播：同时向所有目标发送消息，不保证顺序。")]
    [AgentContext(AgentLanguages.English,"Parallel broadcast: Sends messages to all targets simultaneously, without guaranteeing order.")]
    Parallel = 0,
    [AgentContext(AgentLanguages.Chinese,"广度优先广播：按照连接的层级关系依次向目标发送消息，先发送给直接连接的目标，再发送给间接连接的目标。")]
    [AgentContext(AgentLanguages.English,"Breadth-first broadcast: Sends messages to targets in a hierarchical order, first to directly connected targets, then to indirectly connected targets.")]
    BreadthFirst = 1,
    [AgentContext(AgentLanguages.Chinese,"深度优先广播：按照连接的层级关系依次向目标发送消息，先发送给直接连接的目标，再继续沿着该目标的连接向下发送。")]
    [AgentContext(AgentLanguages.English,"Depth-first broadcast: Sends messages to targets in a hierarchical order, first to directly connected targets, then continues down the connections of those targets.")]
    DepthFirst = 2,
}
