using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace VeloxDev.Core.Generator.Writers
{
    public class CommandWriter : WriterBase
    {
        public List<CommandConfig> CommandConfigs { get; set; } = [];

        public override void Initialize(ClassDeclarationSyntax classDeclaration, INamedTypeSymbol namedTypeSymbol)
        {
            base.Initialize(classDeclaration, namedTypeSymbol);
            ReadCommandConfig(namedTypeSymbol);
        }

        private void ReadCommandConfig(INamedTypeSymbol symbol)
        {
            const string attributeFullName = $"{NAMESPACE_VELOX_MVVM}.VeloxCommandAttribute";
            var list = new List<CommandConfig>();

            foreach (var methodSymbol in symbol.GetMembers().OfType<IMethodSymbol>())
            {
                // 检查是否标记了 VeloxCommandAttribute
                var attribute = methodSymbol.GetAttributes().FirstOrDefault(attr =>
                    attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == attributeFullName
                );
                if (attribute == null) continue;

                // 生成方法签名
                var signature = $"{methodSymbol.Name}({string.Join(",", methodSymbol.Parameters.Select(p => p.Type?.ToString()))})";

                // 解析命令配置
                string commandName = "Auto";
                bool canValidate = false;
                int semaphore = 1;

                // 处理命名参数（优先）
                foreach (var namedArg in attribute.NamedArguments)
                {
                    switch (namedArg.Key)
                    {
                        case "name":
                            commandName = (string)(namedArg.Value.Value ?? "Auto");
                            break;
                        case "canValidate":
                            canValidate = (bool)(namedArg.Value.Value ?? false);
                            break;
                        case "semaphore":
                            semaphore = (int)(namedArg.Value.Value ?? 1);
                            break;
                    }
                }

                // 处理位置参数（兼容旧写法）
                if (attribute.ConstructorArguments.Length >= 1)
                    commandName = (string)(attribute.ConstructorArguments[0].Value ?? "Auto");
                if (attribute.ConstructorArguments.Length >= 2)
                    canValidate = (bool)(attribute.ConstructorArguments[1].Value ?? false);
                if (attribute.ConstructorArguments.Length >= 3)
                    semaphore = (int)(attribute.ConstructorArguments[2].Value ?? 1);
                if (semaphore <= 0)
                    semaphore = 1;

                // 处理"Auto"命名规则
                if (commandName == "Auto")
                {
                    string methodName = methodSymbol.Name;
                    commandName = methodName.EndsWith("Async")
                        ? methodName.Substring(0, methodName.Length - 5)
                        : methodName;
                }

                // 添加到配置列表
                list.Add(new CommandConfig
                {
                    CommandName = commandName,
                    CanValidate = canValidate,
                    Semaphore = semaphore,
                    MethodName = methodSymbol.Name,
                    MethodSignature = signature
                });
            }

            CommandConfigs = list;
        }

        public override bool CanWrite()
        {
            return CommandConfigs.Count > 0;
        }

        public override string GetFileName()
        {
            if (Syntax == null || Symbol == null)
            {
                return string.Empty;
            }

            return $"{Syntax.Identifier.Text}_{Symbol.ContainingNamespace.ToDisplayString().Replace('.', '_')}_VeloxCommand.g.cs";
        }

        public override string Write()
        {
            if (!CanWrite()) return string.Empty;

            StringBuilder builder = new();
            builder.AppendLine(GenerateHead());
            builder.AppendLine(GeneratePartialClass(GenerateCommandProperties()));

            return builder.ToString();
        }

        private string GeneratePartialClass(string body)
        {
            if (Syntax == null || Symbol == null)
            {
                return string.Empty;
            }

            StringBuilder sourceBuilder = new();
            string classDeclaration = $"{Syntax.Modifiers} class {Syntax.Identifier.Text}";

            var source = $$"""
                           {{classDeclaration}}
                           {
                           {{body}}
                           }
                           """;
            sourceBuilder.AppendLine(source);

            return sourceBuilder.ToString();
        }

        private string GenerateCommandProperties()
        {
            var builder = new StringBuilder();

            foreach (var config in CommandConfigs)
            {
                if (config.Semaphore > 1)
                {
                    if (config.CanValidate)
                    {
                        builder.AppendLine($$"""
                                                private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _{{config.CommandName.ToLower()}}Command = null;
                                                public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand {{config.CommandName}}Command
                                                {
                                                    get
                                                    {
                                                        _{{config.CommandName.ToLower()}}Command ??= new {{NAMESPACE_VELOX_MVVM}}.ConcurrentVeloxCommand(
                                                            executeAsync: {{config.MethodName}},
                                                            canExecute: CanExecute{{config.CommandName}}Command,
                                                            semaphore: {{config.Semaphore}});
                                                        return _{{config.CommandName.ToLower()}}Command;
                                                    }
                                                }
                                                private partial bool CanExecute{{config.CommandName}}Command(object? parameter);
                                             """);
                    }
                    else
                    {
                        builder.AppendLine($$"""
                                                private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _{{config.CommandName.ToLower()}}Command = null;
                                                public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand {{config.CommandName}}Command
                                                {
                                                    get
                                                    {
                                                        _{{config.CommandName.ToLower()}}Command ??= new {{NAMESPACE_VELOX_MVVM}}.ConcurrentVeloxCommand(
                                                            executeAsync: {{config.MethodName}},
                                                            canExecute: _ => true,
                                                            semaphore: {{config.Semaphore}});
                                                        return _{{config.CommandName.ToLower()}}Command;
                                                    }
                                                }
                                             """);
                    }
                }
                else
                {
                    if (config.CanValidate)
                    {
                        builder.AppendLine($$"""
                                                private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _{{config.CommandName.ToLower()}}Command = null;
                                                public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand {{config.CommandName}}Command
                                                {
                                                    get
                                                    {
                                                       _{{config.CommandName.ToLower()}}Command ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                                                           executeAsync: {{config.MethodName}},
                                                           canExecute: CanExecute{{config.CommandName}}Command);
                                                       return _{{config.CommandName.ToLower()}}Command;
                                                    }
                                                }
                                                private partial bool CanExecute{{config.CommandName}}Command(object? parameter);
                                             """);
                    }
                    else
                    {
                        builder.AppendLine($$"""
                                                private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _{{config.CommandName.ToLower()}}Command = null;
                                                public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand {{config.CommandName}}Command
                                                {
                                                   get
                                                   {
                                                      _{{config.CommandName.ToLower()}}Command ??= new {{NAMESPACE_VELOX_MVVM}}.VeloxCommand(
                                                          executeAsync: {{config.MethodName}},
                                                          canExecute: _ => true);
                                                      return _{{config.CommandName.ToLower()}}Command;
                                                   }
                                                }
                                             """);
                    }
                }
                builder.AppendLine();
            }

            return builder.ToString();
        }
    }

    public class CommandConfig
    {
        public string CommandName { get; set; } = string.Empty;
        public bool CanValidate { get; set; }
        public int Semaphore { get; set; } = 1;
        public string MethodName { get; set; } = string.Empty;
        public string MethodSignature { get; set; } = string.Empty;
    }
}