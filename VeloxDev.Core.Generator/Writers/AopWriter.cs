using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace VeloxDev.Core.Generator.Writers
{
    public class AopWriter : WriterBase
    {
        private bool IsAop { get; set; } = false;

        public override void Initialize(ClassDeclarationSyntax classDeclaration, INamedTypeSymbol namedTypeSymbol)
        {
            base.Initialize(classDeclaration, namedTypeSymbol);
            ReadAopConfig(classDeclaration);
        }

        private void ReadAopConfig(ClassDeclarationSyntax classDeclaration)
        {
            IsAop = classDeclaration.Members
                .OfType<MemberDeclarationSyntax>()
                .Any(member => member.AttributeLists
                    .SelectMany(al => al.Attributes)
                    .Any(attr => attr.Name.ToString() == "AspectOriented"));
        }

        public override bool CanWrite() => IsAop;

        public override string GetFileName()
        {
            if (Syntax == null || Symbol == null)
            {
                return string.Empty;
            }

            return $"{Syntax.Identifier.Text}_{Symbol.ContainingNamespace.ToDisplayString().Replace('.', '_')}_AOP.g.cs";
        }

        public override string[] GenerateBaseInterfaces() =>
        [
            $"{NAMESPACE_VELOX_AOP}.{Syntax?.Identifier.Text}_{Symbol?.ContainingNamespace.ToDisplayString().Replace('.', '_')}_Aop"
        ];
        public override string[] GenerateBaseTypes() => [];

        public override string GenerateBody()
        {
            if (Syntax == null || Symbol == null || !IsAop)
            {
                return string.Empty;
            }

            var strAop = $"{NAMESPACE_VELOX_AOP}.{Syntax.Identifier.Text}_{Symbol.ContainingNamespace.ToDisplayString().Replace('.', '_')}_Aop";

            return $$"""
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
            """;
        }
    }
}