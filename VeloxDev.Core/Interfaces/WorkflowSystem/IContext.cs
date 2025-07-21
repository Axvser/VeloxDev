using System.ComponentModel;
using VeloxDev.Core.Interfaces.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.Core.Interfaces.WorkflowSystem
{
    public interface IContext : INotifyPropertyChanging, INotifyPropertyChanged, IContextState
    {
        public Anchor Anchor { get; set; }
        public IContextTree? Tree { get; set; }

        public IVeloxCommand ConnectCommand { get; }
    }
}
