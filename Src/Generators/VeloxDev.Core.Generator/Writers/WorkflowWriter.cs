using Microsoft.CodeAnalysis;
using System;
using System.Linq;
using System.Text;

namespace VeloxDev.Core.Generator.Writers
{
    public class WorkflowWriter : WriterBase
    {
        public const string ObservableCollectionFullName = "global::System.Collections.ObjectModel.ObservableCollection";
        public const string DictionaryFullName = "global::System.Collections.Generic.Dictionary";
        public const string ObjectFullName = "global::System.Object";
        public const string TaskFullName = "global::System.Threading.Tasks.Task";
        public const string CancellationTokenFullName = "global::System.Threading.CancellationToken";

        public override bool CanWrite()
        {
            if (Symbol == null) return false;

            var attributes = Symbol.GetAttributes();
            return attributes.Any(attr => IsWorkflowViewModelAttribute(attr));
        }

        public override string GetFileName()
        {
            return Symbol?.Name + ".g.cs";
        }

        public override string[] GenerateBaseTypes()
        {
            return new string[] { };
        }

        public override string[] GenerateBaseInterfaces()
        {
            if (Symbol == null) return Array.Empty<string>();

            var attributes = Symbol.GetAttributes();
            var workflowAttribute = attributes.FirstOrDefault(attr => IsWorkflowViewModelAttribute(attr));

            if (workflowAttribute == null) return Array.Empty<string>();

            var model = CreateWorkflowAttributeModel(workflowAttribute, Symbol);
            if (model == null) return Array.Empty<string>();

            return new string[] { GetWorkflowInterfaceName(model.WorkflowType) };
        }

        private bool IsWorkflowViewModelAttribute(AttributeData attribute)
        {
            var attributeClass = attribute.AttributeClass;
            if (attributeClass == null) return false;

            var fullName = attributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return fullName.Contains("WorkflowBuilder.ViewModel.TreeAttribute") ||
                   fullName.Contains("WorkflowBuilder.ViewModel.NodeAttribute") ||
                   fullName.Contains("WorkflowBuilder.ViewModel.SlotAttribute") ||
                   fullName.Contains("WorkflowBuilder.ViewModel.LinkAttribute");
        }

        private WorkflowAttributeModel? CreateWorkflowAttributeModel(AttributeData attribute, INamedTypeSymbol targetClass)
        {
            var attributeClass = attribute.AttributeClass;
            if (attributeClass == null) return null;

            var fullName = attributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (fullName.Contains("WorkflowBuilder.ViewModel.TreeAttribute"))
            {
                return CreateTreeModel(attribute, targetClass);
            }
            else if (fullName.Contains("WorkflowBuilder.ViewModel.NodeAttribute"))
            {
                return CreateNodeModel(attribute, targetClass);
            }
            else if (fullName.Contains("WorkflowBuilder.ViewModel.SlotAttribute"))
            {
                return CreateSlotModel(attribute, targetClass);
            }
            else if (fullName.Contains("WorkflowBuilder.ViewModel.LinkAttribute"))
            {
                return CreateLinkModel(attribute, targetClass);
            }

            return null;
        }

        private TreeAttributeModel CreateTreeModel(AttributeData attribute, INamedTypeSymbol targetClass)
        {
            var model = new TreeAttributeModel
            {
                AttributeData = attribute,
                TargetClassSymbol = targetClass,
                AttributeSymbol = attribute.AttributeClass
            };

            // 解析泛型参数
            if (attribute.AttributeClass is INamedTypeSymbol namedType && namedType.TypeArguments.Length > 0)
            {
                model.HelperType = namedType.TypeArguments[0];
            }
            else
            {
                model.HelperType = GetDefaultHelperType("Tree");
            }

            // 改进的参数解析：正确处理默认值
            var virtualLinkType = GetConstructorArgumentAsType(attribute, "virtualLinkType", 0);
            model.VirtualLinkType = virtualLinkType ?? GetConcreteDefaultVirtualLinkType();

            var virtualSlotType = GetConstructorArgumentAsType(attribute, "virtualSlotType", 1);
            model.VirtualSlotType = virtualSlotType ?? GetConcreteDefaultVirtualSlotType();

            return model;
        }

        private NodeAttributeModel CreateNodeModel(AttributeData attribute, INamedTypeSymbol targetClass)
        {
            var model = new NodeAttributeModel
            {
                AttributeData = attribute,
                TargetClassSymbol = targetClass,
                AttributeSymbol = attribute.AttributeClass
            };

            if (attribute.AttributeClass is INamedTypeSymbol namedType && namedType.TypeArguments.Length > 0)
            {
                model.HelperType = namedType.TypeArguments[0];
            }
            else
            {
                model.HelperType = GetDefaultHelperType("Node");
            }

            // 改进的参数解析：提供默认值
            model.WorkSemaphore = GetConstructorArgumentAsInt(attribute, "workSemaphore", 0) ?? 1;

            return model;
        }

        private SlotAttributeModel CreateSlotModel(AttributeData attribute, INamedTypeSymbol targetClass)
        {
            var model = new SlotAttributeModel
            {
                AttributeData = attribute,
                TargetClassSymbol = targetClass,
                AttributeSymbol = attribute.AttributeClass
            };

            if (attribute.AttributeClass is INamedTypeSymbol namedType && namedType.TypeArguments.Length > 0)
            {
                model.HelperType = namedType.TypeArguments[0];
            }
            else
            {
                model.HelperType = GetDefaultHelperType("Slot");
            }

            return model;
        }

        private LinkAttributeModel CreateLinkModel(AttributeData attribute, INamedTypeSymbol targetClass)
        {
            var model = new LinkAttributeModel
            {
                AttributeData = attribute,
                TargetClassSymbol = targetClass,
                AttributeSymbol = attribute.AttributeClass
            };

            if (attribute.AttributeClass is INamedTypeSymbol namedType && namedType.TypeArguments.Length > 0)
            {
                model.HelperType = namedType.TypeArguments[0];
            }
            else
            {
                model.HelperType = GetDefaultHelperType("Link");
            }

            // 改进的参数解析
            var slotType = GetConstructorArgumentAsType(attribute, "slotType", 0);
            model.SlotType = slotType ?? GetConcreteDefaultSlotType();

            return model;
        }

        private INamedTypeSymbol? GetConstructorArgumentAsType(AttributeData attribute, string parameterName, int position)
        {
            // 优先按名称查找命名参数
            foreach (var namedArg in attribute.NamedArguments)
            {
                if (namedArg.Key == parameterName &&
                    namedArg.Value.Kind == TypedConstantKind.Type &&
                    namedArg.Value.Value is INamedTypeSymbol typeSymbol)
                {
                    return typeSymbol;
                }
            }

            // 按位置查找构造函数参数
            if (position < attribute.ConstructorArguments.Length)
            {
                var arg = attribute.ConstructorArguments[position];
                if (arg.Kind == TypedConstantKind.Type && arg.Value is INamedTypeSymbol typeSymbol)
                {
                    return typeSymbol;
                }
            }

            return null;
        }

        private int? GetConstructorArgumentAsInt(AttributeData attribute, string parameterName, int position)
        {
            // 优先按名称查找命名参数
            foreach (var namedArg in attribute.NamedArguments)
            {
                if (namedArg.Key == parameterName &&
                    namedArg.Value.Kind == TypedConstantKind.Primitive &&
                    namedArg.Value.Value is int intValue)
                {
                    return intValue;
                }
            }

            // 按位置查找构造函数参数
            if (position < attribute.ConstructorArguments.Length)
            {
                var arg = attribute.ConstructorArguments[position];
                if (arg.Kind == TypedConstantKind.Primitive && arg.Value is int intValue)
                {
                    return intValue;
                }
            }

            return null;
        }

        private ITypeSymbol GetDefaultHelperType(string workflowType)
        {
            var defaultHelperName = $"WorkflowHelper.ViewModel.{workflowType}";
            return GetTypeSymbolFromReferencedAssemblies(defaultHelperName) ??
                   GetTypeSymbolFromReferencedAssemblies($"VeloxDev.Core.WorkflowSystem.{defaultHelperName}");
        }

        private INamedTypeSymbol? GetConcreteDefaultVirtualLinkType()
        {
            return GetTypeSymbolFromReferencedAssemblies("VeloxDev.Core.WorkflowSystem.LinkViewModelBase")
                   ?? GetTypeSymbolFromReferencedAssemblies("VeloxDev.Core.WorkflowSystem.Templates.LinkViewModelBase");
        }

        private INamedTypeSymbol? GetConcreteDefaultVirtualSlotType()
        {
            return GetTypeSymbolFromReferencedAssemblies("VeloxDev.Core.WorkflowSystem.SlotViewModelBase")
                   ?? GetTypeSymbolFromReferencedAssemblies("VeloxDev.Core.WorkflowSystem.Templates.SlotViewModelBase");
        }

        private INamedTypeSymbol? GetConcreteDefaultSlotType()
        {
            return GetTypeSymbolFromReferencedAssemblies("VeloxDev.Core.WorkflowSystem.SlotViewModelBase")
                   ?? GetTypeSymbolFromReferencedAssemblies("VeloxDev.Core.WorkflowSystem.Templates.SlotViewModelBase");
        }

        private INamedTypeSymbol? GetTypeSymbolFromReferencedAssemblies(string fullyQualifiedName)
        {
            if (Symbol == null) return null;

            // 首先在当前程序集中查找
            var typeSymbol = Symbol.ContainingAssembly?.GetTypeByMetadataName(fullyQualifiedName);
            if (typeSymbol != null) return typeSymbol;

            // 然后在所有引用程序集中查找
            foreach (var reference in Symbol.ContainingAssembly.Modules.SelectMany(m => m.ReferencedAssemblySymbols))
            {
                typeSymbol = reference.GetTypeByMetadataName(fullyQualifiedName);
                if (typeSymbol != null) return typeSymbol;
            }

            return null;
        }

        private string GetWorkflowInterfaceName(int workflowType)
        {
            return workflowType switch
            {
                1 => $"{NAMESPACE_VELOX_IWORKFLOW}.IWorkflowTreeViewModel",
                2 => $"{NAMESPACE_VELOX_IWORKFLOW}.IWorkflowNodeViewModel",
                3 => $"{NAMESPACE_VELOX_IWORKFLOW}.IWorkflowSlotViewModel",
                4 => $"{NAMESPACE_VELOX_IWORKFLOW}.IWorkflowLinkViewModel",
                _ => throw new ArgumentException($"Unknown workflow type: {workflowType}")
            };
        }

        public override string GenerateBody()
        {
            if (Symbol == null) return string.Empty;

            var attributes = Symbol.GetAttributes();
            var workflowAttribute = attributes.FirstOrDefault(attr => IsWorkflowViewModelAttribute(attr));

            if (workflowAttribute == null) return string.Empty;

            var model = CreateWorkflowAttributeModel(workflowAttribute, Symbol);
            if (model == null) return string.Empty;

            var sb = new StringBuilder();

            switch (model.WorkflowType)
            {
                case 1: // Tree
                    GenerateTreeBody(sb, (TreeAttributeModel)model);
                    break;
                case 2: // Node
                    GenerateNodeBody(sb, (NodeAttributeModel)model);
                    break;
                case 3: // Slot
                    GenerateSlotBody(sb, (SlotAttributeModel)model);
                    break;
                case 4: // Link
                    GenerateLinkBody(sb, (LinkAttributeModel)model);
                    break;
            }

            return sb.ToString();
        }

        private void GenerateTreeBody(StringBuilder sb, TreeAttributeModel model)
        {
            sb.AppendLine($$"""
         public {{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowTreeViewModelHelper Helper { get; protected set; } = new {{model.HelperType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}}();

         private {{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowLinkViewModel virtualLink = new {{model.VirtualLinkType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}}() { Sender = new {{model.VirtualSlotType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}}(), Receiver = new {{model.VirtualSlotType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}}() };
         private {{ObservableCollectionFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowNodeViewModel> nodes = [];
         private {{ObservableCollectionFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowLinkViewModel> links = [];
         private {{DictionaryFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel, {{DictionaryFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel, {{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowLinkViewModel>> linksMap = [];

         protected virtual {{TaskFullName}} CreateNode({{ObjectFullName}}? parameter, {{CancellationTokenFullName}} ct)
         {
             if (parameter is not {{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowNodeViewModel node) return {{TaskFullName}}.CompletedTask;
             Helper.CreateNode(node);
             return {{TaskFullName}}.CompletedTask;
         }
         protected virtual {{TaskFullName}} SetPointer({{ObjectFullName}}? parameter, {{CancellationTokenFullName}} ct)
         {
             if (parameter is not {{NAMESPACE_VELOX_WORKFLOW}}.Anchor anchor) return {{TaskFullName}}.CompletedTask;
             Helper.SetPointer(anchor);
             return {{TaskFullName}}.CompletedTask;
         }
         protected virtual {{TaskFullName}} ResetVirtualLink({{ObjectFullName}}? parameter, {{CancellationTokenFullName}} ct)
         {
             Helper.ResetVirtualLink();
             return {{TaskFullName}}.CompletedTask;
         }
         protected virtual {{TaskFullName}} SendConnection({{ObjectFullName}}? parameter, {{CancellationTokenFullName}} ct)
         {
             if (parameter is not {{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel slot) return {{TaskFullName}}.CompletedTask;
             Helper.SendConnection(slot);
             return {{TaskFullName}}.CompletedTask;
         }
         protected virtual {{TaskFullName}} ReceiveConnection({{ObjectFullName}}? parameter, {{CancellationTokenFullName}} ct)
         {
             if (parameter is not {{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel slot) return {{TaskFullName}}.CompletedTask;
             Helper.ReceiveConnection(slot);
             return {{TaskFullName}}.CompletedTask;
         }
         protected virtual {{TaskFullName}} Submit({{ObjectFullName}}? parameter, {{CancellationTokenFullName}} ct)
         {
             if (parameter is not {{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowActionPair actionPair) return {{TaskFullName}}.CompletedTask;
             Helper.Submit(actionPair);
             return {{TaskFullName}}.CompletedTask;
         }
         protected virtual {{TaskFullName}} Redo({{ObjectFullName}}? parameter, {{CancellationTokenFullName}} ct)
         {
             Helper.Redo();
             return {{TaskFullName}}.CompletedTask;
         }
         protected virtual {{TaskFullName}} Undo({{ObjectFullName}}? parameter, {{CancellationTokenFullName}} ct)
         {
             Helper.Undo();
             return {{TaskFullName}}.CompletedTask;
         }
         protected virtual async {{TaskFullName}} Close({{ObjectFullName}}? parameter, {{CancellationTokenFullName}} ct)
         {
             await Helper.CloseAsync();
         }

         public virtual {{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowTreeViewModelHelper GetHelper() => Helper;
         public virtual void InitializeWorkflow() => Helper.Install(this);
         public virtual void SetHelper({{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowTreeViewModelHelper helper)
         {
             Helper.Uninstall(this);
             helper.Install(this);
             Helper = helper;
         }

        public {{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowLinkViewModel VirtualLink
        {
            get => virtualLink;
            set
            {
               if({{ObjectFullName}}.Equals(virtualLink,value)) return;
               var old = virtualLink;
               OnPropertyChanging(nameof(VirtualLink));
               OnVirtualLinkChanging(old,value);
               virtualLink = value;
               OnVirtualLinkChanged(old,value);
               OnPropertyChanged(nameof(VirtualLink));
            }
        }
        partial void OnVirtualLinkChanging({{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowLinkViewModel oldValue,{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowLinkViewModel newValue);
        partial void OnVirtualLinkChanged({{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowLinkViewModel oldValue,{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowLinkViewModel newValue);
        public {{ObservableCollectionFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowNodeViewModel> Nodes
        {
            get => nodes;
            set
            {
               if({{ObjectFullName}}.Equals(nodes,value)) return;
               var old = nodes;
               OnPropertyChanging(nameof(Nodes));
               OnNodesChanging(old,value);
               nodes = value;
               OnNodesChanged(old,value);
               OnPropertyChanged(nameof(Nodes));
            }
        }
        partial void OnNodesChanging({{ObservableCollectionFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowNodeViewModel> oldValue,{{ObservableCollectionFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowNodeViewModel> newValue);
        partial void OnNodesChanged({{ObservableCollectionFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowNodeViewModel> oldValue,{{ObservableCollectionFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowNodeViewModel> newValue);
        public {{ObservableCollectionFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowLinkViewModel> Links
        {
            get => links;
            set
            {
               if({{ObjectFullName}}.Equals(links,value)) return;
               var old = links;
               OnPropertyChanging(nameof(Links));
               OnLinksChanging(old,value);
               links = value;
               OnLinksChanged(old,value);
               OnPropertyChanged(nameof(Links));
            }
        }
        partial void OnLinksChanging({{ObservableCollectionFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowLinkViewModel> oldValue,{{ObservableCollectionFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowLinkViewModel> newValue);
        partial void OnLinksChanged({{ObservableCollectionFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowLinkViewModel> oldValue,{{ObservableCollectionFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowLinkViewModel> newValue);
        public {{DictionaryFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel, {{DictionaryFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel, {{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowLinkViewModel>> LinksMap
        {
            get => linksMap;
            set
            {
               if({{ObjectFullName}}.Equals(linksMap,value)) return;
               var old = linksMap;
               OnPropertyChanging(nameof(LinksMap));
               OnLinksMapChanging(old,value);
               linksMap = value;
               OnLinksMapChanged(old,value);
               OnPropertyChanged(nameof(LinksMap));
            }
        }
        partial void OnLinksMapChanging({{DictionaryFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel, {{DictionaryFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel, {{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowLinkViewModel>> oldValue,{{DictionaryFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel, {{DictionaryFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel, {{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowLinkViewModel>> newValue);
        partial void OnLinksMapChanged({{DictionaryFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel, {{DictionaryFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel, {{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowLinkViewModel>> oldValue,{{DictionaryFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel, {{DictionaryFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel, {{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowLinkViewModel>> newValue);

        private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_CreateNodeCommand = null;
        public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand CreateNodeCommand
        {
           get
           {
              _buffer_CreateNodeCommand ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                  command: CreateNode,
                  canExecute: _ => true);
              return _buffer_CreateNodeCommand;
           }
        }
        private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_SetPointerCommand = null;
        public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand SetPointerCommand
        {
           get
           {
              _buffer_SetPointerCommand ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                  command: SetPointer,
                  canExecute: _ => true);
              return _buffer_SetPointerCommand;
           }
        }
        private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_ResetVirtualLinkCommand = null;
        public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand ResetVirtualLinkCommand
        {
           get
           {
              _buffer_ResetVirtualLinkCommand ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                  command: ResetVirtualLink,
                  canExecute: _ => true);
              return _buffer_ResetVirtualLinkCommand;
           }
        }
        private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_SendConnectionCommand = null;
        public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand SendConnectionCommand
        {
           get
           {
              _buffer_SendConnectionCommand ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                  command: SendConnection,
                  canExecute: _ => true);
              return _buffer_SendConnectionCommand;
           }
        }
        private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_ReceiveConnectionCommand = null;
        public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand ReceiveConnectionCommand
        {
           get
           {
              _buffer_ReceiveConnectionCommand ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                  command: ReceiveConnection,
                  canExecute: _ => true);
              return _buffer_ReceiveConnectionCommand;
           }
        }
        private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_SubmitCommand = null;
        public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand SubmitCommand
        {
           get
           {
              _buffer_SubmitCommand ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                  command: Submit,
                  canExecute: _ => true);
              return _buffer_SubmitCommand;
           }
        }
        private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_RedoCommand = null;
        public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand RedoCommand
        {
           get
           {
              _buffer_RedoCommand ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                  command: Redo,
                  canExecute: _ => true);
              return _buffer_RedoCommand;
           }
        }
        private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_UndoCommand = null;
        public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand UndoCommand
        {
           get
           {
              _buffer_UndoCommand ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                  command: Undo,
                  canExecute: _ => true);
              return _buffer_UndoCommand;
           }
        }
        private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_CloseCommand = null;
        public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand CloseCommand
        {
           get
           {
              _buffer_CloseCommand ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                  command: Close,
                  canExecute: _ => true);
              return _buffer_CloseCommand;
           }
        }
    """);
        }
        private void GenerateNodeBody(StringBuilder sb, NodeAttributeModel model)
        {
            sb.AppendLine($$"""
                    public {{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowNodeViewModelHelper Helper { get; protected set; } = new {{model.HelperType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}}();

                    private {{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowTreeViewModel? parent = null;
                    private {{NAMESPACE_VELOX_WORKFLOW}}.Anchor anchor = new();
                    private {{NAMESPACE_VELOX_WORKFLOW}}.Size size = new();
                    private {{ObservableCollectionFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel> slots = [];

                    protected virtual {{TaskFullName}} Move({{ObjectFullName}}? parameter, {{CancellationTokenFullName}} ct)
                    {
                        if (parameter is not {{NAMESPACE_VELOX_WORKFLOW}}.Offset offset) return {{TaskFullName}}.CompletedTask;
                        Helper.Move(offset);
                        return {{TaskFullName}}.CompletedTask;
                    }
                    protected virtual {{TaskFullName}} SetAnchor({{ObjectFullName}}? parameter, {{CancellationTokenFullName}} ct)
                    {
                        if (parameter is not {{NAMESPACE_VELOX_WORKFLOW}}.Anchor anchor) return {{TaskFullName}}.CompletedTask;
                        Helper.SetAnchor(anchor);
                        return {{TaskFullName}}.CompletedTask;
                    }
                    protected virtual {{TaskFullName}} SetSize({{ObjectFullName}}? parameter, {{CancellationTokenFullName}} ct)
                    {
                        if (parameter is not {{NAMESPACE_VELOX_WORKFLOW}}.Size scale) return {{TaskFullName}}.CompletedTask;
                        Helper.SetSize(scale);
                        return {{TaskFullName}}.CompletedTask;
                    }
                    protected virtual {{TaskFullName}} CreateSlot({{ObjectFullName}}? parameter, {{CancellationTokenFullName}} ct)
                    {
                        if (parameter is not {{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel slot) return {{TaskFullName}}.CompletedTask;
                        Helper.CreateSlot(slot);
                        return {{TaskFullName}}.CompletedTask;
                    }
                    protected virtual {{TaskFullName}} Delete({{ObjectFullName}}? parameter, {{CancellationTokenFullName}} ct)
                    {
                        Helper.Delete();
                        return {{TaskFullName}}.CompletedTask;
                    }
                    protected virtual async {{TaskFullName}} Work({{ObjectFullName}}? parameter, {{CancellationTokenFullName}} ct)
                    {
                        await Helper.WorkAsync(parameter,ct);
                    }
                    protected virtual async {{TaskFullName}} Broadcast({{ObjectFullName}}? parameter, {{CancellationTokenFullName}} ct)
                    {
                        await Helper.BroadcastAsync(parameter,ct);
                    }
                    protected virtual async {{TaskFullName}} Close({{ObjectFullName}}? parameter, {{CancellationTokenFullName}} ct)
                    {
                        await Helper.CloseAsync();
                    }

                    public virtual {{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowNodeViewModelHelper GetHelper() => Helper;
                    public virtual void InitializeWorkflow() => Helper.Install(this);
                    public virtual void SetHelper({{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowNodeViewModelHelper helper)
                    {
                        Helper.Uninstall(this);
                        helper.Install(this);
                        Helper = helper;
                    }

                    public {{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowTreeViewModel Parent
                    {
                        get => parent;
                        set
                        {
                           if({{ObjectFullName}}.Equals(parent,value)) return;
                           var old = parent;
                           OnPropertyChanging(nameof(Parent));
                           OnParentChanging(old,value);
                           parent = value;
                           OnParentChanged(old,value);
                           OnPropertyChanged(nameof(Parent));
                        }
                    }
                    partial void OnParentChanging({{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowTreeViewModel oldValue,{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowTreeViewModel newValue);
                    partial void OnParentChanged({{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowTreeViewModel oldValue,{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowTreeViewModel newValue);
                    public {{NAMESPACE_VELOX_WORKFLOW}}.Anchor Anchor
                    {
                        get => anchor;
                        set
                        {
                           if({{ObjectFullName}}.Equals(anchor,value)) return;
                           var old = anchor;
                           OnPropertyChanging(nameof(Anchor));
                           OnAnchorChanging(old,value);
                           anchor = value;
                           OnAnchorChanged(old,value);
                           OnPropertyChanged(nameof(Anchor));
                        }
                    }
                    partial void OnAnchorChanging({{NAMESPACE_VELOX_WORKFLOW}}.Anchor oldValue,{{NAMESPACE_VELOX_WORKFLOW}}.Anchor newValue);
                    partial void OnAnchorChanged({{NAMESPACE_VELOX_WORKFLOW}}.Anchor oldValue,{{NAMESPACE_VELOX_WORKFLOW}}.Anchor newValue);
                    public {{NAMESPACE_VELOX_WORKFLOW}}.Size Size
                    {
                        get => size;
                        set
                        {
                           if({{ObjectFullName}}.Equals(size,value)) return;
                           var old = size;
                           OnPropertyChanging(nameof(Size));
                           OnSizeChanging(old,value);
                           size = value;
                           OnSizeChanged(old,value);
                           OnPropertyChanged(nameof(Size));
                        }
                    }
                    partial void OnSizeChanging({{NAMESPACE_VELOX_WORKFLOW}}.Size oldValue,{{NAMESPACE_VELOX_WORKFLOW}}.Size newValue);
                    partial void OnSizeChanged({{NAMESPACE_VELOX_WORKFLOW}}.Size oldValue,{{NAMESPACE_VELOX_WORKFLOW}}.Size newValue);
                    public {{ObservableCollectionFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel> Slots
                    {
                        get => slots;
                        set
                        {
                           if({{ObjectFullName}}.Equals(slots,value)) return;
                           var old = slots;
                           OnPropertyChanging(nameof(Slots));
                           OnSlotsChanging(old,value);
                           slots = value;
                           OnSlotsChanged(old,value);
                           OnPropertyChanged(nameof(Slots));
                        }
                    }
                    partial void OnSlotsChanging({{ObservableCollectionFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel> oldValue,{{ObservableCollectionFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel> newValue);
                    partial void OnSlotsChanged({{ObservableCollectionFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel> oldValue,{{ObservableCollectionFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel> newValue);

                    private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_MoveCommand = null;
                    public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand MoveCommand
                    {
                       get
                       {
                          _buffer_MoveCommand ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                              command: Move,
                              canExecute: _ => true);
                          return _buffer_MoveCommand;
                       }
                    }
                    private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_SetAnchorCommand = null;
                    public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand SetAnchorCommand
                    {
                       get
                       {
                          _buffer_SetAnchorCommand ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                              command: SetAnchor,
                              canExecute: _ => true);
                          return _buffer_SetAnchorCommand;
                       }
                    }
                    private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_SetSizeCommand = null;
                    public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand SetSizeCommand
                    {
                       get
                       {
                          _buffer_SetSizeCommand ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                              command: SetSize,
                              canExecute: _ => true);
                          return _buffer_SetSizeCommand;
                       }
                    }
                    private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_CreateSlotCommand = null;
                    public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand CreateSlotCommand
                    {
                       get
                       {
                          _buffer_CreateSlotCommand ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                              command: CreateSlot,
                              canExecute: _ => true);
                          return _buffer_CreateSlotCommand;
                       }
                    }
                    private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_DeleteCommand = null;
                    public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand DeleteCommand
                    {
                       get
                       {
                          _buffer_DeleteCommand ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                              command: Delete,
                              canExecute: _ => true);
                          return _buffer_DeleteCommand;
                       }
                    }
                """);
            sb.AppendLine($$"""
                    private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_WorkCommand = null;
                    public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand WorkCommand
                    {
                       get
                       {
                          _buffer_WorkCommand ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                              command: Work,
                              canExecute: _ => true,
                              semaphore: {{model.WorkSemaphore}});
                          return _buffer_WorkCommand;
                       }
                    }
                """);
            sb.AppendLine($$"""
                    private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_BroadcastCommand = null;
                    public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand BroadcastCommand
                    {
                       get
                       {
                          _buffer_BroadcastCommand ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                              command: Broadcast,
                              canExecute: _ => true);
                          return _buffer_BroadcastCommand;
                       }
                    }
                    private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_CloseCommand = null;
                    public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand CloseCommand
                    {
                       get
                       {
                          _buffer_CloseCommand ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                              command: Close,
                              canExecute: _ => true);
                          return _buffer_CloseCommand;
                       }
                    }
                """);
        }
        private void GenerateSlotBody(StringBuilder sb, SlotAttributeModel model)
        {
            sb.AppendLine($$"""
        public {{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModelHelper Helper { get; protected set; } = new {{model.HelperType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}}();

        private {{ObservableCollectionFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel> targets = [];
        private {{ObservableCollectionFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel> sources = [];
        private {{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowNodeViewModel? parent = null;
        private {{NAMESPACE_VELOX_IWORKFLOW}}.SlotChannel channel = {{NAMESPACE_VELOX_IWORKFLOW}}.SlotChannel.OneBoth;
        private {{NAMESPACE_VELOX_IWORKFLOW}}.SlotState state = {{NAMESPACE_VELOX_IWORKFLOW}}.SlotState.StandBy;
        private {{NAMESPACE_VELOX_WORKFLOW}}.VisualPoint visualPoint = new();
        private {{NAMESPACE_VELOX_WORKFLOW}}.Anchor anchor = new();
        private {{NAMESPACE_VELOX_WORKFLOW}}.Offset offset = new();
        private {{NAMESPACE_VELOX_WORKFLOW}}.Size size = new();

        protected virtual {{TaskFullName}} SetSize({{ObjectFullName}}? parameter, {{CancellationTokenFullName}} ct)
        {
            if (parameter is not {{NAMESPACE_VELOX_WORKFLOW}}.Size scale) return {{TaskFullName}}.CompletedTask;
            Helper.SetSize(scale);
            return {{TaskFullName}}.CompletedTask;
        }
        protected virtual {{TaskFullName}} SetChannel({{ObjectFullName}}? parameter, {{CancellationTokenFullName}} ct)
        {
            if (parameter is not {{NAMESPACE_VELOX_IWORKFLOW}}.SlotChannel slotChannel) return {{TaskFullName}}.CompletedTask;
            Helper.SetChannel(slotChannel);
            return {{TaskFullName}}.CompletedTask;
        }
        protected virtual {{TaskFullName}} SendConnection({{ObjectFullName}}? parameter, {{CancellationTokenFullName}} ct)
        {
            Helper.SendConnection();
            return {{TaskFullName}}.CompletedTask;
        }
        protected virtual {{TaskFullName}} ReceiveConnection({{ObjectFullName}}? parameter, {{CancellationTokenFullName}} ct)
        {
            Helper.ReceiveConnection();
            return {{TaskFullName}}.CompletedTask;
        }
        protected virtual {{TaskFullName}} Delete({{ObjectFullName}}? parameter, {{CancellationTokenFullName}} ct)
        {
            Helper.Delete();
            return {{TaskFullName}}.CompletedTask;
        }
        protected virtual async {{TaskFullName}} Close({{ObjectFullName}}? parameter, {{CancellationTokenFullName}} ct)
        {
            await Helper.CloseAsync();
        }

        public virtual {{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModelHelper GetHelper() => Helper;
        public virtual void InitializeWorkflow() => Helper.Install(this);
        public virtual void SetHelper({{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModelHelper helper)
        {
            Helper.Uninstall(this);
            helper.Install(this);
            Helper = helper;
        }

        public {{ObservableCollectionFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel> Targets
        {
            get => targets;
            set
            {
               if({{ObjectFullName}}.Equals(targets,value)) return;
               var old = targets;
               OnPropertyChanging(nameof(Targets));
               OnTargetsChanging(old,value);
               targets = value;
               OnTargetsChanged(old,value);
               OnPropertyChanged(nameof(Targets));
            }
        }
        partial void OnTargetsChanging({{ObservableCollectionFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel> oldValue,{{ObservableCollectionFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel> newValue);
        partial void OnTargetsChanged({{ObservableCollectionFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel> oldValue,{{ObservableCollectionFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel> newValue);
        public {{ObservableCollectionFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel> Sources
        {
            get => sources;
            set
            {
               if({{ObjectFullName}}.Equals(sources,value)) return;
               var old = sources;
               OnPropertyChanging(nameof(Sources));
               OnSourcesChanging(old,value);
               sources = value;
               OnSourcesChanged(old,value);
               OnPropertyChanged(nameof(Sources));
            }
        }
        partial void OnSourcesChanging({{ObservableCollectionFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel> oldValue,{{ObservableCollectionFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel> newValue);
        partial void OnSourcesChanged({{ObservableCollectionFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel> oldValue,{{ObservableCollectionFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel> newValue);
        public {{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowNodeViewModel Parent
        {
            get => parent;
            set
            {
               if({{ObjectFullName}}.Equals(parent,value)) return;
               var old = parent;
               OnPropertyChanging(nameof(Parent));
               OnParentChanging(old,value);
               parent = value;
               OnParentChanged(old,value);
               OnPropertyChanged(nameof(Parent));
            }
        }
        partial void OnParentChanging({{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowNodeViewModel oldValue,{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowNodeViewModel newValue);
        partial void OnParentChanged({{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowNodeViewModel oldValue,{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowNodeViewModel newValue);
        public {{NAMESPACE_VELOX_IWORKFLOW}}.SlotChannel Channel
        {
            get => channel;
            set
            {
               if({{ObjectFullName}}.Equals(channel,value)) return;
               var old = channel;
               OnPropertyChanging(nameof(Channel));
               OnChannelChanging(old,value);
               channel = value;
               OnChannelChanged(old,value);
               OnPropertyChanged(nameof(Channel));
            }
        }
        partial void OnChannelChanging({{NAMESPACE_VELOX_IWORKFLOW}}.SlotChannel oldValue,{{NAMESPACE_VELOX_IWORKFLOW}}.SlotChannel newValue);
        partial void OnChannelChanged({{NAMESPACE_VELOX_IWORKFLOW}}.SlotChannel oldValue,{{NAMESPACE_VELOX_IWORKFLOW}}.SlotChannel newValue);
        public {{NAMESPACE_VELOX_IWORKFLOW}}.SlotState State
        {
            get => state;
            set
            {
               if({{ObjectFullName}}.Equals(state,value)) return;
               var old = state;
               OnPropertyChanging(nameof(State));
               OnStateChanging(old,value);
               state = value;
               OnStateChanged(old,value);
               OnPropertyChanged(nameof(State));
            }
        }
        partial void OnStateChanging({{NAMESPACE_VELOX_IWORKFLOW}}.SlotState oldValue,{{NAMESPACE_VELOX_IWORKFLOW}}.SlotState newValue);
        partial void OnStateChanged({{NAMESPACE_VELOX_IWORKFLOW}}.SlotState oldValue,{{NAMESPACE_VELOX_IWORKFLOW}}.SlotState newValue);
        public {{NAMESPACE_VELOX_WORKFLOW}}.VisualPoint VisualPoint
        {
            get => visualPoint;
            set
            {
               if({{ObjectFullName}}.Equals(visualPoint,value)) return;
               var old = visualPoint;
               OnPropertyChanging(nameof(VisualPoint));
               OnVisualPointChanging(old, value);
               visualPoint = value;
               OnVisualPointChanged(old, value);
               OnPropertyChanged(nameof(VisualPoint));
            }
        }
        partial void OnVisualPointChanging({{NAMESPACE_VELOX_WORKFLOW}}.VisualPoint oldValue, {{NAMESPACE_VELOX_WORKFLOW}}.VisualPoint newValue);
        partial void OnVisualPointChanged({{NAMESPACE_VELOX_WORKFLOW}}.VisualPoint oldValue, {{NAMESPACE_VELOX_WORKFLOW}}.VisualPoint newValue);
        public {{NAMESPACE_VELOX_WORKFLOW}}.Anchor Anchor
        {
            get => anchor;
            set
            {
               if({{ObjectFullName}}.Equals(anchor,value)) return;
               var old = anchor;
               OnPropertyChanging(nameof(Anchor));
               OnAnchorChanging(old,value);
               anchor = value;
               OnAnchorChanged(old,value);
               OnPropertyChanged(nameof(Anchor));
            }
        }
        partial void OnAnchorChanging({{NAMESPACE_VELOX_WORKFLOW}}.Anchor oldValue,{{NAMESPACE_VELOX_WORKFLOW}}.Anchor newValue);
        partial void OnAnchorChanged({{NAMESPACE_VELOX_WORKFLOW}}.Anchor oldValue,{{NAMESPACE_VELOX_WORKFLOW}}.Anchor newValue);
        public {{NAMESPACE_VELOX_WORKFLOW}}.Offset Offset
        {
            get => offset;
            set
            {
               if({{ObjectFullName}}.Equals(offset,value)) return;
               var old = offset;
               OnPropertyChanging(nameof(Offset));
               OnOffsetChanging(old,value);
               offset = value;
               OnOffsetChanged(old,value);
               OnPropertyChanged(nameof(Offset));
            }
        }
        partial void OnOffsetChanging({{NAMESPACE_VELOX_WORKFLOW}}.Offset oldValue,{{NAMESPACE_VELOX_WORKFLOW}}.Offset newValue);
        partial void OnOffsetChanged({{NAMESPACE_VELOX_WORKFLOW}}.Offset oldValue,{{NAMESPACE_VELOX_WORKFLOW}}.Offset newValue);
        public {{NAMESPACE_VELOX_WORKFLOW}}.Size Size
        {
            get => size;
            set
            {
               if({{ObjectFullName}}.Equals(size,value)) return;
               var old = size;
               OnPropertyChanging(nameof(Size));
               OnSizeChanging(old,value);
               size = value;
               OnSizeChanged(old,value);
               OnPropertyChanged(nameof(Size));
            }
        }
        partial void OnSizeChanging({{NAMESPACE_VELOX_WORKFLOW}}.Size oldValue,{{NAMESPACE_VELOX_WORKFLOW}}.Size newValue);
        partial void OnSizeChanged({{NAMESPACE_VELOX_WORKFLOW}}.Size oldValue,{{NAMESPACE_VELOX_WORKFLOW}}.Size newValue);

        private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_SetSizeCommand = null;
        public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand SetSizeCommand
        {
           get
           {
              _buffer_SetSizeCommand ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                  command: SetSize,
                  canExecute: _ => true);
              return _buffer_SetSizeCommand;
           }
        }
        private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_SetChannelCommand = null;
        public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand SetChannelCommand
        {
           get
           {
              _buffer_SetChannelCommand ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                  command: SetChannel,
                  canExecute: _ => true);
              return _buffer_SetChannelCommand;
           }
        }
        private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_SendConnectionCommand = null;
        public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand SendConnectionCommand
        {
           get
           {
              _buffer_SendConnectionCommand ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                  command: SendConnection,
                  canExecute: _ => true);
              return _buffer_SendConnectionCommand;
           }
        }
        private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_ReceiveConnectionCommand = null;
        public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand ReceiveConnectionCommand
        {
           get
           {
              _buffer_ReceiveConnectionCommand ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                  command: ReceiveConnection,
                  canExecute: _ => true);
              return _buffer_ReceiveConnectionCommand;
           }
        }
        private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_DeleteCommand = null;
        public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand DeleteCommand
        {
           get
           {
              _buffer_DeleteCommand ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                  command: Delete,
                  canExecute: _ => true);
              return _buffer_DeleteCommand;
           }
        }
        private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_CloseCommand = null;
        public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand CloseCommand
        {
           get
           {
              _buffer_CloseCommand ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                  command: Close,
                  canExecute: _ => true);
              return _buffer_CloseCommand;
           }
        }
    """);
        }
        private void GenerateLinkBody(StringBuilder sb, LinkAttributeModel model)
        {
            sb.AppendLine($$"""
        public {{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowLinkViewModelHelper Helper { get; protected set; } = new {{model.HelperType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}}();

        private {{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel sender = new {{model.SlotType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}}();
        private {{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel receiver = new {{model.SlotType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}}();
        private bool isVisible = false;

        protected virtual {{TaskFullName}} Delete({{ObjectFullName}}? parameter, {{CancellationTokenFullName}} ct)
        {
            Helper.Delete();
            return {{TaskFullName}}.CompletedTask;
        }
        protected virtual async {{TaskFullName}} Close({{ObjectFullName}}? parameter, {{CancellationTokenFullName}} ct)
        {
            await Helper.CloseAsync();
        }

        public virtual {{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowLinkViewModelHelper GetHelper() => Helper;
        public virtual void InitializeWorkflow() => Helper.Install(this);
        public virtual void SetHelper({{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowLinkViewModelHelper helper)
        {
            Helper.Uninstall(this);
            helper.Install(this);
            Helper = helper;
        }

        public {{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel Sender
        {
            get => sender;
            set
            {
               if({{ObjectFullName}}.Equals(sender,value)) return;
               var old = sender;
               OnPropertyChanging(nameof(Sender));
               OnSenderChanging(old,value);
               sender = value;
               OnSenderChanged(old,value);
               OnPropertyChanged(nameof(Sender));
            }
        }
        partial void OnSenderChanging({{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel oldValue,{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel newValue);
        partial void OnSenderChanged({{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel oldValue,{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel newValue);
        public {{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel Receiver
        {
            get => receiver;
            set
            {
               if({{ObjectFullName}}.Equals(receiver,value)) return;
               var old = receiver;
               OnPropertyChanging(nameof(Receiver));
               OnReceiverChanging(old,value);
               receiver = value;
               OnReceiverChanged(old,value);
               OnPropertyChanged(nameof(Receiver));
            }
        }
        partial void OnReceiverChanging({{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel oldValue,{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel newValue);
        partial void OnReceiverChanged({{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel oldValue,{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel newValue);
        public bool IsVisible
        {
            get => isVisible;
            set
            {
               if({{ObjectFullName}}.Equals(isVisible,value)) return;
               var old = isVisible;
               OnPropertyChanging(nameof(IsVisible));
               OnIsVisibleChanging(old,value);
               isVisible = value;
               OnIsVisibleChanged(old,value);
               OnPropertyChanged(nameof(IsVisible));
            }
        }
        partial void OnIsVisibleChanging({{ObjectFullName}} oldValue,{{ObjectFullName}} newValue);
        partial void OnIsVisibleChanged({{ObjectFullName}} oldValue,{{ObjectFullName}} newValue);

        private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_DeleteCommand = null;
        public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand DeleteCommand
        {
           get
           {
              _buffer_DeleteCommand ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                  command: Delete,
                  canExecute: _ => true);
              return _buffer_DeleteCommand;
           }
        }
        private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_CloseCommand = null;
        public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand CloseCommand
        {
           get
           {
              _buffer_CloseCommand ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                  command: Close,
                  canExecute: _ => true);
              return _buffer_CloseCommand;
           }
        }
    """);
        }

        #region 内部模型定义

        private abstract class WorkflowAttributeModel
        {
            public INamedTypeSymbol AttributeSymbol { get; set; }
            public INamedTypeSymbol TargetClassSymbol { get; set; }
            public ITypeSymbol HelperType { get; set; }
            public AttributeData AttributeData { get; set; }
            public abstract int WorkflowType { get; }
        }

        private class TreeAttributeModel : WorkflowAttributeModel
        {
            public INamedTypeSymbol VirtualLinkType { get; set; }
            public INamedTypeSymbol VirtualSlotType { get; set; }
            public override int WorkflowType => 1;
        }

        private class NodeAttributeModel : WorkflowAttributeModel
        {
            public int WorkSemaphore { get; set; } = 1;
            public override int WorkflowType => 2;
        }

        private class SlotAttributeModel : WorkflowAttributeModel
        {
            public override int WorkflowType => 3;
        }

        private class LinkAttributeModel : WorkflowAttributeModel
        {
            public INamedTypeSymbol SlotType { get; set; }
            public override int WorkflowType => 4;
        }

        #endregion
    }
}