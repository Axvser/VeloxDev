using System.ComponentModel;
using VeloxDev.AI;
using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

[AgentContext(AgentLanguages.Chinese, "工作流组件接口，任何工作流组件都实现该接口")]
[AgentContext(AgentLanguages.English, "Workflow ViewModel Base interface")]
public interface IWorkflowViewModel : INotifyPropertyChanging, INotifyPropertyChanged
{
    [AgentContext(AgentLanguages.Chinese, "初始化工作流组件，无参数")]
    [AgentContext(AgentLanguages.English, "Initializes the workflow component, no parameters")]
    public void InitializeWorkflow();

    [AgentContext(AgentLanguages.Chinese, "属性即将更改时通知UI更新，参数为属性名称")]
    [AgentContext(AgentLanguages.English, "Triggered when a property is about to change, parameter is the property name")]
    public void OnPropertyChanging(string propertyName);

    [AgentContext(AgentLanguages.Chinese, "属性已更改时通知UI更新，参数为属性名称")]
    [AgentContext(AgentLanguages.English, "Triggered when a property has changed, parameter is the property name")]
    public void OnPropertyChanged(string propertyName);

    [AgentContext(AgentLanguages.Chinese, "终结所有执行中的任务，参数为Null")]
    [AgentContext(AgentLanguages.English, "Terminates all ongoing tasks, parameter is Null")]
    [AgentCommandParameter]
    public IVeloxCommand CloseCommand { get; }
}
