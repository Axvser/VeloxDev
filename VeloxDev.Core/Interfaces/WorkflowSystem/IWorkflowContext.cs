using System.ComponentModel;

namespace VeloxDev.Core.Interfaces.WorkflowSystem
{
    public interface IWorkflowContext : INotifyPropertyChanging, INotifyPropertyChanged
    {
        public bool IsEnabled { get; set; }
        public string UID { get; set; }
        public string Name { get; set; }
    }
}
