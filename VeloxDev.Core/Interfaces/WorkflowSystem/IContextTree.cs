using System.Collections.ObjectModel;
using System.ComponentModel;
using VeloxDev.Core.Interfaces.MVVM;

namespace VeloxDev.Core.Interfaces.WorkflowSystem
{
    public interface IContextTree : INotifyPropertyChanging, INotifyPropertyChanged, IContextState
    {
        public IContextConnector VirtualConnector { get; set; }
        public ObservableCollection<IContext> Children { get; set; }
        public ObservableCollection<IContextConnector> Connectors { get; set; }

        public IVeloxCommand CreateNodeCommand { get; }
    }
}
