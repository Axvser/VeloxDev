using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using System.Text;

namespace VeloxDev.WPF.Generator
{
    internal abstract class ClassRoslyn
    {
        protected const string NAMESPACE_AOP = "global::VeloxDev.WPF.AopInterfaces.";
        protected const string NAMESPACE_ITHEME = "global::VeloxDev.WPF.StructuralDesign.Theme.";
        protected const string NAMESPACE_TRANSITOIN = "global::VeloxDev.WPF.TransitionSystem.";
        protected const string NAMESPACE_THEME = "global::VeloxDev.WPF.Theme.";
        protected const string NAMESPACE_MODEL = "global::System.ComponentModel.";
        protected const string NAMESPACE_PROXYEX = "global::VeloxDev.WPF.AspectOriented.ProxyExtension.";

        protected const string FULLNAME_MONOCONFIG = "global::VeloxDev.WPF.SourceGeneratorMark.MonoBehaviourAttribute";
        protected const string FULLNAME_CONSTRUCTOR = "global::VeloxDev.WPF.SourceGeneratorMark.ConstructorAttribute";
        protected const string FULLNAME_IGNORECONSTRUCTOR = "global::VeloxDev.WPF.SourceGeneratorMark.IgnoreConstructorGenerationAttribute";

        internal ClassRoslyn(ClassDeclarationSyntax classDeclarationSyntax, INamedTypeSymbol namedTypeSymbol, Compilation compilation)
        {
            Syntax = classDeclarationSyntax;
            Symbol = namedTypeSymbol;
            Compilation = compilation;

            IsAop = AnalizeHelper.IsAopClass(classDeclarationSyntax);
            ReadMonoConfig(namedTypeSymbol);
            ReadConstructorConfig(namedTypeSymbol);
        }

        public ClassDeclarationSyntax Syntax { get; set; }
        public INamedTypeSymbol Symbol { get; set; }
        public Compilation Compilation { get; set; }

        public bool IsAop { get; set; } = false;

        public bool IsMono { get; set; } = false;
        public double MonoSpan { get; set; } = 17;

        public bool CanGenerateConstructor { get; set; } = true;

        private void ReadMonoConfig(INamedTypeSymbol symbol)
        {
            var attributeData = symbol.GetAttributes()
                .FirstOrDefault(ad => ad.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == FULLNAME_MONOCONFIG);
            if (attributeData != null)
            {
                IsMono = true;
                MonoSpan = (double)attributeData.ConstructorArguments[0].Value!;
            }
        }
        private void ReadConstructorConfig(INamedTypeSymbol symbol)
        {
            var attributeData = symbol.GetAttributes()
                .FirstOrDefault(ad => ad.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == FULLNAME_IGNORECONSTRUCTOR);
            if (attributeData != null)
            {
                CanGenerateConstructor = false;
            }
        }

        public string GenerateUsing()
        {
            StringBuilder sourceBuilder = new();
            sourceBuilder.AppendLine("#nullable enable");
            sourceBuilder.AppendLine();
            return sourceBuilder.ToString();
        }
        public string GenerateNamespace()
        {
            StringBuilder sourceBuilder = new();
            sourceBuilder.AppendLine($"namespace {Symbol.ContainingNamespace}");
            sourceBuilder.AppendLine("{");
            return sourceBuilder.ToString();
        }
        public string GenerateEnd()
        {
            StringBuilder sourceBuilder = new();
            sourceBuilder.AppendLine("   }");
            sourceBuilder.AppendLine("}");
            return sourceBuilder.ToString();
        }
        public abstract string GetMonoUpdateBody();
        public abstract string GetMonoAwakeBody();
    }
}
