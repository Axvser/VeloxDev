using System.ComponentModel;

namespace VeloxDev.Core.Interfaces.WorkflowSystem
{
    public interface IContextConnector : INotifyPropertyChanging, INotifyPropertyChanged, IContextState
    {
        public IContext? Start { get; set; }
        public IContext? End { get; set; }
    }
}
