using System.ComponentModel;
using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.Core.Interfaces.WorkflowSystem.ViewModel
{
    public interface IContext : INotifyPropertyChanging, INotifyPropertyChanged
    {
        public bool IsEnabled { get; set; }
        public Anchor Anchor { get; set; }
        public IContextTree? Tree { get; set; }
        public void BroadcastTask(params object?[] args);
        public void ExecuteTask(IContext sender, params object?[] args);
    }
}
