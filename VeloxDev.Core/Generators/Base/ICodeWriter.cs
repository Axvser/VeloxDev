using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace VeloxDev.Core.Generators.Base
{
    public interface ICodeWriter
    {
        public void Initialize(ClassDeclarationSyntax classDeclaration, INamedTypeSymbol namedTypeSymbol);
        public bool CanWrite();
        public string Write();
        public string GetFileName();
    }
}
