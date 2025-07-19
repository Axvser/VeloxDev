using System.Collections.ObjectModel;
using VeloxDev.Core.Interfaces.WorkflowSystem.ViewModel;
using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;
using VeloxDev.WPF.WorkflowSystem.Views;

namespace VeloxDev.WPF.WorkflowSystem.ViewModels
{
    [Workflow.ContextTree]
    public partial class FactoryViewModel : IContextTree
    {
        [VeloxProperty]
        private ObservableCollection<IContext> children = [];
        [VeloxProperty]
        private ObservableCollection<IContextConnector> connectors = [];

        private static readonly Dictionary<Type, Type> viewMappings = [];
        static FactoryViewModel()
        {
            viewMappings.Add(typeof(FactoryViewModel), typeof(Factory));
            viewMappings.Add(typeof(ShowerNodeViewModel), typeof(Shower));
        }
    }
}
