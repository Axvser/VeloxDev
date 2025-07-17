using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using VeloxDev.Core.Interfaces.WorkflowSystem.Helper;
using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.Core.Interfaces.WorkflowSystem.ViewModel
{
    public interface IContextTree : INotifyPropertyChanging, INotifyPropertyChanged
    {
        public ObservableCollection<IContext> Children { get; set; }
        public ObservableCollection<IContextConnector> Connectors { get; set; }
        public ConcurrentDictionary<IContext, HashSet<IContext>> SourceLinks { get; set; }
        public ConcurrentDictionary<IContext, HashSet<IContext>> ProcessorLinks { get; set; }

        public bool CreateNode<TContext>(params object?[] args) where TContext : IContext, new();
        public bool RemoveNode(IContext context);

        public void BroadcastTask(IContext context, params object?[] args);
        public bool TryGetTaskFuse(Type exceptionType, out ITaskFuse? fuse);
        public bool RegisterTaskFuse(Type exceptionType, ITaskFuse fuse);
        public bool UnregisterTaskFuse(Type exceptionType, out ITaskFuse? fuse);

        public void BuildConnection(IContext sender, IContext processor);
        public void RemoveConnection(IContext sender, IContext processor);

        public void UpdateAnchor(IContext sender, Anchor anchor);
        public void UpdateConnectors(IContext sender);
    }
}
