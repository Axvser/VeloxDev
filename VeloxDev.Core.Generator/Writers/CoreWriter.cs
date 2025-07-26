using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VeloxDev.Core.Generator.Base;
using VeloxDev.Core.Generator.Templates.Workflow;

namespace VeloxDev.Core.Generator.Writers
{
    public class CoreWriter : WriterBase
    {
        public const string NAMESPACE_VELOX_MONO = "global::VeloxDev.Core.Mono";
        public const string NAMESPACE_VELOX_IMVVM = "global::VeloxDev.Core.Interfaces.MVVM";
        public const string NAMESPACE_VELOX_MVVM = "global::VeloxDev.Core.MVVM";
        public const string NAMESPACE_VELOX_AOP = "global::VeloxDev.Core.AopInterfaces";
        public const string NAMESPACE_SYSTEM_MVVM = "global::System.ComponentModel";
        public const string NAMESPACE_VELOX_IWORKFLOW = "global::VeloxDev.Core.Interfaces.WorkflowSystem";
        public const string NAMESPACE_VELOX_WORKFLOW = "global::VeloxDev.Core.WorkflowSystem";

        public bool IsMVVM { get; set; } = false;
        public bool IsAop { get; set; } = false;
        public bool IsMono { get; set; } = false;
        public int MonoSpan { get; set; } = 17;
        public int WorkflowType { get; set; } = 0;
        List<Tuple<string, bool, bool, string>> CommandConfig { get; set; } = [];
        List<Analizer.MVVMPropertyFactory> MVVMProperties { get; set; } = [];

        public override void Initialize(ClassDeclarationSyntax classDeclaration, INamedTypeSymbol namedTypeSymbol)
        {
            base.Initialize(classDeclaration, namedTypeSymbol);
            ReadAopConfig(classDeclaration);
            ReadMonoConfig(namedTypeSymbol);
            ReadMVVMConfig(namedTypeSymbol);
            ReadCommandConfig(namedTypeSymbol);
            ReadWorkflowConfig(namedTypeSymbol);
        }

        private void ReadWorkflowConfig(INamedTypeSymbol symbol)
        {
            var configs = symbol.GetAttributes();
            var s1 = NAMESPACE_VELOX_WORKFLOW + "Workflow.Context.TreeAttribute";
            var s2 = NAMESPACE_VELOX_WORKFLOW + "Workflow.Context.NodeAttribute";
            var s3 = NAMESPACE_VELOX_WORKFLOW + "Workflow.Context.SlotAttribute";
            var s4 = NAMESPACE_VELOX_WORKFLOW + "Workflow.Context.LinkAttribute";
            foreach (var config in configs)
            {
                var name = config.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (name == s2)
                {
                    WorkflowType = 2;
                    return;
                }
                else if (name == s3)
                {
                    WorkflowType = 3;
                    return;
                }
                else if (name == s1)
                {
                    WorkflowType = 1;
                    return;
                }
                else if (name == s4)
                {
                    WorkflowType = 4;
                    return;
                }
                else
                {
                    WorkflowType = 0;
                    return;
                }
            }
        }
        private void ReadMVVMConfig(INamedTypeSymbol symbol)
        {
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
            IsMVVM = MVVMProperties.Count > 0 || WorkflowType != 0;
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
            if (IsMVVM)
            {
                list.Add($"{NAMESPACE_SYSTEM_MVVM}.INotifyPropertyChanging");
                list.Add($"{NAMESPACE_SYSTEM_MVVM}.INotifyPropertyChanged");
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
                builder.AppendLine(GenerateProperty());
            }
            if (WorkflowType != 0)
            {
                builder.AppendLine(GenerateWorkflow());
            }
            if (CommandConfig.Count > 0)
            {
                builder.AppendLine(GenerateCommand());
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
                if (config.Item3)
                {
                    if (config.Item2)
                    {
                        builder.AppendLine($$"""
                         private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_{{config.Item1}}Command = null;
                         public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand {{config.Item1}}Command
                         {
                             get
                             {
                                 _buffer_{{config.Item1}}Command ??= new {{NAMESPACE_VELOX_MVVM}}.ConcurrentVeloxCommand(
                                     executeAsync: {{config.Item4}},
                                     canExecute: CanExecute{{config.Item1}}Command);
                                 return _buffer_{{config.Item1}}Command;
                             }
                         }
                         private partial bool CanExecute{{config.Item1}}Command(object? parameter);
                      """);
                    }
                    else
                    {
                        builder.AppendLine($$"""
                         private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_{{config.Item1}}Command = null;
                         public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand {{config.Item1}}Command
                         {
                             get
                             {
                                 _buffer_{{config.Item1}}Command ??= new {{NAMESPACE_VELOX_MVVM}}.ConcurrentVeloxCommand(
                                     executeAsync: {{config.Item4}},
                                     canExecute: _ => true);
                                 return _buffer_{{config.Item1}}Command;
                             }
                         }
                      """);
                    }

                }
                else
                {
                    if (config.Item2)
                    {
                        builder.AppendLine($$"""
                         private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_{{config.Item1}}Command = null;
                         public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand {{config.Item1}}Command
                         {
                             get
                             {
                                _buffer_{{config.Item1}}Command ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                                    executeAsync: {{config.Item4}},
                                    canExecute: CanExecute{{config.Item1}}Command);
                                return _buffer_{{config.Item1}}Command;
                             }
                         }
                         private partial bool CanExecute{{config.Item1}}Command(object? parameter);
                      """);
                    }
                    else
                    {
                        builder.AppendLine($$"""
                         private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_{{config.Item1}}Command = null;
                         public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand {{config.Item1}}Command
                         {
                            get
                            {
                               _buffer_{{config.Item1}}Command ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                                   executeAsync: {{config.Item4}},
                                   canExecute: _ => true);
                               return _buffer_{{config.Item1}}Command;
                            }
                         }
                      """);
                    }
                }
            }

            return builder.ToString();
        }
        private string GenerateWorkflow() => WorkflowType switch
        {
            1 => TreeTemplate.Normal,
            2 => NodeTemplate.Normal,
            3 => SlotTemplate.Normal,
            4 => LinkTemplate.Normal,
            _ => string.Empty
        };
    }
}