using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using System.Text;
using VeloxDev.Core.Generator.Base;

namespace VeloxDev.Core.Generator.Writers
{
    public class WorkflowWriter : WriterBase
    {
        private int WorkflowType { get; set; } = 0;

        public override void Initialize(ClassDeclarationSyntax classDeclaration, INamedTypeSymbol namedTypeSymbol)
        {
            base.Initialize(classDeclaration, namedTypeSymbol);
            ReadWorkflowConfig(namedTypeSymbol);
        }

        private void ReadWorkflowConfig(INamedTypeSymbol symbol)
        {
            // 这里需要根据实际的工作流属性检测逻辑来实现
            // 暂时保持原有逻辑
            WorkflowType = 0; // 需要根据实际检测逻辑设置
        }

        public override bool CanWrite() => WorkflowType != 0;

        public override string GetFileName()
        {
            if (Syntax == null || Symbol == null)
            {
                return string.Empty;
            }

            return $"{Syntax.Identifier.Text}_{Symbol.ContainingNamespace.ToDisplayString().Replace('.', '_')}_Workflow.g.cs";
        }

        public override string[] GenerateBaseInterfaces()
            => WorkflowType switch
            {
                1 => [$"{NAMESPACE_VELOX_IWORKFLOW}.IWorkflowTreeViewModel"],
                2 => [$"{NAMESPACE_VELOX_IWORKFLOW}.IWorkflowNodeViewModel"],
                3 => [$"{NAMESPACE_VELOX_IWORKFLOW}.IWorkflowSlotViewModel"],
                4 => [$"{NAMESPACE_VELOX_IWORKFLOW}.IWorkflowLinkViewModel"],
                _ => []
            };

        public override string GenerateBody()
        {
            // 工作流相关的具体实现逻辑
            // 根据WorkflowType生成不同的工作流相关代码
            return string.Empty;
        }

        public override string[] GenerateBaseTypes() => [];
    }
}