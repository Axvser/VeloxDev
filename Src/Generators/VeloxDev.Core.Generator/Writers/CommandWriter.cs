using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace VeloxDev.Generators.Writers
{
    public class CommandWriter : WriterBase
    {
        // 一条 [VeloxCommand] 的生成配置。
        // CommandExpression 是交给 `command:` 的表达式：要么是方法组名，要么是一段转换 thunk。
        // ValueTask 既不能隐式转成 Task，也不像 Task<T> 那样能靠委托协变落进 Func<..., Task>
        // （协变要求返回类型之间本身就有引用转换，而 ValueTask 是结构体），所以必须显式 thunk。
        // thunk 的产物是 Func<object?, CancellationToken, Task> —— 它在四个 TFM 上都存在，
        // 于是生成代码不依赖运行时的 ValueTask 入口，netstandard2.0 / net461 的生成目标不受影响。
        // 用普通 class 而不是 record：本项目是 netstandard2.0，位置记录需要
        // System.Runtime.CompilerServices.IsExternalInit，而该类型在 ns2.0 上不存在。
        private sealed class CommandSpec(
            string name, bool canValidate, int semaphore, string commandExpression, int constructorType)
        {
            public string Name { get; } = name;
            public bool CanValidate { get; } = canValidate;
            public int Semaphore { get; } = semaphore;
            public string CommandExpression { get; } = commandExpression;
            public int ConstructorType { get; } = constructorType;
        }

        private List<CommandSpec> CommandConfig { get; set; } = [];

        public override void Initialize(ClassDeclarationSyntax classDeclaration, INamedTypeSymbol namedTypeSymbol)
        {
            base.Initialize(classDeclaration, namedTypeSymbol);
            ReadCommandConfig(namedTypeSymbol);
        }
        private void ReadCommandConfig(INamedTypeSymbol symbol)
        {
            const string attributeFullName = $"{NAMESPACE_VELOX_MVVM}.VeloxCommandAttribute";
            var list = new List<CommandSpec>();

            foreach (var methodSymbol in symbol.GetMembers().OfType<IMethodSymbol>())
            {
                // Only check whether VeloxCommandAttribute is applied
                var attribute = methodSymbol.GetAttributes()
                    .FirstOrDefault(attr =>
                        attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == attributeFullName);

                if (attribute == null) continue;

                // Parse the configuration: positional arguments first, then named-argument overrides
                string commandName = "Auto";
                bool canValidate = false;
                int semaphore = 1;

                // Positional arguments (in order)
                if (attribute.ConstructorArguments.Length >= 1 && attribute.ConstructorArguments[0].Value is string nameArg)
                    commandName = nameArg;
                if (attribute.ConstructorArguments.Length >= 2 && attribute.ConstructorArguments[1].Value is bool canValArg)
                    canValidate = canValArg;
                if (attribute.ConstructorArguments.Length >= 3 && attribute.ConstructorArguments[2].Value is int semaArg)
                    semaphore = semaArg;

                // Named arguments (override positional arguments)
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

                // Auto naming rule
                if (commandName == "Auto")
                {
                    commandName = methodSymbol.Name.Replace("Async", "");
                }

                // Analyze the construction mode
                int constructorType = ParseConstructorType(methodSymbol, out string commandExpression);

                // Record the context
                list.Add(new CommandSpec(commandName, canValidate, Math.Max(1, semaphore), commandExpression, constructorType));
            }

            CommandConfig = list;
        }

        private const string TASK = "global::System.Threading.Tasks.Task";
        private const string VALUE_TASK = "global::System.Threading.Tasks.ValueTask";
        private const string CANCEL_TOKEN = "global::System.Threading.CancellationToken";

        // 返回值决定「值怎么变成 Task」，形参决定「走哪个构造入口」。两件事分开判。
        private int ParseConstructorType(IMethodSymbol methodSymbol, out string commandExpression)
        {
            commandExpression = methodSymbol.Name;
            var parameters = methodSymbol.Parameters;
            string returnTypeName = methodSymbol.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            bool isTask = returnTypeName == TASK || returnTypeName.StartsWith(TASK + "<");
            bool isValueTask = returnTypeName == VALUE_TASK || returnTypeName.StartsWith(VALUE_TASK + "<");

            // ValueTask 需要转换 thunk，见 CommandSpec 的说明。thunk 的形参个数同时决定构造入口。
            if (isValueTask && TryBuildValueTaskThunk(methodSymbol, out string thunk, out int valueTaskType))
            {
                commandExpression = thunk;
                return valueTaskType;
            }

            if (parameters.Length != 1) return 0;
            if (!isTask) return 0;

            var paramType = parameters[0].Type;
            string paramTypeName = paramType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            // Check the object? type precisely
            // Method 1: check the nullable object type
            bool isObjectOrNullableObject =
                paramType.SpecialType == SpecialType.System_Object ||
                (paramType.NullableAnnotation == NullableAnnotation.Annotated &&
                 paramType.SpecialType == SpecialType.System_Object) ||
                paramTypeName == "global::System.Object" ||
                paramTypeName == "global::System.Object?";

            if (isObjectOrNullableObject)
            {
                return 1;  // CreateTaskOnlyWithParameter
            }

            // Check for CancellationToken
            if (paramTypeName == CANCEL_TOKEN)
            {
                return 2;  // CreateTaskOnlyWithCancellationToken
            }

            return 0;
        }

        // 只认 [VeloxCommand] 文档承诺的四种形参形态。其余形态返回 false，
        // 于是方法组原样落地 —— 编不过，但报错方式与改动前一致，不会静默生成错东西。
        //
        // thunk 的形参个数决定了它绑到哪个构造入口，所以这里必须同时给出 constructorType：
        //   0 参        → Func<Task>（`new VeloxCommand` 的 0 参重载）
        //   1 参 object? → CreateTaskOnlyWithParameter —— 与 `Task (object?)` 一致，**不建 CTS**
        //   1 参 ct      → CreateTaskOnlyWithCancellationToken —— token 真能到达命令体
        //   2 参        → 主构造 —— token 真能到达命令体
        // 早先 1 参也一律走主构造，于是 `ValueTask (object?)` 每次执行白建一个命令体看不到的 CTS，
        // 与 `Task (object?)` 不一致。改成单参 lambda 后两边对齐。
        private static bool TryBuildValueTaskThunk(
            IMethodSymbol methodSymbol, out string thunk, out int constructorType)
        {
            thunk = string.Empty;
            constructorType = 0;
            var parameters = methodSymbol.Parameters;
            string name = methodSymbol.Name;

            static bool IsObject(IParameterSymbol p) => p.Type.SpecialType == SpecialType.System_Object;

            static bool IsToken(IParameterSymbol p) =>
                p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == CANCEL_TOKEN;

            switch (parameters.Length)
            {
                case 0:
                    thunk = $"() => {name}().AsTask()";
                    return true;
                case 1 when IsObject(parameters[0]):
                    thunk = $"parameter => {name}(parameter).AsTask()";
                    constructorType = 1;
                    return true;
                case 1 when IsToken(parameters[0]):
                    thunk = $"ct => {name}(ct).AsTask()";
                    constructorType = 2;
                    return true;
                case 2 when IsObject(parameters[0]) && IsToken(parameters[1]):
                    thunk = $"(parameter, ct) => {name}(parameter, ct).AsTask()";
                    return true;
                default:
                    return false;
            }
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
                $"{Syntax.Identifier.Text}_{NamespaceFileSegment()}_Commands.g.cs";
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
                string constructor = config.ConstructorType switch
                {
                    1 => $"{NAMESPACE_VELOX_MVVM}.VeloxCommand.CreateTaskOnlyWithParameter(",
                    2 => $"{NAMESPACE_VELOX_MVVM}.VeloxCommand.CreateTaskOnlyWithCancellationToken(",
                    _ => $"new {NAMESPACE_VELOX_MVVM}.VeloxCommand("
                };
                if (config.CanValidate)
                {
                    builder.AppendLine($$"""
                                                private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_{{config.Name}}Command = null;
                                                public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand {{config.Name}}Command
                                                {
                                                    get
                                                    {
                                                        _buffer_{{config.Name}}Command ??= {{constructor}}
                                                            command: {{config.CommandExpression}},
                                                            canExecute: CanExecute{{config.Name}}Command,
                                                            semaphore: {{config.Semaphore}});
                                                        return _buffer_{{config.Name}}Command;
                                                    }
                                                }
                                                private partial bool CanExecute{{config.Name}}Command(object? parameter);
                                             """);
                }
                else
                {
                    builder.AppendLine($$"""
                                                private {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand? _buffer_{{config.Name}}Command = null;
                                                public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand {{config.Name}}Command
                                                {
                                                    get
                                                    {
                                                        _buffer_{{config.Name}}Command ??= {{constructor}}
                                                            command: {{config.CommandExpression}},
                                                            canExecute: _ => true,
                                                            semaphore: {{config.Semaphore}});
                                                        return _buffer_{{config.Name}}Command;
                                                    }
                                                }
                                             """);
                }
            }

            return builder.ToString();
        }
    }
}