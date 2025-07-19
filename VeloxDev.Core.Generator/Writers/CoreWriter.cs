using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace VeloxDev.Core.Generator.Writers
{
    public class CoreWriter : WriterBase
    {
        public const string NAMESPACE_VELOX_MONO = "global::VeloxDev.Core.Mono.MonoBehaviourAttribute";
        public const string NAMESPACE_VELOX_IMVVM = "global::VeloxDev.Core.Interfaces.MVVM";
        public const string NAMESPACE_VELOX_MVVM = "global::VeloxDev.Core.MVVM";
        public const string NAMESPACE_VELOX_AOP = "global::VeloxDev.Core.AopInterfaces";
        public const string NAMESPACE_VELOX_WORKFLOW = "global::VeloxDev.Core.WorkflowSystem";

        public bool IsAop { get; set; } = false;
        public bool IsMono { get; set; } = false;
        public int MonoSpan { get; set; } = 17;
        List<Tuple<string, bool, bool, string>> CommandConfig { get; set; } = [];

        public override void Initialize(ClassDeclarationSyntax classDeclaration, INamedTypeSymbol namedTypeSymbol)
        {
            base.Initialize(classDeclaration, namedTypeSymbol);
            ReadAopConfig(classDeclaration);
            ReadMonoConfig(namedTypeSymbol);
            ReadCommandConfig(namedTypeSymbol);
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
            // 只处理直接标记的特性（通过语法树检查）
            var attributeData = symbol.GetAttributes()
                .FirstOrDefault(ad =>
                    ad.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == NAMESPACE_VELOX_MONO &&
                    ad.ApplicationSyntaxReference?.GetSyntax() is AttributeSyntax attrSyntax && // 确保是语法节点
                    attrSyntax.Parent?.Parent is ClassDeclarationSyntax // 确保是直接标记在类上
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
            return IsAop || IsMono || CommandConfig.Count > 0;
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
            if(CommandConfig.Count > 0)
            {
                builder.AppendLine(GenerateCommand());
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
    }
}