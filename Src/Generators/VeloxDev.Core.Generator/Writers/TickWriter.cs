using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace VeloxDev.Generators.Writers
{
    public class TickWriter : WriterBase
    {
        private bool IsTickable { get; set; } = false;
        private string Channel { get; set; } = "default";
        private int TargetFPS { get; set; } = -1;

        /// <inheritdoc />
        public override void Initialize(ClassDeclarationSyntax classDeclaration, INamedTypeSymbol namedTypeSymbol)
        {
            base.Initialize(classDeclaration, namedTypeSymbol);
            ReadTickableConfig(namedTypeSymbol);
        }

        private void ReadTickableConfig(INamedTypeSymbol symbol)
        {
            var attributeData = symbol.GetAttributes()
                .FirstOrDefault(ad =>
                    ad.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                    NAMESPACE_VELOX_TIMELINE + ".TickableAttribute" &&
                    ad.ApplicationSyntaxReference?.GetSyntax() is AttributeSyntax attrSyntax &&
                    attrSyntax.Parent?.Parent is ClassDeclarationSyntax
                );
            IsTickable = attributeData != null;

            if (IsTickable && attributeData != null)
            {
                // 读构造位置参数：(string channel, int fps)
                var ctorArgs = attributeData.ConstructorArguments;
                if (ctorArgs.Length >= 1 && ctorArgs[0].Value is string ctorChannel && !string.IsNullOrEmpty(ctorChannel))
                    Channel = ctorChannel;
                if (ctorArgs.Length >= 2 && ctorArgs[1].Value is int ctorFps)
                    TargetFPS = ctorFps;

                // 命名参数覆盖位置参数
                var channelArg = attributeData.NamedArguments
                    .FirstOrDefault(kv => kv.Key == "Channel");
                if (channelArg.Value.Value is string channelValue && !string.IsNullOrEmpty(channelValue))
                    Channel = channelValue;

                var fpsArg = attributeData.NamedArguments
                    .FirstOrDefault(kv => kv.Key == "TargetFPS");
                if (fpsArg.Value.Value is int fpsValue)
                    TargetFPS = fpsValue;
            }
        }

        /// <inheritdoc />
        public override bool CanWrite() => IsTickable;

        /// <inheritdoc />
        public override string GetFileName()
        {
            if (Syntax == null || Symbol == null)
            {
                return string.Empty;
            }

            return $"{Syntax.Identifier.Text}_{NamespaceFileSegment()}_Tick.g.cs";
        }

        /// <inheritdoc />
        public override string[] GenerateBaseInterfaces()
        {
            return IsTickable ? ["global::VeloxDev.TimeLine.ITickable"] : [];
        }

        /// <inheritdoc />
        public override string GenerateBody()
        {
            if (Syntax == null || Symbol == null || !IsTickable)
            {
                return string.Empty;
            }

            var setFpsLine = TargetFPS >= 1
                ? $"{NAMESPACE_VELOX_TIMELINE}.TickManager.SetTargetFPS({TargetFPS}, \"{Channel}\");\n                    "
                : string.Empty;

            return $$"""
                public void InitializeTickable()
                {
                    {{setFpsLine}}{{NAMESPACE_VELOX_TIMELINE}}.TickManager.RegisterBehaviour(this, "{{Channel}}");
                }

                public void CloseTickable()
                {
                    {{NAMESPACE_VELOX_TIMELINE}}.TickManager.UnregisterBehaviour(this, "{{Channel}}");
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

        /// <inheritdoc />
        public override string[] GenerateBaseTypes() => [];
    }
}