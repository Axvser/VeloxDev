using VeloxDev.AI;

namespace VeloxDev.WorkflowSystem;

[AgentContext(AgentLanguages.Chinese, "工作流组件运行时唯一标识接口，提供在整个程序生命周期内唯一的ID")]
[AgentContext(AgentLanguages.English, "Workflow component runtime identity interface, provides a unique ID throughout the program lifetime")]
public interface IWorkflowIdentifiable
{
    [AgentContext(AgentLanguages.Chinese, "运行时唯一ID，在组件创建时分配，不随集合变化")]
    [AgentContext(AgentLanguages.English, "Runtime unique ID assigned at creation, stable across collection mutations")]
    string RuntimeId { get; }
}
