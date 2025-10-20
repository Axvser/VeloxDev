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

            // 解析构造函数参数
            if (attribute.ConstructorArguments.Length >= 1)
            {
                model.VirtualLinkType = attribute.ConstructorArguments[0].Value as INamedTypeSymbol;
            }
            if (attribute.ConstructorArguments.Length >= 2)
            {
                model.VirtualSlotType = attribute.ConstructorArguments[1].Value as INamedTypeSymbol;
            }

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

            // 解析构造函数参数
            if (attribute.ConstructorArguments.Length >= 1)
            {
                model.WorkSemaphore = (int)(attribute.ConstructorArguments[0].Value ?? 1);
            }

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

            // 解析构造函数参数
            if (attribute.ConstructorArguments.Length >= 1)
            {
                model.SlotType = attribute.ConstructorArguments[0].Value as INamedTypeSymbol;
            }

            return model;
        }

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

                 private IWorkflowLinkViewModel virtualLink = new {{model.VirtualLinkType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}}() { Sender = new {{model.VirtualSlotType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}}(), Receiver = new {{model.VirtualSlotType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}}() };
                 private ObservableCollection<IWorkflowNodeViewModel> nodes = [];
                 private ObservableCollection<IWorkflowLinkViewModel> links = [];
                 private Dictionary<IWorkflowSlotViewModel, Dictionary<IWorkflowSlotViewModel, IWorkflowLinkViewModel>> linksMap = [];

                 protected virtual Task CreateNode(object? parameter, CancellationToken ct)
                 {
                     if (parameter is not IWorkflowNodeViewModel node) return Task.CompletedTask;
                     Helper.CreateNode(node);
                     return Task.CompletedTask;
                 }
                 protected virtual Task SetPointer(object? parameter, CancellationToken ct)
                 {
                     if (parameter is not Anchor anchor) return Task.CompletedTask;
                     Helper.SetPointer(anchor);
                     return Task.CompletedTask;
                 }
                 protected virtual Task ResetVirtualLink(object? parameter, CancellationToken ct)
                 {
                     Helper.ResetVirtualLink();
                     return Task.CompletedTask;
                 }
                 protected virtual Task ApplyConnection(object? parameter, CancellationToken ct)
                 {
                     if (parameter is not IWorkflowSlotViewModel slot) return Task.CompletedTask;
                     Helper.ApplyConnection(slot);
                     return Task.CompletedTask;
                 }
                 protected virtual Task ReceiveConnection(object? parameter, CancellationToken ct)
                 {
                     if (parameter is not IWorkflowSlotViewModel slot) return Task.CompletedTask;
                     Helper.ReceiveConnection(slot);
                     return Task.CompletedTask;
                 }
                 protected virtual Task Submit(object? parameter, CancellationToken ct)
                 {
                     if (parameter is not IWorkflowActionPair actionPair) return Task.CompletedTask;
                     Helper.Submit(actionPair);
                     return Task.CompletedTask;
                 }
                 protected virtual Task Redo(object? parameter, CancellationToken ct)
                 {
                     Helper.Redo();
                     return Task.CompletedTask;
                 }
                 protected virtual Task Undo(object? parameter, CancellationToken ct)
                 {
                     Helper.Undo();
                     return Task.CompletedTask;
                 }
                 protected virtual async Task Close(object? parameter, CancellationToken ct)
                 {
                     await Helper.CloseAsync();
                 }

                 public virtual IWorkflowTreeViewModelHelper GetHelper() => Helper;
                 public virtual void InitializeWorkflow() => Helper.Initialize(this);
                 public virtual void SetHelper(IWorkflowTreeViewModelHelper helper)
                 {
                     helper.Initialize(this);
                     Helper = helper;
                 }

                public event global::System.ComponentModel.PropertyChangingEventHandler? PropertyChanging;
                public event global::System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
                public void OnPropertyChanging(string propertyName)
                {
                    PropertyChanging?.Invoke(this, new global::System.ComponentModel.PropertyChangingEventArgs(propertyName));
                }
                public void OnPropertyChanged(string propertyName)
                {
                    PropertyChanged?.Invoke(this, new global::System.ComponentModel.PropertyChangedEventArgs(propertyName));
                }
                public global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLinkViewModel VirtualLink
                {
                    get => virtualLink;
                    set
                    {
                       if(object.Equals(virtualLink,value)) return;
                       var old = virtualLink;
                       OnPropertyChanging(nameof(VirtualLink));
                       OnVirtualLinkChanging(old,value);
                       virtualLink = value;
                       OnVirtualLinkChanged(old,value);
                       OnPropertyChanged(nameof(VirtualLink));
                    }
                }
                partial void OnVirtualLinkChanging(global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLinkViewModel oldValue,global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLinkViewModel newValue);
                partial void OnVirtualLinkChanged(global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLinkViewModel oldValue,global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLinkViewModel newValue);
                public global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNodeViewModel> Nodes
                {
                    get => nodes;
                    set
                    {
                       if(object.Equals(nodes,value)) return;
                       var old = nodes;
                       OnPropertyChanging(nameof(Nodes));
                       OnNodesChanging(old,value);
                       nodes = value;
                       OnNodesChanged(old,value);
                       OnPropertyChanged(nameof(Nodes));
                    }
                }
                partial void OnNodesChanging(global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNodeViewModel> oldValue,global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNodeViewModel> newValue);
                partial void OnNodesChanged(global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNodeViewModel> oldValue,global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNodeViewModel> newValue);
                public global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLinkViewModel> Links
                {
                    get => links;
                    set
                    {
                       if(object.Equals(links,value)) return;
                       var old = links;
                       OnPropertyChanging(nameof(Links));
                       OnLinksChanging(old,value);
                       links = value;
                       OnLinksChanged(old,value);
                       OnPropertyChanged(nameof(Links));
                    }
                }
                partial void OnLinksChanging(global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLinkViewModel> oldValue,global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLinkViewModel> newValue);
                partial void OnLinksChanged(global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLinkViewModel> oldValue,global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLinkViewModel> newValue);
                public global::System.Collections.Generic.Dictionary<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlotViewModel, global::System.Collections.Generic.Dictionary<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlotViewModel, global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLinkViewModel>> LinksMap
                {
                    get => linksMap;
                    set
                    {
                       if(object.Equals(linksMap,value)) return;
                       var old = linksMap;
                       OnPropertyChanging(nameof(LinksMap));
                       OnLinksMapChanging(old,value);
                       linksMap = value;
                       OnLinksMapChanged(old,value);
                       OnPropertyChanged(nameof(LinksMap));
                    }
                }
                partial void OnLinksMapChanging(global::System.Collections.Generic.Dictionary<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlotViewModel, global::System.Collections.Generic.Dictionary<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlotViewModel, global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLinkViewModel>> oldValue,global::System.Collections.Generic.Dictionary<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlotViewModel, global::System.Collections.Generic.Dictionary<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlotViewModel, global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLinkViewModel>> newValue);
                partial void OnLinksMapChanged(global::System.Collections.Generic.Dictionary<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlotViewModel, global::System.Collections.Generic.Dictionary<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlotViewModel, global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLinkViewModel>> oldValue,global::System.Collections.Generic.Dictionary<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlotViewModel, global::System.Collections.Generic.Dictionary<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlotViewModel, global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLinkViewModel>> newValue);

                private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_CreateNodeCommand = null;
                public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand CreateNodeCommand
                {
                   get
                   {
                      _buffer_CreateNodeCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                          executeAsync: CreateNode,
                          canExecute: _ => true);
                      return _buffer_CreateNodeCommand;
                   }
                }
                private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_SetPointerCommand = null;
                public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand SetPointerCommand
                {
                   get
                   {
                      _buffer_SetPointerCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                          executeAsync: SetPointer,
                          canExecute: _ => true);
                      return _buffer_SetPointerCommand;
                   }
                }
                private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_ResetVirtualLinkCommand = null;
                public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand ResetVirtualLinkCommand
                {
                   get
                   {
                      _buffer_ResetVirtualLinkCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                          executeAsync: ResetVirtualLink,
                          canExecute: _ => true);
                      return _buffer_ResetVirtualLinkCommand;
                   }
                }
                private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_ApplyConnectionCommand = null;
                public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand ApplyConnectionCommand
                {
                   get
                   {
                      _buffer_ApplyConnectionCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                          executeAsync: ApplyConnection,
                          canExecute: _ => true);
                      return _buffer_ApplyConnectionCommand;
                   }
                }
                private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_ReceiveConnectionCommand = null;
                public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand ReceiveConnectionCommand
                {
                   get
                   {
                      _buffer_ReceiveConnectionCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                          executeAsync: ReceiveConnection,
                          canExecute: _ => true);
                      return _buffer_ReceiveConnectionCommand;
                   }
                }
                private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_SubmitCommand = null;
                public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand SubmitCommand
                {
                   get
                   {
                      _buffer_SubmitCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                          executeAsync: Submit,
                          canExecute: _ => true);
                      return _buffer_SubmitCommand;
                   }
                }
                private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_RedoCommand = null;
                public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand RedoCommand
                {
                   get
                   {
                      _buffer_RedoCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                          executeAsync: Redo,
                          canExecute: _ => true);
                      return _buffer_RedoCommand;
                   }
                }
                private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_UndoCommand = null;
                public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand UndoCommand
                {
                   get
                   {
                      _buffer_UndoCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                          executeAsync: Undo,
                          canExecute: _ => true);
                      return _buffer_UndoCommand;
                   }
                }
                private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_CloseCommand = null;
                public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand CloseCommand
                {
                   get
                   {
                      _buffer_CloseCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
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

                private IWorkflowTreeViewModel? parent = null;
                private Anchor anchor = new();
                private Size size = new();
                private ObservableCollection<IWorkflowSlotViewModel> slots = [];

                protected virtual Task Move(object? parameter, CancellationToken ct)
                {
                    if (parameter is not Offset offset) return Task.CompletedTask;
                    Helper.Move(offset);
                    return Task.CompletedTask;
                }
                protected virtual Task SaveAnchor(object? parameter, CancellationToken ct)
                {
                    Helper.SaveAnchor();
                    return Task.CompletedTask;
                }
                protected virtual Task SaveSize(object? parameter, CancellationToken ct)
                {
                    Helper.SaveSize();
                    return Task.CompletedTask;
                }
                protected virtual Task SetAnchor(object? parameter, CancellationToken ct)
                {
                    if (parameter is not Anchor anchor) return Task.CompletedTask;
                    Helper.SetAnchor(anchor);
                    return Task.CompletedTask;
                }
                protected virtual Task SetSize(object? parameter, CancellationToken ct)
                {
                    if (parameter is not Size scale) return Task.CompletedTask;
                    Helper.SetSize(scale);
                    return Task.CompletedTask;
                }
                protected virtual Task CreateSlot(object? parameter, CancellationToken ct)
                {
                    if (parameter is not IWorkflowSlotViewModel slot) return Task.CompletedTask;
                    Helper.CreateSlot(slot);
                    return Task.CompletedTask;
                }
                protected virtual Task Delete(object? parameter, CancellationToken ct)
                {
                    Helper.Delete();
                    return Task.CompletedTask;
                }
                protected virtual Task Work(object? parameter, CancellationToken ct)
                {
                    return Task.CompletedTask;
                }
                protected virtual Task Broadcast(object? parameter, CancellationToken ct)
                {
                    return Task.CompletedTask;
                }
                protected virtual async Task Close(object? parameter, CancellationToken ct)
                {
                    await Helper.CloseAsync();
                }

                public virtual IWorkflowNodeViewModelHelper GetHelper() => Helper;
                public virtual void InitializeWorkflow() => Helper.Initialize(this);
                public virtual void SetHelper(IWorkflowNodeViewModelHelper helper)
                {
                    helper.Initialize(this);
                    Helper = helper;
                }

                public event global::System.ComponentModel.PropertyChangingEventHandler? PropertyChanging;
                public event global::System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
                public void OnPropertyChanging(string propertyName)
                {
                    PropertyChanging?.Invoke(this, new global::System.ComponentModel.PropertyChangingEventArgs(propertyName));
                }
                public void OnPropertyChanged(string propertyName)
                {
                    PropertyChanged?.Invoke(this, new global::System.ComponentModel.PropertyChangedEventArgs(propertyName));
                }
                public global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowTreeViewModel Parent
                {
                    get => parent;
                    set
                    {
                       if(object.Equals(parent,value)) return;
                       var old = parent;
                       OnPropertyChanging(nameof(Parent));
                       OnParentChanging(old,value);
                       parent = value;
                       OnParentChanged(old,value);
                       OnPropertyChanged(nameof(Parent));
                    }
                }
                partial void OnParentChanging(global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowTreeViewModel oldValue,global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowTreeViewModel newValue);
                partial void OnParentChanged(global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowTreeViewModel oldValue,global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowTreeViewModel newValue);
                public global::VeloxDev.Core.WorkflowSystem.Anchor Anchor
                {
                    get => anchor;
                    set
                    {
                       if(object.Equals(anchor,value)) return;
                       var old = anchor;
                       OnPropertyChanging(nameof(Anchor));
                       OnAnchorChanging(old,value);
                       anchor = value;
                       OnAnchorChanged(old,value);
                       OnPropertyChanged(nameof(Anchor));
                    }
                }
                partial void OnAnchorChanging(global::VeloxDev.Core.WorkflowSystem.Anchor oldValue,global::VeloxDev.Core.WorkflowSystem.Anchor newValue);
                partial void OnAnchorChanged(global::VeloxDev.Core.WorkflowSystem.Anchor oldValue,global::VeloxDev.Core.WorkflowSystem.Anchor newValue);
                public global::VeloxDev.Core.WorkflowSystem.Size Size
                {
                    get => size;
                    set
                    {
                       if(object.Equals(size,value)) return;
                       var old = size;
                       OnPropertyChanging(nameof(Size));
                       OnSizeChanging(old,value);
                       size = value;
                       OnSizeChanged(old,value);
                       OnPropertyChanged(nameof(Size));
                    }
                }
                partial void OnSizeChanging(global::VeloxDev.Core.WorkflowSystem.Size oldValue,global::VeloxDev.Core.WorkflowSystem.Size newValue);
                partial void OnSizeChanged(global::VeloxDev.Core.WorkflowSystem.Size oldValue,global::VeloxDev.Core.WorkflowSystem.Size newValue);
                public global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlotViewModel> Slots
                {
                    get => slots;
                    set
                    {
                       if(object.Equals(slots,value)) return;
                       var old = slots;
                       OnPropertyChanging(nameof(Slots));
                       OnSlotsChanging(old,value);
                       slots = value;
                       OnSlotsChanged(old,value);
                       OnPropertyChanged(nameof(Slots));
                    }
                }
                partial void OnSlotsChanging(global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlotViewModel> oldValue,global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlotViewModel> newValue);
                partial void OnSlotsChanged(global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlotViewModel> oldValue,global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlotViewModel> newValue);

                private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_MoveCommand = null;
                public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand MoveCommand
                {
                   get
                   {
                      _buffer_MoveCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                          executeAsync: Move,
                          canExecute: _ => true);
                      return _buffer_MoveCommand;
                   }
                }
                private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_SaveAnchorCommand = null;
                public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand SaveAnchorCommand
                {
                   get
                   {
                      _buffer_SaveAnchorCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                          executeAsync: SaveAnchor,
                          canExecute: _ => true);
                      return _buffer_SaveAnchorCommand;
                   }
                }
                private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_SaveSizeCommand = null;
                public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand SaveSizeCommand
                {
                   get
                   {
                      _buffer_SaveSizeCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                          executeAsync: SaveSize,
                          canExecute: _ => true);
                      return _buffer_SaveSizeCommand;
                   }
                }
                private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_SetAnchorCommand = null;
                public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand SetAnchorCommand
                {
                   get
                   {
                      _buffer_SetAnchorCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                          executeAsync: SetAnchor,
                          canExecute: _ => true);
                      return _buffer_SetAnchorCommand;
                   }
                }
                private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_SetSizeCommand = null;
                public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand SetSizeCommand
                {
                   get
                   {
                      _buffer_SetSizeCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                          executeAsync: SetSize,
                          canExecute: _ => true);
                      return _buffer_SetSizeCommand;
                   }
                }
                private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_CreateSlotCommand = null;
                public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand CreateSlotCommand
                {
                   get
                   {
                      _buffer_CreateSlotCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                          executeAsync: CreateSlot,
                          canExecute: _ => true);
                      return _buffer_CreateSlotCommand;
                   }
                }
                private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_DeleteCommand = null;
                public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand DeleteCommand
                {
                   get
                   {
                      _buffer_DeleteCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                          executeAsync: Delete,
                          canExecute: _ => true);
                      return _buffer_DeleteCommand;
                   }
                }
            """);
            if (model.WorkSemaphore > 1)
            {
                sb.AppendLine($$"""
                private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_WorkCommand = null;
                public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand WorkCommand
                {
                   get
                   {
                      _buffer_WorkCommand ??= new global::VeloxDev.Core.MVVM.ConcurrentVeloxCommand(
                          executeAsync: Work,
                          canExecute: _ => true,
                          semaphore = {{model.WorkSemaphore}});
                      return _buffer_WorkCommand;
                   }
                }
            """);
            }
            else
            {
                sb.AppendLine($$"""
                private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_WorkCommand = null;
                public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand WorkCommand
                {
                   get
                   {
                      _buffer_WorkCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                          executeAsync: Work,
                          canExecute: _ => true);
                      return _buffer_WorkCommand;
                   }
                }
            """);
            }
            sb.AppendLine($$"""
                private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_BroadcastCommand = null;
                public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand BroadcastCommand
                {
                   get
                   {
                      _buffer_BroadcastCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                          executeAsync: Broadcast,
                          canExecute: _ => true);
                      return _buffer_BroadcastCommand;
                   }
                }
                private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_CloseCommand = null;
                public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand CloseCommand
                {
                   get
                   {
                      _buffer_CloseCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
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

                private HashSet<IWorkflowSlotViewModel> targets = [];
                private HashSet<IWorkflowSlotViewModel> sources = [];
                private IWorkflowNodeViewModel? parent = null;
                private SlotChannel channel = SlotChannel.OneBoth;
                private SlotState state = SlotState.StandBy;
                private Anchor anchor = new();
                private Offset offset = new();
                private Size size = new();

                protected virtual Task SaveOffset(object? parameter, CancellationToken ct)
                {
                    Helper.SaveOffset();
                    return Task.CompletedTask;
                }
                protected virtual Task SaveSize(object? parameter, CancellationToken ct)
                {
                    Helper.SaveSize();
                    return Task.CompletedTask;
                }
                protected virtual Task SetOffset(object? parameter, CancellationToken ct)
                {
                    if (parameter is not Offset offset) return Task.CompletedTask;
                    Helper.SetOffset(offset);
                    return Task.CompletedTask;
                }
                protected virtual Task SetSize(object? parameter, CancellationToken ct)
                {
                    if (parameter is not Size scale) return Task.CompletedTask;
                    Helper.SetSize(scale);
                    return Task.CompletedTask;
                }
                protected virtual Task SetChannel(object? parameter, CancellationToken ct)
                {
                    if (parameter is not SlotChannel slotChannel) return Task.CompletedTask;
                    Helper.SetChannel(slotChannel);
                    return Task.CompletedTask;
                }
                protected virtual Task ApplyConnection(object? parameter, CancellationToken ct)
                {
                    Helper.ApplyConnection();
                    return Task.CompletedTask;
                }
                protected virtual Task ReceiveConnection(object? parameter, CancellationToken ct)
                {
                    Helper.ReceiveConnection();
                    return Task.CompletedTask;
                }
                protected virtual Task Delete(object? parameter, CancellationToken ct)
                {
                    Helper.Delete();
                    return Task.CompletedTask;
                }
                protected virtual async Task Close(object? parameter, CancellationToken ct)
                {
                    await Helper.CloseAsync();
                }

                public virtual IWorkflowSlotViewModelHelper GetHelper() => Helper;
                public virtual void InitializeWorkflow() => Helper.Initialize(this);
                public virtual void SetHelper(IWorkflowSlotViewModelHelper helper)
                {
                    helper.Initialize(this);
                    Helper = helper;
                }

                public event global::System.ComponentModel.PropertyChangingEventHandler? PropertyChanging;
                public event global::System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
                public void OnPropertyChanging(string propertyName)
                {
                    PropertyChanging?.Invoke(this, new global::System.ComponentModel.PropertyChangingEventArgs(propertyName));
                }
                public void OnPropertyChanged(string propertyName)
                {
                    PropertyChanged?.Invoke(this, new global::System.ComponentModel.PropertyChangedEventArgs(propertyName));
                }
                public global::System.Collections.Generic.HashSet<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlotViewModel> Targets
                {
                    get => targets;
                    set
                    {
                       if(object.Equals(targets,value)) return;
                       var old = targets;
                       OnPropertyChanging(nameof(Targets));
                       OnTargetsChanging(old,value);
                       targets = value;
                       OnTargetsChanged(old,value);
                       OnPropertyChanged(nameof(Targets));
                    }
                }
                partial void OnTargetsChanging(global::System.Collections.Generic.HashSet<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlotViewModel> oldValue,global::System.Collections.Generic.HashSet<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlotViewModel> newValue);
                partial void OnTargetsChanged(global::System.Collections.Generic.HashSet<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlotViewModel> oldValue,global::System.Collections.Generic.HashSet<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlotViewModel> newValue);
                public global::System.Collections.Generic.HashSet<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlotViewModel> Sources
                {
                    get => sources;
                    set
                    {
                       if(object.Equals(sources,value)) return;
                       var old = sources;
                       OnPropertyChanging(nameof(Sources));
                       OnSourcesChanging(old,value);
                       sources = value;
                       OnSourcesChanged(old,value);
                       OnPropertyChanged(nameof(Sources));
                    }
                }
                partial void OnSourcesChanging(global::System.Collections.Generic.HashSet<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlotViewModel> oldValue,global::System.Collections.Generic.HashSet<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlotViewModel> newValue);
                partial void OnSourcesChanged(global::System.Collections.Generic.HashSet<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlotViewModel> oldValue,global::System.Collections.Generic.HashSet<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlotViewModel> newValue);
                public global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNodeViewModel Parent
                {
                    get => parent;
                    set
                    {
                       if(object.Equals(parent,value)) return;
                       var old = parent;
                       OnPropertyChanging(nameof(Parent));
                       OnParentChanging(old,value);
                       parent = value;
                       OnParentChanged(old,value);
                       OnPropertyChanged(nameof(Parent));
                    }
                }
                partial void OnParentChanging(global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNodeViewModel oldValue,global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNodeViewModel newValue);
                partial void OnParentChanged(global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNodeViewModel oldValue,global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNodeViewModel newValue);
                public global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotChannel Channel
                {
                    get => channel;
                    set
                    {
                       if(object.Equals(channel,value)) return;
                       var old = channel;
                       OnPropertyChanging(nameof(Channel));
                       OnChannelChanging(old,value);
                       channel = value;
                       OnChannelChanged(old,value);
                       OnPropertyChanged(nameof(Channel));
                    }
                }
                partial void OnChannelChanging(global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotChannel oldValue,global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotChannel newValue);
                partial void OnChannelChanged(global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotChannel oldValue,global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotChannel newValue);
                public global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotState State
                {
                    get => state;
                    set
                    {
                       if(object.Equals(state,value)) return;
                       var old = state;
                       OnPropertyChanging(nameof(State));
                       OnStateChanging(old,value);
                       state = value;
                       OnStateChanged(old,value);
                       OnPropertyChanged(nameof(State));
                    }
                }
                partial void OnStateChanging(global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotState oldValue,global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotState newValue);
                partial void OnStateChanged(global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotState oldValue,global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotState newValue);
                public global::VeloxDev.Core.WorkflowSystem.Anchor Anchor
                {
                    get => anchor;
                    set
                    {
                       if(object.Equals(anchor,value)) return;
                       var old = anchor;
                       OnPropertyChanging(nameof(Anchor));
                       OnAnchorChanging(old,value);
                       anchor = value;
                       OnAnchorChanged(old,value);
                       OnPropertyChanged(nameof(Anchor));
                    }
                }
                partial void OnAnchorChanging(global::VeloxDev.Core.WorkflowSystem.Anchor oldValue,global::VeloxDev.Core.WorkflowSystem.Anchor newValue);
                partial void OnAnchorChanged(global::VeloxDev.Core.WorkflowSystem.Anchor oldValue,global::VeloxDev.Core.WorkflowSystem.Anchor newValue);
                public global::VeloxDev.Core.WorkflowSystem.Offset Offset
                {
                    get => offset;
                    set
                    {
                       if(object.Equals(offset,value)) return;
                       var old = offset;
                       OnPropertyChanging(nameof(Offset));
                       OnOffsetChanging(old,value);
                       offset = value;
                       OnOffsetChanged(old,value);
                       OnPropertyChanged(nameof(Offset));
                    }
                }
                partial void OnOffsetChanging(global::VeloxDev.Core.WorkflowSystem.Offset oldValue,global::VeloxDev.Core.WorkflowSystem.Offset newValue);
                partial void OnOffsetChanged(global::VeloxDev.Core.WorkflowSystem.Offset oldValue,global::VeloxDev.Core.WorkflowSystem.Offset newValue);
                public global::VeloxDev.Core.WorkflowSystem.Size Size
                {
                    get => size;
                    set
                    {
                       if(object.Equals(size,value)) return;
                       var old = size;
                       OnPropertyChanging(nameof(Size));
                       OnSizeChanging(old,value);
                       size = value;
                       OnSizeChanged(old,value);
                       OnPropertyChanged(nameof(Size));
                    }
                }
                partial void OnSizeChanging(global::VeloxDev.Core.WorkflowSystem.Size oldValue,global::VeloxDev.Core.WorkflowSystem.Size newValue);
                partial void OnSizeChanged(global::VeloxDev.Core.WorkflowSystem.Size oldValue,global::VeloxDev.Core.WorkflowSystem.Size newValue);

                private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_SaveOffsetCommand = null;
                public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand SaveOffsetCommand
                {
                   get
                   {
                      _buffer_SaveOffsetCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                          executeAsync: SaveOffset,
                          canExecute: _ => true);
                      return _buffer_SaveOffsetCommand;
                   }
                }
                private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_SaveSizeCommand = null;
                public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand SaveSizeCommand
                {
                   get
                   {
                      _buffer_SaveSizeCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                          executeAsync: SaveSize,
                          canExecute: _ => true);
                      return _buffer_SaveSizeCommand;
                   }
                }
                private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_SetOffsetCommand = null;
                public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand SetOffsetCommand
                {
                   get
                   {
                      _buffer_SetOffsetCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                          executeAsync: SetOffset,
                          canExecute: _ => true);
                      return _buffer_SetOffsetCommand;
                   }
                }
                private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_SetSizeCommand = null;
                public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand SetSizeCommand
                {
                   get
                   {
                      _buffer_SetSizeCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                          executeAsync: SetSize,
                          canExecute: _ => true);
                      return _buffer_SetSizeCommand;
                   }
                }
                private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_SetChannelCommand = null;
                public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand SetChannelCommand
                {
                   get
                   {
                      _buffer_SetChannelCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                          executeAsync: SetChannel,
                          canExecute: _ => true);
                      return _buffer_SetChannelCommand;
                   }
                }
                private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_ApplyConnectionCommand = null;
                public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand ApplyConnectionCommand
                {
                   get
                   {
                      _buffer_ApplyConnectionCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                          executeAsync: ApplyConnection,
                          canExecute: _ => true);
                      return _buffer_ApplyConnectionCommand;
                   }
                }
                private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_ReceiveConnectionCommand = null;
                public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand ReceiveConnectionCommand
                {
                   get
                   {
                      _buffer_ReceiveConnectionCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                          executeAsync: ReceiveConnection,
                          canExecute: _ => true);
                      return _buffer_ReceiveConnectionCommand;
                   }
                }
                private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_DeleteCommand = null;
                public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand DeleteCommand
                {
                   get
                   {
                      _buffer_DeleteCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                          executeAsync: Delete,
                          canExecute: _ => true);
                      return _buffer_DeleteCommand;
                   }
                }
                private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_CloseCommand = null;
                public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand CloseCommand
                {
                   get
                   {
                      _buffer_CloseCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
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

                private IWorkflowSlotViewModel sender = new {{model.SlotType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}}();
                private IWorkflowSlotViewModel receiver = new {{model.SlotType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}}();
                private bool isVisible = false;

                protected virtual Task Delete(object? parameter, CancellationToken ct)
                {
                    Helper.Delete();
                    return Task.CompletedTask;
                }
                protected virtual async Task Close(object? parameter, CancellationToken ct)
                {
                    await Helper.CloseAsync();
                }

                public virtual IWorkflowLinkViewModelHelper GetHelper() => Helper;
                public virtual void InitializeWorkflow() => Helper.Initialize(this);
                public virtual void SetHelper(IWorkflowLinkViewModelHelper helper)
                {
                    helper.Initialize(this);
                    Helper = helper;
                }

                public event global::System.ComponentModel.PropertyChangingEventHandler? PropertyChanging;
                public event global::System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
                public void OnPropertyChanging(string propertyName)
                {
                    PropertyChanging?.Invoke(this, new global::System.ComponentModel.PropertyChangingEventArgs(propertyName));
                }
                public void OnPropertyChanged(string propertyName)
                {
                    PropertyChanged?.Invoke(this, new global::System.ComponentModel.PropertyChangedEventArgs(propertyName));
                }
                public global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlotViewModel Sender
                {
                    get => sender;
                    set
                    {
                       if(object.Equals(sender,value)) return;
                       var old = sender;
                       OnPropertyChanging(nameof(Sender));
                       OnSenderChanging(old,value);
                       sender = value;
                       OnSenderChanged(old,value);
                       OnPropertyChanged(nameof(Sender));
                    }
                }
                partial void OnSenderChanging(global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlotViewModel oldValue,global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlotViewModel newValue);
                partial void OnSenderChanged(global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlotViewModel oldValue,global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlotViewModel newValue);
                public global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlotViewModel Receiver
                {
                    get => receiver;
                    set
                    {
                       if(object.Equals(receiver,value)) return;
                       var old = receiver;
                       OnPropertyChanging(nameof(Receiver));
                       OnReceiverChanging(old,value);
                       receiver = value;
                       OnReceiverChanged(old,value);
                       OnPropertyChanged(nameof(Receiver));
                    }
                }
                partial void OnReceiverChanging(global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlotViewModel oldValue,global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlotViewModel newValue);
                partial void OnReceiverChanged(global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlotViewModel oldValue,global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlotViewModel newValue);
                public bool IsVisible
                {
                    get => isVisible;
                    set
                    {
                       if(object.Equals(isVisible,value)) return;
                       var old = isVisible;
                       OnPropertyChanging(nameof(IsVisible));
                       OnIsVisibleChanging(old,value);
                       isVisible = value;
                       OnIsVisibleChanged(old,value);
                       OnPropertyChanged(nameof(IsVisible));
                    }
                }
                partial void OnIsVisibleChanging(bool oldValue,bool newValue);
                partial void OnIsVisibleChanged(bool oldValue,bool newValue);

                private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_DeleteCommand = null;
                public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand DeleteCommand
                {
                   get
                   {
                      _buffer_DeleteCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                          executeAsync: Delete,
                          canExecute: _ => true);
                      return _buffer_DeleteCommand;
                   }
                }
                private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_CloseCommand = null;
                public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand CloseCommand
                {
                   get
                   {
                      _buffer_CloseCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                          executeAsync: Close,
                          canExecute: _ => true);
                      return _buffer_CloseCommand;
                   }
                }
            """);
        }

        public override string[] GenerateBaseTypes() => [];

        #region 内部模型定义

        private abstract class WorkflowAttributeModel
        {
            public INamedTypeSymbol? AttributeSymbol { get; set; }
            public INamedTypeSymbol? TargetClassSymbol { get; set; }
            public ITypeSymbol? HelperType { get; set; }
            public AttributeData? AttributeData { get; set; }
            public abstract int WorkflowType { get; }
        }

        private class TreeAttributeModel : WorkflowAttributeModel
        {
            public INamedTypeSymbol? VirtualLinkType { get; set; }
            public INamedTypeSymbol? VirtualSlotType { get; set; }
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
            public INamedTypeSymbol? SlotType { get; set; }
            public override int WorkflowType => 4;
        }

        #endregion
    }
}