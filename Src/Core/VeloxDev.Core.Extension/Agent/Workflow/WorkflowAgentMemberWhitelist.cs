using System;
using System.Collections.Generic;
using System.Linq;

namespace VeloxDev.Core.Extension.Agent.Workflow;

public static class WorkflowAgentMemberWhitelist
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<Type, HashSet<string>> PropertyWhitelistMap = [];
    private static readonly Dictionary<Type, HashSet<string>> CommandWhitelistMap = [];
    private static readonly Dictionary<Type, HashSet<string>> MethodWhitelistMap = [];

    public static void ConfigureWorkflowAgentPropertyWhitelist(this Type type, params string[] propertyNames)
        => SetWhitelist(PropertyWhitelistMap, type, propertyNames);

    public static void ConfigureWorkflowAgentCommandWhitelist(this Type type, params string[] commandNames)
        => SetWhitelist(CommandWhitelistMap, type, commandNames);

    public static void ConfigureWorkflowAgentMethodWhitelist(this Type type, params string[] methodNames)
        => SetWhitelist(MethodWhitelistMap, type, methodNames);

    public static bool ClearWorkflowAgentPropertyWhitelist(this Type type)
        => RemoveWhitelist(PropertyWhitelistMap, type);

    public static bool ClearWorkflowAgentCommandWhitelist(this Type type)
        => RemoveWhitelist(CommandWhitelistMap, type);

    public static bool ClearWorkflowAgentMethodWhitelist(this Type type)
        => RemoveWhitelist(MethodWhitelistMap, type);

    public static void ClearAllWorkflowAgentWhitelists()
    {
        lock (SyncRoot)
        {
            PropertyWhitelistMap.Clear();
            CommandWhitelistMap.Clear();
            MethodWhitelistMap.Clear();
        }
    }

    internal static bool IsMemberAllowed(Type runtimeType, WorkflowAgentMemberKind kind, string memberName)
    {
        if (runtimeType is null)
        {
            throw new ArgumentNullException(nameof(runtimeType));
        }

        if (string.IsNullOrWhiteSpace(memberName))
        {
            throw new ArgumentException("memberName cannot be null or empty.", nameof(memberName));
        }

        var map = kind switch
        {
            WorkflowAgentMemberKind.Property => PropertyWhitelistMap,
            WorkflowAgentMemberKind.Command => CommandWhitelistMap,
            WorkflowAgentMemberKind.Method => MethodWhitelistMap,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

        lock (SyncRoot)
        {
            var matchedWhitelist = false;
            foreach (var type in EnumerateRelevantTypes(runtimeType))
            {
                if (!map.TryGetValue(type, out var whitelist))
                {
                    continue;
                }

                matchedWhitelist = true;
                if (whitelist.Contains(memberName))
                {
                    return true;
                }
            }

            return !matchedWhitelist;
        }
    }

    private static void SetWhitelist(Dictionary<Type, HashSet<string>> map, Type type, IEnumerable<string> memberNames)
    {
        if (type is null)
        {
            throw new ArgumentNullException(nameof(type));
        }

        if (memberNames is null)
        {
            throw new ArgumentNullException(nameof(memberNames));
        }

        var members = memberNames
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name.Trim())
            .Aggregate(new HashSet<string>(StringComparer.OrdinalIgnoreCase), static (set, name) =>
            {
                set.Add(name);
                return set;
            });

        lock (SyncRoot)
        {
            map[type] = members;
        }
    }

    private static bool RemoveWhitelist(Dictionary<Type, HashSet<string>> map, Type type)
    {
        if (type is null)
        {
            throw new ArgumentNullException(nameof(type));
        }

        lock (SyncRoot)
        {
            return map.Remove(type);
        }
    }

    private static IEnumerable<Type> EnumerateRelevantTypes(Type runtimeType)
    {
        for (var current = runtimeType; current is not null; current = current.BaseType)
        {
            yield return current;
        }

        foreach (var interfaceType in runtimeType.GetInterfaces())
        {
            yield return interfaceType;
        }
    }
}

internal enum WorkflowAgentMemberKind : byte
{
    Property = 0,
    Command = 1,
    Method = 2
}
