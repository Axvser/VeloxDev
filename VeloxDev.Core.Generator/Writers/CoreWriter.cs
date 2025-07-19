using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VeloxDev.Core.Generator.Base;

namespace VeloxDev.Core.Generator.Writers
{
    public class CoreWriter : WriterBase
    {
        public const string NAMESPACE_VELOX_MONO = "global::VeloxDev.Core.Mono";
        public const string NAMESPACE_VELOX_IMVVM = "global::VeloxDev.Core.Interfaces.MVVM";
        public const string NAMESPACE_VELOX_MVVM = "global::VeloxDev.Core.MVVM";
        public const string NAMESPACE_VELOX_AOP = "global::VeloxDev.Core.AopInterfaces";
        public const string NAMESPACE_VELOX_IWORKFLOW_VIEWMODEL = "global::VeloxDev.Core.Interfaces.WorkflowSystem.ViewModel";
        public const string NAMESPACE_VELOX_WORKFLOW = "global::VeloxDev.Core.WorkflowSystem";
        public const string NAMESPACE_SYSTEM_MVVM = "global::System.ComponentModel";

        public bool IsMVVM { get; set; } = false;
        public bool IsAop { get; set; } = false;
        public bool IsMono { get; set; } = false;
        public bool IsWorkflowContext { get; set; } = false;
        public bool IsWorkflowContextTree { get; set; } = false;
        public int MonoSpan { get; set; } = 17;
        List<Tuple<string, bool, bool, string>> CommandConfig { get; set; } = [];
        List<Analizer.MVVMPropertyFactory> MVVMProperties { get; set; } = [];

        public override void Initialize(ClassDeclarationSyntax classDeclaration, INamedTypeSymbol namedTypeSymbol)
        {
            base.Initialize(classDeclaration, namedTypeSymbol);
            ReadAopConfig(classDeclaration);
            ReadMonoConfig(namedTypeSymbol);
            ReadCommandConfig(namedTypeSymbol);
            ReadMVVMConfig(namedTypeSymbol);
        }

        private void ReadMVVMConfig(INamedTypeSymbol symbol)
        {
            IsWorkflowContext = symbol.GetAttributes()
                .Any(ad => ad.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == NAMESPACE_VELOX_WORKFLOW + ".Workflow.ContextAttribute"
                );
            IsWorkflowContextTree = symbol.GetAttributes()
                .Any(ad => ad.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == NAMESPACE_VELOX_WORKFLOW + ".Workflow.ContextTreeAttribute"
                );
            MVVMProperties = [.. symbol.GetMembers()
                .OfType<IFieldSymbol>()
                .Where(field => field.GetAttributes().Any(attr =>
                    attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == NAMESPACE_VELOX_MVVM + ".VeloxPropertyAttribute"))
                .Select(field => new Analizer.MVVMFieldAnalizer(field))
                .Select(analizer => new Analizer.MVVMPropertyFactory(analizer, "public", false)
                {
                    SetteringBody = [$"OnPropertyChanging(nameof({analizer.PropertyName}));"],
                    SetteredBody = [$"OnPropertyChanged(nameof({analizer.PropertyName}));"],
                })];
            IsMVVM = MVVMProperties.Count > 0 || IsWorkflowContext || IsWorkflowContextTree;
        }
        private void ReadAopConfig(ClassDeclarationSyntax classDeclaration)
        {
            IsAop = classDeclaration.Members
                    .OfType<MemberDeclarationSyntax>()
                    .Any(member => member.AttributeLists
                    .SelectMany(al => al.Attributes)
                    .Any(attr => attr.Name.ToString() == AnalizeHelper.NAME_ASPECTORIENTED));
        }
        private void ReadMonoConfig(INamedTypeSymbol symbol)
        {
            var attributeData = symbol.GetAttributes()
                .FirstOrDefault(ad =>
                    ad.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == NAMESPACE_VELOX_MONO + ".MonoBehaviourAttribute" &&
                    ad.ApplicationSyntaxReference?.GetSyntax() is AttributeSyntax attrSyntax &&
                    attrSyntax.Parent?.Parent is ClassDeclarationSyntax
                );

            IsMono = attributeData != null;
            if (IsMono)
            {
                var value = (int)attributeData!.ConstructorArguments[0].Value!;
                MonoSpan = (int)(1000d / value > 0 ? value : 1);
            }
        }
        private void ReadCommandConfig(INamedTypeSymbol symbol)
        {
            const string attributeFullName = $"{NAMESPACE_VELOX_MVVM}.VeloxCommandAttribute";
            var list = new List<Tuple<string, bool, bool, string>>();

            foreach (var methodSymbol in symbol.GetMembers().OfType<IMethodSymbol>())
            {
                // 1. 检查是否标记了 VeloxCommandAttribute
                var attribute = methodSymbol.GetAttributes().FirstOrDefault(attr =>
                    attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == attributeFullName
                );
                if (attribute == null) continue;

                // 2. 生成方法签名（包含参数类型）
                var signature = $"{methodSymbol.Name}({string.Join(",", methodSymbol.Parameters.Select(p => p.Type?.ToString()))})";

                // 4. 解析命令配置
                string commandName = "Auto"; // 默认值
                bool canValidate = false;
                bool canConcurrent = false;

                // 5. 处理命名参数（优先）
                foreach (var namedArg in attribute.NamedArguments)
                {
                    switch (namedArg.Key)
                    {
                        case "Name":
                            commandName = (string)(namedArg.Value.Value ?? "Auto");
                            break;
                        case "CanValidate":
                            canValidate = (bool)(namedArg.Value.Value ?? false);
                            break;
                        case "CanConcurrent":
                            canConcurrent = (bool)(namedArg.Value.Value ?? false);
                            break;
                    }
                }

                // 6. 处理位置参数（兼容旧写法）
                if (attribute.ConstructorArguments.Length >= 1)
                    commandName = (string)(attribute.ConstructorArguments[0].Value ?? "Auto");
                if (attribute.ConstructorArguments.Length >= 2)
                    canValidate = (bool)(attribute.ConstructorArguments[1].Value ?? false);
                if (attribute.ConstructorArguments.Length >= 3)
                    canConcurrent = (bool)(attribute.ConstructorArguments[2].Value ?? false);

                // 7. 处理"Auto"命名规则
                if (commandName == "Auto")
                {
                    string methodName = methodSymbol.Name;
                    commandName = methodName.EndsWith("Async")
                        ? methodName.Substring(0, methodName.Length - 5)  // 移除最后5个字符("Async")
                        : methodName;
                }

                // 8. 添加到配置列表
                list.Add(Tuple.Create(
                    commandName,
                    canValidate,
                    canConcurrent,
                    methodSymbol.Name
                ));
            }

            CommandConfig = list;
        }

        public override bool CanWrite()
        {
            return IsAop || IsMono || CommandConfig.Count > 0 || IsMVVM;
        }

        public override string GetFileName()
        {
            if (Syntax == null || Symbol == null)
            {
                return string.Empty;
            }
            return $"{Syntax.Identifier.Text}_{Symbol.ContainingNamespace.ToDisplayString().Replace('.', '_')}_VeloxCore.g.cs";
        }

        public override string Write()
        {
            StringBuilder builder = new();

            builder.AppendLine(GenerateHead());
            builder.AppendLine(GeneratePartial(GenerateBody()));

            return builder.ToString();
        }

        private string GeneratePartial(string body)
        {
            if (Syntax == null || Symbol == null)
            {
                return string.Empty;
            }
            StringBuilder sourceBuilder = new();
            string share = $"{Syntax.Modifiers} class {Syntax.Identifier.Text}";

            var list = new List<string>();

            if (IsAop)
            {
                list.Add($"{NAMESPACE_VELOX_AOP}.{Syntax.Identifier.Text}_{Symbol.ContainingNamespace.ToDisplayString().Replace('.', '_')}_Aop");
            }
            if (IsMVVM && !(IsWorkflowContext || IsWorkflowContextTree))
            {
                list.Add($"{NAMESPACE_SYSTEM_MVVM}.INotifyPropertyChanging");
                list.Add($"{NAMESPACE_SYSTEM_MVVM}.INotifyPropertyChanged");
            }
            if (IsWorkflowContextTree)
            {
                list.Add($"{NAMESPACE_VELOX_IWORKFLOW_VIEWMODEL}.IContextTree");
            }
            if (IsWorkflowContext)
            {
                list.Add($"{NAMESPACE_VELOX_IWORKFLOW_VIEWMODEL}.IContext");
            }
            if (list.Count > 0)
            {
                var result = string.Join(", ", list);
                var source = $$"""
                           {{share}} : {{result}}
                           {
                           """;
                sourceBuilder.AppendLine(source);
            }
            else
            {
                var source = $$"""
                           {{share}}
                           {
                           """;
                sourceBuilder.AppendLine(source);
            }

            sourceBuilder.AppendLine(body);
            sourceBuilder.AppendLine("}");

            return sourceBuilder.ToString();
        }

        private string GenerateBody()
        {
            if (Syntax == null || Symbol == null)
            {
                return string.Empty;
            }
            StringBuilder builder = new();
            var strAop = $"{NAMESPACE_VELOX_AOP}.{Syntax.Identifier.Text}_{Symbol.ContainingNamespace.ToDisplayString().Replace('.', '_')}_Aop";
            if (IsAop)
            {
                builder.AppendLine($$"""
                                        private {{strAop}}? _proxy = null;
                                        public {{strAop}} Proxy
                                        {
                                            get
                                            {
                                                if (_proxy == null)
                                                {
                                                    var newproxy = global::VeloxDev.Core.AspectOriented.ProxyEx.CreateProxy<{{strAop}}>(this);
                                                    _proxy = newproxy;
                                                    return newproxy;
                                                }
                                                return _proxy;
                                            }
                                        }
                                     """);
                builder.AppendLine();
            }
            if (IsMono)
            {
                builder.AppendLine($$"""
                       private global::System.Threading.CancellationTokenSource? cts_mono = null;

                       private bool _canmonobehaviour = false;
                       public bool CanMonoBehaviour
                       {
                           get => _canmonobehaviour;
                           set
                           {
                               if(_canmonobehaviour != value)
                               {
                                   _canmonobehaviour = value;
                                   if (value)
                                   {
                                       var monofunc = new global::System.Func<global::System.Threading.Tasks.Task>(async () =>
                                       {
                                           await _inner_Update();
                                       });
                                       monofunc?.Invoke();
                                   }
                                   else
                                   {
                                       _innerCleanMonoToken();
                                   }
                               }
                           }
                       }

                       private async global::System.Threading.Tasks.Task _inner_Update()
                       {
                           _innerCleanMonoToken();

                           var newmonocts = new global::System.Threading.CancellationTokenSource();
                           cts_mono = newmonocts;

                           try
                           {
                              if(CanMonoBehaviour) Start();

                              while (CanMonoBehaviour && !newmonocts.Token.IsCancellationRequested)
                              {
                                  Update();
                                  LateUpdate();
                                  await global::System.Threading.Tasks.Task.Delay({{MonoSpan}},newmonocts.Token);
                              }
                           }
                           catch (global::System.Exception ex)
                           {
                               global::System.Diagnostics.Debug.WriteLine(ex.Message);
                           }
                           finally
                           {
                               if (global::System.Threading.Interlocked.CompareExchange(ref cts_mono, null, newmonocts) == newmonocts) 
                               {
                                   newmonocts.Dispose();
                               }
                               ExitMonoBehaviour();
                           }
                       }

                       partial void Start();
                       partial void Update();
                       partial void LateUpdate();
                       partial void ExitMonoBehaviour();

                       private void _innerCleanMonoToken()
                       {
                           var oldCts = global::System.Threading.Interlocked.Exchange(ref cts_mono, null);
                           if (oldCts != null)
                           {
                               try { oldCts.Cancel(); } catch { }
                               oldCts.Dispose();
                           }
                       }

                    """);
            }
            if (IsMVVM)
            {
                builder.AppendLine($$"""
                       public event {{NAMESPACE_SYSTEM_MVVM}}.PropertyChangingEventHandler? PropertyChanging;
                       public event {{NAMESPACE_SYSTEM_MVVM}}.PropertyChangedEventHandler? PropertyChanged;
                       public void OnPropertyChanging(string propertyName)
                       {
                           PropertyChanging?.Invoke(this, new {{NAMESPACE_SYSTEM_MVVM}}.PropertyChangingEventArgs(propertyName));
                       }
                       public void OnPropertyChanged(string propertyName)
                       {
                           PropertyChanged?.Invoke(this, new {{NAMESPACE_SYSTEM_MVVM}}.PropertyChangedEventArgs(propertyName));
                       }
                    """);
            }
            if (CommandConfig.Count > 0)
            {
                builder.AppendLine(GenerateCommand());
            }
            if (IsMVVM)
            {
                builder.AppendLine(GenerateProperty());
            }
            if (IsWorkflowContext)
            {
                builder.AppendLine(GenerateWorkflowContext());
            }
            if (IsWorkflowContextTree)
            {
                builder.AppendLine(GenerateWorkflowContextTree());
            }
            return builder.ToString();
        }
        private string GenerateProperty()
        {
            StringBuilder builder = new();
            foreach (var factory in MVVMProperties)
            {
                builder.AppendLine(factory.Generate());
            }
            return builder.ToString();
        }
        private string GenerateCommand()
        {
            var builder = new StringBuilder();

            foreach (var config in CommandConfig)
            {
                if (config.Item3) // 是否并发
                {
                    if (config.Item2) // 是否验证
                    {
                        builder.AppendLine($$"""
                         public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand {{config.Item1}}Command => new {{NAMESPACE_VELOX_MVVM}}.ConcurrentVeloxCommand(
                         executeAsync: {{config.Item4}},
                         canExecute: CanExecute{{config.Item1}}Command);
                         private partial bool CanExecute{{config.Item1}}Command(object? parameter);
                     """);
                    }
                    else
                    {
                        builder.AppendLine($$"""
                         public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand {{config.Item1}}Command => new {{NAMESPACE_VELOX_MVVM}}.ConcurrentVeloxCommand(
                         executeAsync: {{config.Item4}},
                         canExecute: _ => true);
                     """);
                    }

                }
                else
                {
                    if (config.Item2)
                    {
                        builder.AppendLine($$"""
                         public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand {{config.Item1}}Command => new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                         executeAsync: {{config.Item4}},
                         canExecute: CanExecute{{config.Item1}}Command);
                         private partial bool CanExecute{{config.Item1}}Command(object? parameter);
                     """);
                    }
                    else
                    {
                        builder.AppendLine($$"""
                         public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand {{config.Item1}}Command => new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                         executeAsync: {{config.Item4}},
                         canExecute: _ => true);
                     """);
                    }
                }
            }

            return builder.ToString();
        }
        private string GenerateWorkflowContext()
        {
            return $$"""
                    private bool isEnabled = true;
                    public bool IsEnabled
                    {
                        get => isEnabled;
                        set
                        {
                            if (object.Equals(isEnabled, value)) return;
                            var old = isEnabled;
                            OnPropertyChanging(nameof(IsEnabled));
                            OnIsEnabledChanging(old, value);
                            isEnabled = value;
                            OnIsEnabledChanged(old, value);
                            OnPropertyChanged(nameof(IsEnabled));
                        }
                    }
                    partial void OnIsEnabledChanging(bool oldValue, bool newValue);
                    partial void OnIsEnabledChanged(bool oldValue, bool newValue);
                    private {{NAMESPACE_VELOX_WORKFLOW}}.Anchor anchor = {{NAMESPACE_VELOX_WORKFLOW}}.Anchor.Default;
                    public {{NAMESPACE_VELOX_WORKFLOW}}.Anchor Anchor
                    {
                        get => anchor;
                        set
                        {
                            if (object.Equals(anchor, value)) return;
                            var old = anchor;
                            OnPropertyChanging(nameof(Anchor));
                            OnAnchorChanging(old, value);
                            anchor = value;
                            OnAnchorChanged(old, value);
                            OnPropertyChanged(nameof(Anchor));
                        }
                    }
                    partial void OnAnchorChanging({{NAMESPACE_VELOX_WORKFLOW}}.Anchor oldValue, {{NAMESPACE_VELOX_WORKFLOW}}.Anchor newValue);
                    partial void OnAnchorChanged({{NAMESPACE_VELOX_WORKFLOW}}.Anchor oldValue, {{NAMESPACE_VELOX_WORKFLOW}}.Anchor newValue);
                    private {{NAMESPACE_VELOX_IWORKFLOW_VIEWMODEL}}.IContextTree? tree = null;
                    public {{NAMESPACE_VELOX_IWORKFLOW_VIEWMODEL}}.IContextTree Tree
                    {
                        get => tree;
                        set
                        {
                            if (object.Equals(tree, value)) return;
                            var old = tree;
                            OnPropertyChanging(nameof(Tree));
                            OnTreeChanging(old, value);
                            tree = value;
                            OnTreeChanged(old, value);
                            OnPropertyChanged(nameof(Tree));
                        }
                    }
                    partial void OnTreeChanging({{NAMESPACE_VELOX_IWORKFLOW_VIEWMODEL}}.IContextTree oldValue, {{NAMESPACE_VELOX_IWORKFLOW_VIEWMODEL}}.IContextTree newValue);
                    partial void OnTreeChanged({{NAMESPACE_VELOX_IWORKFLOW_VIEWMODEL}}.IContextTree oldValue, {{NAMESPACE_VELOX_IWORKFLOW_VIEWMODEL}}.IContextTree newValue);
                 """;
        }
        private string GenerateWorkflowContextTree()
        {
            return $$"""
                    private global::System.Collections.ObjectModel.ObservableCollection<{{NAMESPACE_VELOX_IWORKFLOW_VIEWMODEL}}.IContext> children = [];
                    public global::System.Collections.ObjectModel.ObservableCollection<{{NAMESPACE_VELOX_IWORKFLOW_VIEWMODEL}}.IContext> Children
                    {
                        get => children;
                        set
                        {
                            if (object.Equals(children, value)) return;
                            var old = children;
                            OnPropertyChanging(nameof(Children));
                            OnChildrenChanging(old, value);
                            children = value;
                            OnChildrenChanged(old, value);
                            OnPropertyChanged(nameof(Children));
                        }
                    }
                    partial void OnChildrenChanging(global::System.Collections.ObjectModel.ObservableCollection<{{NAMESPACE_VELOX_IWORKFLOW_VIEWMODEL}}.IContext> oldValue, global::System.Collections.ObjectModel.ObservableCollection<{{NAMESPACE_VELOX_IWORKFLOW_VIEWMODEL}}.IContext> newValue);
                    partial void OnChildrenChanged(global::System.Collections.ObjectModel.ObservableCollection<{{NAMESPACE_VELOX_IWORKFLOW_VIEWMODEL}}.IContext> oldValue, global::System.Collections.ObjectModel.ObservableCollection<{{NAMESPACE_VELOX_IWORKFLOW_VIEWMODEL}}.IContext> newValue);

                    private global::System.Collections.ObjectModel.ObservableCollection<{{NAMESPACE_VELOX_IWORKFLOW_VIEWMODEL}}.IContextConnector> connectors = [];
                    public global::System.Collections.ObjectModel.ObservableCollection<{{NAMESPACE_VELOX_IWORKFLOW_VIEWMODEL}}.IContextConnector> Connectors
                    {
                        get => connectors;
                        set
                        {
                            if (object.Equals(connectors, value)) return;
                            var old = connectors;
                            OnPropertyChanging(nameof(Connectors));
                            OnConnectorsChanging(old, value);
                            connectors = value;
                            OnConnectorsChanged(old, value);
                            OnPropertyChanged(nameof(Connectors));
                        }
                    }
                    partial void OnConnectorsChanging(global::System.Collections.ObjectModel.ObservableCollection<{{NAMESPACE_VELOX_IWORKFLOW_VIEWMODEL}}.IContextConnector> oldValue, global::System.Collections.ObjectModel.ObservableCollection<{{NAMESPACE_VELOX_IWORKFLOW_VIEWMODEL}}.IContextConnector> newValue);
                    partial void OnConnectorsChanged(global::System.Collections.ObjectModel.ObservableCollection<{{NAMESPACE_VELOX_IWORKFLOW_VIEWMODEL}}.IContextConnector> oldValue, global::System.Collections.ObjectModel.ObservableCollection<{{NAMESPACE_VELOX_IWORKFLOW_VIEWMODEL}}.IContextConnector> newValue);
                 """;
        }
    }
}