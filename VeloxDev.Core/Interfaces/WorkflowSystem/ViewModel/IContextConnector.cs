using System.ComponentModel;
using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.Core.Interfaces.WorkflowSystem.ViewModel
{
    public interface IContextConnector : INotifyPropertyChanging, INotifyPropertyChanged
    {
        public Anchor StartAnchor { get; set; }
        public Anchor EndAnchor { get; set; }
    }
}
