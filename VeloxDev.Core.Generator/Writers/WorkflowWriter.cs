using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Linq;
using System.Text;
using VeloxDev.Core.Generator.Base;

namespace VeloxDev.Core.Generator.Writers
{
    public class WorkflowWriter : WriterBase
    {
        private const string ObservableCollectionFullName = "global::System.Collections.ObjectModel.ObservableCollection";
        private const string DictionaryFullName = "global::System.Collections.Generic.Dictionary";
        private const string HashSetFullName = "global::System.Collections.Generic.HashSet";
        private const string CancellationTokenFullName = "global::System.Threading.CancellationToken";
        private const string TaskFullName = "global::System.Threading.Tasks.Task";
        private const string FuncTaskFullName = "global::System.Func<global::System.Threading.Tasks.Task>";
        private const string ObjectFullName = "global::System.Object";
        private const string ExceptionFullName = "global::System.Exception";
        private const string DebugFullName = "global::System.Diagnostics.Debug";
        private const string InterlockedFullName = "global::System.Threading.Interlocked";
        private const string ActionFullName = "global::System.Action";
        private const string ActionObjectFullName = "global::System.Action<global::System.Object?>";

        private int _workflowType = 0;
        private WorkflowAttributeModel? _detectedModel;

        public override void Initialize(ClassDeclarationSyntax classDeclaration, INamedTypeSymbol namedTypeSymbol)
        {
            base.Initialize(classDeclaration, namedTypeSymbol);
            DetectWorkflowAttribute(namedTypeSymbol);
        }
        private void DetectWorkflowAttribute(INamedTypeSymbol symbol)
        {
            var attributes = symbol.GetAttributes();

            foreach (var attribute in attributes)
            {
                var attributeClass = attribute.AttributeClass;
                if (attributeClass == null) continue;

                // 修正特性识别逻辑
                var model = CreateAttributeModel(attribute, symbol);
                if (model != null)
                {
                    _detectedModel = model;
                    _workflowType = model.WorkflowType;
                    break;
                }
            }
        }

        #region 参数解析
        private WorkflowAttributeModel? CreateAttributeModel(AttributeData attribute, INamedTypeSymbol targetClass)
        {
            var attributeClass = attribute.AttributeClass;
            if (attributeClass == null) return null;

            // 修正特性识别逻辑
            var containingType = attributeClass.ContainingType;
            if (containingType?.Name != "ViewModel" ||
                containingType.ContainingType?.Name != "WorkflowBuilder" ||
                containingType.ContainingNamespace?.ToDisplayString() != "VeloxDev.Core.WorkflowSystem")
            {
                return null;
            }

            // 处理泛型特性名称（如 TreeAttribute`1 -> TreeAttribute）
            var attributeName = attributeClass.Name;
            if (attributeName.Contains('`'))
            {
                attributeName = attributeName.Substring(0, attributeName.IndexOf('`'));
            }

            return attributeName switch
            {
                "TreeAttribute" => CreateTreeModel(attribute, targetClass),
                "NodeAttribute" => CreateNodeModel(attribute, targetClass),
                "SlotAttribute" => CreateSlotModel(attribute, targetClass),
                "LinkAttribute" => CreateLinkModel(attribute, targetClass),
                _ => null
            };
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
                // 如果没有泛型参数，使用默认的Helper类型
                model.HelperType = GetDefaultHelperType("Tree");
            }

            // 直接获取构造函数参数，如果为default则使用具体的默认类型
            model.VirtualLinkType = GetConstructorArgumentAsType(attribute, 0)
                ?? GetConcreteDefaultVirtualLinkType();

            model.VirtualSlotType = GetConstructorArgumentAsType(attribute, 1)
                ?? GetConcreteDefaultVirtualSlotType();

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

            // 直接获取参数值，如果为default则使用默认值
            model.WorkSemaphore = GetConstructorArgumentAsInt(attribute, 0) ?? 1;

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

            // 直接获取参数值，如果为default则使用具体的默认类型
            model.SlotType = GetConstructorArgumentAsType(attribute, 0)
                ?? GetConcreteDefaultSlotType();

            return model;
        }
        private INamedTypeSymbol? GetConstructorArgumentAsType(AttributeData attribute, int index)
        {
            if (attribute.ConstructorArguments.Length > index)
            {
                var arg = attribute.ConstructorArguments[index];
                if (arg.Kind == TypedConstantKind.Type && arg.Value is INamedTypeSymbol typeSymbol)
                {
                    return typeSymbol;
                }
            }
            return null; // 返回null表示使用默认值
        }
        private int? GetConstructorArgumentAsInt(AttributeData attribute, int index)
        {
            if (attribute.ConstructorArguments.Length > index)
            {
                var arg = attribute.ConstructorArguments[index];
                if (arg.Kind == TypedConstantKind.Primitive && arg.Value is int intValue)
                {
                    return intValue;
                }
            }
            return null; // 返回null表示使用默认值
        }
        private INamedTypeSymbol? GetConcreteDefaultVirtualLinkType()
        {
            return GetTypeSymbolFromReferencedAssemblies("VeloxDev.Core.WorkflowSystem.LinkViewModelBase");
        }
        private INamedTypeSymbol? GetConcreteDefaultVirtualSlotType()
        {
            return GetTypeSymbolFromReferencedAssemblies("VeloxDev.Core.WorkflowSystem.SlotViewModelBase");
        }
        private INamedTypeSymbol? GetConcreteDefaultSlotType()
        {
            return GetTypeSymbolFromReferencedAssemblies("VeloxDev.Core.WorkflowSystem.SlotViewModelBase");
        }
        private ITypeSymbol? GetDefaultHelperType(string workflowType)
        {
            var typeName = $"VeloxDev.Core.WorkflowSystem.WorkflowHelper.ViewModel.{workflowType}";
            return GetTypeSymbolFromReferencedAssemblies(typeName);
        }
        private INamedTypeSymbol? GetTypeSymbolFromReferencedAssemblies(string fullyQualifiedName)
        {
            if (Symbol == null) return null;

            // 首先尝试当前程序集
            var typeSymbol = Symbol.ContainingAssembly?.GetTypeByMetadataName(fullyQualifiedName);
            if (typeSymbol != null) return typeSymbol;

            // 然后尝试所有引用程序集
            foreach (var reference in Symbol.ContainingAssembly.Modules.SelectMany(m => m.ReferencedAssemblySymbols))
            {
                typeSymbol = reference.GetTypeByMetadataName(fullyQualifiedName);
                if (typeSymbol != null) return typeSymbol;
            }

            // 如果找不到，返回null（让调用方处理）
            return null;
        }
        #endregion

        public override bool CanWrite() => _workflowType != 0;
        public override string GetFileName()
        {
            if (Syntax == null || Symbol == null)
            {
                return string.Empty;
            }

            return $"{Syntax.Identifier.Text}_{Symbol.ContainingNamespace.ToDisplayString().Replace('.', '_')}_Workflow.g.cs";
        }
        public override string[] GenerateBaseInterfaces()
            => _workflowType switch
            {
                1 => [$"{NAMESPACE_VELOX_IWORKFLOW}.IWorkflowTreeViewModel"],
                2 => [$"{NAMESPACE_VELOX_IWORKFLOW}.IWorkflowNodeViewModel"],
                3 => [$"{NAMESPACE_VELOX_IWORKFLOW}.IWorkflowSlotViewModel"],
                4 => [$"{NAMESPACE_VELOX_IWORKFLOW}.IWorkflowLinkViewModel"],
                _ => []
            };
        public override string[] GenerateBaseTypes() => [];
        public override string GenerateBody()
        {
            if (_detectedModel == null)
                return string.Empty;

            var sb = new StringBuilder();

            switch (_detectedModel)
            {
                case TreeAttributeModel treeModel:
                    GenerateTreeBody(sb, treeModel);
                    break;
                case NodeAttributeModel nodeModel:
                    GenerateNodeBody(sb, nodeModel);
                    break;
                case SlotAttributeModel slotModel:
                    GenerateSlotBody(sb, slotModel);
                    break;
                case LinkAttributeModel linkModel:
                    GenerateLinkBody(sb, linkModel);
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
         protected virtual {{TaskFullName}} ApplyConnection({{ObjectFullName}}? parameter, {{CancellationTokenFullName}} ct)
         {
             if (parameter is not {{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel slot) return {{TaskFullName}}.CompletedTask;
             Helper.ApplyConnection(slot);
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
         public virtual void InitializeWorkflow() => Helper.Initialize(this);
         public virtual void SetHelper({{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowTreeViewModelHelper helper)
         {
             helper.Initialize(this);
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
                  executeAsync: CreateNode,
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
                  executeAsync: SetPointer,
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
                  executeAsync: ResetVirtualLink,
                  canExecute: _ => true);
              return _buffer_ResetVirtualLinkCommand;
           }
        }
        private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_ApplyConnectionCommand = null;
        public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand ApplyConnectionCommand
        {
           get
           {
              _buffer_ApplyConnectionCommand ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                  executeAsync: ApplyConnection,
                  canExecute: _ => true);
              return _buffer_ApplyConnectionCommand;
           }
        }
        private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_ReceiveConnectionCommand = null;
        public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand ReceiveConnectionCommand
        {
           get
           {
              _buffer_ReceiveConnectionCommand ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                  executeAsync: ReceiveConnection,
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
                  executeAsync: Submit,
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
                  executeAsync: Redo,
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
                  executeAsync: Undo,
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
                  executeAsync: Close,
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
        protected virtual {{TaskFullName}} SaveAnchor({{ObjectFullName}}? parameter, {{CancellationTokenFullName}} ct)
        {
            Helper.SaveAnchor();
            return {{TaskFullName}}.CompletedTask;
        }
        protected virtual {{TaskFullName}} SaveSize({{ObjectFullName}}? parameter, {{CancellationTokenFullName}} ct)
        {
            Helper.SaveSize();
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
        protected virtual {{TaskFullName}} Work({{ObjectFullName}}? parameter, {{CancellationTokenFullName}} ct)
        {
            return {{TaskFullName}}.CompletedTask;
        }
        protected virtual {{TaskFullName}} Broadcast({{ObjectFullName}}? parameter, {{CancellationTokenFullName}} ct)
        {
            return {{TaskFullName}}.CompletedTask;
        }
        protected virtual async {{TaskFullName}} Close({{ObjectFullName}}? parameter, {{CancellationTokenFullName}} ct)
        {
            await Helper.CloseAsync();
        }

        public virtual {{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowNodeViewModelHelper GetHelper() => Helper;
        public virtual void InitializeWorkflow() => Helper.Initialize(this);
        public virtual void SetHelper({{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowNodeViewModelHelper helper)
        {
            helper.Initialize(this);
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
                  executeAsync: Move,
                  canExecute: _ => true);
              return _buffer_MoveCommand;
           }
        }
        private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_SaveAnchorCommand = null;
        public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand SaveAnchorCommand
        {
           get
           {
              _buffer_SaveAnchorCommand ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                  executeAsync: SaveAnchor,
                  canExecute: _ => true);
              return _buffer_SaveAnchorCommand;
           }
        }
        private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_SaveSizeCommand = null;
        public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand SaveSizeCommand
        {
           get
           {
              _buffer_SaveSizeCommand ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                  executeAsync: SaveSize,
                  canExecute: _ => true);
              return _buffer_SaveSizeCommand;
           }
        }
        private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_SetAnchorCommand = null;
        public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand SetAnchorCommand
        {
           get
           {
              _buffer_SetAnchorCommand ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                  executeAsync: SetAnchor,
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
                  executeAsync: SetSize,
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
                  executeAsync: CreateSlot,
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
                  executeAsync: Delete,
                  canExecute: _ => true);
              return _buffer_DeleteCommand;
           }
        }
    """);

            if (model.WorkSemaphore > 1)
            {
                sb.AppendLine($$"""
        private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_WorkCommand = null;
        public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand WorkCommand
        {
           get
           {
              _buffer_WorkCommand ??= new {{NAMESPACE_VELOX_MVVM}}.ConcurrentVeloxCommand(
                  executeAsync: Work,
                  canExecute: _ => true,
                  semaphore: {{model.WorkSemaphore}});
              return _buffer_WorkCommand;
           }
        }
    """);
            }
            else
            {
                sb.AppendLine($$"""
        private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_WorkCommand = null;
        public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand WorkCommand
        {
           get
           {
              _buffer_WorkCommand ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                  executeAsync: Work,
                  canExecute: _ => true);
              return _buffer_WorkCommand;
           }
        }
    """);
            }

            sb.AppendLine($$"""
        private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_BroadcastCommand = null;
        public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand BroadcastCommand
        {
           get
           {
              _buffer_BroadcastCommand ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                  executeAsync: Broadcast,
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
                  executeAsync: Close,
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

        private {{HashSetFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel> targets = [];
        private {{HashSetFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel> sources = [];
        private {{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowNodeViewModel? parent = null;
        private {{NAMESPACE_VELOX_IWORKFLOW}}.SlotChannel channel = {{NAMESPACE_VELOX_IWORKFLOW}}.SlotChannel.OneBoth;
        private {{NAMESPACE_VELOX_IWORKFLOW}}.SlotState state = {{NAMESPACE_VELOX_IWORKFLOW}}.SlotState.StandBy;
        private {{NAMESPACE_VELOX_WORKFLOW}}.Anchor anchor = new();
        private {{NAMESPACE_VELOX_WORKFLOW}}.Offset offset = new();
        private {{NAMESPACE_VELOX_WORKFLOW}}.Size size = new();

        protected virtual {{TaskFullName}} SaveOffset({{ObjectFullName}}? parameter, {{CancellationTokenFullName}} ct)
        {
            Helper.SaveOffset();
            return {{TaskFullName}}.CompletedTask;
        }
        protected virtual {{TaskFullName}} SaveSize({{ObjectFullName}}? parameter, {{CancellationTokenFullName}} ct)
        {
            Helper.SaveSize();
            return {{TaskFullName}}.CompletedTask;
        }
        protected virtual {{TaskFullName}} SetOffset({{ObjectFullName}}? parameter, {{CancellationTokenFullName}} ct)
        {
            if (parameter is not {{NAMESPACE_VELOX_WORKFLOW}}.Offset offset) return {{TaskFullName}}.CompletedTask;
            Helper.SetOffset(offset);
            return {{TaskFullName}}.CompletedTask;
        }
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
        protected virtual {{TaskFullName}} ApplyConnection({{ObjectFullName}}? parameter, {{CancellationTokenFullName}} ct)
        {
            Helper.ApplyConnection();
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
        public virtual void InitializeWorkflow() => Helper.Initialize(this);
        public virtual void SetHelper({{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModelHelper helper)
        {
            helper.Initialize(this);
            Helper = helper;
        }

        public {{HashSetFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel> Targets
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
        partial void OnTargetsChanging({{HashSetFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel> oldValue,{{HashSetFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel> newValue);
        partial void OnTargetsChanged({{HashSetFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel> oldValue,{{HashSetFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel> newValue);
        public {{HashSetFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel> Sources
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
        partial void OnSourcesChanging({{HashSetFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel> oldValue,{{HashSetFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel> newValue);
        partial void OnSourcesChanged({{HashSetFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel> oldValue,{{HashSetFullName}}<{{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModel> newValue);
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

        private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_SaveOffsetCommand = null;
        public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand SaveOffsetCommand
        {
           get
           {
              _buffer_SaveOffsetCommand ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                  executeAsync: SaveOffset,
                  canExecute: _ => true);
              return _buffer_SaveOffsetCommand;
           }
        }
        private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_SaveSizeCommand = null;
        public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand SaveSizeCommand
        {
           get
           {
              _buffer_SaveSizeCommand ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                  executeAsync: SaveSize,
                  canExecute: _ => true);
              return _buffer_SaveSizeCommand;
           }
        }
        private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_SetOffsetCommand = null;
        public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand SetOffsetCommand
        {
           get
           {
              _buffer_SetOffsetCommand ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                  executeAsync: SetOffset,
                  canExecute: _ => true);
              return _buffer_SetOffsetCommand;
           }
        }
        private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_SetSizeCommand = null;
        public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand SetSizeCommand
        {
           get
           {
              _buffer_SetSizeCommand ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                  executeAsync: SetSize,
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
                  executeAsync: SetChannel,
                  canExecute: _ => true);
              return _buffer_SetChannelCommand;
           }
        }
        private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_ApplyConnectionCommand = null;
        public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand ApplyConnectionCommand
        {
           get
           {
              _buffer_ApplyConnectionCommand ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                  executeAsync: ApplyConnection,
                  canExecute: _ => true);
              return _buffer_ApplyConnectionCommand;
           }
        }
        private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_ReceiveConnectionCommand = null;
        public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand ReceiveConnectionCommand
        {
           get
           {
              _buffer_ReceiveConnectionCommand ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                  executeAsync: ReceiveConnection,
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
                  executeAsync: Delete,
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
                  executeAsync: Close,
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
        public virtual void InitializeWorkflow() => Helper.Initialize(this);
        public virtual void SetHelper({{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowLinkViewModelHelper helper)
        {
            helper.Initialize(this);
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
                  executeAsync: Delete,
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
                  executeAsync: Close,
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