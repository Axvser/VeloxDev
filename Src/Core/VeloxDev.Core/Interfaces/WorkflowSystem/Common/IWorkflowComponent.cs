using System.ComponentModel;
using VeloxDev.AI;
using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

/// <summary>The base contract every workflow component implements.</summary>
public interface IWorkflowComponent : INotifyPropertyChanging, INotifyPropertyChanged
{
    /// <summary>Initializes the workflow component.</summary>
    public void InitializeWorkflow();

    /// <summary>Raises a change notification before <paramref name="propertyName"/> changes.</summary>
    public void OnPropertyChanging(string propertyName);

    /// <summary>Raises a change notification after <paramref name="propertyName"/> has changed.</summary>
    public void OnPropertyChanged(string propertyName);

    /// <summary>Terminates all running tasks.</summary>
    [AgentCommandParameter]
    public IVeloxCommand CloseCommand { get; }
}
