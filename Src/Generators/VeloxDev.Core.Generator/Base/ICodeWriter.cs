using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace VeloxDev.Generators.Base
{
    public interface ICodeWriter
    {
        /// <summary>Initializes the writer with the partial class declaration and its symbol.</summary>
        public void Initialize(ClassDeclarationSyntax classDeclaration, INamedTypeSymbol namedTypeSymbol);
        /// <summary>Returns whether this writer can produce output for the initialized symbol.</summary>
        public bool CanWrite();
        /// <summary>Returns the generated member bodies.</summary>
        public string Write();
        /// <summary>Returns the file name the generated source is added under.</summary>
        public string GetFileName();
    }
}
