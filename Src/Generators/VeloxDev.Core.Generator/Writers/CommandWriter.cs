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
        private static bool IsObject(IParameterSymbol p) => p.Type.SpecialType == SpecialType.System_Object;

        private static bool IsToken(IParameterSymbol p) =>
            p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == CANCEL_TOKEN;

        // 形状判定：**返回类型**决定「值怎么变成 Task」，**形参**决定「走哪个构造入口」。
        //
        // 前导形参只支持 0 个或 1 个，末尾可再跟一个 CancellationToken：
        //   0 个      → 命令参数用不上（token 只有在末尾才有意义）
        //   1 个      → 命令参数就是它：是 object? 就原样传方法组，否则在 thunk 里强转
        // 多于 1 个前导形参不在这里支持 —— 那要求调用方传元组或 DTO，是另一个设计；
        // 让方法组原样落地去报错，不会静默生成错东西。
        //
        // constructorType：0 = new VeloxCommand（主构造 / Func<Task> / Action），
        //                  1 = CreateTaskOnlyWithParameter，
        //                  2 = CreateTaskOnlyWithCancellationToken。
        private int ParseConstructorType(IMethodSymbol methodSymbol, out string commandExpression)
        {
            string name = methodSymbol.Name;
            commandExpression = name;

            var parameters = methodSymbol.Parameters;
            string returnTypeName = methodSymbol.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            bool isTask = returnTypeName == TASK || returnTypeName.StartsWith(TASK + "<");
            bool isValueTask = returnTypeName == VALUE_TASK || returnTypeName.StartsWith(VALUE_TASK + "<");
            bool isVoid = methodSymbol.ReturnsVoid;

            // 自定义可等待类型等：不认识，交给方法组去报错。
            if (!isTask && !isValueTask && !isVoid) return 0;

            // 不用 `[^1]`：那是 System.Index，netstandard2.0 上没有。
            bool hasToken = parameters.Length > 0 && IsToken(parameters[parameters.Length - 1]);
            int leading = parameters.Length - (hasToken ? 1 : 0);

            if (leading > 1) return 0;

            bool isObjectParam = leading == 1 && IsObject(parameters[0]);

            // 非 object? 的单参数要在 thunk 里强转；object? 则整段省掉，方法组能直接绑。
            // 只在 leading == 1 时读 parameters[0] —— 零参方法读它会 IndexOutOfRange。
            string argument = leading == 0
                ? string.Empty
                : isObjectParam
                    ? "parameter"
                    : $"({parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})parameter!";

            if (isVoid)
            {
                // void 体观察不到 token，所以带尾随 CancellationToken 的形态一律不支持 ——
                // 那正是「接受一个永远用不上的 token」。
                if (hasToken) return 0;

                if (leading == 1 && !isObjectParam)
                {
                    commandExpression = $"parameter => {name}({argument})";
                }

                return 0;   // Action / Action<object?>
            }

            if (isTask)
            {
                if (leading == 0)
                {
                    return hasToken ? 2 : 0;    // 方法组：Func<Task> 或 Func<CancellationToken, Task>
                }

                if (isObjectParam)
                {
                    return hasToken ? 0 : 1;    // 方法组：主构造 或 CreateTaskOnlyWithParameter
                }

                // 非 object?：方法组转不过去，必须强转
                commandExpression = hasToken
                    ? $"(parameter, ct) => {name}({argument}, ct)"
                    : $"parameter => {name}({argument})";
                return hasToken ? 0 : 1;
            }

            // ValueTask：没有到 Task 的隐式转换，也不能像 Task<T> 那样靠协变（它是结构体），
            // 所以一律需要 .AsTask() 转换 thunk。末尾的 ct 必须留在 lambda 的最后。
            if (leading == 0)
            {
                commandExpression = hasToken
                    ? $"ct => {name}(ct).AsTask()"
                    : $"() => {name}().AsTask()";
                return hasToken ? 2 : 0;
            }

            commandExpression = hasToken
                ? $"(parameter, ct) => {name}({argument}, ct).AsTask()"
                : $"parameter => {name}({argument}).AsTask()";
            return hasToken ? 0 : 1;
        }

        // （转换 thunk 的构造已并入 ParseConstructorType —— 形参个数与返回类型要一起判。）

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