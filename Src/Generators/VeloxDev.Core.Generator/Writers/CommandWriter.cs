using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace VeloxDev.Core.Generator.Writers
{
    public class CommandWriter : WriterBase
    {
        List<Tuple<string, bool, int, string, int>> CommandConfig { get; set; } = [];

        public override void Initialize(ClassDeclarationSyntax classDeclaration, INamedTypeSymbol namedTypeSymbol)
        {
            base.Initialize(classDeclaration, namedTypeSymbol);
            ReadCommandConfig(namedTypeSymbol);
        }
        private void ReadCommandConfig(INamedTypeSymbol symbol)
        {
            const string attributeFullName = $"{NAMESPACE_VELOX_MVVM}.VeloxCommandAttribute";
            var list = new List<Tuple<string, bool, int, string, int>>();

            foreach (var methodSymbol in symbol.GetMembers().OfType<IMethodSymbol>())
            {
                // 仅检查是否标记了 VeloxCommandAttribute
                var attribute = methodSymbol.GetAttributes()
                    .FirstOrDefault(attr =>
                        attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == attributeFullName);

                if (attribute == null) continue;

                // 解析配置：先位置参数，后命名参数覆盖
                string commandName = "Auto";
                bool canValidate = false;
                int semaphore = 1;

                // 位置参数（按顺序）
                if (attribute.ConstructorArguments.Length >= 1 && attribute.ConstructorArguments[0].Value is string nameArg)
                    commandName = nameArg;
                if (attribute.ConstructorArguments.Length >= 2 && attribute.ConstructorArguments[1].Value is bool canValArg)
                    canValidate = canValArg;
                if (attribute.ConstructorArguments.Length >= 3 && attribute.ConstructorArguments[2].Value is int semaArg)
                    semaphore = semaArg;

                // 命名参数（覆盖位置参数）
                foreach (var namedArg in attribute.NamedArguments)
                {
                    switch (namedArg.Key)
                    {
                        case "name" when namedArg.Value.Value is string s:
                            commandName = s;
                            break;
                        case "canValidate" when namedArg.Value.Value is bool b:
                            canValidate = b;
                            break;
                        case "semaphore" when namedArg.Value.Value is int i:
                            semaphore = i;
                            break;
                    }
                }

                // Auto 命名规则
                if (commandName == "Auto")
                {
                    commandName = methodSymbol.Name;
                }

                // 构造方式分析
                int constructorType = ParseConstructorType(methodSymbol);

                // 记录上下文
                list.Add(Tuple.Create(commandName, canValidate, Math.Max(1, semaphore), methodSymbol.Name, constructorType));
            }

            CommandConfig = list;
        }
        private int ParseConstructorType(IMethodSymbol methodSymbol)
        {
            var parameters = methodSymbol.Parameters;
            var returnType = methodSymbol.ReturnType;

            // 检查返回类型是否为 Task
            var taskSymbol = methodSymbol.ContainingAssembly.GetTypeByMetadataName("System.Threading.Tasks.Task");
            bool isTask = taskSymbol != null && SymbolEqualityComparer.Default.Equals(returnType, taskSymbol);

            if (isTask && parameters.Length == 1)
            {
                var paramType = parameters[0].Type;

                // 判断是否为 object（包括 object?）
                if (paramType.SpecialType == SpecialType.System_Object)
                {
                    return 1;
                }

                // 判断是否为 CancellationToken
                var cancellationTokenSymbol = methodSymbol.ContainingAssembly.GetTypeByMetadataName("System.Threading.CancellationToken");
                if (cancellationTokenSymbol != null && SymbolEqualityComparer.Default.Equals(paramType, cancellationTokenSymbol))
                {
                    return 2;
                }
            }

            return 0;
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
                string constructor = config.Item5 switch
                {
                    1 => $"{NAMESPACE_VELOX_MVVM}.VeloxCommand.CreateTaskOnlyWithParameter(",
                    2 => $"{NAMESPACE_VELOX_MVVM}.VeloxCommand.CreateTaskOnlyWithCancellationToken(",
                    _ => $"new {NAMESPACE_VELOX_MVVM}.VeloxCommand("
                };
                if (config.Item2)
                {
                    builder.AppendLine($$"""
                                                private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_{{config.Item1}}Command = null;
                                                public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand {{config.Item1}}Command
                                                {
                                                    get
                                                    {
                                                        _buffer_{{config.Item1}}Command ??= {{constructor}}
                                                            command: {{config.Item4}},
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
                                                        _buffer_{{config.Item1}}Command ??= {{constructor}}
                                                            command: {{config.Item4}},
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