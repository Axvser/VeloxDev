using System.Collections.ObjectModel;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.WPF.WorkflowSystem.ViewModels
{
    [Workflow.Context]
    public partial class ShowerNodeViewModel : IContext
    {
        [VeloxProperty]
        private bool isEnabled = true;
        [VeloxProperty]
        private Anchor anchor = Anchor.Default;
        [VeloxProperty]
        private IContextTree? tree = null;
        [VeloxProperty]
        private ObservableCollection<IContext> targets = [];

        [VeloxCommand]
        public Task Move(object? parameter, CancellationToken ct)
        {
            if (parameter is Anchor anchor)
            {
                Anchor = anchor;
            }
            OnMove(parameter, ct);
            return Task.CompletedTask;
        }
        partial void OnMove(object? parameter, CancellationToken ct);

        [VeloxCommand]
        public Task Delete(object? parameter, CancellationToken ct)
        {
            Tree?.Children.Remove(this);
            OnDelete(parameter, ct);
            return Task.CompletedTask;
        }
        partial void OnDelete(object? parameter, CancellationToken ct);

        [VeloxCommand]
        public Task Connect(object? parameter, CancellationToken ct)
        {
            if (tree != null)
            {
                if (tree.VirtualConnector.Start is null)
                {
                    tree.VirtualConnector.Start = this;
                }
                else
                {
                    if (tree.VirtualConnector.Start == this)
                    {
                        tree.VirtualConnector.Start = null;
                    }
                    else
                    {
                        tree.Connectors.Add(new ConnectorContext()
                        {
                            Start = tree.VirtualConnector.Start,
                            End = this
                        });
                        tree.VirtualConnector.Start = null;
                    }
                }
            }
            return Task.CompletedTask;
        }
    }
}
