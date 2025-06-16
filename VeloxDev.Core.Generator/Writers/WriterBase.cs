﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text;
using VeloxDev.Core.Generator.Base;

namespace VeloxDev.Core.Generator.Writers
{
    public abstract class WriterBase : ICodeWriter
    {
        public abstract bool CanWrite();
        public abstract string GetFileName();
        public abstract string Write();

        public virtual void Initialize(ClassDeclarationSyntax classDeclaration, INamedTypeSymbol namedTypeSymbol)
        {
            Syntax = classDeclaration;
            Symbol = namedTypeSymbol;
        }

        public ClassDeclarationSyntax? Syntax { get; protected set; }
        public INamedTypeSymbol? Symbol { get; protected set; }

        public string GenerateHead()
        {
            if (Syntax == null)
            {
                return string.Empty;
            }
            StringBuilder sourceBuilder = new();
            sourceBuilder.AppendLine("#nullable enable");
            sourceBuilder.AppendLine();
            sourceBuilder.AppendLine($"namespace {AnalizeHelper.GetNamespace(Syntax)}");
            sourceBuilder.AppendLine("{");
            return sourceBuilder.ToString();
        }
        public string GenerateEnd()
        {
            if (Syntax == null)
            {
                return string.Empty;
            }
            StringBuilder sourceBuilder = new();
            sourceBuilder.AppendLine("   }");
            sourceBuilder.AppendLine("}");
            return sourceBuilder.ToString();
        }
    }
}
