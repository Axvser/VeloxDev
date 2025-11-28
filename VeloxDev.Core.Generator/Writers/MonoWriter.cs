using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace VeloxDev.Core.Generator.Writers
{
    public class MonoWriter : WriterBase
    {
        private bool IsMono { get; set; } = false;

        public override void Initialize(ClassDeclarationSyntax classDeclaration, INamedTypeSymbol namedTypeSymbol)
        {
            base.Initialize(classDeclaration, namedTypeSymbol);
            ReadMonoConfig(namedTypeSymbol);
        }

        private void ReadMonoConfig(INamedTypeSymbol symbol)
        {
            var attributeData = symbol.GetAttributes()
                .FirstOrDefault(ad =>
                    ad.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                    NAMESPACE_VELOX_TIMELINE + ".MonoBehaviourAttribute" &&
                    ad.ApplicationSyntaxReference?.GetSyntax() is AttributeSyntax attrSyntax &&
                    attrSyntax.Parent?.Parent is ClassDeclarationSyntax
                );
            IsMono = attributeData != null;
        }

        public override bool CanWrite() => IsMono;

        public override string GetFileName()
        {
            if (Syntax == null || Symbol == null)
            {
                return string.Empty;
            }

            return $"{Syntax.Identifier.Text}_{Symbol.ContainingNamespace.ToDisplayString().Replace('.', '_')}_Mono.g.cs";
        }

        public override string[] GenerateBaseInterfaces()
        {
            return IsMono ? ["global::VeloxDev.Core.Interfaces.MonoBehavior.IMonoBehavior"] : [];
        }

        public override string GenerateBody()
        {
            if (Syntax == null || Symbol == null || !IsMono)
            {
                return string.Empty;
            }

            return $$"""
                public void InitializeMonoBehavior()
                {
                    {{NAMESPACE_VELOX_TIMELINE}}.MonoBehaviourManager.RegisterBehavior(this);
                }

                public void CloseMonoBehavior()
                {
                    {{NAMESPACE_VELOX_TIMELINE}}.MonoBehaviourManager.UnregisterBehavior(this);
                }

                public void InvokeAwake()
                {
                    Awake();
                }

                public void InvokeStart()
                {
                    Start();
                }

                public void InvokeUpdate({{NAMESPACE_VELOX_TIMELINE}}.FrameEventArgs e)
                {
                    Update(e);
                }

                public void InvokeLateUpdate({{NAMESPACE_VELOX_TIMELINE}}.FrameEventArgs e)
                {
                    LateUpdate(e);
                }

                public void InvokeFixedUpdate({{NAMESPACE_VELOX_TIMELINE}}.FrameEventArgs e)
                {
                    FixedUpdate(e);
                }
            
                partial void Awake();
                partial void Start();
                partial void Update({{NAMESPACE_VELOX_TIMELINE}}.FrameEventArgs e);
                partial void LateUpdate({{NAMESPACE_VELOX_TIMELINE}}.FrameEventArgs e);
                partial void FixedUpdate({{NAMESPACE_VELOX_TIMELINE}}.FrameEventArgs e);
            """;
        }

        public override string[] GenerateBaseTypes() => [];
    }
}