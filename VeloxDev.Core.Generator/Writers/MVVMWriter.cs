using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static VeloxDev.Core.Generator.Base.Analizer;

namespace VeloxDev.Core.Generator.Writers
{
    public class MVVMWriter : WriterBase
    {
        public bool IsMVVM { get; private set; } = false;
        public List<MVVMPropertyFactory> MVVMProperties { get; private set; } = [];

        public override void Initialize(ClassDeclarationSyntax classDeclaration, INamedTypeSymbol namedTypeSymbol)
        {
            base.Initialize(classDeclaration, namedTypeSymbol);
            ReadMVVMConfig(namedTypeSymbol);
        }

        private void ReadMVVMConfig(INamedTypeSymbol symbol)
        {
            // 从 symbol 的字段读取标注了 VeloxPropertyAttribute 的字段，
            // 直接从 IFieldSymbol 获取类型并判断 NullableAnnotation，
            // 然后把带 ? 的类型名用于生成 OnPropertyChanging<T> / OnPropertyChanged<T> 的调用。
            MVVMProperties =
            [
                .. symbol.GetMembers()
                    .OfType<IFieldSymbol>()
                    .Where(field => field.GetAttributes().Any(attr =>
                        attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                        NAMESPACE_VELOX_MVVM + ".VeloxPropertyAttribute"))
                    .Select(field =>
                    {
                        // 根据 field.Type 与 NullableAnnotation 生成类型名文本（包含 ?）
                        var rawTypeName = field.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                        var typeName = rawTypeName;
                        // 如果启用了可空注解并且不是值类型已经用 Nullable<T> 表示的情况，附加 '?'
                        // 对于引用类型，NullableAnnotation == Annotated 表示 `Type?`
                        if (field.NullableAnnotation == NullableAnnotation.Annotated)
                        {
                            // 如果类型字符串已经以 '?' 结尾则无需重复追加（保险检查）
                            if (!typeName.EndsWith("?"))
                            {
                                typeName = typeName + "?";
                            }
                        }

                        // analizer 仍按原来方式创建（不假定它有 IsNullable 等成员）
                        var analizer = new MVVMFieldAnalizer(field);

                        // 生成调用 OnPropertyChanging/OnPropertyChanged 时带泛型类型参数
                        // 注意：这里直接在字符串里使用 analizer.PropertyName / analizer.FieldName
                        // 并把 typeName 填入泛型调用 OnPropertyChanging<typeName>(...)
                        var setteringBody = new List<string>
                        {
                            $"var oldValue = {analizer.FieldName};",
                            $"OnPropertyChanging<{typeName}>(nameof({analizer.PropertyName}), oldValue, value);"
                        };

                        var setteredBody = new List<string>
                        {
                            $"OnPropertyChanged<{typeName}>(nameof({analizer.PropertyName}), oldValue, value);"
                        };

                        var factory = new MVVMPropertyFactory(analizer, "public", false)
                        {
                            // 只设置 setter 回调 body，其他行为交由 factory 自身处理
                            SetteringBody = setteringBody,
                            SetteredBody = setteredBody,
                        };

                        return factory;
                    })
            ];

            IsMVVM = MVVMProperties.Count > 0;
        }

        public override bool CanWrite()
        {
            return IsMVVM;
        }

        public override string GetFileName()
        {
            if (Syntax == null || Symbol == null)
            {
                return string.Empty;
            }

            return $"{Syntax.Identifier.Text}_{Symbol.ContainingNamespace.ToDisplayString().Replace('.', '_')}_VeloxMVVM.g.cs";
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

            var interfaces = new List<string>();

            if (IsMVVM)
            {
                interfaces.Add($"{NAMESPACE_SYSTEM_MVVM}.INotifyPropertyChanging");
                interfaces.Add($"{NAMESPACE_SYSTEM_MVVM}.INotifyPropertyChanged");
            }

            if (interfaces.Count > 0)
            {
                var result = string.Join(", ", interfaces);
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

            if (IsMVVM)
            {
                // 生成带泛型回调重载（无泛型与有泛型），这样既向后兼容又能接收具体类型
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

                                        public void OnPropertyChanging<T>(string propertyName, T oldValue, T newValue)
                                        {
                                            PropertyChanging?.Invoke(this, new {{NAMESPACE_SYSTEM_MVVM}}.PropertyChangingEventArgs(propertyName));
                                        }

                                        public void OnPropertyChanged<T>(string propertyName, T oldValue, T newValue)
                                        {
                                            PropertyChanged?.Invoke(this, new {{NAMESPACE_SYSTEM_MVVM}}.PropertyChangedEventArgs(propertyName));
                                        }
                                     """);

                builder.AppendLine(GenerateProperty());
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
    }
}