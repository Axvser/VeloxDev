using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using VeloxDev.Core.Interfaces.WorkflowSystem.Helper;
using VeloxDev.Core.Interfaces.WorkflowSystem.ViewModel;
using VeloxDev.Core.WorkflowSystem;
using VeloxDev.WPF.WorkflowSystem.Views;

namespace VeloxDev.WPF.WorkflowSystem.ViewModels
{
    [Workflow.ContextTree]
    [Workflow.ViewMapping<FactoryViewModel>(typeof(Factory))]
    [Workflow.ViewMapping<ShowerNodeViewModel>(typeof(Shower))]
    public partial class FactoryViewModel : IContextTree
    {
        /* 阶段 1: 源生成上下文到视图的映射关系
         * (1) 使用 Dictionary<Type, Type> 来存储上下文类型到视图类型的映射
         * (2) 在静态构造函数中初始化映射关系
         */
        private static readonly Dictionary<Type, Type> viewMappings = [];
        static FactoryViewModel()
        {
            viewMappings.Add(typeof(FactoryViewModel), typeof(Factory));
            viewMappings.Add(typeof(ShowerNodeViewModel), typeof(Shower));
        }

        /* 阶段 2: 源生成 INotifyPropertyChanging 和 INotifyPropertyChanged 接口
         */
        public event PropertyChangingEventHandler? PropertyChanging;
        public event PropertyChangedEventHandler? PropertyChanged;
        public void OnPropertyChanging(string propertyName)
        {
            PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(propertyName));
        }
        public void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /* 阶段 3: 源生成 Workflow 核心数据块
         * (1) Chidren 用于存储工作节点
         * (2) Connectors 用于呈现工作节点之间的连接 ( 例如，使用装饰器来基于Connector上下文渲染连线 )
         */
        private ObservableCollection<IContext> children = [];
        private ObservableCollection<IContextConnector> connectors = [];
        public ObservableCollection<IContext> Children
        {
            get => children;
            set
            {
                if (Equals(children, value)) return;
                OnPropertyChanging(nameof(Children));
                children = value;
                OnPropertyChanged(nameof(Children));
            }
        }
        public ObservableCollection<IContextConnector> Connectors
        {
            get => connectors;
            set
            {
                if (Equals(connectors, value)) return;
                OnPropertyChanging(nameof(Connectors));
                connectors = value;
                OnPropertyChanged(nameof(Connectors));
            }
        }
        public ConcurrentDictionary<IContext, HashSet<IContext>> SourceLinks { get; set; } = [];
        public ConcurrentDictionary<IContext, HashSet<IContext>> ProcessorLinks { get; set; } = [];

        /* 阶段 4: 源生成开放API对Tree进行操作
         * (2) 在 CreateNode<TContext>() 方法中使用映射关系创建节点
         */
        public void BroadcastTask(IContext sender, params object?[] args)
        {
            if (ProcessorLinks.TryGetValue(sender, out var processors))
            {
                foreach (var processor in processors)
                {
                    processor.ExecuteTask(sender, args);
                }
            }
        }
        public void BuildConnection(IContext sender, IContext processor)
        {
            if (SourceLinks.TryGetValue(sender, out var sources) && !sources.Contains(processor))
            {
                sources.Add(processor);
            }
            else
            {
                SourceLinks[sender] = [processor];
            }
            OnConnectionBuilded(sender, processor);
        }
        public void RemoveConnection(IContext sender, IContext processor)
        {
            OnConnectionRemoved(sender, processor);
        }
        public bool CreateNode<TContext>(params object?[] args) where TContext : IContext, new()
        {
            throw new NotImplementedException();
        }
        public bool RemoveNode(IContext context)
        {
            throw new NotImplementedException();
        }
        public bool TryGetTaskFuse(Type exceptionType, out ITaskFuse? fuse)
        {
            throw new NotImplementedException();
        }
        public bool RegisterTaskFuse(Type exceptionType, ITaskFuse fuse)
        {
            throw new NotImplementedException();
        }
        public bool UnregisterTaskFuse(Type exceptionType, out ITaskFuse? fuse)
        {
            throw new NotImplementedException();
        }
        public void UpdateAnchor(IContext sender, Anchor anchor)
        {
            throw new NotImplementedException();
        }
        public void UpdateConnectors(IContext sender)
        {
            throw new NotImplementedException();
        }

        partial void OnConnectionBuilded(IContext sender, IContext processor);
        partial void OnConnectionRemoved(IContext sender, IContext processor);
    }
}
