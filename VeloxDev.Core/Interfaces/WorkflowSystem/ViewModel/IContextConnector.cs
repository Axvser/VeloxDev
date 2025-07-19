using System.ComponentModel;
using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.Core.Interfaces.WorkflowSystem.ViewModel
{
    public interface IContextConnector : INotifyPropertyChanging, INotifyPropertyChanged
    {
        public bool IsEnabled { get; set; }
        public Anchor Start { get; set; }
        public Anchor End { get; set; }
    }
}
