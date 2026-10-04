using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VeloxDev.Generators.Base;

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
        // 属性形态由「参数类型里出现的类型参数是谁声明的」决定。
        // 类的类型参数在类作用域里写得出 -> TypedProperty；方法的类型参数只在访问器里才有 -> TypedGenericAccessor。
        private enum CommandShape
        {
            UntypedProperty,
            TypedProperty,
            TypedGenericAccessor,
        }

        // 构造入口。原先是 0/1/2 三个 int，加上强类型分支后组合太多，int 的注释已经自解释不下去。
        // Result 后缀的四个是「命令体有返回值」时用的 —— 它们把值接住，交给 IVeloxCommandResult。
        private enum CommandConstruction
        {
            UntypedMainCtor,
            UntypedParameterOnlyFactory,
            UntypedTokenOnlyFactory,
            TypedMainCtor,
            TypedParameterOnlyFactory,
            UntypedResultMainCtor,
            UntypedResultParameterOnlyFactory,
            TypedResultMainCtor,
            TypedResultParameterOnlyFactory,
        }

        private sealed class CommandSpec(
            string name,
            bool canValidate,
            int semaphore,
            string commandExpression,
            CommandShape shape,
            CommandConstruction construction,
            string? parameterTypeName,
            string resultTypeName,
            string validatorParameterList,
            string alwaysTruePredicate,
            string methodTypeParameterList,
            string methodConstraintClauses,
            string[] methodTypeParameterNames,
            string? forcedPropertyTypeName)
        {
            public string Name { get; } = name;
            public bool CanValidate { get; } = canValidate;
            public int Semaphore { get; } = semaphore;
            public string CommandExpression { get; } = commandExpression;
            public CommandShape Shape { get; } = shape;
            public CommandConstruction Construction { get; } = construction;

            // 强类型面才有的：参数类型（带 global:: 前缀）、方法类型参数表与约束子句。
            public string? ParameterTypeName { get; } = parameterTypeName;

            // 命令体的返回值类型；没有返回值时是 object?（恒 null）—— 结果通道始终在，只是空。
            public string ResultTypeName { get; } = resultTypeName;

            // 校验器的形参表：类型与名字都跟随源方法 —— 生成器声明什么，用户就得写什么（否则 CS8826）。
            // 零前导形参时为 object? parameter。
            public string ValidatorParameterList { get; } = validatorParameterList;

            // canValidate: false 时交给构造的恒真谓词。元数必须跟着形参个数走 ——
            // arity 族的构造收 Func<T1..Tn, bool>，写死 `_ => true` 是 CS1593。
            public string AlwaysTruePredicate { get; } = alwaysTruePredicate;

            public string MethodTypeParameterList { get; } = methodTypeParameterList;
            public string MethodConstraintClauses { get; } = methodConstraintClauses;

            // 缓存的键要覆盖**全部**类型参数 —— 只按第一个索引会让 M<T, U> 的不同 U 串在一起。
            public string[] MethodTypeParameterNames { get; } = methodTypeParameterNames;

            // 属性类型被接口实现逼回某个已声明类型时，这里是那个类型的全名；否则为 null。
            // 缓冲字段与构造仍是强类型的，只有属性类型与 getter 的返回类型跟着它走。
            public string? ForcedPropertyTypeName { get; } = forcedPropertyTypeName;
        }

        private List<CommandSpec> CommandConfig { get; set; } = [];

        /// <summary>
        /// What could not be turned into a command. The generator reports these so the error lands on the
        /// author's own line instead of inside the generated file.
        /// </summary>
        public List<Diagnostic> Diagnostics { get; } = [];

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
                // 只检查是否应用了 VeloxCommandAttribute
                var attribute = methodSymbol.GetAttributes()
                    .FirstOrDefault(attr =>
                        attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == attributeFullName);

                if (attribute == null) continue;

                // 解析配置：先位置参数，再命名参数覆盖
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

                // 自动命名规则
                if (commandName == "Auto")
                {
                    // 规则本体在 AIContextNaming：上下文树的生成器看不见这里的产物，只能复现同一个命名规则。
                    commandName = AIContextNaming.CommandBaseName(methodSymbol);
                }

                // 分析构造模式
                var spec = BuildSpec(symbol, methodSymbol, commandName, canValidate, Math.Max(1, semaphore), out string reason);

                if (spec is null)
                {
                    // 跳过它，不生成注定编不过的东西：产物里再冒一个 CS1503 只会把真正的错误埋掉。
                    Diagnostics.Add(Diagnostic.Create(
                        VeloxDev.Generators.Diagnostics.UnsupportedCommandSignature,
                        methodSymbol.Locations.FirstOrDefault(),
                        methodSymbol.Name,
                        reason));
                    continue;
                }

                list.Add(spec);
            }

            CommandConfig = list;
        }

        // 命令体能带多少前导形参。上限由委托决定：体是 Func<T1..Tn, CancellationToken, Task<TResult>>，
        // 共 n + 2 个类型实参，而 Func 最多 17 个 —— 15 是 token 与结果都占一格后的最大值。
        private const int MaxLeadingParameters = 15;

        private const string TASK = "global::System.Threading.Tasks.Task";
        private const string VALUE_TASK = "global::System.Threading.Tasks.ValueTask";
        private const string CANCEL_TOKEN = "global::System.Threading.CancellationToken";

        // 返回值决定「值怎么变成 Task」，形参决定「走哪个构造入口」。两件事分开判。
        private static bool IsObject(IParameterSymbol p) => p.Type.SpecialType == SpecialType.System_Object;

        private static bool IsToken(IParameterSymbol p) =>
            p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == CANCEL_TOKEN;

        // 形状判定：**返回类型**决定「值怎么变成 Task」，**形参**决定「走哪个构造入口」与「属性强不强类型」。
        //
        // 前导形参只支持 0 个或 1 个，末尾可再跟一个 CancellationToken：
        //   0 个      → 命令参数用不上（token 只有在末尾才有意义）
        //   1 个      → 命令参数就是它
        // 多于 1 个前导形参不在这里支持 —— 那要求调用方传元组或 DTO，是另一个设计；
        // 让方法组原样落地去报错，不会静默生成错东西。
        //
        // 强类型只在「T 已知」时成立，分两种：
        //   情形 1  参数类型里的类型参数全由所属类声明 -> 属性类型写得出 IVeloxCommand<P>
        //   情形 2  参数类型里出现了方法自己的类型参数   -> 类作用域里没有它，只能生成
        //                                                   Get{名}Command<T>() 访问器
        // 判据按**符号身份**判，不按名字 —— `class Vm<T> { Task M<T>(T x) }` 里方法的 T 遮蔽了类的 T，
        // 按名字判会把它错当成情形 1，生成一个能编译但语义错的产物。
        private CommandSpec? BuildSpec(
            INamedTypeSymbol containingType,
            IMethodSymbol methodSymbol,
            string name,
            bool canValidate,
            int semaphore,
            out string reason)
        {
            string methodName = methodSymbol.Name;
            reason = string.Empty;

            var parameters = methodSymbol.Parameters;
            string returnTypeName = methodSymbol.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            bool isTask = returnTypeName == TASK || returnTypeName.StartsWith(TASK + "<");
            bool isValueTask = returnTypeName == VALUE_TASK || returnTypeName.StartsWith(VALUE_TASK + "<");
            bool isVoid = methodSymbol.ReturnsVoid;

            // 不用 `[^1]`：那是 System.Index，netstandard2.0 上没有。
            bool hasToken = parameters.Length > 0 && IsToken(parameters[parameters.Length - 1]);
            int leading = parameters.Length - (hasToken ? 1 : 0);
            bool isObjectParam = leading == 1 && IsObject(parameters[0]);

            // 泛型方法：类型实参由调用点选定，而一个命令实例的 T 在构造时就固定了。
            // 只有 T 出现在参数类型里，生成的访问器才有办法把它作为自己的类型参数暴露出去。
            bool methodTypeParamsInParameter = methodSymbol.IsGenericMethod
                && leading >= 1
                && parameters.Take(leading).SelectMany(static parameter => CollectTypeParameters(parameter.Type)).Any(candidate =>
                    methodSymbol.TypeParameters.Any(tp => SymbolEqualityComparer.Default.Equals(tp, candidate)));

            if (methodSymbol.IsGenericMethod && !methodTypeParamsInParameter)
            {
                reason = "it is a generic method whose type parameters do not appear in its parameter type; a command instance fixes its type argument when it is built, so the generated accessor has no way to carry one - put the type parameters in the parameter type (M<T>(T value)) or make the method non-generic";
                return null;
            }

            if (leading > MaxLeadingParameters)
            {
                reason = $"it takes {leading} parameters before the optional CancellationToken, and the generated command carries at most {MaxLeadingParameters}; take one type of your own instead (a record or a tuple both work)";
                return null;
            }

            if (!isTask && !isValueTask && !isVoid)
            {
                reason = $"it returns '{methodSymbol.ReturnType.ToDisplayString()}', but a command body must return Task, Task<T>, ValueTask, ValueTask<T> or void";
                return null;
            }

            if (isVoid && hasToken)
            {
                reason = "it returns void and takes a CancellationToken, which nothing in a synchronous body can observe; return Task when the body is meant to be cancellable, or drop the parameter";
                return null;
            }

            // 命令体的返回值。Task<T>/ValueTask<T> 才有值，Task/ValueTask/void 取不到。
            string? valueType = (isTask || isValueTask)
                && methodSymbol.ReturnType is INamedTypeSymbol { TypeArguments.Length: 1 } returnNamed
                    ? FullyQualifiedWithNullability(returnNamed.TypeArguments[0])
                    : null;

            // 没有返回值时结果类型仍是 object?（恒 null）—— 结果通道始终在，只是空。
            // 写成渲染后的形式而不是字面 "object?"：属性类型要与接口声明的类型做字符串比较，
            // 两边必须用同一种渲染，否则同一种类型会因为拼写不同被判成不可赋值。
            string resultTypeName = valueType ?? "global::System.Object?";

            // 校验器的形参表跟随源方法：类型与名字都是源方法自己的（否则 CS8826）。
            // 零前导形参的方法没有源形参可抄，只能用 object? parameter。
            string alwaysTruePredicate = leading >= 2
                ? $"({string.Join(", ", System.Linq.Enumerable.Range(0, leading).Select(static _ => "_"))}) => true"
                : "_ => true";

            string validatorParameterList = leading == 0
                ? "object? parameter"
                : string.Join(", ", parameters.Take(leading).Select(static parameter =>
                    $"{FullyQualifiedWithNullability(parameter.Type)} {(SyntaxFacts.GetKeywordKind(parameter.Name) != SyntaxKind.None ? "@" + parameter.Name : parameter.Name)}"));

            // object? 形参维持今天的非强类型形态 —— 它本来就没有类型可强。
            // 单个 object? 形参维持非强类型（它本来就没有类型可强）；其余一律强类型，元数 >= 2 走 arity 族。
            bool isTyped = leading >= 1 && !(leading == 1 && isObjectParam);
            string? parameterType = isTyped
                ? string.Join(", ", parameters.Take(leading).Select(static parameter => FullyQualifiedWithNullability(parameter.Type)))
                : null;

            var shape = !isTyped
                ? CommandShape.UntypedProperty
                : methodTypeParamsInParameter
                    ? CommandShape.TypedGenericAccessor
                    : CommandShape.TypedProperty;

            string propertyName = $"{name}Command";

            // 同名成员已存在时，今天的产物会在生成文件里撞成 CS0102 —— 报出来比埋掉好。
            if (containingType.GetMembers(propertyName).Length > 0)
            {
                reason = $"a member named '{propertyName}' already exists on this type, and the generated command would be a second definition of it";
                return null;
            }

            // 接口/基类里的命令属性声明成的那个类型（IVeloxCommand 或 IVeloxCommand<X>）。
            // 强类型属性不会隐式实现它（CS0738），所以必须退回**声明的那个类型**。
            string? requiredPropertyType = DeclaredCommandPropertyTypeInHierarchy(containingType, propertyName);

            if (shape == CommandShape.TypedGenericAccessor && requiredPropertyType is not null)
            {
                reason = $"it is refused because its type parameter belongs to the method, so the generated accessor is a method - while this type (or a base) already declares '{propertyName}' as a command property, which only a property can satisfy";
                return null;
            }

            // 命中时只把**属性类型**退回，缓冲字段与构造保持强类型，getter 上转即可 ——
            // 但那个上转必须真的存在，否则生成的文件里是 CS0266。
            if (shape == CommandShape.TypedProperty
                && requiredPropertyType is not null
                && !CanUpcastTo(requiredPropertyType, parameterType!, resultTypeName))
            {
                reason = $"it is refused because this type (or a base) declares '{propertyName}' as '{requiredPropertyType}', and a command built from a '{parameterType}' parameter returning '{resultTypeName}' cannot be assigned to it";
                return null;
            }

            string? forcedPropertyType = shape == CommandShape.TypedProperty ? requiredPropertyType : null;

            string commandExpression;
            CommandConstruction construction;

            if (isTyped)
            {
                // lambda 的形参已经是 P，不再需要强转。无 token 的一律走保持 _isCtsNeeded = false 的入口，
                // 否则每条这样的命令都会白分配一个命令体根本观察不到的 CancellationTokenSource。
                // 只有「单形参 + 无返回值」还能走 void 通道。arity 族没有无返回体的公开入口，
                // 多形参的 void 体只能包一层 async lambda，从结果入口走。
                var useResultChannel = valueType is not null || leading >= 2;
                construction = useResultChannel
                    ? (hasToken ? CommandConstruction.TypedResultMainCtor : CommandConstruction.TypedResultParameterOnlyFactory)
                    : (hasToken ? CommandConstruction.TypedMainCtor : CommandConstruction.TypedParameterOnlyFactory);

                // 单形参沿用 value，产物文本与从前逐字一致；多形参用 v1..vn。
                var values = leading == 1
                    ? "value"
                    : string.Join(", ", System.Linq.Enumerable.Range(1, leading).Select(static i => $"v{i}"));
                var lambda = leading == 1 ? values : $"({values})";
                var lambdaWithToken = leading == 1 ? $"(value, ct)" : $"({values}, ct)";

                if (leading >= 2 && valueType is null)
                {
                    // 多形参、无返回值：arity 族没有无返回体的公开入口，而结果通道要 Task<TResult>，
                    // 只能包一层 async lambda。必须显式 return —— async lambda 落到 Task<T> 目标时
                    // 不允许从末尾漏出去（CS1643），这一点与 async Task<T> 方法不同，实测过。
                    // 同步跑完的 async 由编译器缓存状态机，所以这条不额外分配。
                    var statement = isVoid
                        ? $"{methodName}({values});"
                        : hasToken
                            ? $"await {methodName}({values}, ct).ConfigureAwait(false);"
                            : $"await {methodName}({values}).ConfigureAwait(false);";
                    var header = hasToken ? lambdaWithToken : lambda;
                    commandExpression = $"async {header} => {{ {statement} return default!; }}";
                }
                else if (isVoid)
                {
                    // 单形参、无返回值：语句体 lambda 包成 Func<P, Task>，走无 token 工厂（void 带 token 已被拒）。
                    commandExpression = $"value => {{ {methodName}(value); return global::System.Threading.Tasks.Task.CompletedTask; }}";
                }
                else if (isValueTask)
                {
                    commandExpression = hasToken
                        ? $"{lambdaWithToken} => {methodName}({values}, ct).AsTask()"
                        : $"{lambda} => {methodName}({values}).AsTask()";
                }
                else
                {
                    commandExpression = hasToken
                        ? $"{lambdaWithToken} => {methodName}({values}, ct)"
                        : $"{lambda} => {methodName}({values})";
                }
            }
            else
            {
                // 走到这里的只可能是「零个前导形参」或「object? 形参」—— 非 object 的单参数一律是强类型。
                commandExpression = methodName;
                construction = CommandConstruction.UntypedMainCtor;

                if (isTask)
                {
                    if (leading == 0)
                    {
                        construction = hasToken ? CommandConstruction.UntypedTokenOnlyFactory : CommandConstruction.UntypedMainCtor;
                    }
                    else if (isObjectParam)
                    {
                        construction = hasToken ? CommandConstruction.UntypedMainCtor : CommandConstruction.UntypedParameterOnlyFactory;
                    }
                }
                else if (isValueTask)
                {
                    // 没有到 Task 的隐式转换，也不能像 Task<T> 那样靠协变（它是结构体），
                    // 所以一律需要 .AsTask() 转换 thunk。末尾的 ct 必须留在 lambda 的最后。
                    if (leading == 0)
                    {
                        commandExpression = hasToken
                            ? $"ct => {methodName}(ct).AsTask()"
                            : $"() => {methodName}().AsTask()";
                        construction = hasToken ? CommandConstruction.UntypedTokenOnlyFactory : CommandConstruction.UntypedMainCtor;
                    }
                    else
                    {
                        commandExpression = hasToken
                            ? $"(parameter, ct) => {methodName}(parameter, ct).AsTask()"
                            : $"parameter => {methodName}(parameter).AsTask()";
                        construction = hasToken ? CommandConstruction.UntypedMainCtor : CommandConstruction.UntypedParameterOnlyFactory;
                    }
                }
            }

            // 有返回值时换用带结果的构造入口，并把表达式重写成「装箱后交出去」的形式 ——
            // 今天的表达式靠返回类型协变绑定，T 在那一跳就被丢掉了。
            // 非强类型、但有返回值：结果要装箱成 object? 才穿得过通道。
            // 强类型那一半（含多形参）已经在上面按 useResultChannel 选好构造与表达式，这里不能再碰 ——
            // 碰了就会把多形参的产物覆盖回单形参写法（CS1593）。
            if (valueType is not null && !isTyped)
            {
                construction = hasToken
                    ? CommandConstruction.UntypedResultMainCtor
                    : CommandConstruction.UntypedResultParameterOnlyFactory;

                // 元数必须与工厂收的委托一致：无 token 的两个工厂收单参 Func<object?, Task<object?>>，
                // 带 token 的收双参 —— 零形参的命令体也得套一层丢掉实参的 lambda 才绑得上。
                commandExpression = (leading, hasToken) switch
                {
                    (0, false) => $"async _ => (object?)await {methodName}()",
                    (0, true) => $"async (_, ct) => (object?)await {methodName}(ct)",
                    (_, false) => $"async parameter => (object?)await {methodName}(parameter)",
                    (_, true) => $"async (parameter, ct) => (object?)await {methodName}(parameter, ct)",
                };
            }

            var (typeParameterList, constraintClauses) = methodTypeParamsInParameter
                ? TypeParametersOf(methodSymbol)
                : (string.Empty, string.Empty);

            var typeParameterNames = methodTypeParamsInParameter
                ? methodSymbol.TypeParameters.Select(static tp => tp.Name).ToArray()
                : [];

            return new CommandSpec(
                name,
                canValidate,
                semaphore,
                commandExpression,
                shape,
                construction,
                parameterType,
                resultTypeName,
                validatorParameterList,
                alwaysTruePredicate,
                typeParameterList,
                constraintClauses,
                typeParameterNames,
                forcedPropertyType);
        }

        // 递归收集类型里出现的全部类型参数，数组/指针/泛型实参都要下钻。
        private static IEnumerable<ITypeParameterSymbol> CollectTypeParameters(ITypeSymbol type)
        {
            switch (type)
            {
                case ITypeParameterSymbol typeParameter:
                    yield return typeParameter;
                    break;
                case IArrayTypeSymbol array:
                    foreach (var element in CollectTypeParameters(array.ElementType)) yield return element;
                    break;
                case IPointerTypeSymbol pointer:
                    foreach (var pointed in CollectTypeParameters(pointer.PointedAtType)) yield return pointed;
                    break;
                case INamedTypeSymbol named:
                    foreach (var argument in named.TypeArguments)
                        foreach (var nested in CollectTypeParameters(argument)) yield return nested;
                    break;
            }
        }

        // 带 global:: 前缀且保留可空注解的类型名。不能用 SymbolDisplayFormat.FullyQualifiedFormat：
        // 它不带 global::（可能与用户命名空间撞名），也不稳定地带可空注解。
        private static string FullyQualifiedWithNullability(ITypeSymbol type) =>
            type.ToDisplayString(new SymbolDisplayFormat(
                globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
                miscellaneousOptions: SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier));

        // 情形 2 的访问器必须把类型参数表与约束原样搬过去，否则调用方法会 CS0314/CS0452。
        // 优先抄源码文本 —— `where T : U` 依赖类型参数的书写顺序，符号渲染容易改错顺序。
        private static (string List, string Constraints) TypeParametersOf(IMethodSymbol method)
        {
            foreach (var reference in method.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax() is MethodDeclarationSyntax syntax)
                {
                    string list = syntax.TypeParameterList?.ToString() ?? string.Empty;
                    string constraints = syntax.ConstraintClauses.Count == 0
                        ? string.Empty
                        : " " + string.Join(" ", syntax.ConstraintClauses.Select(static clause => clause.ToString()));

                    return (list, constraints);
                }
            }

            var rendered = method.TypeParameters.Select(RenderTypeParameter).Where(static text => text.Length > 0).ToArray();

            return (
                "<" + string.Join(", ", method.TypeParameters.Select(static tp => tp.Name)) + ">",
                rendered.Length == 0 ? string.Empty : " " + string.Join(" ", rendered));
        }

        private static string RenderTypeParameter(ITypeParameterSymbol typeParameter)
        {
            var parts = new List<string>();

            if (typeParameter.HasReferenceTypeConstraint)
            {
                parts.Add(typeParameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class");
            }

            if (typeParameter.HasUnmanagedTypeConstraint)
            {
                parts.Add("unmanaged");
            }
            else if (typeParameter.HasValueTypeConstraint)
            {
                parts.Add("struct");
            }

            if (typeParameter.HasNotNullConstraint)
            {
                parts.Add("notnull");
            }

            foreach (var constraint in typeParameter.ConstraintTypes)
            {
                parts.Add(constraint.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            }

            if (typeParameter.HasConstructorConstraint)
            {
                parts.Add("new()");
            }

            return parts.Count == 0 ? string.Empty : $"where {typeParameter.Name} : {string.Join(", ", parts)}";
        }

        // 接口（含基类贡献的）与基类里是否已经有一个命令属性，返回它**声明成的那个类型**。
        // 强类型属性不会隐式实现声明的 IVeloxCommand 或 IVeloxCommand<X>（CS0738），所以命中时
        // 属性类型必须退回这个字符串，而不是随便退成 IVeloxCommand。
        private static string? DeclaredCommandPropertyTypeInHierarchy(INamedTypeSymbol type, string propertyName)
        {
            foreach (var contract in type.AllInterfaces)
            {
                var declared = DeclaredCommandPropertyType(contract, propertyName);
                if (declared is not null)
                {
                    return declared;
                }
            }

            for (var baseType = type.BaseType;
                 baseType != null && baseType.SpecialType != SpecialType.System_Object;
                 baseType = baseType.BaseType)
            {
                var declared = DeclaredCommandPropertyType(baseType, propertyName);
                if (declared is not null)
                {
                    return declared;
                }
            }

            return null;
        }

        // 生成的强类型属性能不能赋给接口声明的那个类型。
        // 继承链只有一条：IVeloxCommand<TParam1..TParamN, TResult> : IVeloxCommand（没有声明逆变，
        // 所以元数或结果类型不同的两个强类型之间互不可转换）。
        private static bool CanUpcastTo(string declared, string parameterType, string resultType)
        {
            const string untyped = NAMESPACE_VELOX_MVVM + ".IVeloxCommand";

            return declared == untyped
                || declared == $"{untyped}<{parameterType}, {resultType}>";
        }

        private static string? DeclaredCommandPropertyType(INamedTypeSymbol type, string propertyName)
        {
            const string untyped = NAMESPACE_VELOX_MVVM + ".IVeloxCommand";

            foreach (var property in type.GetMembers(propertyName).OfType<IPropertySymbol>())
            {
                // 用保留可空注解的那个格式：FullyQualifiedFormat 会把 object? 抹成 object。
                var declared = FullyQualifiedWithNullability(property.Type);
                if (declared == untyped || declared.StartsWith(untyped + "<"))
                {
                    return declared;
                }
            }

            return null;
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
                if (config.Shape == CommandShape.TypedGenericAccessor)
                {
                    AppendTypeParameterAccessor(builder, config);
                }
                else
                {
                    AppendProperty(builder, config);
                }
            }

            return builder.ToString();
        }

        private static string ConstructionPrefix(CommandSpec config)
        {
            string parameterType = config.ParameterTypeName ?? string.Empty;

            return config.Construction switch
            {
                CommandConstruction.UntypedParameterOnlyFactory => $"{NAMESPACE_VELOX_MVVM}.VeloxCommand.CreateTaskOnlyWithParameter(",
                CommandConstruction.UntypedTokenOnlyFactory => $"{NAMESPACE_VELOX_MVVM}.VeloxCommand.CreateTaskOnlyWithCancellationToken(",
                CommandConstruction.TypedParameterOnlyFactory => $"{NAMESPACE_VELOX_MVVM}.VeloxCommand.CreateTypedTaskOnlyWithParameter<{parameterType}>(",
                CommandConstruction.TypedMainCtor => $"{NAMESPACE_VELOX_MVVM}.VeloxCommand.CreateTypedWithParameter<{parameterType}>(",
                CommandConstruction.UntypedResultMainCtor => $"{NAMESPACE_VELOX_MVVM}.VeloxCommand.CreateTaskWithResult(",
                CommandConstruction.UntypedResultParameterOnlyFactory => $"{NAMESPACE_VELOX_MVVM}.VeloxCommand.CreateTaskOnlyWithResult(",
                CommandConstruction.TypedResultMainCtor => $"new {NAMESPACE_VELOX_MVVM}.VeloxCommand<{parameterType}, {config.ResultTypeName}>(",
                CommandConstruction.TypedResultParameterOnlyFactory => $"{NAMESPACE_VELOX_MVVM}.VeloxCommand<{parameterType}, {config.ResultTypeName}>.CreateTaskOnlyWithResult(",
                _ => $"new {NAMESPACE_VELOX_MVVM}.VeloxCommand("
            };
        }

        private static void AppendProperty(StringBuilder builder, CommandSpec config)
        {
            // 缓冲字段始终是强类型的那一份；只有属性类型可能被接口实现逼回非强类型，那时 getter 隐式上转。
            // 强类型一律写成 2-arity：无返回值的命令 ResultTypeName 是 object?（恒 null），
            // VeloxCommand<P> 正是靠实现 IVeloxCommand<P, object?> 让这一条统一成立。
            string typed = config.ParameterTypeName is null
                ? $"{NAMESPACE_VELOX_IMVVM}.IVeloxCommand"
                : $"{NAMESPACE_VELOX_IMVVM}.IVeloxCommand<{config.ParameterTypeName}, {config.ResultTypeName}>";
            string propertyType = config.ForcedPropertyTypeName ?? typed;
            string constructor = ConstructionPrefix(config);

            if (config.CanValidate)
            {
                builder.AppendLine($$"""
                                            private {{typed}}? _buffer_{{config.Name}}Command = null;
                                            public {{propertyType}} {{config.Name}}Command
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
                                            private partial bool CanExecute{{config.Name}}Command({{config.ValidatorParameterList}});
                                         """);
            }
            else
            {
                builder.AppendLine($$"""
                                            private {{typed}}? _buffer_{{config.Name}}Command = null;
                                            public {{propertyType}} {{config.Name}}Command
                                            {
                                                get
                                                {
                                                    _buffer_{{config.Name}}Command ??= {{constructor}}
                                                        command: {{config.CommandExpression}},
                                                        canExecute: {{config.AlwaysTruePredicate}},
                                                        semaphore: {{config.Semaphore}});
                                                    return _buffer_{{config.Name}}Command;
                                                }
                                            }
                                         """);
            }
        }

        // 情形 2：类型参数属于方法，类作用域里没有它，属性写不出来，只能给访问器方法。
        // 名字加 Get 前缀是必需的 —— 同一类型里属性与方法同名是 CS0102，而 {名}Command 已被属性形式占用。
        private static void AppendTypeParameterAccessor(StringBuilder builder, CommandSpec config)
        {
            string parameterType = config.ParameterTypeName!;
            string constructor = ConstructionPrefix(config);
            string canExecute = config.CanValidate ? $"CanExecute{config.Name}Command" : "_ => true";

            // 缓存的键覆盖全部类型参数。只按第一个索引会让 M<T, U> 的不同 U 共用同一个命令实例。
            string keyType = config.MethodTypeParameterNames.Length == 1
                ? "global::System.Type"
                : "(" + string.Join(", ", config.MethodTypeParameterNames.Select(static _ => "global::System.Type")) + ")";
            string keyExpression = config.MethodTypeParameterNames.Length == 1
                ? $"typeof({config.MethodTypeParameterNames[0]})"
                : "(" + string.Join(", ", config.MethodTypeParameterNames.Select(static name => $"typeof({name})")) + ")";

            builder.AppendLine($$"""
                                        private readonly global::System.Collections.Concurrent.ConcurrentDictionary<{{keyType}}, {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand> _buffer_{{config.Name}}Command = new();
                                        public {{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand<{{parameterType}}, {{config.ResultTypeName}}> Get{{config.Name}}Command{{config.MethodTypeParameterList}}(){{config.MethodConstraintClauses}}
                                        {
                                            return ({{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand<{{parameterType}}, {{config.ResultTypeName}}>)_buffer_{{config.Name}}Command.GetOrAdd(
                                                {{keyExpression}},
                                                _ => ({{NAMESPACE_VELOX_IMVVM}}.IVeloxCommand){{constructor}}
                                                    command: {{config.CommandExpression}},
                                                    canExecute: {{canExecute}},
                                                    semaphore: {{config.Semaphore}}));
                                        }
                                     """);

            if (config.CanValidate)
            {
                builder.AppendLine($"        private partial bool CanExecute{config.Name}Command{config.MethodTypeParameterList}({config.ValidatorParameterList}){config.MethodConstraintClauses};");
            }
        }
    }
}
