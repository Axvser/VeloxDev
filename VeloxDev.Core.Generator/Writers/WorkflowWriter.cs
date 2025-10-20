using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Linq;
using System.Text;
using VeloxDev.Core.Generator.Base;

namespace VeloxDev.Core.Generator.Writers
{
    public class WorkflowWriter : WriterBase
    {
        private int _workflowType = 0;
        private WorkflowAttributeModel? _detectedModel;

        public override void Initialize(ClassDeclarationSyntax classDeclaration, INamedTypeSymbol namedTypeSymbol)
        {
            base.Initialize(classDeclaration, namedTypeSymbol);
            DetectWorkflowAttribute(namedTypeSymbol);
        }

        private void DetectWorkflowAttribute(INamedTypeSymbol symbol)
        {
            var attributes = symbol.GetAttributes();

            foreach (var attribute in attributes)
            {
                var attributeClass = attribute.AttributeClass;
                if (attributeClass == null) continue;

                // 修正特性识别逻辑
                var model = CreateAttributeModel(attribute, symbol);
                if (model != null)
                {
                    _detectedModel = model;
                    _workflowType = model.WorkflowType;
                    break;
                }
            }
        }

        private WorkflowAttributeModel? CreateAttributeModel(AttributeData attribute, INamedTypeSymbol targetClass)
        {
            var attributeClass = attribute.AttributeClass;
            if (attributeClass == null) return null;

            // 修正特性识别逻辑
            var containingType = attributeClass.ContainingType;
            if (containingType?.Name != "ViewModel" ||
                containingType.ContainingType?.Name != "WorkflowBuilder" ||
                containingType.ContainingNamespace?.ToDisplayString() != "VeloxDev.Core.WorkflowSystem")
            {
                return null;
            }

            // 处理泛型特性名称（如 TreeAttribute`1 -> TreeAttribute）
            var attributeName = attributeClass.Name;
            if (attributeName.Contains('`'))
            {
                attributeName = attributeName.Substring(0, attributeName.IndexOf('`'));
            }

            return attributeName switch
            {
                "TreeAttribute" => CreateTreeModel(attribute, targetClass),
                "NodeAttribute" => CreateNodeModel(attribute, targetClass),
                "SlotAttribute" => CreateSlotModel(attribute, targetClass),
                "LinkAttribute" => CreateLinkModel(attribute, targetClass),
                _ => null
            };
        }

        private TreeAttributeModel CreateTreeModel(AttributeData attribute, INamedTypeSymbol targetClass)
        {
            var model = new TreeAttributeModel
            {
                AttributeData = attribute,
                TargetClassSymbol = targetClass,
                AttributeSymbol = attribute.AttributeClass
            };

            // 解析泛型参数
            if (attribute.AttributeClass is INamedTypeSymbol namedType && namedType.TypeArguments.Length > 0)
            {
                model.HelperTypeName = namedType.TypeArguments[0].ToDisplayString();
            }

            // 解析构造函数参数
            if (attribute.ConstructorArguments.Length >= 1)
            {
                model.VirtualLinkType = attribute.ConstructorArguments[0].Value as INamedTypeSymbol;
            }
            if (attribute.ConstructorArguments.Length >= 2)
            {
                model.VirtualSlotType = attribute.ConstructorArguments[1].Value as INamedTypeSymbol;
            }

            return model;
        }

        private NodeAttributeModel CreateNodeModel(AttributeData attribute, INamedTypeSymbol targetClass)
        {
            var model = new NodeAttributeModel
            {
                AttributeData = attribute,
                TargetClassSymbol = targetClass,
                AttributeSymbol = attribute.AttributeClass
            };

            if (attribute.AttributeClass is INamedTypeSymbol namedType && namedType.TypeArguments.Length > 0)
            {
                model.HelperTypeName = namedType.TypeArguments[0].ToDisplayString();
            }

            // 解析构造函数参数
            if (attribute.ConstructorArguments.Length >= 1)
            {
                model.WorkSemaphore = (int)(attribute.ConstructorArguments[0].Value ?? 1);
            }

            return model;
        }

        private SlotAttributeModel CreateSlotModel(AttributeData attribute, INamedTypeSymbol targetClass)
        {
            var model = new SlotAttributeModel
            {
                AttributeData = attribute,
                TargetClassSymbol = targetClass,
                AttributeSymbol = attribute.AttributeClass
            };

            if (attribute.AttributeClass is INamedTypeSymbol namedType && namedType.TypeArguments.Length > 0)
            {
                model.HelperTypeName = namedType.TypeArguments[0].ToDisplayString();
            }

            return model;
        }

        private LinkAttributeModel CreateLinkModel(AttributeData attribute, INamedTypeSymbol targetClass)
        {
            var model = new LinkAttributeModel
            {
                AttributeData = attribute,
                TargetClassSymbol = targetClass,
                AttributeSymbol = attribute.AttributeClass
            };

            if (attribute.AttributeClass is INamedTypeSymbol namedType && namedType.TypeArguments.Length > 0)
            {
                model.HelperTypeName = namedType.TypeArguments[0].ToDisplayString();
            }

            // 解析构造函数参数
            if (attribute.ConstructorArguments.Length >= 1)
            {
                model.SlotType = attribute.ConstructorArguments[0].Value as INamedTypeSymbol;
            }

            return model;
        }

        public override bool CanWrite() => _workflowType != 0;

        public override string GetFileName()
        {
            if (Syntax == null || Symbol == null)
            {
                return string.Empty;
            }

            return $"{Syntax.Identifier.Text}_{Symbol.ContainingNamespace.ToDisplayString().Replace('.', '_')}_Workflow.g.cs";
        }

        public override string[] GenerateBaseInterfaces()
            => _workflowType switch
            {
                1 => [$"{NAMESPACE_VELOX_IWORKFLOW}.IWorkflowTreeViewModel"],
                2 => [$"{NAMESPACE_VELOX_IWORKFLOW}.IWorkflowNodeViewModel"],
                3 => [$"{NAMESPACE_VELOX_IWORKFLOW}.IWorkflowSlotViewModel"],
                4 => [$"{NAMESPACE_VELOX_IWORKFLOW}.IWorkflowLinkViewModel"],
                _ => []
            };

        public override string GenerateBody()
        {
            if (_detectedModel == null)
                return string.Empty;

            var sb = new StringBuilder();

            switch (_detectedModel)
            {
                case TreeAttributeModel treeModel:
                    GenerateTreeBody(sb, treeModel);
                    break;
                case NodeAttributeModel nodeModel:
                    GenerateNodeBody(sb, nodeModel);
                    break;
                case SlotAttributeModel slotModel:
                    GenerateSlotBody(sb, slotModel);
                    break;
                case LinkAttributeModel linkModel:
                    GenerateLinkBody(sb, linkModel);
                    break;
            }

            return sb.ToString();
        }

        private void GenerateTreeBody(StringBuilder sb, TreeAttributeModel model)
        {
            sb.AppendLine($$"""
                public {{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowTreeViewModelHelper Helper { get; protected set; } = new {{model.HelperTypeName}}();
            """);
        }

        private void GenerateNodeBody(StringBuilder sb, NodeAttributeModel model)
        {
            sb.AppendLine($$"""
                public {{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowNodeViewModelHelper Helper { get; protected set; } = new {{model.HelperTypeName}}();
            """);
        }

        private void GenerateSlotBody(StringBuilder sb, SlotAttributeModel model)
        {
            sb.AppendLine($$"""
                public {{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowSlotViewModelHelper Helper { get; protected set; } = new {{model.HelperTypeName}}();
            """);
        }

        private void GenerateLinkBody(StringBuilder sb, LinkAttributeModel model)
        {
            sb.AppendLine($$"""
                public {{NAMESPACE_VELOX_IWORKFLOW}}.IWorkflowLinkViewModelHelper Helper { get; protected set; } = new {{model.HelperTypeName}}();
            """);
        }

        public override string[] GenerateBaseTypes() => [];

        #region 内部模型定义

        private abstract class WorkflowAttributeModel
        {
            public INamedTypeSymbol? AttributeSymbol { get; set; }
            public INamedTypeSymbol? TargetClassSymbol { get; set; }
            public string? HelperTypeName { get; set; }
            public AttributeData? AttributeData { get; set; }
            public abstract int WorkflowType { get; }
        }

        private class TreeAttributeModel : WorkflowAttributeModel
        {
            public INamedTypeSymbol? VirtualLinkType { get; set; }
            public INamedTypeSymbol? VirtualSlotType { get; set; }
            public override int WorkflowType => 1;
        }

        private class NodeAttributeModel : WorkflowAttributeModel
        {
            public int WorkSemaphore { get; set; } = 1;
            public override int WorkflowType => 2;
        }

        private class SlotAttributeModel : WorkflowAttributeModel
        {
            public override int WorkflowType => 3;
        }

        private class LinkAttributeModel : WorkflowAttributeModel
        {
            public INamedTypeSymbol? SlotType { get; set; }
            public override int WorkflowType => 4;
        }

        #endregion
    }
}