using System.Collections.ObjectModel;
using System.ComponentModel;
using VeloxDev.Core.Interfaces.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.Core.Interfaces.WorkflowSystem.ViewModel
{
    public interface IContextTree : INotifyPropertyChanging, INotifyPropertyChanged
    {
        public IContextConnector VirtualConnection { get; set; }
        public ObservableCollection<IContext> Children { get; set; }
        public ObservableCollection<IContextConnector> Connectors { get; set; }
        
        public IVeloxCommand BroadcastCommand { get; }
        public IVeloxCommand CreateNodeCommand { get; }
        public IVeloxCommand DeleteNodeCommand { get; }
        public IVeloxCommand SetVirtualStartCommand { get; }
        public IVeloxCommand SetVirtualEndCommand { get; }
        public IVeloxCommand ClearVirtualConnectionCommand { get; }
    }
}
