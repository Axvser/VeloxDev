using System;
using System.Collections.Generic;
using System.Text;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.AI.Workflow;

public class WorkflowAgentScope(IWorkflowTreeViewModel tree)
{
    public IWorkflowTreeViewModel Tree { get; } = tree;

    private static readonly Type[] FrameworkEnums =
        [typeof(SlotChannel), typeof(SlotState)];

    private static readonly Type[] FrameworkInterfaces =
        [typeof(IWorkflowTreeViewModel), typeof(IWorkflowNodeViewModel), typeof(IWorkflowSlotViewModel), typeof(IWorkflowLinkViewModel), typeof(IWorkflowViewModel)];

    private static readonly Type[] FrameworkComponents =
        [typeof(TreeViewModelBase), typeof(NodeViewModelBase), typeof(SlotViewModelBase), typeof(LinkViewModelBase),
         typeof(Anchor),typeof(Offset),typeof(Size)
        ];

    private readonly Dictionary<AgentLanguages, HashSet<Type>> CustomerEnums = [];
    private readonly Dictionary<AgentLanguages, HashSet<Type>> CustomerInterfaces = [];
    private readonly Dictionary<AgentLanguages, HashSet<Type>> CustomerComponents = [];

    public WorkflowAgentScope WithEnums(AgentLanguages language, params Type[] enums)
    {
        if (CustomerEnums.TryGetValue(language, out var set))
        {
            foreach (var item in enums)
            {
                set.Add(item);
            }
        }
        else
        {
            CustomerEnums[language] = [.. enums];
        }
        return this;
    }

    public WorkflowAgentScope WithInterfaces(AgentLanguages language, params Type[] interfaces)
    {
        if (CustomerInterfaces.TryGetValue(language, out var set))
        {
            foreach (var item in interfaces)
            {
                set.Add(item);
            }
        }
        else
        {
            CustomerInterfaces[language] = [.. interfaces];
        }
        return this;
    }

    public WorkflowAgentScope WithComponents(AgentLanguages language, params Type[] components)
    {
        if (CustomerComponents.TryGetValue(language, out var set))
        {
            foreach (var item in components)
            {
                set.Add(item);
            }
        }
        else
        {
            CustomerComponents[language] = [.. components];
        }
        return this;
    }

    public string ProvideAllContexts(AgentLanguages language)
    {
        var result = new StringBuilder();

        result.AppendLine($"# Workflow Agent Context");
        result.AppendLine();
        result.AppendLine("> Agent can learn about the structure of the Workflow Framework and how to Takeover a workflow system with Takeover Protocol.");
        result.AppendLine("> Agent can read source code from https://github.com/Axvser/VeloxDev");
        result.AppendLine();
        result.AppendLine("## Framework Context");
        result.AppendLine();
        result.AppendLine(ProvideFrameworkContext(language));
        result.AppendLine();
        result.AppendLine("## Customer Context");
        result.AppendLine();
        result.AppendLine(ProvideCustomerContext(language));

        return result.ToString();
    }

    public string ProvideFrameworkContext(AgentLanguages language = AgentLanguages.English)
    {
        var result = new StringBuilder();

        foreach (var framework in FrameworkEnums)
        {
            result.AppendLine(AgentContextCollector.GetEnumContext(framework, language));
        }
        foreach (var framework in FrameworkInterfaces)
        {
            result.AppendLine(AgentContextCollector.GetInterfaceContext(framework, language));
        }
        foreach (var framework in FrameworkComponents)
        {
            result.AppendLine(AgentContextCollector.GetClassContext(framework, language));
        }

        return result.ToString();
    }

    public string ProvideCustomerContext(AgentLanguages language = AgentLanguages.English)
    {
        var result = new StringBuilder();

        foreach (var kvp in CustomerEnums)
        {
            foreach (var framework in kvp.Value)
            {
                result.AppendLine(AgentContextCollector.GetEnumContext(framework, kvp.Key));
            }
        }
        foreach (var kvp in CustomerInterfaces)
        {
            foreach (var framework in kvp.Value)
            {
                result.AppendLine(AgentContextCollector.GetInterfaceContext(framework, kvp.Key));
            }
        }
        foreach (var kvp in CustomerComponents)
        {
            foreach (var framework in kvp.Value)
            {
                result.AppendLine(AgentContextCollector.GetClassContext(framework, kvp.Key));
            }
        }

        return result.ToString();
    }
}
