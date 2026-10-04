using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VeloxDev.Generators.Base;

namespace VeloxDev.Generators.Writers
{
    public abstract class WriterBase : ICodeWriter
    {
        /// <summary>Fully qualified <c>VeloxDev.TimeLine</c> namespace emitted into generated sources.</summary>
        public const string NAMESPACE_VELOX_TIMELINE = "global::VeloxDev.TimeLine";
        /// <summary>Fully qualified <c>VeloxDev.MVVM</c> namespace for MVVM interfaces, emitted into generated sources.</summary>
        public const string NAMESPACE_VELOX_IMVVM = "global::VeloxDev.MVVM";
        /// <summary>Fully qualified <c>VeloxDev.MVVM</c> namespace emitted into generated sources.</summary>
        public const string NAMESPACE_VELOX_MVVM = "global::VeloxDev.MVVM";
        /// <summary>Fully qualified <c>VeloxDev.AopInterfaces</c> namespace emitted into generated sources.</summary>
        public const string NAMESPACE_VELOX_AOP = "global::VeloxDev.AopInterfaces";
        /// <summary>Fully qualified <c>System.ComponentModel</c> namespace emitted into generated sources.</summary>
        public const string NAMESPACE_SYSTEM_MVVM = "global::System.ComponentModel";
        /// <summary>Fully qualified <c>VeloxDev.WorkflowSystem</c> namespace for workflow interfaces, emitted into generated sources.</summary>
        public const string NAMESPACE_VELOX_IWORKFLOW = "global::VeloxDev.WorkflowSystem";
        /// <summary>Fully qualified <c>VeloxDev.WorkflowSystem</c> namespace emitted into generated sources.</summary>
        public const string NAMESPACE_VELOX_WORKFLOW = "global::VeloxDev.WorkflowSystem";

        /// <summary>Gets the class declaration this writer was initialized with.</summary>
        public ClassDeclarationSyntax? Syntax { get; protected set; }
        /// <summary>Gets the symbol of the class this writer was initialized with.</summary>
        public INamedTypeSymbol? Symbol { get; protected set; }
        /// <summary>Gets the enclosing class declarations when the target class is nested.</summary>
        public List<ClassDeclarationSyntax>? OuterClasses { get; protected set; }

        // 拼进生成物文件名与生成类型名的命名空间片段。
        // 全局命名空间的 ToDisplayString() 返回 "<global namespace>" —— 那个尖括号是非法的文件名与标识符字符，
        // 直接拼进去会让生成器整个抛 ArgumentException（宿主只报 CS8785「生成器未能生成源」），
        // 报错跟命名空间毫无关系，极难查。用 "Global" 占位。
        /// <summary>Returns a file-name and identifier-safe namespace segment (<c>Global</c> for the global namespace).</summary>
        protected string NamespaceFileSegment()
            => Symbol is null || Symbol.ContainingNamespace.IsGlobalNamespace
                ? "Global"
                : Symbol.ContainingNamespace.ToDisplayString().Replace('.', '_');

        /// <inheritdoc />
        public virtual void Initialize(ClassDeclarationSyntax classDeclaration, INamedTypeSymbol namedTypeSymbol)
        {
            Syntax = classDeclaration;
            Symbol = namedTypeSymbol;

            // 若是嵌套类，收集外层类
            OuterClasses = [];
            var parent = classDeclaration.Parent;
            while (parent is ClassDeclarationSyntax outerClass)
            {
                OuterClasses.Insert(0, outerClass);
                parent = parent.Parent;
            }
        }

        // 外层类必须**原样带上类型形参**：`partial class Outer` 与 `partial class Outer<T>` 是两个 arity
        // 不同的类型，不合并 —— 编译器会另造一个空的 `Outer`，里面什么都没有，于是内层类的方法全成了
        // 「当前上下文中不存在该名称」（CS0103），而报错指向生成文件，极难反推。
        private string OuterClassHeader(ClassDeclarationSyntax outerClass)
        {
            string modifiers = FormatModifiers(outerClass.Modifiers.ToString());
            string typeParameters = outerClass.TypeParameterList?.ToString() ?? string.Empty;
            string constraints = outerClass.ConstraintClauses.Count > 0
                ? " " + string.Join(" ", outerClass.ConstraintClauses)
                : string.Empty;

            return $"{modifiers}class {outerClass.Identifier.Text}{typeParameters}{constraints}";
        }

        // 全局命名空间不能写成 `namespace X;`：ContainingNamespace.ToDisplayString() 给的是
        // "<global namespace>"，拼出来是 `namespace <global namespace>;` —— 非法语法，产物编不过。
        // 什么都不写才是对的。
        private void AppendNamespace(StringBuilder builder)
        {
            if (Symbol is null || Symbol.ContainingNamespace.IsGlobalNamespace)
            {
                return;
            }

            builder.AppendLine($"namespace {Symbol.ContainingNamespace};");
            builder.AppendLine();
        }

        /// <inheritdoc />
        public virtual string Write()
        {
            if (Syntax == null || Symbol == null) return string.Empty;

            StringBuilder sourceBuilder = new();

            // 取类型参数
            string typeParameters = GetTypeParameters();

            // 取约束子句
            string constraints = GetConstraints();

            // 取基类型与接口列表
            string baseTypes = GetBaseTypes();

            sourceBuilder.AppendLine("// <auto-generated>");
            sourceBuilder.AppendLine("#pragma warning disable");
            sourceBuilder.AppendLine("#nullable enable");
            sourceBuilder.AppendLine();

            // 若不是嵌套类，或最外层类不在命名空间里，就加命名空间
            if (OuterClasses == null || OuterClasses.Count == 0)
            {
                AppendNamespace(sourceBuilder);
            }

            // 若是嵌套类，生成外层类
            if (OuterClasses != null && OuterClasses.Count > 0)
            {
                // 从最外层类开始
                var outermostClass = OuterClasses[0];
                AppendNamespace(sourceBuilder);

                // 格式化修饰符，确保 partial 关键字位置正确
                sourceBuilder.AppendLine(OuterClassHeader(outermostClass));
                sourceBuilder.AppendLine("{");

                // 生成内层类
                for (int i = 1; i < OuterClasses.Count; i++)
                {
                    sourceBuilder.AppendLine(OuterClassHeader(OuterClasses[i]));
                    sourceBuilder.AppendLine("{");
                }
            }

            // 生成当前类声明
            var currentModifiers = FormatModifiers(Syntax.Modifiers.ToString());
            sourceBuilder.AppendLine($"{currentModifiers}class {Syntax.Identifier.Text}{typeParameters}{baseTypes}{constraints}");
            sourceBuilder.AppendLine("{");
            sourceBuilder.AppendLine(GenerateBody());
            sourceBuilder.AppendLine("}");

            // 关闭所有外层类（若是嵌套类）
            if (OuterClasses != null && OuterClasses.Count > 0)
            {
                for (int i = 0; i < OuterClasses.Count; i++)
                {
                    sourceBuilder.AppendLine("}");
                }
            }

            return sourceBuilder.ToString();
        }

        private string GetTypeParameters()
        {
            if (Syntax?.TypeParameterList == null)
                return string.Empty;

            return $"<{string.Join(", ", Syntax.TypeParameterList.Parameters.Select(p => p.ToString()))}>";
        }

        private string GetConstraints()
        {
            if (Syntax?.ConstraintClauses == null || !Syntax.ConstraintClauses.Any())
                return string.Empty;

            var constraints = new StringBuilder();

            foreach (var constraintClause in Syntax.ConstraintClauses)
            {
                // 直接加上完整约束子句。约束里的类型名在用户代码的命名空间上下文里解析
                constraints.Append(" ").Append(constraintClause.ToString());
            }

            return constraints.ToString();
        }

        private string GetBaseTypes()
        {
            if (Symbol == null)
                return string.Empty;

            var baseTypes = new List<string>();

            // 1. 加上原类的基类型与接口（语义模型给出的完全限定名）
            if (Symbol.Interfaces.Length > 0)
            {
                // 取每个接口的完全限定名
                foreach (var interfaceSymbol in Symbol.Interfaces)
                {
                    string fullName = GetFullyQualifiedTypeName(interfaceSymbol);
                    if (!string.IsNullOrEmpty(fullName))
                    {
                        baseTypes.Add(fullName);
                    }
                }
            }

            // 2. 加上基类（若有）
            if (Symbol.BaseType != null && Symbol.BaseType.SpecialType != SpecialType.System_Object)
            {
                string baseTypeName = GetFullyQualifiedTypeName(Symbol.BaseType);
                if (!string.IsNullOrEmpty(baseTypeName))
                {
                    // 基类放在接口之前
                    baseTypes.Insert(0, baseTypeName);
                }
            }

            // 3. 加上生成器需要补充的基类型
            baseTypes.AddRange(GenerateBaseTypes());
            baseTypes.AddRange(GenerateBaseInterfaces());

            if (baseTypes.Count == 0)
                return string.Empty;

            return " : " + string.Join(", ", baseTypes.Distinct());
        }

        private string GetFullyQualifiedTypeName(ITypeSymbol typeSymbol)
        {
            if (typeSymbol == null)
                return string.Empty;

            // 见 Analizer.cs 里的实现
            var displayFormat = new SymbolDisplayFormat(
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
                miscellaneousOptions: SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

            return typeSymbol.ToDisplayString(displayFormat);
        }

        private string FormatModifiers(string modifiers)
        {
            // 拆分修饰符并处理
            var modifierList = modifiers.Split([' '], StringSplitOptions.RemoveEmptyEntries)
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Distinct()
                .ToList();

            // 移除已有的 partial 修饰符
            bool hasPartial = modifierList.Remove("partial");

            // 排序剩余修饰符（访问修饰符在前）
            var orderedModifiers = modifierList
                .OrderBy(m => m == "public" ? 0 :
                            m == "internal" ? 1 :
                            m == "protected" ? 2 :
                            m == "private" ? 3 : 4)
                .ToList();

            // 若有 partial 修饰符，最后加
            if (hasPartial)
            {
                orderedModifiers.Add("partial");
            }

            // 合并修饰符，补上适当空格
            return orderedModifiers.Count > 0 ? string.Join(" ", orderedModifiers) + " " : "";
        }

        /// <inheritdoc />
        public abstract bool CanWrite();
        /// <inheritdoc />
        public abstract string GetFileName();
        /// <summary>Returns the base types the generated partial class must add.</summary>
        public abstract string[] GenerateBaseTypes();
        /// <summary>Returns the interfaces the generated partial class must add.</summary>
        public abstract string[] GenerateBaseInterfaces();
        /// <summary>Returns the generated member bodies for the partial class.</summary>
        public abstract string GenerateBody();
    }
}