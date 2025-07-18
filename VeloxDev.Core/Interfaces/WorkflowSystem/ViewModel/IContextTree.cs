using System.Collections.ObjectModel;
using System.ComponentModel;

namespace VeloxDev.Core.Interfaces.WorkflowSystem.ViewModel
{
    public interface IContextTree : INotifyPropertyChanging, INotifyPropertyChanged
    {
        public ObservableCollection<IContext> Children { get; set; }
        public ObservableCollection<IContextConnector> Connectors { get; set; }
    }
}
