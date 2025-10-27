using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VeloxDev.Core.Generator.Base;

namespace VeloxDev.Core.Generator.Writers
{
    public class CommandWriter : WriterBase
    {
        List<Tuple<string, bool, int, string>> CommandConfig { get; set; } = [];

        public override void Initialize(ClassDeclarationSyntax classDeclaration, INamedTypeSymbol namedTypeSymbol)
        {
            base.Initialize(classDeclaration, namedTypeSymbol);
            ReadCommandConfig(namedTypeSymbol);
        }
        private void ReadCommandConfig(INamedTypeSymbol symbol)
        {
            const string attributeFullName = $"{NAMESPACE_VELOX_MVVM}.VeloxCommandAttribute";
            var list = new List<Tuple<string, bool, int, string>>();

            foreach (var methodSymbol in symbol.GetMembers().OfType<IMethodSymbol>())
            {
                // 1. 检查是否标记了 VeloxCommandAttribute
                var attribute = methodSymbol.GetAttributes().FirstOrDefault(attr =>
                    attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == attributeFullName
                );
                if (attribute == null) continue;

                // 2. 生成方法签名（包含参数类型）
                var signature =
                    $"{methodSymbol.Name}({string.Join(",", methodSymbol.Parameters.Select(p => p.Type?.ToString()))})";

                // 4. 解析命令配置
                string commandName = "Auto"; // 默认值
                bool canValidate = false;
                int semaphore = 1;

                // 5. 处理命名参数（优先）
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
                            semaphore = (int)(namedArg.Value.Value ?? false);
                            break;
                    }
                }

                // 6. 处理位置参数（兼容旧写法）
                if (attribute.ConstructorArguments.Length >= 1)
                    commandName = (string)(attribute.ConstructorArguments[0].Value ?? "Auto");
                if (attribute.ConstructorArguments.Length >= 2)
                    canValidate = (bool)(attribute.ConstructorArguments[1].Value ?? false);
                if (attribute.ConstructorArguments.Length >= 3)
                    semaphore = (int)(attribute.ConstructorArguments[2].Value ?? false);

                // 7. 处理"Auto"命名规则
                if (commandName == "Auto")
                {
                    commandName = methodSymbol.Name;
                }

                // 8. 添加到配置列表
                list.Add(Tuple.Create(
                    commandName,
                    canValidate,
                    semaphore,
                    methodSymbol.Name
                ));
            }

            CommandConfig = list;
        }

        public override bool CanWrite() => CommandConfig.Count > 0;
        public override string[] GenerateBaseTypes() => [];
        public override string[] GenerateBaseInterfaces() => [];
        public override string GetFileName()
        {
            if (Syntax == null || Symbol == null)
            {
                return string.Empty;
            }

            return
                $"{Syntax.Identifier.Text}_{Symbol.ContainingNamespace.ToDisplayString().Replace('.', '_')}_Commands.g.cs";
        }

        public override string GenerateBody()
        {
            if (Syntax == null || Symbol == null || CommandConfig.Count < 1)
            {
                return string.Empty;
            }
            return GenerateCommand();
        }
        private string GenerateCommand()
        {
            var builder = new StringBuilder();

            foreach (var config in CommandConfig)
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
                                                            canExecute: CanExecute{{config.Item1}}Command,
                                                            semaphore: {{config.Item3}});
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
                                                            canExecute: _ => true,
                                                            semaphore: {{config.Item3}});
                                                        return _buffer_{{config.Item1}}Command;
                                                    }
                                                }
                                             """);
                }
            }

            return builder.ToString();
        }
    }
}